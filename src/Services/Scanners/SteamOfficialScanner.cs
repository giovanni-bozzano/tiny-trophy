using TinyTrophy.Models;

namespace TinyTrophy.Services.Scanners;

/// <summary>
/// Fetches the user's official Steam achievements via the Steam Web API.
/// Requires both a Steam API key and a Steam ID to be configured.
/// </summary>
public sealed class SteamOfficialScanner(
	ISettingsService settings,
	ISteamApiService steamApi)
	: IAchievementScanner
{
	public AchievementSource Source => AchievementSource.SteamOfficial;
	public string DisplayName => "Steam library";

	public async Task<IReadOnlyList<Game>> ParseAsync(
		IProgress<(int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		if (!settings.Settings.SteamOfficialEnabled)
			return [];

		string steamId = settings.Settings.SteamId;
		if (string.IsNullOrWhiteSpace(settings.Settings.SteamApiKey) || string.IsNullOrWhiteSpace(steamId))
			return [];

		List<string> appIds = await steamApi.GetOwnedGamesAsync(steamId, ct);
		if (appIds.Count == 0)
			return [];

		int total = appIds.Count;
		int completed = 0;
		List<Game> games = [];
		using SemaphoreSlim semaphore = new(5);

		IEnumerable<Task> tasks = appIds.Select(async appId =>
		{
			await semaphore.WaitAsync(ct);
			try
			{
				List<Achievement> achievements = await steamApi.GetPlayerAchievementsAsync(steamId, appId, ct);

				int done = Interlocked.Increment(ref completed);
				progress?.Report((done, total));

				if (achievements.Count == 0)
					return;

				lock (games)
				{
					games.Add(new Game
					{
						AppId = appId,
						Name = $"AppID: {appId}",
						Source = AchievementSource.SteamOfficial,
						Achievements = achievements
					});
				}
			}
			finally
			{
				semaphore.Release();
			}
		});

		await Task.WhenAll(tasks);

		return games;
	}
}
