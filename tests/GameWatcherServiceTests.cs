using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Watchers;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class GameWatcherServiceTests
{
	private sealed class FakeWatcher
		: IGameWatcher
	{
		public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
		public event EventHandler<GameChangedEventArgs>? AchievementsChanged;

		public int StartCalls;
		public int StopCalls;
		public int DisposeCalls;
		public bool? LastReportChanges;
		public (int Added, int Removed) Changes { get; set; }

		public (int Added, int Removed) LastRestartChanges => Changes;

		public void Start(bool reportChanges = true)
		{
			StartCalls++;
			LastReportChanges = reportChanges;
		}

		public void Stop() => StopCalls++;

		public void Dispose() => DisposeCalls++;

		public void RaiseUnlocked(string appId) =>
			AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs(appId, new Achievement()));

		public void RaiseChanged(string gameId) =>
			AchievementsChanged?.Invoke(this, new GameChangedEventArgs(AchievementSource.SteamEmulator, gameId));
	}

	private static (GameWatcherService Service, FakeWatcher First, FakeWatcher Second) Create()
	{
		FakeWatcher first = new() { Changes = (1, 2) };
		FakeWatcher second = new() { Changes = (3, 0) };
		return (new GameWatcherService([first, second]), first, second);
	}

	[Fact]
	public void Restart_SumsTheChangesOfEveryWatcher()
	{
		(GameWatcherService service, _, _) = Create();

		(int Added, int Removed) changes = service.Restart();

		Assert.Equal((4, 2), changes);
	}

	[Fact]
	public void Restart_ForwardsReportChangesToEveryWatcher()
	{
		(GameWatcherService service, FakeWatcher first, FakeWatcher second) = Create();

		service.Restart(reportChanges: false);

		Assert.False(first.LastReportChanges);
		Assert.False(second.LastReportChanges);
	}

	[Fact]
	public void Restart_StopsEveryWatcherBeforeStartingThemAgain()
	{
		(GameWatcherService service, FakeWatcher first, FakeWatcher second) = Create();

		service.Restart();

		Assert.Equal(1, first.StopCalls);
		Assert.Equal(1, second.StopCalls);
		Assert.Equal(1, first.StartCalls);
		Assert.Equal(1, second.StartCalls);
	}

	[Fact]
	public void Start_ReportsChangesByDefault()
	{
		(GameWatcherService service, FakeWatcher first, _) = Create();

		service.Start();

		Assert.True(first.LastReportChanges);
	}

	[Fact]
	public void Stop_StopsEveryWatcher()
	{
		(GameWatcherService service, FakeWatcher first, FakeWatcher second) = Create();

		service.Stop();

		Assert.Equal(1, first.StopCalls);
		Assert.Equal(1, second.StopCalls);
	}

	[Fact]
	public void Dispose_DisposesEveryWatcher()
	{
		(GameWatcherService service, FakeWatcher first, FakeWatcher second) = Create();

		service.Dispose();

		Assert.Equal(1, first.DisposeCalls);
		Assert.Equal(1, second.DisposeCalls);
	}

	[Fact]
	public void Start_ForwardsUnlockAndChangeEvents()
	{
		(GameWatcherService service, FakeWatcher first, FakeWatcher second) = Create();
		List<(string AppId, Achievement Achievement)> unlocked = [];
		List<(AchievementSource Source, string GameId)> changed = [];
		service.AchievementUnlocked += (_, e) => unlocked.Add((e.AppId, e.Achievement));
		service.AchievementsChanged += (_, e) => changed.Add((e.Source, e.GameId));

		service.Start();
		first.RaiseUnlocked("480");
		second.RaiseUnlocked("730");
		first.RaiseChanged("480");

		Assert.Equal(new[] { "480", "730" }, unlocked.Select(u => u.AppId));
		Assert.Single(changed);
		Assert.Equal("480", changed[0].GameId);
	}

	[Fact]
	public void Start_CalledTwice_DoesNotDuplicateEvents()
	{
		(GameWatcherService service, FakeWatcher first, _) = Create();
		int raised = 0;
		service.AchievementUnlocked += (_, _) => raised++;

		service.Start();
		service.Start();
		first.RaiseUnlocked("480");

		Assert.Equal(1, raised);
	}

	[Fact]
	public void Restart_WithoutWatchers_ReturnsNoChanges()
	{
		using GameWatcherService service = new([]);

		Assert.Equal((0, 0), service.Restart());
	}
}
