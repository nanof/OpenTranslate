namespace OpenTranslate.Models;

public sealed class UsageSummary
{
    public int DayTranslations { get; init; }
    public long DayTotalChars { get; init; }
    public long DayEstimatedTokens { get; init; }

    public int MonthTranslations { get; init; }
    public long MonthTotalChars { get; init; }
    public long MonthEstimatedTokens { get; init; }

    public static UsageSummary FromStats(UsageStats stats, DateTime now)
    {
        var dayKey = now.ToString("yyyy-MM-dd");
        var monthKey = now.ToString("yyyy-MM");

        var dayInput = stats.DayKey == dayKey ? stats.DayInputChars : 0;
        var dayOutput = stats.DayKey == dayKey ? stats.DayOutputChars : 0;
        var dayTranslations = stats.DayKey == dayKey ? stats.DayTranslations : 0;

        var monthInput = stats.MonthKey == monthKey ? stats.MonthInputChars : 0;
        var monthOutput = stats.MonthKey == monthKey ? stats.MonthOutputChars : 0;
        var monthTranslations = stats.MonthKey == monthKey ? stats.MonthTranslations : 0;

        return new UsageSummary
        {
            DayTranslations = dayTranslations,
            DayTotalChars = dayInput + dayOutput,
            DayEstimatedTokens = EstimateTokens(dayInput + dayOutput),
            MonthTranslations = monthTranslations,
            MonthTotalChars = monthInput + monthOutput,
            MonthEstimatedTokens = EstimateTokens(monthInput + monthOutput)
        };
    }

    public static long EstimateTokens(long totalChars) =>
        totalChars <= 0 ? 0 : (totalChars + 3) / 4;
}
