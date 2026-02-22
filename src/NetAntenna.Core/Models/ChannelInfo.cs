using System.Text.Json.Serialization;

namespace NetAntenna.Core.Models;

/// <summary>
/// Represents a single channel from the HDHomeRun lineup, mapped from /lineup.json.
/// </summary>
public sealed class ChannelInfo
{
    [JsonPropertyName("GuideNumber")]
    public string GuideNumber { get; set; } = string.Empty;

    [JsonPropertyName("GuideName")]
    public string GuideName { get; set; } = string.Empty;

    [JsonPropertyName("URL")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("Tags")]
    public string? Tags { get; set; }

    // Local-only fields (not from JSON, managed by the app)

    /// <summary>Whether the user has marked this channel as a favorite.</summary>
    [JsonIgnore]
    public bool IsFavorite { get; set; }

    /// <summary>Whether the channel is hidden on the tuner.</summary>
    [JsonIgnore]
    public bool IsHidden { get; set; }

    /// <summary>Last recorded Signal Strength.</summary>
    [JsonIgnore]
    public int? LastSs { get; set; }

    /// <summary>Last recorded Signal-to-Noise Quality.</summary>
    [JsonIgnore]
    public int? LastSnq { get; set; }

    /// <summary>Last recorded Symbol Quality.</summary>
    [JsonIgnore]
    public int? LastSeq { get; set; }

    /// <summary>Last time signal data was updated (Unix ms).</summary>
    [JsonIgnore]
    public long? LastUpdatedUnixMs { get; set; }

    public override string ToString() => $"{GuideNumber} {GuideName}";
}
