using TinyTrophy.Models;

namespace TinyTrophy.Services.Scanners;

public interface IAchievementScanner
{
	AchievementSource Source { get; }
	string DisplayName { get; }
	Task<IReadOnlyList<Game>> ParseAsync(IProgress<(int current, int total)>? progress = null);
}
