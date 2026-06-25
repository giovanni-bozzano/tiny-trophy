using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TinyTrophy.ViewModels;

public sealed partial class InitialSetupViewModel(Action<string?, string?> onComplete)
	: ObservableObject
{
	[ObservableProperty]
	public partial string ApiKey { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SteamId { get; set; } = string.Empty;

	[RelayCommand]
	private async Task SaveAsync()
	{
		string key = ApiKey.Trim();
		string id = SteamId.Trim();
		onComplete(key, string.IsNullOrWhiteSpace(id) ? null : id);
	}
}
