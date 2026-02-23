using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NetAntenna.Core.Services;

public sealed class NominatimGeocodingService : IGeocodingService
{
    private readonly HttpClient _http;
    private readonly ILogger<NominatimGeocodingService> _logger;

    public NominatimGeocodingService(HttpClient http, ILogger<NominatimGeocodingService> logger)
    {
        _http = http;
        _logger = logger;
        
        // Nominatim requires a descriptive User-Agent
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("NetAntenna/1.0 (https://github.com/LstDtchMn/Net-Antenna)"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "NetAntenna/1.0 (https://github.com/LstDtchMn/Net-Antenna)");
        }
    }

    public async Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default)
    {
        var suggestions = await GetSuggestionsAsync(address, ct);
        if (suggestions.Count == 0) return null;
        var first = suggestions[0];
        return (first.Latitude, first.Longitude);
    }

    public async Task<List<GeocodingSuggestion>> GetSuggestionsAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            return new List<GeocodingSuggestion>();

        try
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=5&addressdetails=0&countrycodes=us";

            var responseText = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(responseText);

            var results = new List<GeocodingSuggestion>();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("display_name", out var dispProp) ? dispProp.GetString() ?? "" : "";
                if (!item.TryGetProperty("lat", out var latProp) || !item.TryGetProperty("lon", out var lonProp)) continue;
                if (!double.TryParse(latProp.GetString(), out var lat) || !double.TryParse(lonProp.GetString(), out var lon)) continue;
                results.Add(new GeocodingSuggestion(name, lat, lon));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while getting suggestions for: {Query}", query);
            return new List<GeocodingSuggestion>();
        }
    }
}
