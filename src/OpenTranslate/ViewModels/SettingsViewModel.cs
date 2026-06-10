using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTranslate.Models;
using OpenTranslate.Services;

namespace OpenTranslate.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SecureSettingsStore _settingsStore;
    private readonly OpenRouterTranslationClient _translationClient;
    private readonly WindowsStartupService _startupService;

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _model = AppSettings.DefaultModel;

    [ObservableProperty]
    private string _sourceLanguage = "es";

    [ObservableProperty]
    private string _targetLanguage = "en";

    [ObservableProperty]
    private IReadOnlyList<TranslationLanguage> _availableLanguages =
        TranslationLanguages.Supported;

    [ObservableProperty]
    private string _autoDetectLanguageLabel = "Detectar idioma automáticamente";

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

    public event EventHandler? SettingsSaved;

    public SettingsViewModel(
        SecureSettingsStore settingsStore,
        OpenRouterTranslationClient translationClient,
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
        ApiKey = settings.ApiKey ?? "";
        Model = string.IsNullOrWhiteSpace(settings.Model) ? AppSettings.DefaultModel : settings.Model;
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
        StatusMessage = "Atajo actualizado. Guarda para aplicarlo.";
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

    private void RefreshAvailableLanguages() =>
        AvailableLanguages = TranslationLanguages.BuildOptions(SourceLanguage, TargetLanguage);

    private void UpdateAutoDetectLanguageLabel()
    {
        var source = TranslationLanguages.ResolveName(SourceLanguage);
        var target = TranslationLanguages.ResolveName(TargetLanguage);
        AutoDetectLanguageLabel = $"Detectar idioma automáticamente ({source} ↔ {target})";
    }

    private void UpdateShortcutDisplay() =>
        ShortcutDisplay = ShortcutFormatter.Format(ActivationShortcut);

    [RelayCommand]
    private async Task TestTranslationAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "La API key es obligatoria para probar.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Probando traducción…";

        try
        {
            var settings = BuildSettingsFromViewModel();
            var result = await _translationClient.TranslateAsync("Hola mundo", settings);
            StatusMessage = $"Conexión correcta. Ejemplo: {result}";
        }
        catch (OpenRouterApiException ex)
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
            StatusMessage = "Indica idioma origen y destino.";
            return;
        }

        if (string.Equals(SourceLanguage.Trim(), TargetLanguage.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "El idioma origen y destino deben ser distintos.";
            return;
        }

        if (!ActivationShortcut.IsValid)
        {
            StatusMessage = "Configura un atajo de teclado válido.";
            return;
        }

        if (TooltipFontSize is < 8 or > 36)
        {
            StatusMessage = "El tamaño de fuente del tooltip debe estar entre 8 y 36.";
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
            StatusMessage = $"Ajustes guardados, pero falló el inicio con Windows: {ex.Message}";
            IsBusy = false;
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            return;
        }

        StatusMessage = "Ajustes guardados.";
        IsBusy = false;
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings BuildSettingsFromViewModel()
    {
        ActivationShortcut.DoublePress = ShortcutDoublePress;

        return new AppSettings
        {
            ApiKey = ApiKey.Trim(),
            Model = string.IsNullOrWhiteSpace(Model) ? AppSettings.DefaultModel : Model.Trim(),
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
