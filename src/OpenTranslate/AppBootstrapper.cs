using Hardcodet.Wpf.TaskbarNotification;
using OpenTranslate.Models;
using OpenTranslate.Services;
using OpenTranslate.ViewModels;
using OpenTranslate.Views;

namespace OpenTranslate;

public sealed class AppBootstrapper : IDisposable
{
    private const string MutexName = "Global\\OpenTranslate_SingleInstance";

    private readonly Mutex? _mutex;
    private readonly bool _initialized;
    private readonly SecureSettingsStore? _settingsStore;
    private readonly TranslationClient? _translationClient;
    private readonly ModelCatalogService? _modelCatalog;
    private readonly ClipboardService? _clipboardService;
    private readonly KeyboardHookService? _keyboardHookService;
    private readonly TranslationOrchestrator? _translationOrchestrator;
    private readonly WindowsStartupService? _startupService;
    private readonly TrayIconService? _trayService;
    private SettingsWindow? _settingsWindow;

    public AppBootstrapper()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "OpenTranslate is already running.",
                "OpenTranslate",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            System.Windows.Application.Current.Shutdown();
            return;
        }

        _initialized = true;
        _settingsStore = new SecureSettingsStore();
        _translationClient = new TranslationClient();
        _modelCatalog = new ModelCatalogService();
        _clipboardService = new ClipboardService();
        _keyboardHookService = new KeyboardHookService();
        _startupService = new WindowsStartupService();
        _translationOrchestrator = new TranslationOrchestrator(
            _settingsStore,
            _translationClient,
            _clipboardService,
            _keyboardHookService);

        _translationOrchestrator.StatusChanged += OnStatusChanged;
        _translationOrchestrator.TranslationFailed += OnTranslationFailed;

        _keyboardHookService.ShortcutActivated += OnShortcutActivated;

        _trayService = new TrayIconService(
            OpenSettings,
            () => _translationOrchestrator!.TranslateClipboardAsync(),
            ShutdownApplication);

        ApplyShortcutFromSettings(_settingsStore.Load());

        var startupSettings = _settingsStore.Load();
        if (TranslationProviders.RequiresApiKey(startupSettings.Provider)
            && string.IsNullOrWhiteSpace(startupSettings.GetActiveApiKey()))
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(OpenSettings);
        }
    }

    private void OnShortcutActivated(object? sender, ShortcutActivatedEventArgs e)
    {
        _ = _translationOrchestrator!.TranslateClipboardAsync(
            fromShortcut: true,
            targetWindow: e.TargetWindow,
            targetControl: e.TargetControl,
            clipboardAlreadyUpdated: e.ClipboardAlreadyUpdated);
    }

    private void OnStatusChanged(object? sender, string message) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _trayService!.SetStatus(message));

    private void OnTranslationFailed(object? sender, string message) =>
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            _trayService!.ShowBalloon("OpenTranslate", message, BalloonIcon.Error));

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(
            _settingsStore!,
            _translationClient!,
            _modelCatalog!,
            _startupService!);

        viewModel.SettingsSaved += (_, _) =>
            ApplyShortcutFromSettings(_settingsStore!.Load());

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplyShortcutFromSettings(AppSettings settings) =>
        _keyboardHookService!.UpdateShortcut(settings.ActivationShortcut);

    private void ShutdownApplication() => System.Windows.Application.Current.Shutdown();

    public void Dispose()
    {
        if (!_initialized)
        {
            _mutex?.Dispose();
            return;
        }

        _keyboardHookService!.ShortcutActivated -= OnShortcutActivated;
        _translationOrchestrator!.StatusChanged -= OnStatusChanged;
        _translationOrchestrator.TranslationFailed -= OnTranslationFailed;

        _keyboardHookService.Dispose();
        _trayService!.Dispose();
        _translationClient!.Dispose();
        _modelCatalog!.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
