namespace NetAntenna.Core.Models;

/// <summary>
/// Represents a discrete run of the active signal sweeper.
/// Acts as a parent container for all SignalSamples collected during that timeframe.
/// </summary>
public sealed class SweepSession
{
    public long Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public long StartTimeUnixMs { get; set; }
    public long EndTimeUnixMs { get; set; }
    
    /// <summary>
    /// e.g. FixedCount, TimeLimited, Indefinite
    /// </summary>
    public string Mode { get; set; } = string.Empty;
    
    public int TargetRuns { get; set; }
    public int CompletedRuns { get; set; }
}
