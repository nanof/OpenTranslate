using System.Drawing;
using System.Drawing.Drawing2D;

namespace OpenTranslate.Services;

public static class AppIconHelper
{
    private static Icon? _cachedIcon;

    public static Icon GetAppIcon()
    {
        if (_cachedIcon is not null)
            return _cachedIcon;

        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(30, 30, 46));

        using var fill = new SolidBrush(Color.FromArgb(137, 180, 250));
        graphics.FillEllipse(fill, 2, 2, size - 4, size - 4);

        using var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.DrawString("T", font, Brushes.White, 7, 5);

        _cachedIcon = Icon.FromHandle(bitmap.GetHicon());
        return _cachedIcon;
    }
}
