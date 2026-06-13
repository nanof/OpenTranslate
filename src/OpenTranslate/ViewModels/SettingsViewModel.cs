using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenTranslate.Models;
using OpenTranslate.Services;

namespace OpenTranslate.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SecureSettingsStore _settingsStore;
    private readonly TranslationClient _translationClient;
    private readonly ModelCatalogService _modelCatalog;
    private readonly WindowsStartupService _startupService;
    private readonly UsageTrackingService _usageTracking;
    private TranslationProvider _previousProvider = TranslationProvider.OpenRouter;
    private bool _isSyncingProvider;
    private bool _isSyncingModel;
    private bool _isSyncingApiKey;
    private Dictionary<TranslationProvider, string> _apiKeys = [];
    private ICollectionView? _filteredModelsView;
    private CancellationTokenSource? _modelLoadCts;
    private CancellationTokenSource? _apiKeyDebounceCts;

    [ObservableProperty]
    private TranslationProvider _provider = TranslationProvider.OpenRouter;

    [ObservableProperty]
    private TranslationProviderOption _selectedProvider = TranslationProviders.Options[0];

    [ObservableProperty]
    private string _apiKeyLabel = TranslationProviders.GetApiKeyLabel(TranslationProvider.OpenRouter);

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private bool _requiresApiKey = true;

    [ObservableProperty]
    private bool _supportsModelSelection = true;

    [ObservableProperty]
    private bool _supportsAutoDetect = true;

    [ObservableProperty]
    private bool _supportsImprovement = true;

    [ObservableProperty]
    private TextImprovementOption _selectedImprovement = TextImprovementModes.Options[0];

    [ObservableProperty]
    private string _model = AppSettings.DefaultOpenRouterModel;

    [ObservableProperty]
    private string _modelFilter = "";

    [ObservableProperty]
    private ModelOption? _selectedModel;

    [ObservableProperty]
    private string _modelPerformanceHint = "";

    [ObservableProperty]
    private bool _modelPerformanceIsWarning;

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
    private bool _typewriterPaste = true;

    [ObservableProperty]
    private bool _preserveFormatAndCode = true;

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
    private string _usageSummary = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isModelsLoading;

    public ObservableCollection<ModelOption> AllModels { get; } = [];

    public ICollectionView FilteredModels => _filteredModelsView
        ?? throw new InvalidOperationException("Model list has not been initialized.");

    public IReadOnlyList<TranslationProviderOption> AvailableProviders => TranslationProviders.Options;

    public IReadOnlyList<TextImprovementOption> AvailableImprovements => TextImprovementModes.SettingsOptions;

    public event EventHandler? SettingsSaved;

    public SettingsViewModel(
        SecureSettingsStore settingsStore,
        TranslationClient translationClient,
        ModelCatalogService modelCatalog,
        WindowsStartupService startupService,
        UsageTrackingService usageTracking)
    {
        _settingsStore = settingsStore;
        _translationClient = translationClient;
        _modelCatalog = modelCatalog;
        _startupService = startupService;
        _usageTracking = usageTracking;

        _filteredModelsView = CollectionViewSource.GetDefaultView(AllModels);
        _filteredModelsView.Filter = FilterModel;

        LoadFromStore();
    }

    public void LoadFromStore()
    {
        var settings = _settingsStore.Load();
        LoadApiKeysFromSettings(settings);

        _isSyncingProvider = true;
        _previousProvider = settings.Provider;
        Provider = settings.Provider;
        SelectedProvider = AvailableProviders.First(option => option.Provider == settings.Provider);
        ApiKeyLabel = TranslationProviders.GetApiKeyLabel(settings.Provider);
        RequiresApiKey = TranslationProviders.RequiresApiKey(settings.Provider);
        SupportsModelSelection = TranslationProviders.SupportsModelSelection(settings.Provider);
        SupportsAutoDetect = TranslationProviders.SupportsAutoDetect(settings.Provider);
        SupportsImprovement = TranslationProviders.SupportsImprovement(settings.Provider);
        SelectedImprovement = TextImprovementModes.FromMode(settings.ImprovementMode);
        _isSyncingApiKey = true;
        ApiKey = GetApiKeyForProvider(settings.Provider);
        _isSyncingApiKey = false;
        _isSyncingProvider = false;
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
        TypewriterPaste = settings.TypewriterPaste;
        PreserveFormatAndCode = settings.PreserveFormatAndCode;
        TooltipFontSize = settings.TooltipFontSize is > 0
            ? settings.TooltipFontSize
            : AppSettings.DefaultTooltipFontSize;
        ActivationShortcut = settings.ActivationShortcut ?? ActivationShortcut.Default;
        ShortcutDoublePress = ActivationShortcut.DoublePress;
        UpdateShortcutDisplay();
        UpdateModelPerformanceHint();
        RefreshUsageSummary();
    }

    public void RefreshUsageSummary()
    {
        var summary = _usageTracking.GetSummary();
        UsageSummary = _usageTracking.FormatSummary(summary);
    }

    [RelayCommand]
    private void ResetUsage()
    {
        _usageTracking.Reset();
        RefreshUsageSummary();
        StatusMessage = "Usage statistics reset.";
    }

    public async Task LoadModelsAsync()
    {
        _modelLoadCts?.Cancel();
        _modelLoadCts?.Dispose();
        _modelLoadCts = new CancellationTokenSource();
        var cancellationToken = _modelLoadCts.Token;

        IsModelsLoading = true;

        try
        {
            var models = await _modelCatalog
                .GetModelsAsync(Provider, ApiKey, cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
            {
                IsModelsLoading = false;
                return;
            }

            AllModels.Clear();

            var currentModel = Model?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(currentModel)
                && models.All(model => !string.Equals(model.Id, currentModel, StringComparison.OrdinalIgnoreCase)))
            {
                AllModels.Add(new ModelOption
                {
                    Id = currentModel,
                    Description = "Current selection"
                });
            }

            foreach (var model in models)
                AllModels.Add(model);

            _filteredModelsView?.Refresh();
            EnsureModelValid();
            SyncSelectedModel();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsModelsLoading = false;
        }
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

    partial void OnModelChanged(string value)
    {
        _filteredModelsView?.Refresh();
        UpdateModelPerformanceHint();
    }

    private void UpdateModelPerformanceHint()
    {
        var info = ModelPerformanceCatalog.GetInfo(Model);
        ModelPerformanceHint = info?.ToHint() ?? "";
        ModelPerformanceIsWarning = info?.Deprecated ?? false;
    }

    partial void OnModelFilterChanged(string value) => _filteredModelsView?.Refresh();

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        if (_isSyncingModel || value is null)
            return;

        if (!string.Equals(Model, value.Id, StringComparison.OrdinalIgnoreCase))
        {
            _isSyncingModel = true;
            Model = value.Id;
            _isSyncingModel = false;
        }
    }

    partial void OnSelectedProviderChanged(TranslationProviderOption value)
    {
        if (_isSyncingProvider)
            return;

        if (Provider != value.Provider)
            Provider = value.Provider;
    }

    partial void OnProviderChanged(TranslationProvider value)
    {
        if (!_isSyncingProvider)
        {
            var selected = AvailableProviders.FirstOrDefault(option => option.Provider == value);
            if (selected is not null && SelectedProvider.Provider != value)
            {
                _isSyncingProvider = true;
                SelectedProvider = selected;
                _isSyncingProvider = false;
            }
        }

        ApiKeyLabel = TranslationProviders.GetApiKeyLabel(value);
        RequiresApiKey = TranslationProviders.RequiresApiKey(value);
        SupportsModelSelection = TranslationProviders.SupportsModelSelection(value);
        SupportsAutoDetect = TranslationProviders.SupportsAutoDetect(value);
        SupportsImprovement = TranslationProviders.SupportsImprovement(value);

        if (!SupportsAutoDetect)
            AutoDetectLanguage = false;

        if (!SupportsImprovement)
            SelectedImprovement = TextImprovementModes.Options[0];

        if (!_isSyncingProvider)
        {
            PersistApiKeyForProvider(_previousProvider, ApiKey);
            _isSyncingApiKey = true;
            ApiKey = GetApiKeyForProvider(value);
            _isSyncingApiKey = false;
        }

        ModelFilter = "";

        if (!TranslationProviders.IsModelCompatibleWithProvider(Model, value))
        {
            _isSyncingModel = true;
            Model = TranslationProviders.GetDefaultModel(value);
            _isSyncingModel = false;
        }

        _previousProvider = value;
        _ = LoadModelsAsync();
    }

    partial void OnApiKeyChanged(string value)
    {
        if (_isSyncingApiKey)
            return;

        PersistApiKeyForProvider(Provider, value);

        _apiKeyDebounceCts?.Cancel();
        _apiKeyDebounceCts?.Dispose();
        _apiKeyDebounceCts = new CancellationTokenSource();
        var token = _apiKeyDebounceCts.Token;
        _ = DebounceReloadModelsAsync(token);
    }

    private async Task DebounceReloadModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(600, cancellationToken).ConfigureAwait(true);
            await LoadModelsAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LoadApiKeysFromSettings(AppSettings settings)
    {
        _apiKeys = new Dictionary<TranslationProvider, string>();
        foreach (TranslationProvider provider in Enum.GetValues<TranslationProvider>())
            _apiKeys[provider] = settings.GetApiKey(provider);
    }

    private void PersistApiKeyForProvider(TranslationProvider provider, string apiKey) =>
        _apiKeys[provider] = apiKey.Trim();

    private string GetApiKeyForProvider(TranslationProvider provider) =>
        _apiKeys.TryGetValue(provider, out var apiKey) ? apiKey : "";

    private void EnsureModelValid()
    {
        if (!string.IsNullOrWhiteSpace(Model)
            && TranslationProviders.IsModelCompatibleWithProvider(Model, Provider))
        {
            return;
        }

        _isSyncingModel = true;
        Model = TranslationProviders.GetDefaultModel(Provider);
        _isSyncingModel = false;
    }

    private void SyncSelectedModel()
    {
        var match = AllModels.FirstOrDefault(option =>
            string.Equals(option.Id, Model, StringComparison.OrdinalIgnoreCase));

        _isSyncingModel = true;
        SelectedModel = match;
        _isSyncingModel = false;
    }

    private bool FilterModel(object item)
    {
        if (item is not ModelOption model)
            return false;

        if (!string.IsNullOrWhiteSpace(Model)
            && string.Equals(model.Id, Model, StringComparison.OrdinalIgnoreCase))
            return true;

        return model.MatchesFilter(ModelFilter);
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
        if (RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "An API key is required to test.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Testing translation…";

        try
        {
            var settings = BuildSettingsFromViewModel();
            var stopwatch = Stopwatch.StartNew();
            var result = await _translationClient.TranslateAsync("Hello world", settings);
            stopwatch.Stop();
            var benchmark = ModelPerformanceCatalog.GetInfo(Model)?.ToBenchmarkSuffix() ?? "";
            StatusMessage = $"Connection OK · {stopwatch.ElapsedMilliseconds} ms round-trip.{benchmark} Example: {result}";
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

        if (SupportsModelSelection && string.IsNullOrWhiteSpace(Model))
        {
            StatusMessage = "Select a model.";
            return;
        }

        IsBusy = true;

        PersistApiKeyForProvider(Provider, ApiKey);

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
        var current = _settingsStore.Load();

        var settings = new AppSettings
        {
            Provider = Provider,
            Model = string.IsNullOrWhiteSpace(Model)
                ? TranslationProviders.GetDefaultModel(Provider)
                : Model!.Trim(),
            SourceLanguage = string.IsNullOrWhiteSpace(SourceLanguage) ? "es" : SourceLanguage.Trim(),
            TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "en" : TargetLanguage.Trim(),
            AutoDetectLanguage = AutoDetectLanguage,
            ImprovementMode = SupportsImprovement
                ? SelectedImprovement.Mode
                : TextImprovementMode.None,
            StartWithWindows = StartWithWindows,
            PlaySoundOnTranslationStart = PlaySoundOnTranslationStart,
            TypewriterPaste = TypewriterPaste,
            PreserveFormatAndCode = PreserveFormatAndCode,
            TooltipFontSize = TooltipFontSize,
            TooltipWidth = current.TooltipWidth,
            TooltipHeight = current.TooltipHeight,
            ActivationShortcut = ActivationShortcut
        };

        foreach (var (provider, apiKey) in _apiKeys)
            settings.SetApiKey(provider, apiKey);

        return settings;
    }
}
