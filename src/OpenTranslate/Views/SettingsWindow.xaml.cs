using System.Windows;
using OpenTranslate.ViewModels;

namespace OpenTranslate.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _isSyncingPassword;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        SyncPasswordBoxFromViewModel();

    private void SyncPasswordBoxFromViewModel()
    {
        _isSyncingPassword = true;
        ApiKeyBox.Password = _viewModel.ApiKey;
        _isSyncingPassword = false;
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPassword)
            return;

        _viewModel.ApiKey = ApiKeyBox.Password;
    }

    private void OnChangeShortcut(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CapturedShortcut is not null)
            _viewModel.ApplyCapturedShortcut(dialog.CapturedShortcut);
    }
}
