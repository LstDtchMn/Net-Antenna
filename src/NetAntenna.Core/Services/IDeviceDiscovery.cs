using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// Discovers HDHomeRun devices on the local network via UDP broadcast and/or HTTP.
/// </summary>
public interface IDeviceDiscovery
{
    /// <summary>
    /// Discover all HDHomeRun devices on the local network.
    /// Sends UDP broadcast to port 65001, then calls /discover.json on each respondent.
    /// </summary>
    Task<IReadOnlyList<HdHomeRunDevice>> DiscoverDevicesAsync(
        TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Get device info by directly querying a known IP address.
    /// Useful as a fallback when UDP broadcast fails (e.g., across VLANs).
    /// </summary>
    Task<HdHomeRunDevice?> GetDeviceByIpAsync(
        string ipAddress, CancellationToken ct = default);
}
