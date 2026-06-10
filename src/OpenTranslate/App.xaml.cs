namespace OpenTranslate;

public partial class App : System.Windows.Application
{
    private AppBootstrapper? _bootstrapper;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _bootstrapper = new AppBootstrapper();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _bootstrapper?.Dispose();
        base.OnExit(e);
    }
}
