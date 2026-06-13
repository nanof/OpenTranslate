namespace OpenTranslate.Models;

public sealed class ModelPerformanceInfo
{
    public required string Headline { get; init; }
    public double? ThroughputTokensPerSecond { get; init; }
    public int? TimeToFirstTokenMs { get; init; }
    public bool Recommended { get; init; }
    public bool Deprecated { get; init; }

    private string BuildMetrics()
    {
        var parts = new List<string>();
        if (ThroughputTokensPerSecond is { } tps)
            parts.Add($"~{tps:0} tok/s");
        if (TimeToFirstTokenMs is { } ttft)
            parts.Add($"~{ttft} ms to first token");
        return parts.Count > 0 ? string.Join(" · ", parts) : "";
    }

    public string ToHint()
    {
        var metrics = BuildMetrics();
        return metrics.Length > 0 ? $"{Headline} ({metrics})" : Headline;
    }

    public string ToBenchmarkSuffix()
    {
        var metrics = BuildMetrics();
        return metrics.Length > 0 ? $" Benchmark: {metrics}." : "";
    }
}
