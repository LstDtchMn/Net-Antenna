using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

public class RfPredictionEngine : IRfPredictionEngine
{
    private const double EarthRadiusKm = 6371.0;

    public double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var rLat1 = ToRadians(lat1);
        var rLat2 = ToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(rLat1) * Math.Cos(rLat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    public double CalculateBearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRadians(lon2 - lon1);
        var rLat1 = ToRadians(lat1);
        var rLat2 = ToRadians(lat2);

        var y = Math.Sin(dLon) * Math.Cos(rLat2);
        var x = Math.Cos(rLat1) * Math.Sin(rLat2) -
                Math.Sin(rLat1) * Math.Cos(rLat2) * Math.Cos(dLon);

        var bearingRad = Math.Atan2(y, x);
        var bearingDeg = ToDegrees(bearingRad);

        return (bearingDeg + 360) % 360;
    }

    public double CalculatePredictedPowerDbm(FccTower tower, double rxLat, double rxLon)
    {
        // 1. Calculate Distance
        var distKm = CalculateDistanceKm(tower.Latitude, tower.Longitude, rxLat, rxLon);
        if (distKm < 0.1) distKm = 0.1; // avoid log(0)

        // 2. Frequency Estimation (Center Frequency in MHz)
        // FCC TV channels: VHF-Lo (2-6), VHF-Hi (7-13), UHF (14-36)
        // Simplified mapping for FSPL calculation
        double freqMhz;
        if (tower.TransmitChannel >= 14)
            freqMhz = 470 + (tower.TransmitChannel - 14) * 6 + 3; // UHF band starts at 470MHz, 6MHz wide
        else if (tower.TransmitChannel >= 7)
            freqMhz = 174 + (tower.TransmitChannel - 7) * 6 + 3; // VHF-Hi starts at 174MHz
        else
            freqMhz = 54 + (tower.TransmitChannel - 2) * 6 + 3; // VHF-Lo starts at 54MHz

        // 3. Free Space Path Loss (FSPL) formula:
        // FSPL(dB) = 20*log10(d_km) + 20*log10(f_MHz) + 32.44
        var fspl = 20 * Math.Log10(distKm) + 20 * Math.Log10(freqMhz) + 32.44;

        // 4. Transmitter Power: ERP (kW) -> dBm
        // dBm = 10*log10(Watts) + 30
        var txPowerDbm = 10 * Math.Log10(tower.ErpKw * 1000) + 30;

        // 5. Predicted Receive Power (assuming 0dBi RX antenna gain and LOS)
        var rxPowerDbm = txPowerDbm - fspl;

        return rxPowerDbm;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
