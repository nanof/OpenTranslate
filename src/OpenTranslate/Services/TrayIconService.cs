using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly Action _onOpenSettings;
    private readonly Func<Task> _onTranslateNow;
    private readonly Action _onExit;
    private MenuItem? _downloadUpdateItem;
    private Separator? _downloadUpdateSeparator;
    private string? _downloadUpdateUrl;

    public TrayIconService(
        Action onOpenSettings,
        Func<Task> onTranslateNow,
        Action onExit)
    {
        _onOpenSettings = onOpenSettings;
        _onTranslateNow = onTranslateNow;
        _onExit = onExit;

        _taskbarIcon = new TaskbarIcon
        {
            Icon = AppIconHelper.GetAppIcon(),
            ToolTipText = "OpenTranslate — ready"
        };

        var translateItem = new MenuItem { Header = "Translate clipboard now" };
        translateItem.Click += async (_, _) => await _onTranslateNow();

        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => _onOpenSettings();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => _onExit();

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(translateItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        _taskbarIcon.ContextMenu = contextMenu;
        _taskbarIcon.TrayMouseDoubleClick += (_, _) => _onOpenSettings();
    }

    public void SetStatus(string message)
    {
        _taskbarIcon.ToolTipText = string.IsNullOrWhiteSpace(message)
            ? "OpenTranslate"
            : $"OpenTranslate — {message}";
    }

    public void ShowBalloon(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        _taskbarIcon.ShowBalloonTip(title, message, icon);
    }

    public void SetUpdateAvailable(UpdateInfo? update)
    {
        var menu = _taskbarIcon.ContextMenu;
        if (menu is null)
            return;

        if (update is null)
        {
            if (_downloadUpdateItem is not null)
                menu.Items.Remove(_downloadUpdateItem);

            if (_downloadUpdateSeparator is not null)
                menu.Items.Remove(_downloadUpdateSeparator);

            _downloadUpdateItem = null;
            _downloadUpdateSeparator = null;
            _downloadUpdateUrl = null;
            return;
        }

        _downloadUpdateUrl = update.DownloadUrl;

        if (_downloadUpdateItem is null)
        {
            _downloadUpdateItem = new MenuItem();
            _downloadUpdateItem.Click += OnDownloadUpdateClick;
            _downloadUpdateSeparator = new Separator();
            menu.Items.Insert(0, _downloadUpdateItem);
            menu.Items.Insert(1, _downloadUpdateSeparator);
        }

        _downloadUpdateItem.Header = $"Download v{update.Version}…";
    }

    private void OnDownloadUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_downloadUpdateUrl))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = _downloadUpdateUrl,
            UseShellExecute = true
        });
    }

    public void Dispose()
    {
        if (_downloadUpdateItem is not null)
            _downloadUpdateItem.Click -= OnDownloadUpdateClick;

        _taskbarIcon.ContextMenu = null;
        _taskbarIcon.Dispose();
    }
}
