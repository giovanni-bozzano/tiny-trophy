using System.Text.Json.Serialization;
using TinyTrophy.Models;

namespace TinyTrophy;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SteamGameMetadata))]
[JsonSerializable(typeof(SteamAchievementSchema))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubReleaseAsset))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AppJsonContext
	: JsonSerializerContext;
