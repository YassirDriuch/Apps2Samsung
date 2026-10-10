using Microsoft.Extensions.DependencyInjection;

namespace Apps2Samsung.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// The pages hide the navigation bar and draw their own header, and on iOS a ContentPage is
		// edge to edge by default, so the content would sit under the status bar / Dynamic Island
		// and the home indicator. Keep every page inside the safe area (and above the keyboard).
		if (DeviceInfo.Current.Platform == DevicePlatform.iOS)
			PageAppearing += (_, page) =>
			{
				if (page is ContentPage content)
					content.SafeAreaEdges = SafeAreaEdges.All;
			};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Resolve the first page from the DI container so its Core services are injected.
		var services = IPlatformApplication.Current!.Services;
		return new Window(new NavigationPage(services.GetRequiredService<Pages.InstallerPage>()));
	}
}