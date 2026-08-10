using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Services;

/// <summary>
/// Outcome of a Steam Web API key check.
/// </summary>
public enum ApiKeyValidationResult
{
	/// <summary>The key was accepted by Steam.</summary>
	Valid,
	/// <summary>Steam rejected the key, or no key was provided.</summary>
	Invalid,
	/// <summary>Steam could not be contacted, so the key could not be checked.</summary>
	Unreachable
}

/// <summary>
/// User facing warnings shown when the Steam Web API cannot be used.
/// </summary>
public static class ApiKeyWarningMessages
{
	public const string Invalid = "Steam API key is invalid. Metadata and official achievements may not load. Check your key in Settings.";
	public const string Unreachable = "Could not reach Steam servers. You may be offline. Some features may not work.";

	/// <summary>
	/// Returns the warning matching <paramref name="result"/>, or an empty string when the key is valid.
	/// </summary>
	public static string FromResult(ApiKeyValidationResult result) => result switch
	{
		ApiKeyValidationResult.Invalid => Invalid,
		ApiKeyValidationResult.Unreachable => Unreachable,
		_ => string.Empty
	};
}

/// <summary>
/// Reads game and achievement data from the Steam Web API, backed by a memory and disk cache.
/// </summary>
public interface ISteamApiService
{
	/// <summary>
	/// Fired whenever the configured API key is checked, so the UI can report a key or connectivity
	/// problem no matter which background operation ran into it.
	/// </summary>
	event EventHandler<ApiKeyValidationResult>? ApiKeyValidated;

	/// <summary>
	/// Checks whether <paramref name="apiKey"/> is accepted by Steam.
	/// </summary>
	Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default);
	/// <summary>
	/// Gets the cached metadata of a game, fetching and caching it when missing or outdated.
	/// </summary>
	Task<SteamGameMetadata?> GetSteamGameMetadataAsync(string appId, CancellationToken ct = default);
	/// <summary>
	/// Gets the global unlock percentage of every achievement of a game, keyed by achievement id.
	/// </summary>
	Task<Dictionary<string, double>> GetGlobalAchievementStatsAsync(string appId, CancellationToken ct = default);
	/// <summary>
	/// Gets the app ids of the games owned by a Steam user.
	/// </summary>
	Task<List<string>> GetOwnedGamesAsync(string steamId, CancellationToken ct = default);
	/// <summary>
	/// Gets the unlock state of a Steam user's achievements for a game.
	/// </summary>
	Task<List<Achievement>> GetPlayerAchievementsAsync(string steamId, string appId, CancellationToken ct = default);
	/// <summary>
	/// Drops both the in-memory and the disk metadata cache.
	/// </summary>
	void ClearCache();
}

/// <inheritdoc cref="ISteamApiService"/>
public sealed class SteamApiService
	: ISteamApiService
	, IDisposable
{
	private readonly ISettingsService _settings;
	private readonly ConcurrentDictionary<string, SteamGameMetadata> _cache = new();

	/// <summary>
	/// Shared by every instance, because an <see cref="HttpClient"/> per instance would leak sockets: a
	/// disposed one keeps its connections in TIME_WAIT for minutes.
	/// </summary>
	/// <remarks>
	/// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> recycles connections so that DNS changes
	/// are still picked up, which a long lived client would otherwise miss.
	/// </remarks>
	private static readonly HttpClient Http = new(
		new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
	{
		Timeout = TimeSpan.FromSeconds(10)
	};

	/// <summary>
	/// One gate per game, so concurrent lookups of the same game fetch once instead of racing each other
	/// over the network and over the same cache file.
	/// </summary>
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

	private static readonly string CacheDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"TinyTrophy",
		"cache");

	/// <summary>
	/// How long Steam is considered unreachable after a connection failure. Keeps a single timeout from
	/// being paid again for every game while offline.
	/// </summary>
	private static readonly TimeSpan OfflineCooldown = TimeSpan.FromSeconds(30);
	private long _offlineUntilTicks;

	public SteamApiService(ISettingsService settings)
	{
		_settings = settings;
		Directory.CreateDirectory(CacheDir);
	}

	/// <summary>
	/// Tells whether requests should be skipped because Steam recently failed to answer.
	/// </summary>
	private bool IsOffline => Interlocked.Read(ref _offlineUntilTicks) > DateTime.UtcNow.Ticks;

	/// <summary>
	/// Records the outcome of a request so later ones can fail fast while Steam is unreachable.
	/// </summary>
	private void SetOffline(bool offline) => Interlocked.Exchange(
		ref _offlineUntilTicks,
		offline ? DateTime.UtcNow.Add(OfflineCooldown).Ticks : 0);

	/// <summary>
	/// Tells whether an exception means Steam could not be reached, as opposed to answering with an error.
	/// </summary>
	private static bool IsConnectionFailure(Exception ex) => ex switch
	{
		// A status code means the server answered, so the connection itself is fine
		HttpRequestException http => http.StatusCode is null,
		// Not a caller cancellation at this point, so it is the HttpClient timeout
		TaskCanceledException => true,
		_ => false
	};

	/// <summary>
	/// Sends a GET request and parses the JSON body, skipping the call entirely while Steam is unreachable.
	/// </summary>
	/// <returns><see langword="null"/> when the request was skipped, failed or returned invalid JSON.</returns>
	/// <exception cref="OperationCanceledException">The caller cancelled <paramref name="ct"/>.</exception>
	private async Task<JsonDocument?> GetJsonAsync(
		string url,
		CancellationToken ct = default)
	{
		if (IsOffline)
			return null;

		try
		{
			string response = await Http.GetStringAsync(url, ct);
			// One success ends the cooldown early, so coming back online is noticed immediately
			SetOffline(false);
			return JsonDocument.Parse(response);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			if (IsConnectionFailure(ex))
				SetOffline(true);
			return null;
		}
	}

	/// <inheritdoc/>
	public event EventHandler<ApiKeyValidationResult>? ApiKeyValidated;

	/// <inheritdoc/>
	/// <returns>
	/// <see cref="ApiKeyValidationResult.Unreachable"/> when Steam cannot be contacted, so callers can
	/// tell a network outage apart from a genuinely bad key.
	/// </returns>
	public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(
		string apiKey,
		CancellationToken ct = default)
	{
		ApiKeyValidationResult result = await CheckApiKeyAsync(apiKey, ct);
		ApiKeyValidated?.Invoke(this, result);
		return result;
	}

	/// <summary>
	/// Performs the key check itself, leaving the reporting to <see cref="ValidateApiKeyAsync"/>.
	/// </summary>
	private async Task<ApiKeyValidationResult> CheckApiKeyAsync(
		string apiKey,
		CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
			return ApiKeyValidationResult.Invalid;

		if (IsOffline)
			return ApiKeyValidationResult.Unreachable;

		try
		{
			// Any endpoint needing a key would do, but this one is cheap and not tied to a game
			HttpResponseMessage response = await Http.GetAsync($"https://api.steampowered.com/ISteamWebAPIUtil/GetSupportedAPIList/v1/?key={apiKey}", ct);
			SetOffline(false);
			if (response.IsSuccessStatusCode)
				return ApiKeyValidationResult.Valid;
			// Only these two mean Steam judged the key; anything else (500, 429, ...) is Steam having a
			// bad day and says nothing about the key, so the user is not told to go fix it
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				return ApiKeyValidationResult.Invalid;
			return ApiKeyValidationResult.Unreachable;
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			if (IsConnectionFailure(ex))
				SetOffline(true);
			return ApiKeyValidationResult.Unreachable;
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Lookups prefer the memory cache, then the disk cache, then Steam. A cached entry that is missing
	/// achievements reported by Steam is considered outdated, because it means the game was extended after
	/// it was cached, and is rebuilt from scratch rather than patched.
	/// </remarks>
	public async Task<SteamGameMetadata?> GetSteamGameMetadataAsync(
		string appId,
		CancellationToken ct = default)
	{
		// Checked before taking the gate so warm lookups, which are the vast majority, stay lock free
		if (_cache.TryGetValue(appId, out SteamGameMetadata? cached))
			return cached;

		SemaphoreSlim gate = _gates.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(ct);
		try
		{
			// The waiting turned this into a cache hit: the caller ahead did the fetch for everyone
			if (_cache.TryGetValue(appId, out cached))
				return cached;

			return await LoadMetadataAsync(appId, ct);
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>
	/// Resolves the metadata of a game from the disk cache or Steam. Callers must hold the gate of the game.
	/// </summary>
	private async Task<SteamGameMetadata?> LoadMetadataAsync(
		string appId,
		CancellationToken ct = default)
	{
		Dictionary<string, double>? globalStats = null;
		SteamGameMetadata? staleMetadata = null;

		SteamGameMetadata? diskMetadata = await ReadDiskCacheAsync(appId, ct);
		if (diskMetadata is not null)
		{
			// Doubles as the freshness probe: it lists every achievement of the game and needs no API key,
			// so it works even for users who never configured one
			globalStats = await GetGlobalAchievementStatsAsync(appId, ct);

			// Achievements missing from the cache mean the game was extended on Steam's side, so rebuild it from scratch
			if (HasUnknownAchievements(diskMetadata, globalStats))
			{
				staleMetadata = diskMetadata;
			}
			else
			{
				// Nothing came back (offline, or the game has no achievements), so leave the file untouched
				if (globalStats.Count > 0)
				{
					ApplyGlobalPercentages(diskMetadata, globalStats);
					await WriteDiskCacheAsync(appId, diskMetadata, ct);
				}

				_cache[appId] = diskMetadata;
				return diskMetadata;
			}
		}

		SteamGameMetadata metadata = await BuildMetadataAsync(appId, ct);

		// A nameless result means every source failed, so an outdated cache still beats showing nothing.
		// Returning without caching also lets the next call retry instead of freezing the failure in place.
		if (string.IsNullOrWhiteSpace(metadata.Name))
			return staleMetadata ?? metadata;

		// Reused when the freshness probe above already fetched it, so a rebuild costs no extra request
		globalStats ??= await GetGlobalAchievementStatsAsync(appId, ct);
		ApplyGlobalPercentages(metadata, globalStats);

		await WriteDiskCacheAsync(appId, metadata, ct);
		_cache[appId] = metadata;
		return metadata;
	}

	/// <summary>
	/// Fetches the complete metadata of a game from Steam, without touching any cache.
	/// </summary>
	/// <returns>
	/// Metadata with an empty <see cref="SteamGameMetadata.Name"/> when every source failed.
	/// </returns>
	private async Task<SteamGameMetadata> BuildMetadataAsync(
		string appId,
		CancellationToken ct = default)
	{
		(string schemaName, Dictionary<string, SteamAchievementSchema> achievements) = await FetchGameSchemaAsync(appId, ct);
		SteamGameMetadata metadata = new()
		{
			Name = schemaName,
			Achievements = achievements
		};

		// The schema is the only source of achievements, but its gameName is often an internal title, and
		// it is skipped entirely without an API key. The store has neither limitation, so it wins on the name.
		SteamGameMetadata? basic = await GetBasicMetadataAsync(appId, ct);
		if (basic is not null)
		{
			if (!string.IsNullOrWhiteSpace(basic.Name))
				metadata.Name = basic.Name;
			metadata.HeaderImageUri = basic.HeaderImageUri;
		}

		// Games missing from the store still have their image on the CDN, which follows a fixed URL shape
		if (string.IsNullOrWhiteSpace(metadata.HeaderImageUri))
			metadata.HeaderImageUri = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

		return metadata;
	}

	/// <summary>
	/// Tells whether <paramref name="globalStats"/> reports achievements that <paramref name="metadata"/> does not know about.
	/// </summary>
	private static bool HasUnknownAchievements(
		SteamGameMetadata metadata,
		Dictionary<string, double> globalStats)
	{
		// The two endpoints do not agree on the casing of achievement ids, so comparing them literally
		// would report the whole game as new on every single lookup
		HashSet<string> known = new(metadata.Achievements.Keys, StringComparer.OrdinalIgnoreCase);
		return globalStats.Keys.Any(id => !known.Contains(id));
	}

	/// <summary>
	/// Copies the global unlock percentages onto the achievements already known by <paramref name="metadata"/>.
	/// Percentages without a matching achievement are ignored.
	/// </summary>
	private static void ApplyGlobalPercentages(
		SteamGameMetadata metadata,
		Dictionary<string, double> globalStats)
	{
		// A case insensitive view over the achievements, since the dictionary itself must keep the exact
		// ids that the rest of the app matches against
		Dictionary<string, SteamAchievementSchema> lookup = new(metadata.Achievements, StringComparer.OrdinalIgnoreCase);
		foreach ((string id, double percentage) in globalStats)
		{
			if (lookup.TryGetValue(id, out SteamAchievementSchema? achievement))
				achievement.GlobalPercentage = percentage;
		}
	}

	/// <summary>
	/// Gets the path of the disk cache file of a game.
	/// </summary>
	private static string GetCacheFilePath(string appId) => Path.Combine(CacheDir, $"{appId}.json");

	/// <summary>
	/// Reads the disk cache of a game, returning <see langword="null"/> when it is missing or unreadable.
	/// </summary>
	private static async Task<SteamGameMetadata?> ReadDiskCacheAsync(
		string appId,
		CancellationToken ct = default)
	{
		string cacheFile = GetCacheFilePath(appId);
		if (!File.Exists(cacheFile))
			return null;

		try
		{
			string json = await File.ReadAllTextAsync(cacheFile, ct);
			return JsonSerializer.Deserialize(json, AppJsonContext.Default.SteamGameMetadata);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Writes the disk cache of a game, ignoring I/O failures since the cache is only an optimization.
	/// </summary>
	/// <remarks>
	/// The file is written aside and then moved into place, so a reader never observes a half written file
	/// and a crash cannot leave a corrupted cache behind.
	/// </remarks>
	private static async Task WriteDiskCacheAsync(
		string appId,
		SteamGameMetadata metadata,
		CancellationToken ct = default)
	{
		string cacheFile = GetCacheFilePath(appId);
		// The thread id keeps two writers from fighting over the same temporary file
		string tempFile = $"{cacheFile}.{Environment.CurrentManagedThreadId}.tmp";
		try
		{
			string json = JsonSerializer.Serialize(metadata, AppJsonContext.Default.SteamGameMetadata);
			await File.WriteAllTextAsync(tempFile, json, ct);
			File.Move(tempFile, cacheFile, overwrite: true);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			DeleteQuietly(tempFile);
			throw;
		}
		catch
		{
			DeleteQuietly(tempFile);
		}
	}

	/// <summary>
	/// Deletes a file, ignoring any failure.
	/// </summary>
	private static void DeleteQuietly(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch { }
	}

	/// <inheritdoc/>
	/// <returns>An empty dictionary when the game has no achievements or the request failed.</returns>
	public async Task<Dictionary<string, double>> GetGlobalAchievementStatsAsync(
		string appId,
		CancellationToken ct = default)
	{
		Dictionary<string, double> result = new(StringComparer.OrdinalIgnoreCase);

		string url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json";
		using JsonDocument? doc = await GetJsonAsync(url, ct);
		if (doc is null)
			return result;

		try
		{
			JsonElement achievements = doc.RootElement
				.GetProperty("achievementpercentages")
				.GetProperty("achievements");

			foreach (JsonElement ach in achievements.EnumerateArray())
			{
				string name = ach.GetProperty("name").GetString() ?? string.Empty;
				JsonElement percentRaw = ach.GetProperty("percent");
				// Steam is inconsistent here and sends the percentage as a number or as a string, and the
				// string form uses a dot even in locales where that is not the decimal separator
				double percent = percentRaw.ValueKind == JsonValueKind.String
					? double.Parse(percentRaw.GetString()!, CultureInfo.InvariantCulture)
					: percentRaw.GetDouble();
				// Rounding on Steam's side can push the value slightly past 100
				result[name] = Math.Min(percent, 100.0);
			}
		}
		catch { }

		return result;
	}

	/// <summary>
	/// Fetches the achievement schema of a game, which holds the localized names, descriptions and icons.
	/// </summary>
	/// <returns>Empty values when no API key is configured or the request failed.</returns>
	private async Task<(string Name, Dictionary<string, SteamAchievementSchema> Achievements)> FetchGameSchemaAsync(
		string appId,
		CancellationToken ct = default)
	{
		string gameTitle = string.Empty;
		Dictionary<string, SteamAchievementSchema> result = new(StringComparer.OrdinalIgnoreCase);

		// This is the one endpoint that needs a key, which is why a keyless user still gets game names and
		// unlock percentages but no achievement titles or icons
		string apiKey = _settings.Settings.SteamApiKey;
		if (string.IsNullOrWhiteSpace(apiKey))
			return (gameTitle, result);

		// The language decides the wording returned for every achievement, so a language change invalidates
		// the cached copy of the whole game
		string schemaUrl = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v0002/?key={apiKey}&appid={appId}&l={_settings.Settings.Language}&format=json";
		using JsonDocument? doc = await GetJsonAsync(schemaUrl, ct);
		if (doc is null)
			return (gameTitle, result);

		try
		{
			JsonElement game = doc.RootElement.GetProperty("game");
			if (game.TryGetProperty("gameName", out JsonElement gameName))
				gameTitle = gameName.GetString() ?? string.Empty;

			if (game.TryGetProperty("availableGameStats", out JsonElement stats) &&
				stats.TryGetProperty("achievements", out JsonElement achievements))
			{
				foreach (JsonElement ach in achievements.EnumerateArray())
				{
					string name = ach.GetProperty("name").GetString() ?? "";
					result[name] = new SteamAchievementSchema
					{
						Id = name,
						Name = ach.TryGetProperty("displayName", out JsonElement dn) ? dn.GetString() ?? name : name,
						Description = ach.TryGetProperty("description", out JsonElement desc) ? desc.GetString() ?? "" : "",
						IconUri = ach.TryGetProperty("icon", out JsonElement icon) ? icon.GetString() ?? "" : "",
						IconLockedUri = ach.TryGetProperty("icongray", out JsonElement iconGray) ? iconGray.GetString() ?? "" : "",
						IsHidden = ach.TryGetProperty("hidden", out JsonElement hidden) && hidden.GetInt32() == 1
					};
				}
			}
		}
		catch { }

		return (gameTitle, result);
	}

	/// <summary>
	/// Fetches the store name and header image of a game. Needs no API key.
	/// </summary>
	/// <returns><see langword="null"/> when the store has no entry for the game or the request failed.</returns>
	private async Task<SteamGameMetadata?> GetBasicMetadataAsync(
		string appId,
		CancellationToken ct = default)
	{
		string url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
		using JsonDocument? doc = await GetJsonAsync(url, ct);
		if (doc is null)
			return null;

		try
		{
			// The payload is keyed by app id, and a delisted or region locked game answers with success:false
			// and no data at all, which is why both steps are probed rather than assumed
			if (doc.RootElement.TryGetProperty(appId, out JsonElement appData) && appData.TryGetProperty("data", out JsonElement data))
			{
				SteamGameMetadata metadata = new()
				{
					Name = data.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? $"Game {appId}" : $"Game {appId}",
					HeaderImageUri = data.TryGetProperty("header_image", out JsonElement img) ? img.GetString() ?? "" : ""
				};

				return metadata;
			}
		}
		catch { }

		return null;
	}

	/// <inheritdoc/>
	/// <returns>An empty list when no API key or Steam id is configured, or the request failed.</returns>
	public async Task<List<string>> GetOwnedGamesAsync(
		string steamId,
		CancellationToken ct = default)
	{
		List<string> appIds = [];
		string apiKey = _settings.Settings.SteamApiKey;
		if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId))
			return appIds;

		string url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={apiKey}&steamid={steamId}&include_played_free_games=true&format=json";
		using JsonDocument? doc = await GetJsonAsync(url, ct);
		if (doc is null)
			return appIds;

		try
		{
			if (doc.RootElement.TryGetProperty("response", out JsonElement resp) && resp.TryGetProperty("games", out JsonElement games))
			{
				foreach (JsonElement game in games.EnumerateArray())
				{
					if (game.TryGetProperty("appid", out JsonElement appid))
						appIds.Add(appid.GetInt32().ToString());
				}
			}
		}
		catch { }

		return appIds;
	}

	/// <inheritdoc/>
	/// <returns>
	/// Achievements carrying only ids and unlock state, since display data comes from
	/// <see cref="GetSteamGameMetadataAsync"/>. Empty when the profile is private or the request failed.
	/// </returns>
	public async Task<List<Achievement>> GetPlayerAchievementsAsync(
		string steamId,
		string appId,
		CancellationToken ct = default)
	{
		List<Achievement> achievements = [];
		string apiKey = _settings.Settings.SteamApiKey;
		if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(steamId))
			return achievements;

		string url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={apiKey}&steamid={steamId}&appid={appId}&format=json";
		using JsonDocument? doc = await GetJsonAsync(url, ct);
		if (doc is null)
			return achievements;

		try
		{
			if (doc.RootElement.TryGetProperty("playerstats", out JsonElement stats) && stats.TryGetProperty("achievements", out JsonElement achs))
			{
				foreach (JsonElement ach in achs.EnumerateArray())
				{
					string name = ach.GetProperty("apiname").GetString() ?? "";
					int achieved = ach.TryGetProperty("achieved", out JsonElement a) ? a.GetInt32() : 0;
					long unlockTime = ach.TryGetProperty("unlocktime", out JsonElement ut) ? ut.GetInt64() : 0;

					achievements.Add(new Achievement
					{
						Id = name,
						Name = name,
						IsUnlocked = achieved == 1,
						UnlockTime = unlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(unlockTime).LocalDateTime : null,
					});
				}
			}
		}
		catch { }

		return achievements;
	}

	/// <inheritdoc/>
	public void ClearCache()
	{
		_cache.Clear();

		// An explicit refresh should reach Steam even if it was unreachable a moment ago
		SetOffline(false);
		try
		{
			if (Directory.Exists(CacheDir))
			{
				// Sweep leftover temporaries too, in case a write was interrupted
				foreach (string file in Directory.EnumerateFiles(CacheDir, "*.json*"))
					File.Delete(file);
			}
		}
		catch { }
	}

	/// <summary>
	/// Disposes the per game gates. The shared <see cref="HttpClient"/> outlives every instance and is
	/// deliberately left alone.
	/// </summary>
	public void Dispose()
	{
		foreach (SemaphoreSlim gate in _gates.Values)
			gate.Dispose();
		_gates.Clear();
	}
}
