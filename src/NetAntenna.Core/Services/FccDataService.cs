using System.Globalization;
using System.IO.Compression;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

public class FccDataService : IFccDataService
{
    // The FCC updates this daily
    private const string LmsZipUrl = "https://enterpriseefiling.fcc.gov/dataentry/api/download/lmstv/app";
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _db;

    public FccDataService(HttpClient httpClient, IDatabaseService db)
    {
        _httpClient = httpClient;
        _db = db;
    }

    public async Task DownloadAndIndexLmsDataAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(10); // Downloading

        using var response = await _httpClient.GetAsync(LmsZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var zipStream = await response.Content.ReadAsStreamAsync(ct);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // We need three files: facility.dat, application.dat, tv_app_engineering.dat
        var facilityEntry = archive.GetEntry("facility.dat") ?? throw new FileNotFoundException("Missing facility.dat in LMS dump");
        var appEntry = archive.GetEntry("application.dat") ?? throw new FileNotFoundException("Missing application.dat in LMS dump");
        var engEntry = archive.GetEntry("tv_app_engineering.dat") ?? throw new FileNotFoundException("Missing tv_app_engineering.dat in LMS dump");

        progress?.Report(30); // Parsing Facilities

        var facilities = await ParseFacilitiesAsync(facilityEntry, ct);
        
        progress?.Report(50); // Parsing Applications

        var applications = await ParseApplicationsAsync(appEntry, ct);

        progress?.Report(70); // Parsing Engineering Data

        var towers = await ParseEngineeringDataAsync(engEntry, facilities, applications, ct);

        progress?.Report(90); // Saving to DB

        await _db.ReplaceFccTowersAsync(towers);
        await _db.SetSettingAsync("fcc_last_updated_unix_ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        progress?.Report(100); // Done
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

    private static async Task<Dictionary<int, string>> ParseFacilitiesAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        // Extracts Facility ID -> Call Sign
        var dict = new Dictionary<int, string>();
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var parts = line.Split('|');
            // Format: facility_id|...|call_sign|...
            if (parts.Length > 2 && int.TryParse(parts[0], out var facId) && !string.IsNullOrWhiteSpace(parts[2]))
            {
                dict[facId] = parts[2].Trim();
            }
        }
        return dict;
    }

    private static async Task<Dictionary<string, int>> ParseApplicationsAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        // Extracts Application ID -> Facility ID
        // We only care about active/granted licenses (status = 'G' or 'LIC')
        var dict = new Dictionary<string, int>();
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var parts = line.Split('|');
            if (parts.Length > 5 && int.TryParse(parts[1], out var facId))
            {
                var appId = parts[0];
                var status = parts[7]; // App status

                // Only grab applications that are granted/licensed
                if (status is "LIC" or "G" or "CP" or "CP MOD")
                {
                    dict[appId] = facId;
                }
            }
        }
        return dict;
    }

    private static async Task<List<FccTower>> ParseEngineeringDataAsync(
        ZipArchiveEntry engEntry, 
        IReadOnlyDictionary<int, string> facilities, 
        IReadOnlyDictionary<string, int> applications,
        CancellationToken ct)
    {
        var towers = new List<FccTower>();
        var addedFacIds = new HashSet<int>();

        await using var stream = engEntry.Open();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var parts = line.Split('|');
            if (parts.Length < 32) continue; // Malformed or short line

            var appId = parts[0];
            if (!applications.TryGetValue(appId, out var facId)) continue;
            if (!facilities.TryGetValue(facId, out var callSign)) continue;

            // To avoid duplicates, we only keep the first (primary) engineering record for a facility
            // In a real production app, we'd rank them by application status (License > CP)
            if (!addedFacIds.Add(facId)) continue; 

            // Parse coordinates and power
            if (!double.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out var latLat) ||
                !double.TryParse(parts[15], NumberStyles.Any, CultureInfo.InvariantCulture, out var latMin) ||
                !double.TryParse(parts[16], NumberStyles.Any, CultureInfo.InvariantCulture, out var latSec) ||
                !double.TryParse(parts[18], NumberStyles.Any, CultureInfo.InvariantCulture, out var lonLat) ||
                !double.TryParse(parts[19], NumberStyles.Any, CultureInfo.InvariantCulture, out var lonMin) ||
                !double.TryParse(parts[20], NumberStyles.Any, CultureInfo.InvariantCulture, out var lonSec) ||
                !int.TryParse(parts[22], out var channel))
            {
                continue;
            }

            var latDir = parts[17] == "S" ? -1 : 1;
            var lonDir = parts[21] == "E" ? 1 : -1; // USA is West (-1)

            var lat = latDir * (latLat + latMin / 60.0 + latSec / 3600.0);
            var lon = lonDir * (lonLat + lonMin / 60.0 + lonSec / 3600.0);

            _ = double.TryParse(parts[24], NumberStyles.Any, CultureInfo.InvariantCulture, out var erp);
            _ = double.TryParse(parts[26], NumberStyles.Any, CultureInfo.InvariantCulture, out var haat);

            towers.Add(new FccTower
            {
                FacilityId = facId,
                CallSign = callSign,
                TransmitChannel = channel,
                Latitude = lat,
                Longitude = lon,
                ErpKw = erp,
                HaatMeters = haat,
                IsNextGenTv = false, // ATSC 3.0 parsing is complex, left as default for now
                ServiceType = "DT"
            });
        }

        return towers;
    }
}
