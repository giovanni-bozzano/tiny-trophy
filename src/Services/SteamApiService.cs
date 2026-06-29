using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Services;

public enum ApiKeyValidationResult
{
	Valid,
	Invalid,
	Unreachable
}

public static class ApiKeyWarningMessages
{
	public const string Invalid = "Steam API key is invalid. Metadata and official achievements may not load. Check your key in Settings.";
	public const string Unreachable = "Could not reach Steam servers. You may be offline. Some features may not work.";

	public static string FromResult(ApiKeyValidationResult result) => result switch
	{
		ApiKeyValidationResult.Invalid => Invalid,
		ApiKeyValidationResult.Unreachable => Unreachable,
		_ => string.Empty
	};
}

public interface ISteamApiService
{
	Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey, CancellationToken ct = default);
	Task<SteamGameMetadata?> GetSteamGameMetadataAsync(string appId, CancellationToken ct = default);
	Task<Dictionary<string, double>> GetGlobalAchievementStatsAsync(string appId, CancellationToken ct = default);
	Task<List<string>> GetOwnedGamesAsync(string steamId, CancellationToken ct = default);
	Task<List<Achievement>> GetPlayerAchievementsAsync(string steamId, string appId, CancellationToken ct = default);
	void ClearCache();
	/// <summary>
	/// Frees the in-memory metadata cache to reduce idle memory usage.
	/// The disk cache is kept so future lookups can still read from it.
	/// </summary>
	void ReleaseMemoryCache();
	/// <summary>
	/// True if the last scan encountered a Steam privacy error (profile or game details not public).
	/// </summary>
	bool PrivacyErrorDetected { get; }
	void ResetPrivacyFlag();
}

public sealed class SteamApiService
	: ISteamApiService
	, IDisposable
{
	private readonly HttpClient _http;
	private readonly ISettingsService _settings;
	private readonly ConcurrentDictionary<string, SteamGameMetadata> _cache = new();
	private volatile bool _privacyErrorDetected;
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

	public async Task<SteamGameMetadata?> GetSteamGameMetadataAsync(
		string appId,
		CancellationToken ct = default)
	{
		if (_cache.TryGetValue(appId, out SteamGameMetadata? cached))
			return cached;

		// Check disk cache
		string cacheFile = Path.Combine(CacheDir, $"{appId}.json");
		if (File.Exists(cacheFile))
		{
			try
			{
				string cacheJson = await File.ReadAllTextAsync(cacheFile, ct);
				SteamGameMetadata? cacheMetadata = JsonSerializer.Deserialize(cacheJson, AppJsonContext.Default.SteamGameMetadata);
				if (cacheMetadata is not null)
				{
						Dictionary<string, SteamAchievementSchema> cacheLookup = new(cacheMetadata.Achievements, StringComparer.OrdinalIgnoreCase);
						Dictionary<string, double> backfill = await GetGlobalAchievementStatsAsync(appId, ct);
						foreach ((string? id, double pct) in backfill)
						{
							if (cacheLookup.TryGetValue(id, out SteamAchievementSchema? m))
								m.GlobalPercentage = pct;
							else
								cacheMetadata.Achievements[id] = new SteamAchievementSchema { Id = id, Name = id, GlobalPercentage = pct };
						}
						try
						{
							await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(cacheMetadata, AppJsonContext.Default.SteamGameMetadata), ct);
						}
						catch { }

					_cache[appId] = cacheMetadata;
					return cacheMetadata;
				}
			}
			catch { }
		}

		SteamGameMetadata metadata = new();

		// Fetch the game's achievement schema from Steam
		string apiKey = _settings.Settings.SteamApiKey;
		if (!string.IsNullOrWhiteSpace(apiKey))
		{
			try
			{
				string schemaUrl = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v0002/?key={apiKey}&appid={appId}&l={_settings.Settings.Language}&format=json";
				string response = await _http.GetStringAsync(schemaUrl, ct);
				using JsonDocument doc = JsonDocument.Parse(response);

				JsonElement game = doc.RootElement.GetProperty("game");
				if (game.TryGetProperty("gameName", out JsonElement gameName))
					metadata.Name = gameName.GetString() ?? string.Empty;

				if (game.TryGetProperty("availableGameStats", out JsonElement stats) &&
					stats.TryGetProperty("achievements", out JsonElement achievements))
				{
					foreach (JsonElement ach in achievements.EnumerateArray())
					{
						string name = ach.GetProperty("name").GetString() ?? "";
						metadata.Achievements[name] = new SteamAchievementSchema
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
		}

		// Use the Store API for name and image (more reliable than the schema's gameName)
		SteamGameMetadata? basic = await GetBasicMetadataAsync(appId, ct);
		if (basic is not null)
		{
			if (!string.IsNullOrWhiteSpace(basic.Name))
				metadata.Name = basic.Name;
			if (string.IsNullOrWhiteSpace(metadata.HeaderImageUri))
				metadata.HeaderImageUri = basic.HeaderImageUri;
		}

		// Fall back to the CDN header image
		if (string.IsNullOrWhiteSpace(metadata.HeaderImageUri))
			metadata.HeaderImageUri = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

		// Include global achievement percentages in the cached metadata
		Dictionary<string, double> globalStats = await GetGlobalAchievementStatsAsync(appId, ct);
		Dictionary<string, SteamAchievementSchema> achLookup = new(metadata.Achievements, StringComparer.OrdinalIgnoreCase);
		foreach ((string? id, double pct) in globalStats)
		{
			if (achLookup.TryGetValue(id, out SteamAchievementSchema? m))
				m.GlobalPercentage = pct;
			else
				metadata.Achievements[id] = new SteamAchievementSchema { Id = id, Name = id, GlobalPercentage = pct };
		}

		// Save to disk and memory cache
		if (!string.IsNullOrWhiteSpace(metadata.Name))
		{
			try
			{
				string json = JsonSerializer.Serialize(metadata, AppJsonContext.Default.SteamGameMetadata);
				await File.WriteAllTextAsync(cacheFile, json, ct);
			}
			catch { }

			_cache[appId] = metadata;
		}

		return metadata;
	}

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
				else if (stats.TryGetProperty("success", out JsonElement successEl) &&
						 !successEl.GetBoolean() &&
						 stats.TryGetProperty("error", out JsonElement errorEl))
				{
					string err = errorEl.GetString() ?? string.Empty;
					if (err.Contains("not public", StringComparison.OrdinalIgnoreCase) ||
						err.Contains("not publicly", StringComparison.OrdinalIgnoreCase))
					{
						_privacyErrorDetected = true;
					}
				}
			}
		}
		catch { }

		return achievements;
	}

	public bool PrivacyErrorDetected => _privacyErrorDetected;

	public void ResetPrivacyFlag() => _privacyErrorDetected = false;

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

	public void ReleaseMemoryCache() => _cache.Clear();

	public void Dispose() => _http.Dispose();
}
