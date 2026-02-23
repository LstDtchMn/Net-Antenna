using System.Net.Http.Json;
using System.Text.Json;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// HTTP client for communicating with HDHomeRun device REST endpoints.
/// All calls have a 5-second timeout to prevent UI freezes on unreachable devices.
/// </summary>
public sealed class TunerHttpClient : ITunerClient, IDisposable
{
    private readonly HttpClient _http;

    public TunerHttpClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <inheritdoc />
    public async Task<HdHomeRunDevice> GetDeviceInfoAsync(
        string baseUrl, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(baseUrl)}/discover.json";
        var device = await _http.GetFromJsonAsync<HdHomeRunDevice>(url, ct)
            ?? throw new InvalidOperationException($"No response from {url}");

        // Extract IP from the base URL for convenience
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            device.IpAddress = uri.Host;

        device.LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return device;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChannelInfo>> GetLineupAsync(
        string baseUrl, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(baseUrl)}/lineup.json";
        var channels = await _http.GetFromJsonAsync<List<ChannelInfo>>(url, ct)
            ?? new List<ChannelInfo>();
        return channels;
    }

    public async Task<TunerStatus> GetTunerStatusAsync(
        string baseUrl, int tunerIndex, CancellationToken ct = default)
    {
        try
        {
            // Modern HDHomeRun (FLEX, SCRIBE, CONNECT) returns all tuners in a single JSON array
            var jsonUrl = $"{NormalizeUrl(baseUrl)}/status.json";
            var doc = await _http.GetFromJsonAsync<JsonDocument>(jsonUrl, ct);
            
            if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var targetResource = $"tuner{tunerIndex}";
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("Resource", out var res) && res.GetString() == targetResource)
                    {
                        var status = new TunerStatus
                        {
                            Channel = element.TryGetProperty("VctNumber", out var vct) ? vct.GetString() ?? "" : "",
                            SignalStrength = element.TryGetProperty("SignalStrengthPercent", out var ss) ? ss.GetInt32() : 0,
                            SignalToNoiseQuality = element.TryGetProperty("SignalQualityPercent", out var snq) ? snq.GetInt32() : 0,
                            SymbolQuality = element.TryGetProperty("SymbolQualityPercent", out var seq) ? seq.GetInt32() : 0,
                            BitsPerSecond = element.TryGetProperty("NetworkRate", out var bps) ? bps.GetInt64() : 0,
                            PacketsPerSecond = 0
                        };
                        
                        // Modern API drops the 'Lock' property entirely when streaming, but includes 'VctNumber' and 'Frequency'
                        status.Lock = (!string.IsNullOrEmpty(status.Channel) || element.TryGetProperty("Frequency", out _)) 
                            ? "locked" : "none";
                            
                        return status;
                    }
                }
            }
        }
        catch (HttpRequestException)
        {
            // Some older models might only support the legacy line-based /status endpoint
            try
            {
                var statusUrl = $"{NormalizeUrl(baseUrl)}/tuner{tunerIndex}/status";
                var text = await _http.GetStringAsync(statusUrl, ct);
                return TunerStatus.Parse(text);
            }
            catch (Exception)
            {
                // Fallback completely
            }
        }
        catch (Exception)
        {
            // Fallback
        }

        return new TunerStatus();
    }

    /// <inheritdoc />
    public async Task SetChannelAsync(
        string baseUrl, int tunerIndex, string channel, CancellationToken ct = default)
    {
        // HDHomeRun has no POST "set channel" API. Tuning is done by opening a streaming
        // HTTP connection on port 5004 to /tunerN/<channel>. We send a GET and abort
        // immediately after headers arrive so the device tunes and locks.
        // The REST API (port 80) does NOT serve streaming endpoints — port 5004 does.
        if (channel == "none") return; // Nothing to do for release

        var streamBase = GetStreamingBaseUrl(baseUrl);
        var url = $"{streamBase}/tuner{tunerIndex}/{channel}";
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var tuneCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tuneCts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, tuneCts.Token);
            // 200 = tuner is now streaming (i.e. locked). We drop the connection immediately.
            // Any non-200 means the channel doesn't exist or is off-air — that's fine.
        }
        catch (OperationCanceledException)
        {
            // Expected — we cancelled the stream intentionally after headers arrived.
        }
    }

    /// <summary>
    /// Converts a device REST base URL (port 80) to the HDHomeRun streaming base URL (port 5004).
    /// e.g. http://192.168.2.240 → http://192.168.2.240:5004
    /// </summary>
    private static string GetStreamingBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(NormalizeUrl(baseUrl), UriKind.Absolute, out var uri))
            return baseUrl;

        var builder = new UriBuilder(uri) { Port = 5004 };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    /// <inheritdoc />
    public async Task SetChannelVisibilityAsync(
        string baseUrl, string guideNumber, bool visible, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(baseUrl)}/lineup.post";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["favorite"] = guideNumber,
            // HDHomeRun uses "x" to hide and "-" to show in some firmware versions
            // The exact POST parameter depends on firmware; this covers common cases
        });

        // The lineup.post endpoint uses form-encoded POST data
        // To hide: POST /lineup.post?hide=<channel>
        // To show: POST /lineup.post?hide=-<channel>
        var hideUrl = visible
            ? $"{NormalizeUrl(baseUrl)}/lineup.post?show={guideNumber}"
            : $"{NormalizeUrl(baseUrl)}/lineup.post?hide={guideNumber}";

        using var request = new HttpRequestMessage(HttpMethod.Post, hideUrl);
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task SetChannelFavoriteAsync(
        string baseUrl, string guideNumber, bool favorite, CancellationToken ct = default)
    {
        var favoriteUrl = favorite
            ? $"{NormalizeUrl(baseUrl)}/lineup.post?favorite={guideNumber}"
            : $"{NormalizeUrl(baseUrl)}/lineup.post?favorite=-{guideNumber}";

        using var request = new HttpRequestMessage(HttpMethod.Post, favoriteUrl);
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task StartLineupScanAsync(string baseUrl, CancellationToken ct = default)
    {
        // POST /lineup.post?action=scan starts a full RF channel scan on the device.
        // The device rebuilds its entire lineup; this can take 1-2 minutes.
        var url = $"{NormalizeUrl(baseUrl)}/lineup.post?action=scan";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        var response = await _http.SendAsync(request, ct);
        // Some firmware returns 200 OK, others return 204 No Content; both are fine.
        // Don't call EnsureSuccessStatusCode — caller will poll status independently.
    }

    /// <inheritdoc />
    public async Task<LineupScanStatus> GetLineupScanStatusAsync(string baseUrl, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(baseUrl)}/lineup_status.json";
        var status = await _http.GetFromJsonAsync<LineupScanStatus>(url, ct);
        return status ?? new LineupScanStatus();
    }

    private static string NormalizeUrl(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = $"http://{url}";
        }
        return url;
    }

    public void Dispose() => _http.Dispose();
}
