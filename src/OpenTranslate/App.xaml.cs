using OpenTranslate.Services;

namespace OpenTranslate;

public partial class App : System.Windows.Application
{
    private AppBootstrapper? _bootstrapper;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        AppThemeService.Instance.Initialize(new SecureSettingsStore().Load().ThemePreference);
        base.OnStartup(e);
        _bootstrapper = new AppBootstrapper();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _bootstrapper?.Dispose();
        AppThemeService.Instance.Dispose();
        base.OnExit(e);
    }
}
