using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace OpenTranslate.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly Action _onOpenSettings;
    private readonly Func<Task> _onTranslateNow;
    private readonly Action _onExit;

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
            ToolTipText = "OpenTranslate — listo"
        };

        var translateItem = new MenuItem { Header = "Traducir portapapeles ahora" };
        translateItem.Click += async (_, _) => await _onTranslateNow();

        var settingsItem = new MenuItem { Header = "Configuración…" };
        settingsItem.Click += (_, _) => _onOpenSettings();

        var exitItem = new MenuItem { Header = "Salir" };
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

    public void Dispose()
    {
        _taskbarIcon.ContextMenu = null;
        _taskbarIcon.Dispose();
    }
}
