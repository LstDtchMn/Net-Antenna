using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// HTTP client for communicating with HDHomeRun device REST endpoints.
/// </summary>
public interface ITunerClient
{
    /// <summary>GET /discover.json — Pull device configuration.</summary>
    Task<HdHomeRunDevice> GetDeviceInfoAsync(
        string baseUrl, CancellationToken ct = default);

    /// <summary>GET /lineup.json — Pull the channel lineup.</summary>
    Task<IReadOnlyList<ChannelInfo>> GetLineupAsync(
        string baseUrl, CancellationToken ct = default);

    /// <summary>GET /tuner{n}/status — Poll live signal metrics (returns key=value text).</summary>
    Task<TunerStatus> GetTunerStatusAsync(
        string baseUrl, int tunerIndex, CancellationToken ct = default);

    /// <summary>POST /lineup.post — Set channel visibility (hide/show).</summary>
    Task SetChannelVisibilityAsync(
        string baseUrl, string guideNumber, bool visible, CancellationToken ct = default);

    /// <summary>
    /// Tune a specific tuner by opening a streaming GET to /tunerN/ch&lt;freq_hz&gt;.
    /// The HDHomeRun HTTP API has no POST endpoint for channel control.
    /// Pass "ch473000000" for UHF Ch14, or "ch&lt;freq&gt;" for any physical channel.
    /// Pass "none" to release the tuner (no-op in HTTP mode; simply close the stream).
    /// </summary>
    Task SetChannelAsync(
        string baseUrl, int tunerIndex, string channel, CancellationToken ct = default);

    /// <summary>POST /lineup.post — Set channel favorite status.</summary>
    Task SetChannelFavoriteAsync(
        string baseUrl, string guideNumber, bool favorite, CancellationToken ct = default);
}
