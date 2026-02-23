namespace NetAntenna.Core.Services;

/// <summary>Represents a geocoding suggestion with display label and coordinates.</summary>
public record GeocodingSuggestion(string DisplayName, double Latitude, double Longitude);

public interface IGeocodingService
{
    /// <summary>Converts a freeform address into lat/lng. Returns null if not found.</summary>
    Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default);

    /// <summary>Returns up to 5 autocomplete suggestions for a partial address query.</summary>
    Task<List<GeocodingSuggestion>> GetSuggestionsAsync(string query, CancellationToken ct = default);
}
