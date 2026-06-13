using System.Globalization;
using OpenTranslate.Models;

namespace OpenTranslate.Services;

public sealed class UsageTrackingService
{
    private readonly UsageStatsStore _store;
    private readonly object _sync = new();

    public UsageTrackingService(UsageStatsStore store) => _store = store;

    public void RecordTranslation(string sourceText, string translatedText)
    {
        if (string.IsNullOrEmpty(sourceText) && string.IsNullOrEmpty(translatedText))
            return;

        lock (_sync)
        {
            var stats = _store.Load();
            EnsureCurrentPeriods(stats, DateTime.Now);

            var inputChars = sourceText.Length;
            var outputChars = translatedText.Length;

            stats.DayInputChars += inputChars;
            stats.DayOutputChars += outputChars;
            stats.DayTranslations++;

            stats.MonthInputChars += inputChars;
            stats.MonthOutputChars += outputChars;
            stats.MonthTranslations++;

            _store.Save(stats);
        }
    }

    public UsageSummary GetSummary() =>
        UsageSummary.FromStats(LoadCurrentStats(), DateTime.Now);

    public string FormatSummary(UsageSummary summary)
    {
        var culture = CultureInfo.CurrentCulture;
        var dayChars = summary.DayTotalChars.ToString("N0", culture);
        var dayTokens = summary.DayEstimatedTokens.ToString("N0", culture);
        var monthChars = summary.MonthTotalChars.ToString("N0", culture);
        var monthTokens = summary.MonthEstimatedTokens.ToString("N0", culture);

        return
            $"Today: {summary.DayTranslations} translations · {dayChars} chars · ~{dayTokens} tokens\n" +
            $"This month: {summary.MonthTranslations} translations · {monthChars} chars · ~{monthTokens} tokens";
    }

    public void Reset()
    {
        lock (_sync)
        {
            var stats = new UsageStats();
            EnsureCurrentPeriods(stats, DateTime.Now);
            _store.Save(stats);
        }
    }

    private UsageStats LoadCurrentStats()
    {
        lock (_sync)
        {
            var stats = _store.Load();
            EnsureCurrentPeriods(stats, DateTime.Now);
            return stats;
        }
    }

    private static void EnsureCurrentPeriods(UsageStats stats, DateTime now)
    {
        var dayKey = now.ToString("yyyy-MM-dd");
        var monthKey = now.ToString("yyyy-MM");

        if (!string.Equals(stats.DayKey, dayKey, StringComparison.Ordinal))
        {
            stats.DayKey = dayKey;
            stats.DayInputChars = 0;
            stats.DayOutputChars = 0;
            stats.DayTranslations = 0;
        }

        if (!string.Equals(stats.MonthKey, monthKey, StringComparison.Ordinal))
        {
            stats.MonthKey = monthKey;
            stats.MonthInputChars = 0;
            stats.MonthOutputChars = 0;
            stats.MonthTranslations = 0;
        }
    }
}
