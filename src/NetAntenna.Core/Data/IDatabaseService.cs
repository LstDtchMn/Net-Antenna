using NetAntenna.Core.Models;

namespace NetAntenna.Core.Data;

/// <summary>
/// Database service for all SQLite persistence operations.
/// </summary>
public interface IDatabaseService
{
    /// <summary>Initialize the database and run migrations.</summary>
    Task InitializeAsync();

    // --- Devices ---
    Task UpsertDeviceAsync(HdHomeRunDevice device);
    Task<IReadOnlyList<HdHomeRunDevice>> GetAllDevicesAsync();
    Task<HdHomeRunDevice?> GetDeviceAsync(string deviceId);

    // --- Signal Samples ---
    Task InsertSamplesAsync(IEnumerable<SignalSample> samples);
    Task<IReadOnlyList<SignalSample>> GetSamplesAsync(
        string deviceId, long fromUnixMs, long toUnixMs, int? tunerIndex = null);
    Task<IReadOnlyList<SignalSample>> GetLatestSamplesAsync(
        string deviceId, int tunerIndex, int count = 1);
    Task PurgeOldSamplesAsync(int retentionDays);

    // --- Channel Lineup ---
    Task UpsertChannelAsync(string deviceId, ChannelInfo channel);
    Task UpsertChannelsAsync(string deviceId, IEnumerable<ChannelInfo> channels);
    Task<IReadOnlyList<ChannelInfo>> GetChannelLineupAsync(string deviceId);

    // Settings
    Task<string?> GetSettingAsync(string key);
    Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync();
    Task SetSettingAsync(string key, string value);

    // FCC Data Integration
    Task ReplaceFccTowersAsync(IEnumerable<FccTower> towers);
    Task<IReadOnlyList<FccTower>> GetAllFccTowersAsync();
}
