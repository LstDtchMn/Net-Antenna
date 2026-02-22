namespace NetAntenna.Core.Services;

/// <summary>
/// Service to fetch local weather conditions from the National Weather Service (NWS) API.
/// https://weather-gov.github.io/api/general-faqs
/// </summary>
public interface INwsWeatherService
{
    /// <summary>
    /// Gets the current short weather description (e.g., "Heavy Rain", "Clear")
    /// for the given coordinates. Returns null if unavailable or outside US.
    /// </summary>
    Task<string?> GetCurrentConditionsAsync(double latitude, double longitude, CancellationToken ct = default);
}
