using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetAntenna.Core.Services;

public class NwsWeatherService : INwsWeatherService
{
    private readonly HttpClient _httpClient;

    public NwsWeatherService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // NWS API requires a User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "NetAntenna/1.0 (github.com/LstDtchMn/Net-Antenna)");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/geo+json");
    }

    public async Task<string?> GetCurrentConditionsAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Get the grid endpoint for the coordinates
            var pointUrl = $"https://api.weather.gov/points/{Math.Round(latitude, 4)},{Math.Round(longitude, 4)}";
            var pointResponse = await _httpClient.GetAsync(pointUrl, ct);
            
            if (!pointResponse.IsSuccessStatusCode)
                return null; // Not in the US or API error

            var pointJson = await pointResponse.Content.ReadAsStringAsync(ct);
            var pointData = JsonNode.Parse(pointJson);

            // Extract the observations stations URL
            var stationsUrl = pointData?["properties"]?["observationStations"]?.GetValue<string>();
            if (string.IsNullOrEmpty(stationsUrl))
                return null;

            // Step 2: Get the list of observation stations
            var stationsResponse = await _httpClient.GetAsync(stationsUrl, ct);
            if (!stationsResponse.IsSuccessStatusCode)
                return null;

            var stationsJson = await stationsResponse.Content.ReadAsStringAsync(ct);
            var stationsData = JsonNode.Parse(stationsJson);
            
            // Get the first station ID
            var stationId = stationsData?["features"]?[0]?["properties"]?["stationIdentifier"]?.GetValue<string>();
            if (string.IsNullOrEmpty(stationId))
                return null;

            // Step 3: Get the latest observation from the closest station
            var obsUrl = $"https://api.weather.gov/stations/{stationId}/observations/latest";
            var obsResponse = await _httpClient.GetAsync(obsUrl, ct);
            if (!obsResponse.IsSuccessStatusCode)
                return null;

            var obsJson = await obsResponse.Content.ReadAsStringAsync(ct);
            var obsData = JsonNode.Parse(obsJson);

            // e.g., "Light Rain", "Clear", "Overcast"
            return obsData?["properties"]?["textDescription"]?.GetValue<string>();
        }
        catch
        {
            // NWS API can be flaky, silently fail for weather overlay
            return null;
        }
    }
}
