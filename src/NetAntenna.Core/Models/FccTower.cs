namespace NetAntenna.Core.Models;

/// <summary>
/// Represents a broadcast TV tower parsed from the FCC LMS database.
/// Used for RF prediction, map plotting, and antenna aiming.
/// </summary>
public record FccTower
{
    /// <summary>
    /// The FCC designated facility ID (e.g., 60556)
    /// </summary>
    public int FacilityId { get; init; }

    /// <summary>
    /// The broadcast call sign (e.g., WABC-TV)
    /// </summary>
    public string CallSign { get; init; } = string.Empty;

    /// <summary>
    /// Physical transmission channel (e.g., 7) 
    /// Note: This is NOT the "virtual" guide number (e.g., 7.1)
    /// </summary>
    public int TransmitChannel { get; init; }

    /// <summary>
    /// Transmitter Latitude (WGS84, Decimal Degrees)
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Transmitter Longitude (WGS84, Decimal Degrees)
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Effective Radiated Power (ERP) in kW
    /// Important for calculating expected signal strength at a distance.
    /// </summary>
    public double ErpKw { get; init; }

    /// <summary>
    /// Height Above Average Terrain (HAAT) in meters
    /// </summary>
    public double HaatMeters { get; init; }

    /// <summary>
    /// True if the signal is ATSC 3.0 (NextGen TV)
    /// </summary>
    public bool IsNextGenTv { get; init; }

    /// <summary>
    /// The broadcast service type (e.g., "DT" for Digital TV, "DD" for Distributed Transmission System)
    /// </summary>
    public string ServiceType { get; init; } = string.Empty;
}
