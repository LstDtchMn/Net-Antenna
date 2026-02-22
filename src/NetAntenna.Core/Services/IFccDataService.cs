using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// Service responsible for fetching and parsing the FCC LMS broadcast tower database.
/// https://www.fcc.gov/media/radio/lms-database-downloads
/// </summary>
public interface IFccDataService
{
    /// <summary>
    /// Downloads the latest FCC LMS TV app data ZIP file, extracts the relevant
    /// tables (application/facility data), parses active transmitters, and
    /// persists them to the local database.
    /// </summary>
    /// <param name="progress">Optional progress reporter (0-100%).</param>
    /// <param name="ct">Cancellation token.</param>
    Task DownloadAndIndexLmsDataAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the date the FCC data was last successfully downloaded and indexed.
    /// </summary>
    Task<DateTimeOffset?> GetLastUpdateDateAsync(CancellationToken ct = default);
}
