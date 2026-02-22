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
        if (string.IsNullOrWhiteSpace(address)) return null;

        try
        {
            var encodedAddress = Uri.EscapeDataString(address);
            var url = $"https://nominatim.openstreetmap.org/search?q={encodedAddress}&format=json&limit=1";

            var responseText = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(responseText);
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var firstResult = doc.RootElement[0];
                
                if (firstResult.TryGetProperty("lat", out var latProp) && 
                    firstResult.TryGetProperty("lon", out var lonProp))
                {
                    if (double.TryParse(latProp.GetString(), out var lat) && 
                        double.TryParse(lonProp.GetString(), out var lon))
                    {
                        return (lat, lon);
                    }
                }
            }
            
            _logger.LogWarning("Geocoding failed to find coordinates for address: {Address}", address);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while geocoding address: {Address}", address);
            return null;
        }
    }
}
