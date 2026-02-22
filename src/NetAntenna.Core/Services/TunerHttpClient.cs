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

    /// <inheritdoc />
    public async Task<TunerStatus> GetTunerStatusAsync(
        string baseUrl, int tunerIndex, CancellationToken ct = default)
    {
        try
        {
            var jsonUrl = $"{NormalizeUrl(baseUrl)}/tuner{tunerIndex}/status.json";
            var doc = await _http.GetFromJsonAsync<JsonDocument>(jsonUrl, ct);
            if (doc != null && doc.RootElement.ValueKind != JsonValueKind.Null)
            {
                var root = doc.RootElement;
                return new TunerStatus
                {
                    Channel = root.TryGetProperty("VctNumber", out var vct) ? vct.GetString() ?? "" : "",
                    Lock = root.TryGetProperty("Lock", out var lck) ? lck.GetString() ?? "none" : "none",
                    SignalStrength = root.TryGetProperty("SignalStrength", out var ss) ? ss.GetInt32() : 0,
                    SignalToNoiseQuality = root.TryGetProperty("SignalToNoiseQuality", out var snq) ? snq.GetInt32() : 0,
                    SymbolQuality = root.TryGetProperty("SymbolQuality", out var seq) ? seq.GetInt32() : 0,
                    BitsPerSecond = root.TryGetProperty("Bitrate", out var bps) ? bps.GetInt64() : 0,
                    PacketsPerSecond = 0
                };
            }
        }
        catch (HttpRequestException)
        {
            // Some models might only support the old line-based /status endpoint
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
        // To set a channel via HTTP: POST /tuner{n}/channel (or /vchannel for virtual)
        // Body: channel=8vsb:14
        var url = $"{NormalizeUrl(baseUrl)}/tuner{tunerIndex}/channel";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["channel"] = channel
        });

        using var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
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
