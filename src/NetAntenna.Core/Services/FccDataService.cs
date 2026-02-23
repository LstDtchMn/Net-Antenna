using System.Globalization;
using System.IO.Compression;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

public class FccDataService : IFccDataService
{
    // FCC LMS Public Database page: https://enterpriseefiling.fcc.gov/dataentry/public/tv/lmsDatabase.html
    // "Current_LMS_Dump.zip" is always kept current by the FCC
    private const string LmsDumpUrl = "https://enterpriseefiling.fcc.gov/dataentry/api/download/dbfile/Current_LMS_Dump.zip";
    private readonly HttpClient _httpClient;
    private readonly IDatabaseService _db;

    public FccDataService(HttpClient httpClient, IDatabaseService db)
    {
        _httpClient = httpClient;
        _db = db;
    }

    public async Task DownloadAndIndexLmsDataAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(5);

        // Stream the large zip to a temp file so we get real download progress
        // instead of blocking on ReadAsByteArrayAsync for the entire file
        using var response = await _httpClient.GetAsync(LmsDumpUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0L;
        var tempFile = Path.GetTempFileName();
        try
        {
            await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = File.Create(tempFile))
            {
                var buffer = new byte[81920];
                long bytesRead = 0;
                int read;
                while ((read = await responseStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;
                    if (totalBytes > 0)
                        progress?.Report(5 + (int)(bytesRead * 35 / totalBytes)); // 5% → 40%
                }
            }

            progress?.Report(42);

            using var fileMs = File.OpenRead(tempFile);
            using var archive = new ZipArchive(fileMs, ZipArchiveMode.Read);

            var facilityEntry = FindEntry(archive, "facility.dat")
                ?? throw new FileNotFoundException($"facility.dat not found. Files: {string.Join(", ", archive.Entries.Select(e => e.Name))}");
            var appEntry = FindEntry(archive, "application.dat")
                ?? throw new FileNotFoundException("application.dat not found");
            var engEntry = FindEntry(archive, "tv_app_engineering.dat")
                ?? throw new FileNotFoundException("tv_app_engineering.dat not found");

            progress?.Report(50);
            var facilities   = await ParseFacilitiesAsync(facilityEntry.Open(),   ct);
            progress?.Report(65);
            var applications = await ParseApplicationsAsync(appEntry.Open(),      ct);
            progress?.Report(80);
            var towers       = await ParseEngineeringDataAsync(engEntry.Open(),   facilities, applications, ct);

            progress?.Report(92);
            await _db.ReplaceFccTowersAsync(towers);
            await _db.SetSettingAsync("fcc_last_updated_unix_ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            progress?.Report(100);
        }
        finally
        {
            File.Delete(tempFile); // always clean up temp file
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name) =>
        archive.GetEntry(name)
        ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

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
