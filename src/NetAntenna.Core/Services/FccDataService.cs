using System.Globalization;
using System.IO.Compression;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

public class FccDataService : IFccDataService
{
    // FCC LMS Public Database page: https://enterpriseefiling.fcc.gov/dataentry/public/tv/lmsDatabase.html
    private const string FacilityZipUrl = "https://enterpriseefiling.fcc.gov/dataentry/api/download/dbfile/facility.zip";
    private const string ApplicationZipUrl = "https://enterpriseefiling.fcc.gov/dataentry/api/download/dbfile/application.zip";
    private const string EngineeringZipUrl = "https://enterpriseefiling.fcc.gov/dataentry/api/download/dbfile/tv_app_engineering.zip";
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _db;

    public FccDataService(HttpClient httpClient, IDatabaseService db)
    {
        _httpClient = httpClient;
        _db = db;
    }

    public async Task DownloadAndIndexLmsDataAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(5); // Downloading facility data

        // Download and parse each file separately from the new FCC LMS endpoints
        var facilityEntry = await DownloadAndOpenZipEntry(FacilityZipUrl, "facility.dat", ct);
        progress?.Report(25);

        var appEntry = await DownloadAndOpenZipEntry(ApplicationZipUrl, "application.dat", ct);
        progress?.Report(50);

        var engEntry = await DownloadAndOpenZipEntry(EngineeringZipUrl, "tv_app_engineering.dat", ct);
        progress?.Report(70);

        var facilities = await ParseFacilitiesAsync(facilityEntry, ct);
        var applications = await ParseApplicationsAsync(appEntry, ct);
        var towers = await ParseEngineeringDataAsync(engEntry, facilities, applications, ct);

        progress?.Report(90); // Saving to DB
        await _db.ReplaceFccTowersAsync(towers);
        await _db.SetSettingAsync("fcc_last_updated_unix_ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

        progress?.Report(100); // Done
    }

    private async Task<Stream> DownloadAndOpenZipEntry(string url, string entryName, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // The zip is small enough to buffer in memory so we can dispose the response
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var ms = new MemoryStream(bytes);
        var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

        // The entry might be at root level or inside a sub-folder
        var entry = archive.GetEntry(entryName)
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Could not find '{entryName}' inside zip from {url}. Available: {string.Join(", ", archive.Entries.Select(e => e.Name))}");

        // Extract to a MemoryStream so the ZipArchive can be disposed safely
        var result = new MemoryStream();
        await using var entryStream = entry.Open();
        await entryStream.CopyToAsync(result, ct);
        result.Position = 0;
        return result;
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

    private static async Task<Dictionary<int, (string CallSign, string City, string State)>> ParseFacilitiesAsync(Stream stream, CancellationToken ct)
    {
        // Extracts Facility ID -> (Call Sign, City, State)
        var dict = new Dictionary<int, (string CallSign, string City, string State)>();
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var parts = line.Split('|');
            // Format: facility_id|...|call_sign|...|city|state|...
            if (parts.Length > 5 && int.TryParse(parts[0], out var facId) && !string.IsNullOrWhiteSpace(parts[2]))
            {
                dict[facId] = (parts[2].Trim(), parts[4].Trim(), parts[5].Trim());
            }
        }
        return dict;
    }

    private static async Task<Dictionary<string, int>> ParseApplicationsAsync(Stream stream, CancellationToken ct)
    {
        // Extracts Application ID -> Facility ID
        // We only care about active/granted licenses (status = 'G' or 'LIC')
        var dict = new Dictionary<string, int>();
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
        Stream engStream, 
        IReadOnlyDictionary<int, (string CallSign, string City, string State)> facilities, 
        IReadOnlyDictionary<string, int> applications,
        CancellationToken ct)
    {
        var towers = new List<FccTower>();
        var addedFacIds = new HashSet<int>();

        using var reader = new StreamReader(engStream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var parts = line.Split('|');
            if (parts.Length < 32) continue; // Malformed or short line

            var appId = parts[0];
            if (!applications.TryGetValue(appId, out var facId)) continue;
            if (!facilities.TryGetValue(facId, out var facilityInfo)) continue;

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
                CallSign = facilityInfo.CallSign,
                TransmitChannel = channel,
                Latitude = lat,
                Longitude = lon,
                ErpKw = erp,
                HaatMeters = haat,
                IsNextGenTv = false, // ATSC 3.0 parsing is complex, left as default for now
                ServiceType = "DT",
                City = facilityInfo.City,
                State = facilityInfo.State
            });
        }

        return towers;
    }
}
