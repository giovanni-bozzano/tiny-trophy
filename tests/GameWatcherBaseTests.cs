using TinyTrophy.Infrastructure.Watchers;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class GameWatcherBaseTests
{
	private sealed class TestWatcher
		: GameWatcherBase
	{
		public int ChangedCount { get; private set; }

		protected override AchievementSource Source => AchievementSource.SteamEmulator;

		public TestWatcher() => AchievementsChanged += (_, _) => ChangedCount++;

		/// <summary>
		/// Runs inside <see cref="Start"/>, so a test can change what a restart discovers.
		/// </summary>
		public Action? Initialize { get; set; }

		public int DetectionCount;
		public TaskCompletionSource<bool>? Detected { get; set; }

		protected override void InitializeKnownState() => Initialize?.Invoke();

		protected override void SetupFileWatchers() { }

		protected override Task DetectNewAchievementsAsync(string key)
		{
			Interlocked.Increment(ref DetectionCount);
			Detected?.TrySetResult(true);
			return Task.CompletedTask;
		}

		public void Schedule(string key) => ScheduleDetection(key);

		public bool Mark(string key) => MarkGamePresent(key);

		public List<Achievement> Diff(
			string key,
			List<Achievement> currentUnlocked,
			bool reportChange = true) => DiffAndUpdate(key, currentUnlocked, reportChange);

		public void Seed(
			string key,
			params string[] unlockedIds) =>
			SetKnownUnlocks(key, new HashSet<string>(unlockedIds, StringComparer.OrdinalIgnoreCase));

		public void Remove(string key) => MarkGameRemoved(key);

		public void Raise(
			string key,
			Achievement achievement) => RaiseAchievementUnlocked(key, achievement);
	}

	private static List<Achievement> Unlocked(params string[] ids) =>
		[.. ids.Select(id => new Achievement { Id = id, IsUnlocked = true })];

	[Fact]
	public void MarkGamePresent_ReportsOnlyTheFirstTimeItIsSeen()
	{
		TestWatcher watcher = new();

		Assert.True(watcher.Mark("480"));
		Assert.False(watcher.Mark("480"));

		Assert.Equal(1, watcher.ChangedCount);
	}

	[Fact]
	public void DiffAndUpdate_ReportsEachNewUnlockOnce()
	{
		TestWatcher watcher = new();

		List<Achievement> first = watcher.Diff("480", Unlocked("A"));
		List<Achievement> second = watcher.Diff("480", Unlocked("A"));
		List<Achievement> third = watcher.Diff("480", Unlocked("A", "B"));

		Assert.Equal(new[] { "A" }, first.Select(a => a.Id));
		Assert.Empty(second);
		Assert.Equal(new[] { "B" }, third.Select(a => a.Id));
		Assert.Equal(2, watcher.ChangedCount);
	}

	[Fact]
	public void DiffAndUpdate_WithReportChangeDisabled_StillReturnsNewUnlocks()
	{
		TestWatcher watcher = new();

		List<Achievement> newUnlocks = watcher.Diff("480", Unlocked("A"), reportChange: false);

		Assert.Equal(new[] { "A" }, newUnlocks.Select(a => a.Id));
		Assert.Equal(0, watcher.ChangedCount);
	}

	[Fact]
	public void DiffAndUpdate_DoesNotReportAchievementsSeededAsAlreadyUnlocked()
	{
		TestWatcher watcher = new();
		watcher.Seed("480", "A");

		List<Achievement> newUnlocks = watcher.Diff("480", Unlocked("A", "B"));

		Assert.Equal(new[] { "B" }, newUnlocks.Select(a => a.Id));
		Assert.Equal(1, watcher.ChangedCount);
	}

	[Fact]
	public void DiffAndUpdate_ReportsWhenAnUnlockDisappears()
	{
		TestWatcher watcher = new();
		watcher.Seed("480", "A");

		List<Achievement> newUnlocks = watcher.Diff("480", []);

		Assert.Empty(newUnlocks);
		Assert.Equal(1, watcher.ChangedCount);
	}

	[Fact]
	public void Start_DoesNotReportTheInitialSeed()
	{
		TestWatcher watcher = new();
		watcher.Initialize = () => watcher.Seed("480");

		watcher.Start();

		Assert.Equal(0, watcher.ChangedCount);
		Assert.Equal((0, 0), watcher.LastRestartChanges);
	}

	[Fact]
	public void Start_AfterARestart_ReportsGamesThatAppearedAndDisappeared()
	{
		TestWatcher watcher = new();
		watcher.Initialize = () => watcher.Seed("480");
		watcher.Start();

		watcher.Initialize = () => watcher.Seed("481");
		watcher.Start();

		Assert.Equal((1, 1), watcher.LastRestartChanges);
		Assert.Equal(2, watcher.ChangedCount);
	}

	[Fact]
	public void Start_WithReportChangesDisabled_StillComputesTheChangeCounts()
	{
		TestWatcher watcher = new();
		watcher.Initialize = () => watcher.Seed("480");
		watcher.Start();

		watcher.Initialize = () => watcher.Seed("481");
		watcher.Start(reportChanges: false);

		Assert.Equal((1, 1), watcher.LastRestartChanges);
		Assert.Equal(0, watcher.ChangedCount);
	}

	[Fact]
	public void MarkGameRemoved_ReportsAKnownGameOnce()
	{
		TestWatcher watcher = new();
		watcher.Mark("480");

		watcher.Remove("480");
		watcher.Remove("480");

		// One event for appearing, one for disappearing
		Assert.Equal(2, watcher.ChangedCount);
	}

	[Fact]
	public void MarkGameRemoved_ForAnUnknownGame_DoesNotReport()
	{
		TestWatcher watcher = new();

		watcher.Remove("480");

		Assert.Equal(0, watcher.ChangedCount);
	}

	[Fact]
	public void DiffAndUpdate_AfterStop_IsIgnored()
	{
		TestWatcher watcher = new();
		watcher.Seed("480", "A");
		watcher.Stop();

		List<Achievement> newUnlocks = watcher.Diff("480", Unlocked("B"));

		Assert.Empty(newUnlocks);
		Assert.Equal(0, watcher.ChangedCount);
	}

	[Fact]
	public void MarkGamePresent_AfterStop_IsIgnored()
	{
		TestWatcher watcher = new();
		watcher.Stop();

		Assert.False(watcher.Mark("480"));
		Assert.Equal(0, watcher.ChangedCount);
	}

	[Fact]
	public void RaiseAchievementUnlocked_PublishesTheGameAndAchievement()
	{
		TestWatcher watcher = new();
		Achievement achievement = new() { Id = "A", Name = "First" };
		AchievementUnlockedEventArgs? received = null;
		watcher.AchievementUnlocked += (_, args) => received = args;

		watcher.Raise("480", achievement);

		Assert.NotNull(received);
		Assert.Equal("480", received!.AppId);
		Assert.Same(achievement, received.Achievement);
	}

	[Fact]
	public void RaiseAchievementUnlocked_AfterStop_IsIgnored()
	{
		TestWatcher watcher = new();
		int raised = 0;
		watcher.AchievementUnlocked += (_, _) => raised++;
		watcher.Stop();

		watcher.Raise("480", new Achievement { Id = "A" });

		Assert.Equal(0, raised);
	}

	[Fact]
	public async Task ScheduleDetection_CollapsesRapidEventsIntoOneRun()
	{
		using TestWatcher watcher = new();
		TaskCompletionSource<bool> detected = new(TaskCreationOptions.RunContinuationsAsynchronously);
		watcher.Detected = detected;

		watcher.Schedule("480");
		await Task.Delay(100);
		watcher.Schedule("480");

		await detected.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.Delay(300);

		Assert.Equal(1, watcher.DetectionCount);
	}

	[Fact]
	public void DiffAndUpdate_ComparesIdsCaseInsensitively()
	{
		TestWatcher watcher = new();
		watcher.Seed("480", "ach_one");

		List<Achievement> newUnlocks = watcher.Diff("480", Unlocked("ACH_ONE"));

		Assert.Empty(newUnlocks);
	}
}
