using System.Reflection;

namespace OpenTranslate.Services;

public static class AppVersionHelper
{
    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string CurrentDisplay => Current.ToString(3);
}
