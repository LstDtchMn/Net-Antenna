using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

// ---------------------------------------------------------------------------
// Sweep profile — controls how many passes the sweeper runs
// ---------------------------------------------------------------------------

public enum SweepMode
{
    /// <summary>Stop after a fixed number of full passes.</summary>
    FixedCount,
    /// <summary>Stop after a wall-clock duration.</summary>
    TimeLimited,
    /// <summary>Run until the user presses Stop.</summary>
    Indefinite
}

public sealed class SweepProfile
{
    public SweepMode Mode { get; init; } = SweepMode.FixedCount;
    /// <summary>Number of passes (used when Mode == FixedCount).</summary>
    public int MaxRuns { get; init; } = 1;
    /// <summary>Total duration (used when Mode == TimeLimited).</summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(5);
}

// ---------------------------------------------------------------------------
// Per-run snapshot
// ---------------------------------------------------------------------------

public sealed class SweepRunResult
{
    public int RunNumber { get; init; }
    public long StartMs { get; init; }
    public long EndMs { get; init; }
    public IReadOnlyList<ChannelStatistics> Results { get; init; } = Array.Empty<ChannelStatistics>();
}

// ---------------------------------------------------------------------------
// Live status broadcast
// ---------------------------------------------------------------------------

public sealed class SweeperStatus
{
    public bool IsSweeping { get; init; }

    // Current pass progress
    public int TotalChannels { get; init; }
    public int ChannelsScanned { get; init; }

    // Multi-run progress
    public int CurrentRun { get; init; }
    public int? TargetRuns { get; init; }   // null = time-limited or indefinite

    // Live aggregated results across all completed runs so far
    public IReadOnlyList<ChannelStatistics> LiveResults { get; init; } = Array.Empty<ChannelStatistics>();

    // One entry per completed pass — used to draw the time-series chart
    public IReadOnlyList<SweepRunResult> CompletedRuns { get; init; } = Array.Empty<SweepRunResult>();
}

// ---------------------------------------------------------------------------
// Service interface
// ---------------------------------------------------------------------------

/// <summary>
/// Performs active diagnostic channel sweeps using selected tuners,
/// supporting single and multi-pass aggregation modes.
/// </summary>
public interface ITunerSweeperService
{
    event EventHandler<SweeperStatus>? StatusChanged;

    SweeperStatus CurrentStatus { get; }

    /// <summary>Starts an active channel sweep, locking the specified tuners.</summary>
    Task StartSweepAsync(
        string deviceId,
        IReadOnlyList<int> tunerIndices,
        TimeSpan dwellTime,
        SweepProfile profile);

    /// <summary>Cancels an ongoing sweep.</summary>
    void CancelSweep();
}
