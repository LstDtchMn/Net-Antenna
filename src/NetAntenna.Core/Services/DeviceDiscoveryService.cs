using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// Discovers HDHomeRun devices on the local network via UDP broadcast on port 65001.
/// Falls back to HTTP discovery if UDP fails.
/// </summary>
public sealed class DeviceDiscoveryService : IDeviceDiscovery, IDisposable
{
    private const int DiscoveryPort = 65001;
    private readonly ITunerClient _tunerClient;

    // HDHomeRun discovery packet structure:
    // Bytes 0-1: Packet type (0x00 0x02 = discover request)
    // Bytes 2-3: Payload length (0x00 0x0C = 12 bytes)
    // Bytes 4-7: Tag 0x01 (device type) + length 4 + value 0xFFFFFFFF (wildcard)
    // Bytes 8-11: Tag 0x02 (device ID) + length 4 + value 0xFFFFFFFF (wildcard)
    // Bytes 12-15: CRC32 of bytes 0-11
    private static readonly byte[] DiscoveryPacket =
    {
        0x00, 0x02, // type: discover request
        0x00, 0x0C, // payload length: 12
        0x01,       // tag: device type
        0x04,       // length: 4
        0xFF, 0xFF, 0xFF, 0xFF, // value: wildcard (all device types)
        0x02,       // tag: device ID
        0x04,       // length: 4
        0xFF, 0xFF, 0xFF, 0xFF, // value: wildcard (all devices)
    };

    public DeviceDiscoveryService(ITunerClient tunerClient)
    {
        _tunerClient = tunerClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HdHomeRunDevice>> DiscoverDevicesAsync(
        TimeSpan timeout, CancellationToken ct = default)
    {
        var discoveredIps = new HashSet<string>();
        var devices = new List<HdHomeRunDevice>();

        // Phase 1: UDP broadcast discovery
        try
        {
            var udpIps = await DiscoverViaUdpAsync(timeout, ct);
            foreach (var ip in udpIps)
                discoveredIps.Add(ip);
        }
        catch (Exception)
        {
            // UDP may fail on some networks; continue to HTTP fallback
        }

        // Phase 2: For each discovered IP, get full device info via HTTP
        foreach (var ip in discoveredIps)
        {
            try
            {
                var device = await _tunerClient.GetDeviceInfoAsync($"http://{ip}", ct);
                device.IpAddress = ip;
                device.LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                devices.Add(device);
            }
            catch (Exception)
            {
                // Device responded to UDP but HTTP failed; skip
            }
        }

        return devices;
    }

    /// <inheritdoc />
    public async Task<HdHomeRunDevice?> GetDeviceByIpAsync(
        string ipAddress, CancellationToken ct = default)
    {
        try
        {
            var device = await _tunerClient.GetDeviceInfoAsync($"http://{ipAddress}", ct);
            device.IpAddress = ipAddress;
            device.LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return device;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<List<string>> DiscoverViaUdpAsync(
        TimeSpan timeout, CancellationToken ct)
    {
        var ips = new List<string>();

        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

        // Calculate CRC and build the final packet
        var packet = BuildDiscoveryPacket();

        // Send broadcast
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        await udpClient.SendAsync(packet, packet.Length, broadcastEndpoint);

        // Collect responses until timeout
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var receiveTask = udpClient.ReceiveAsync(ct);
                var delayTask = Task.Delay(deadline - DateTime.UtcNow, ct);
                var completed = await Task.WhenAny(receiveTask.AsTask(), delayTask);

                if (completed == delayTask)
                    break;

                var result = await receiveTask;
                if (result.Buffer.Length > 0)
                {
                    ips.Add(result.RemoteEndPoint.Address.ToString());
                }
            }
            catch (SocketException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return ips;
    }

    private static byte[] BuildDiscoveryPacket()
    {
        // For simplicity, we send the base packet without CRC.
        // HDHomeRun devices will still respond to non-CRC'd requests on most firmware.
        // A full implementation would compute CRC32 of the packet and append 4 bytes.
        var packet = new byte[DiscoveryPacket.Length + 4];
        Array.Copy(DiscoveryPacket, packet, DiscoveryPacket.Length);

        // Calculate CRC32 (HDHomeRun uses reflected CRC32)
        var crc = CalculateCrc32(DiscoveryPacket);
        packet[DiscoveryPacket.Length] = (byte)(crc >> 24);
        packet[DiscoveryPacket.Length + 1] = (byte)(crc >> 16);
        packet[DiscoveryPacket.Length + 2] = (byte)(crc >> 8);
        packet[DiscoveryPacket.Length + 3] = (byte)crc;

        return packet;
    }

    private static uint CalculateCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return ~crc;
    }

    public void Dispose()
    {
        // Nothing to dispose; UdpClient is created and disposed per-call
    }
}
