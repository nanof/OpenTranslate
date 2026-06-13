namespace OpenTranslate.Models;

// Latency / throughput figures are indicative, sourced from public benchmarks
// (Artificial Analysis, provider docs) as of mid-2026. For short clipboard
// translations the perceived latency is dominated by network + time-to-first-token,
// so treat throughput (tok/s) as a relative guide rather than an exact promise.
public static class ModelPerformanceCatalog
{
    public static ModelPerformanceInfo? GetInfo(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        var id = modelId.Trim().ToLowerInvariant();
        var slash = id.LastIndexOf('/');
        if (slash >= 0)
            id = id[(slash + 1)..];

        if (id.StartsWith("gemini-2.0") || id.StartsWith("gemini-1.5") || id.StartsWith("gemini-1.0"))
        {
            return new ModelPerformanceInfo
            {
                Headline = "Discontinued by Google — calls fail. Switch to gemini-3.1-flash-lite",
                Deprecated = true
            };
        }

        if (id.Contains("gemini-3.1-flash-lite") || id.Contains("gemini-3-1-flash-lite"))
        {
            return new ModelPerformanceInfo
            {
                Headline = "Very fast — recommended for translation",
                ThroughputTokensPerSecond = 363,
                Recommended = true
            };
        }

        if (id.Contains("gemini") && id.Contains("flash-lite"))
            return new ModelPerformanceInfo { Headline = "Fast, lightweight model" };

        if (id.Contains("gemini") && id.Contains("flash"))
            return new ModelPerformanceInfo { Headline = "Fast and capable (a bit slower than flash-lite)" };

        if (id.Contains("gemini") && id.Contains("pro"))
            return new ModelPerformanceInfo { Headline = "High quality but slower — overkill for short text" };

        if (id.Contains("gpt-4o-mini") || id.Contains("gpt-4.1-mini"))
        {
            return new ModelPerformanceInfo
            {
                Headline = "Fast and affordable",
                ThroughputTokensPerSecond = 80,
                TimeToFirstTokenMs = 530
            };
        }

        if (id.Contains("nano"))
            return new ModelPerformanceInfo { Headline = "Fastest OpenAI tier", Recommended = true };

        if (id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("o4"))
            return new ModelPerformanceInfo { Headline = "Reasoning model — slow, not ideal for quick translation" };

        if (id.Contains("mini"))
            return new ModelPerformanceInfo { Headline = "Fast, balanced model" };

        if (id.StartsWith("gpt") || id.StartsWith("chatgpt"))
            return new ModelPerformanceInfo { Headline = "Capable but slower — more than enough for translation" };

        if (id.Contains("haiku"))
            return new ModelPerformanceInfo { Headline = "Fast Claude tier" };

        if (id.Contains("llama"))
            return new ModelPerformanceInfo { Headline = "Speed depends on host (very fast on Groq)" };

        return null;
    }
}
