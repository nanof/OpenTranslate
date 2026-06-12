using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTranslate.Models;
using OpenTranslate.Services;

namespace OpenTranslate.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SecureSettingsStore _settingsStore;
    private readonly TranslationClient _translationClient;
    private readonly WindowsStartupService _startupService;
    private TranslationProvider _previousProvider = TranslationProvider.OpenRouter;

    [ObservableProperty]
    private TranslationProvider _provider = TranslationProvider.OpenRouter;

    [ObservableProperty]
    private string _apiKeyLabel = TranslationProviders.GetApiKeyLabel(TranslationProvider.OpenRouter);

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _model = AppSettings.DefaultOpenRouterModel;

    [ObservableProperty]
    private string _sourceLanguage = "es";

    [ObservableProperty]
    private string _targetLanguage = "en";

    [ObservableProperty]
    private IReadOnlyList<TranslationLanguage> _availableLanguages =
        TranslationLanguages.Supported;

    [ObservableProperty]
    private string _autoDetectLanguageLabel = "Auto-detect language";

    [ObservableProperty]
    private bool _autoDetectLanguage;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _playSoundOnTranslationStart;

    [ObservableProperty]
    private double _tooltipFontSize = AppSettings.DefaultTooltipFontSize;

    [ObservableProperty]
    private string _shortcutDisplay = ShortcutFormatter.Format(ActivationShortcut.Default);

    [ObservableProperty]
    private bool _shortcutDoublePress = true;

    [ObservableProperty]
    private ActivationShortcut _activationShortcut = ActivationShortcut.Default;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<TranslationProviderOption> AvailableProviders => TranslationProviders.Options;

    public event EventHandler? SettingsSaved;

    public SettingsViewModel(
        SecureSettingsStore settingsStore,
        TranslationClient translationClient,
        WindowsStartupService startupService)
    {
        _settingsStore = settingsStore;
        _translationClient = translationClient;
        _startupService = startupService;
        LoadFromStore();
    }

    public void LoadFromStore()
    {
        var settings = _settingsStore.Load();
        Provider = settings.Provider;
        _previousProvider = settings.Provider;
        ApiKeyLabel = TranslationProviders.GetApiKeyLabel(settings.Provider);
        ApiKey = settings.ApiKey ?? "";
        Model = string.IsNullOrWhiteSpace(settings.Model)
            ? TranslationProviders.GetDefaultModel(settings.Provider)
            : settings.Model;
        SourceLanguage = settings.SourceLanguage;
        TargetLanguage = settings.TargetLanguage;
        RefreshAvailableLanguages();
        AutoDetectLanguage = settings.AutoDetectLanguage;
        UpdateAutoDetectLanguageLabel();
        StartWithWindows = settings.StartWithWindows;
        PlaySoundOnTranslationStart = settings.PlaySoundOnTranslationStart;
        TooltipFontSize = settings.TooltipFontSize is > 0
            ? settings.TooltipFontSize
            : AppSettings.DefaultTooltipFontSize;
        ActivationShortcut = settings.ActivationShortcut ?? ActivationShortcut.Default;
        ShortcutDoublePress = ActivationShortcut.DoublePress;
        UpdateShortcutDisplay();
    }

    public void ApplyCapturedShortcut(ActivationShortcut shortcut)
    {
        ActivationShortcut = shortcut;
        ActivationShortcut.DoublePress = ShortcutDoublePress;
        UpdateShortcutDisplay();
        StatusMessage = "Shortcut updated. Save to apply.";
    }

    partial void OnShortcutDoublePressChanged(bool value)
    {
        ActivationShortcut.DoublePress = value;
        UpdateShortcutDisplay();
    }

    partial void OnSourceLanguageChanged(string value)
    {
        RefreshAvailableLanguages();
        UpdateAutoDetectLanguageLabel();
    }

    partial void OnTargetLanguageChanged(string value)
    {
        RefreshAvailableLanguages();
        UpdateAutoDetectLanguageLabel();
    }

    partial void OnProviderChanged(TranslationProvider value)
    {
        ApiKeyLabel = TranslationProviders.GetApiKeyLabel(value);

        var previousDefault = TranslationProviders.GetDefaultModel(_previousProvider);
        if (string.IsNullOrWhiteSpace(Model)
            || string.Equals(Model.Trim(), previousDefault, StringComparison.Ordinal))
        {
            Model = TranslationProviders.GetDefaultModel(value);
        }

        _previousProvider = value;
    }

    private void RefreshAvailableLanguages() =>
        AvailableLanguages = TranslationLanguages.BuildOptions(SourceLanguage, TargetLanguage);

    private void UpdateAutoDetectLanguageLabel()
    {
        var source = TranslationLanguages.ResolveName(SourceLanguage);
        var target = TranslationLanguages.ResolveName(TargetLanguage);
        AutoDetectLanguageLabel = $"Auto-detect language ({source} ↔ {target})";
    }

    private void UpdateShortcutDisplay() =>
        ShortcutDisplay = ShortcutFormatter.Format(ActivationShortcut);

    [RelayCommand]
    private async Task TestTranslationAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "An API key is required to test.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing translation…";

        try
        {
            var settings = BuildSettingsFromViewModel();
            var result = await _translationClient.TranslateAsync("Hello world", settings);
            StatusMessage = $"Connection OK. Example: {result}";
        }
        catch (TranslationApiException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(SourceLanguage) || string.IsNullOrWhiteSpace(TargetLanguage))
        {
            StatusMessage = "Specify source and target language.";
            return;
        }

        if (string.Equals(SourceLanguage.Trim(), TargetLanguage.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Source and target language must be different.";
            return;
        }

        if (!ActivationShortcut.IsValid)
        {
            StatusMessage = "Configure a valid keyboard shortcut.";
            return;
        }

        if (TooltipFontSize is < 8 or > 36)
        {
            StatusMessage = "Tooltip font size must be between 8 and 36.";
            return;
        }

        IsBusy = true;

        var settings = BuildSettingsFromViewModel();
        _settingsStore.Save(settings);

        try
        {
            _startupService.Apply(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Settings saved, but Windows startup failed: {ex.Message}";
            IsBusy = false;
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            return;
        }

        StatusMessage = "Settings saved.";
        IsBusy = false;
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings BuildSettingsFromViewModel()
    {
        ActivationShortcut.DoublePress = ShortcutDoublePress;

        return new AppSettings
        {
            Provider = Provider,
            ApiKey = ApiKey.Trim(),
            Model = string.IsNullOrWhiteSpace(Model)
                ? TranslationProviders.GetDefaultModel(Provider)
                : Model.Trim(),
            SourceLanguage = string.IsNullOrWhiteSpace(SourceLanguage) ? "es" : SourceLanguage.Trim(),
            TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "en" : TargetLanguage.Trim(),
            AutoDetectLanguage = AutoDetectLanguage,
            StartWithWindows = StartWithWindows,
            PlaySoundOnTranslationStart = PlaySoundOnTranslationStart,
            TooltipFontSize = TooltipFontSize,
            ActivationShortcut = ActivationShortcut
        };
    }
}
