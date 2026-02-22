using System.Text.Json.Serialization;

namespace NetAntenna.Core.Models;

/// <summary>
/// Represents the progress of an in-progress or completed HDHomeRun device-level channel scan.
/// Parsed from GET /lineup_status.json
/// </summary>
public sealed class LineupScanStatus
{
    /// <summary>1 while a scan is in progress, 0 when idle.</summary>
    [JsonPropertyName("ScanInProgress")]
    public int ScanInProgress { get; set; }

    /// <summary>0-100 scan completion percentage.</summary>
    [JsonPropertyName("Progress")]
    public int Progress { get; set; }

    /// <summary>Number of channels found so far during the scan.</summary>
    [JsonPropertyName("Found")]
    public int Found { get; set; }

    [JsonIgnore]
    public bool IsScanning => ScanInProgress == 1;
}
