using System.Globalization;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

public class FccDataService : IFccDataService
{
    private const string TvqEndpointUrl = "https://transition.fcc.gov/fcc-bin/tvq?state=&call=&city=&arn=&sur=1&facid=&status=&dtxt=xml&list=4";
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _db;
    private readonly ILogger<FccDataService> _logger;

    public FccDataService(HttpClient httpClient, IDatabaseService db, ILogger<FccDataService> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _logger = logger;
    }

    public async Task DownloadAndIndexLmsDataAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(5);

        try
        {
            using var response = await _httpClient.GetAsync(TvqEndpointUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            progress?.Report(20);

            var towers = new List<FccTower>();
            var addedFacIds = new HashSet<int>();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            string? line;
            int lineCount = 0;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                lineCount++;
                if (lineCount % 1000 == 0)
                {
                    progress?.Report(20 + (int)((lineCount / 10000.0) * 60)); // Progress estimate based on ~8000 expected lines
                }

                var parts = line.Split('|');
                if (parts.Length < 32) continue; // Malformed or short line

                var callSign = parts[1].Trim();
                var serviceType = parts[3].Trim();
                var status = parts[9].Trim();
                var city = parts[10].Trim();
                var state = parts[11].Trim();
                
                // Only keep granted/licensed or CP stations, or active STAs
                if (status is not "LIC" and not "CP" and not "STA") continue;

                if (!int.TryParse(parts[4].Trim(), out var channel)) continue;
                if (!int.TryParse(parts[13].Trim(), out var facId)) continue;
                
                if (!addedFacIds.Add(facId)) continue; // Keep only the first record for a facility ID

                _ = double.TryParse(parts[14].Replace("kW", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var erp);

                var latDirStr = parts[19].Trim();
                var lonDirStr = parts[23].Trim();

                if (!double.TryParse(parts[20].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var latDeg) ||
                    !double.TryParse(parts[21].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var latMin) ||
                    !double.TryParse(parts[22].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var latSec) ||
                    !double.TryParse(parts[24].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lonDeg) ||
                    !double.TryParse(parts[25].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lonMin) ||
                    !double.TryParse(parts[26].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lonSec))
                {
                    continue; // Skip invalid coordinates
                }

                var latDir = latDirStr.Equals("S", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
                // USA is West, which is negative longitude in WGS84
                var lonDir = lonDirStr.Equals("E", StringComparison.OrdinalIgnoreCase) ? 1 : -1; 

                var lat = latDir * (latDeg + (latMin / 60.0) + (latSec / 3600.0));
                var lon = lonDir * (lonDeg + (lonMin / 60.0) + (lonSec / 3600.0));

                _ = double.TryParse(parts[31].Replace("m", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var haat);

                towers.Add(new FccTower
                {
                    FacilityId = facId,
                    CallSign = callSign,
                    TransmitChannel = channel,
                    Latitude = lat,
                    Longitude = lon,
                    ErpKw = erp,
                    HaatMeters = haat,
                    IsNextGenTv = false, // Not explicitly defined in this API endpoint
                    ServiceType = serviceType,
                    City = city,
                    State = state
                });
            }

            progress?.Report(90);
            
            await _db.ReplaceFccTowersAsync(towers);
            await _db.SetSettingAsync("fcc_last_updated_unix_ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            progress?.Report(100);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download or parse FCC TVQ data.");
            throw; // Re-throw so TowerMapViewModel catches it and updates the UI
        }
    }

    public async Task<DateTimeOffset?> GetLastUpdateDateAsync(CancellationToken ct = default)
    {
        var unixMsStr = await _db.GetSettingAsync("fcc_last_updated_unix_ms");
        if (long.TryParse(unixMsStr, out var unixMs))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        }
        return null;
    }
}
