using FluentAssertions;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Core.Tests.Services;

public class RfPredictionEngineTests
{
    private readonly RfPredictionEngine _engine = new();

    [Fact]
    public void CalculateDistanceKm_ReturnsCorrectDistance()
    {
        // Central Park, NYC
        var lat1 = 40.785091;
        var lon1 = -73.968285;
        
        // Empire State Building, NYC
        var lat2 = 40.748817;
        var lon2 = -73.985428;

        // Distance should be ~4.3 km
        var distance = _engine.CalculateDistanceKm(lat1, lon1, lat2, lon2);
        
        distance.Should().BeApproximately(4.3, 0.1);
    }

    [Fact]
    public void CalculateBearingDegrees_ReturnsCorrectAzimuth()
    {
        // NYC to London 
        var nycLat = 40.7128;
        var nycLon = -74.0060;
        
        var londonLat = 51.5074;
        var londonLon = -0.1278;

        // Initial bearing from NYC to London is ~51 degrees (Northeast)
        var bearing = _engine.CalculateBearingDegrees(nycLat, nycLon, londonLat, londonLon);
        
        bearing.Should().BeApproximately(51.0, 1.0);
    }

    [Fact]
    public void CalculatePredictedPowerDbm_ReturnsReasonableValues()
    {
        // A typical 1000 kW UHF tower on channel 30 (569 MHz)
        var tower = new FccTower
        {
            FacilityId = 1,
            CallSign = "WTEST",
            TransmitChannel = 30,
            Latitude = 40.0,
            Longitude = -74.0,
            ErpKw = 1000.0 // 1 MW ERP
        };

        // Receiver is exactly 10km away (Lat/Lon math rough approximation)
        // 1 deg lat is ~111km, so 10km is ~0.09 deg
        var rxLat = 40.09;
        var rxLon = -74.0;

        var powerDbm = _engine.CalculatePredictedPowerDbm(tower, rxLat, rxLon);

        // dBm = 10*log10(1,000,000) + 30 = 90 dBm TX power
        // FSPL = 20*log10(10km) + 20*log10(569MHz) + 32.44 = 20 + 55.1 + 32.44 = 107.54 dB loss
        // Predicted RX = 90 - 107.54 = -17.54 dBm
        powerDbm.Should().BeApproximately(-17.5, 1.0);
    }
}
