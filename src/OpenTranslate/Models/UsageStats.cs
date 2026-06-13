namespace OpenTranslate.Models;

public sealed class UsageStats
{
    public string DayKey { get; set; } = "";
    public long DayInputChars { get; set; }
    public long DayOutputChars { get; set; }
    public int DayTranslations { get; set; }

    public string MonthKey { get; set; } = "";
    public long MonthInputChars { get; set; }
    public long MonthOutputChars { get; set; }
    public int MonthTranslations { get; set; }
}
