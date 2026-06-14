using System.Drawing;
using System.IO;
using System.Reflection;

namespace OpenTranslate.Services;

public static class AppIconHelper
{
    private static Icon? _cachedIcon;

    public static Icon GetAppIcon()
    {
        if (_cachedIcon is not null)
            return _cachedIcon;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            _cachedIcon = new Icon(iconPath);
            return _cachedIcon;
        }

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("OpenTranslate.Assets.app.ico");
        if (stream is not null)
        {
            _cachedIcon = new Icon(stream);
            return _cachedIcon;
        }

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            using var extracted = Icon.ExtractAssociatedIcon(exePath);
            if (extracted is not null)
            {
                _cachedIcon = (Icon)extracted.Clone();
                return _cachedIcon;
            }
        }

        throw new InvalidOperationException("Application icon not found.");
    }
}
