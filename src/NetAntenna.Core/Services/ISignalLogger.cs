using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// Background service that continuously polls HDHomeRun tuners and logs signal data.
/// </summary>
public interface ISignalLogger
{
    /// <summary>
    /// Raised each time a new signal sample is recorded.
    /// Subscribe to this for real-time UI updates.
    /// </summary>
    event EventHandler<SignalSample>? SignalSampleReceived;

    /// <summary>
    /// Start continuous polling for a specific device.
    /// </summary>
    Task StartAsync(string deviceId, string baseUrl, int tunerCount,
        TimeSpan interval, CancellationToken ct = default);

    /// <summary>
    /// Stop all active polling.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Whether the logger is currently running.
    /// </summary>
    bool IsRunning { get; }
}
