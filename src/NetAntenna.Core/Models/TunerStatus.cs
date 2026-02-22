namespace NetAntenna.Core.Models;

/// <summary>
/// Represents the live status of a single HDHomeRun tuner, parsed from /tuner{n}/status.json (JSON, FLEX 4K)
/// or /tuner{n}/status (legacy key=value format).
/// NOTE: The HDHomeRun HTTP API uses Ch frequency for TUNING (/tunerN/ch473000000)
/// but returns modulation-prefixed notation in status responses (e.g. Ch = "8vsb:473000000").
/// </summary>
public sealed class TunerStatus
{
    /// <summary>Currently locked channel as reported by device (e.g., "8vsb:473000000").</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Actual modulation lock type (e.g., "8vsb", "qam256", "none").</summary>
    public string Lock { get; set; } = string.Empty;

    /// <summary>Signal Strength (0-100). Raw power level at antenna.</summary>
    public int SignalStrength { get; set; }

    /// <summary>Signal-to-Noise Quality (0-100). Digital clarity after demodulation.</summary>
    public int SignalToNoiseQuality { get; set; }

    /// <summary>Symbol Quality (0-100). % of symbols decoded correctly. THE critical metric.</summary>
    public int SymbolQuality { get; set; }

    /// <summary>Bits per second of the stream.</summary>
    public long BitsPerSecond { get; set; }

    /// <summary>Packets per second of the stream.</summary>
    public long PacketsPerSecond { get; set; }

    /// <summary>Whether the tuner has an active signal lock.</summary>
    public bool IsLocked => !string.IsNullOrEmpty(Lock) &&
                            !Lock.Equals("none", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parse a key=value status response from the HDHomeRun tuner.
    /// Example input:
    /// ch=8vsb:7
    /// lock=8vsb
    /// ss=85
    /// snq=92
    /// seq=100
    /// bps=19392712
    /// pps=2412
    /// </summary>
    public static TunerStatus Parse(string statusText)
    {
        var status = new TunerStatus();

        if (string.IsNullOrWhiteSpace(statusText))
            return status;

        foreach (var line in statusText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex < 0) continue;

            var key = trimmed[..equalsIndex].Trim();
            var value = trimmed[(equalsIndex + 1)..].Trim();

            switch (key)
            {
                case "ch":
                    status.Channel = value;
                    break;
                case "lock":
                    status.Lock = value;
                    break;
                case "ss":
                    if (int.TryParse(value, out var ss)) status.SignalStrength = ss;
                    break;
                case "snq":
                    if (int.TryParse(value, out var snq)) status.SignalToNoiseQuality = snq;
                    break;
                case "seq":
                    if (int.TryParse(value, out var seq)) status.SymbolQuality = seq;
                    break;
                case "bps":
                    if (long.TryParse(value, out var bps)) status.BitsPerSecond = bps;
                    break;
                case "pps":
                    if (long.TryParse(value, out var pps)) status.PacketsPerSecond = pps;
                    break;
            }
        }

        return status;
    }
}
