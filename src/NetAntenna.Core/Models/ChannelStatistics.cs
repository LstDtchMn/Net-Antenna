namespace NetAntenna.Core.Models;

/// <summary>
/// Aggregated statistics for a specific channel over a time period.
/// Used for signal profiling and the Active Signal Sweeper.
/// </summary>
public sealed class ChannelStatistics
{
    public string Channel { get; init; } = string.Empty;
    public double AvgSs { get; init; }
    public double MinSs { get; init; }
    public double MaxSs { get; init; }
    public double AvgSnq { get; init; }
    public double MinSnq { get; init; }
    public double MaxSnq { get; init; }
    public double AvgSeq { get; init; }
    public double MinSeq { get; init; }
    public double MaxSeq { get; init; }
    public int SampleCount { get; init; }
}
