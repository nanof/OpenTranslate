using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using OpenTranslate.ViewModels;

namespace OpenTranslate.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var viewModel = (SettingsViewModel)DataContext;
        viewModel.RefreshUsageSummary();
        await viewModel.LoadModelsAsync();
    }

    private void OnChangeShortcut(object sender, RoutedEventArgs e)
    {
        var viewModel = (SettingsViewModel)DataContext;
        var dialog = new HotkeyCaptureDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CapturedShortcut is not null)
            viewModel.ApplyCapturedShortcut(dialog.CapturedShortcut);
    }

    private void OnOpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
