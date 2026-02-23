using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// Engine for predicting RF signal strength based on distance, frequency, and transmitter power.
/// </summary>
public interface IRfPredictionEngine
{
    /// <summary>
    /// Calculates the predicted receive power in dBm given the transmitter and receiver locations.
    /// Does not account for terrain occlusion (Phase 2 constraint), assumes Line-of-Sight.
    /// </summary>
    double CalculatePredictedPowerDbm(FccTower tower, double rxLat, double rxLon);

    /// <summary>
    /// Calculates the distance in kilometers between two coordinates using the Haversine formula.
    /// </summary>
    double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2);

    /// <summary>
    /// Calculates the initial bearing (azimuth) from point 1 to point 2 in degrees (0 = North).
    /// </summary>
    double CalculateBearingDegrees(double lat1, double lon1, double lat2, double lon2);

    /// <summary>
    /// Calculates the maximum theoretical distance in kilometers at which a target receive power (dBm) is achieved, assuming free space path loss.
    /// </summary>
    double CalculateContourDistanceKm(FccTower tower, double targetRxPowerDbm);
}
