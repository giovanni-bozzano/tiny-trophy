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
	private readonly HttpClient _http;
	private readonly ISettingsService _settings;
	private readonly ConcurrentDictionary<string, SteamGameMetadata> _cache = new();
	private static readonly string CacheDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"TinyTrophy",
		"cache");

	public SteamApiService(ISettingsService settings)
	{
		_settings = settings;
		_http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		Directory.CreateDirectory(CacheDir);
	}

	/// <inheritdoc/>
	/// <returns>
	/// <see cref="ApiKeyValidationResult.Unreachable"/> when Steam cannot be contacted, so callers can
	/// tell a network outage apart from a genuinely bad key.
	/// </returns>
	public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(
		string apiKey,
		CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
			return ApiKeyValidationResult.Invalid;

		try
		{
			HttpResponseMessage response = await _http.GetAsync($"https://api.steampowered.com/ISteamWebAPIUtil/GetSupportedAPIList/v1/?key={apiKey}", ct);
			if (response.IsSuccessStatusCode)
				return ApiKeyValidationResult.Valid;
			if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
				return ApiKeyValidationResult.Invalid;
			return ApiKeyValidationResult.Unreachable;
		}
		catch (HttpRequestException)
		{
			return ApiKeyValidationResult.Unreachable;
		}
		catch (TaskCanceledException)
		{
			return ApiKeyValidationResult.Unreachable;
		}
		catch
		{
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
		if (_cache.TryGetValue(appId, out SteamGameMetadata? cached))
			return cached;

		Dictionary<string, double>? globalStats = null;
		SteamGameMetadata? staleMetadata = null;

		SteamGameMetadata? diskMetadata = await ReadDiskCacheAsync(appId, ct);
		if (diskMetadata is not null)
		{
			globalStats = await GetGlobalAchievementStatsAsync(appId, ct);

			// Achievements missing from the cache mean the game was extended on Steam's side, so rebuild it from scratch
			if (HasUnknownAchievements(diskMetadata, globalStats))
			{
				staleMetadata = diskMetadata;
			}
			else
			{
				ApplyGlobalPercentages(diskMetadata, globalStats);
				await WriteDiskCacheAsync(appId, diskMetadata, ct);
				_cache[appId] = diskMetadata;
				return diskMetadata;
			}
		}

		SteamGameMetadata metadata = await BuildMetadataAsync(appId, ct);

		// The rebuild failed (offline or missing API key), so keep serving the previous cache
		if (string.IsNullOrWhiteSpace(metadata.Name))
			return staleMetadata ?? metadata;

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

		// The Store API is more reliable than the schema's gameName, and provides the header image
		SteamGameMetadata? basic = await GetBasicMetadataAsync(appId, ct);
		if (basic is not null)
		{
			if (!string.IsNullOrWhiteSpace(basic.Name))
				metadata.Name = basic.Name;
			metadata.HeaderImageUri = basic.HeaderImageUri;
		}

		// Fall back to the CDN header image
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
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Writes the disk cache of a game, ignoring I/O failures since the cache is only an optimization.
	/// </summary>
	private static async Task WriteDiskCacheAsync(
		string appId,
		SteamGameMetadata metadata,
		CancellationToken ct = default)
	{
		try
		{
			string json = JsonSerializer.Serialize(metadata, AppJsonContext.Default.SteamGameMetadata);
			await File.WriteAllTextAsync(GetCacheFilePath(appId), json, ct);
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

		try
		{
			string url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json";
			string response = await _http.GetStringAsync(url, ct);
			using JsonDocument doc = JsonDocument.Parse(response);

			JsonElement achievements = doc.RootElement
				.GetProperty("achievementpercentages")
				.GetProperty("achievements");

			foreach (JsonElement ach in achievements.EnumerateArray())
			{
				string name = ach.GetProperty("name").GetString() ?? "";
				JsonElement percentRaw = ach.GetProperty("percent");
				double percent = percentRaw.ValueKind == JsonValueKind.String
					? double.Parse(percentRaw.GetString()!, CultureInfo.InvariantCulture)
					: percentRaw.GetDouble();
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

		string apiKey = _settings.Settings.SteamApiKey;
		if (string.IsNullOrWhiteSpace(apiKey))
			return (gameTitle, result);

		try
		{
			string schemaUrl = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v0002/?key={apiKey}&appid={appId}&l={_settings.Settings.Language}&format=json";
			string response = await _http.GetStringAsync(schemaUrl, ct);
			using JsonDocument doc = JsonDocument.Parse(response);

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
		try
		{
			string url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
			string response = await _http.GetStringAsync(url, ct);
			using JsonDocument doc = JsonDocument.Parse(response);

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

		try
		{
			string url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={apiKey}&steamid={steamId}&include_played_free_games=true&format=json";
			string response = await _http.GetStringAsync(url, ct);
			using JsonDocument doc = JsonDocument.Parse(response);

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

		try
		{
			string url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={apiKey}&steamid={steamId}&appid={appId}&format=json";
			string response = await _http.GetStringAsync(url, ct);
			using JsonDocument doc = JsonDocument.Parse(response);

			if (doc.RootElement.TryGetProperty("playerstats", out JsonElement stats))
			{
				if (stats.TryGetProperty("achievements", out JsonElement achs))
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
		}
		catch { }

		return achievements;
	}

	/// <inheritdoc/>
	public void ClearCache()
	{
		_cache.Clear();
		try
		{
			if (Directory.Exists(CacheDir))
			{
				foreach (string file in Directory.EnumerateFiles(CacheDir, "*.json"))
					File.Delete(file);
			}
		}
		catch { }
	}

	/// <summary>
	/// Disposes the underlying <see cref="HttpClient"/>.
	/// </summary>
	public void Dispose() => _http.Dispose();
}
