using System.Net;
using System.Text.Json.Serialization;

namespace NetAntenna.Core.Models;

/// <summary>
/// Represents a discovered HDHomeRun device, mapped from /discover.json.
/// </summary>
public sealed class HdHomeRunDevice
{
    [JsonPropertyName("DeviceID")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("DeviceAuth")]
    public string DeviceAuth { get; set; } = string.Empty;

    [JsonPropertyName("FriendlyName")]
    public string FriendlyName { get; set; } = string.Empty;

    [JsonPropertyName("ModelNumber")]
    public string ModelNumber { get; set; } = string.Empty;

    [JsonPropertyName("FirmwareName")]
    public string FirmwareName { get; set; } = string.Empty;

    [JsonPropertyName("FirmwareVersion")]
    public string FirmwareVersion { get; set; } = string.Empty;

    [JsonPropertyName("TunerCount")]
    public int TunerCount { get; set; }

    [JsonPropertyName("BaseURL")]
    public string BaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("LineupURL")]
    public string LineupUrl { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the device on the local network (not from JSON, set during discovery).
    /// </summary>
    [JsonIgnore]
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Last time this device was seen on the network (Unix ms).
    /// </summary>
    [JsonIgnore]
    public long LastSeenUnixMs { get; set; }

    public override string ToString() =>
        $"{FriendlyName} ({ModelNumber}) - {DeviceId} @ {IpAddress}";
}
