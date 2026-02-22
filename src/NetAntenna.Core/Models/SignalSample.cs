namespace NetAntenna.Core.Models;

/// <summary>
/// A single timestamped signal reading from an HDHomeRun tuner.
/// Stored in the signal_samples SQLite table.
/// </summary>
public sealed class SignalSample
{
    public long Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public int TunerIndex { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string LockType { get; set; } = string.Empty;
    public int Ss { get; set; }
    public int Snq { get; set; }
    public int Seq { get; set; }
    public long Bps { get; set; }
    public long Pps { get; set; }
    public long TimestampUnixMs { get; set; }

    /// <summary>
    /// Create a SignalSample from a TunerStatus reading.
    /// </summary>
    public static SignalSample FromTunerStatus(
        string deviceId, int tunerIndex, TunerStatus status)
    {
        return new SignalSample
        {
            DeviceId = deviceId,
            TunerIndex = tunerIndex,
            Channel = status.Channel,
            LockType = status.Lock,
            Ss = status.SignalStrength,
            Snq = status.SignalToNoiseQuality,
            Seq = status.SymbolQuality,
            Bps = status.BitsPerSecond,
            Pps = status.PacketsPerSecond,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
