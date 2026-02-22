namespace NetAntenna.Core.Services;

public interface IGeocodingService
{
    /// <summary>
    /// Converts a freeform address string into latitude and longitude coordinates.
    /// Returns null if the address could not be found.
    /// </summary>
    Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default);
}
