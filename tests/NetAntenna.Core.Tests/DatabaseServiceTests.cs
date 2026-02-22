using FluentAssertions;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Tests;

public class DatabaseServiceTests : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _db;

    public DatabaseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"netantenna_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseService(_dbPath);
    }

    public async Task InitializeAsync() => await _db.InitializeAsync();

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }

    // --- Device Tests ---

    [Fact]
    public async Task UpsertDevice_InsertsNewDevice()
    {
        var device = CreateTestDevice("ABCD1234");
        await _db.UpsertDeviceAsync(device);

        var result = await _db.GetDeviceAsync("ABCD1234");
        result.Should().NotBeNull();
        result!.DeviceId.Should().Be("ABCD1234");
        result.FriendlyName.Should().Be("HDHomeRun FLEX 4K");
    }

    [Fact]
    public async Task UpsertDevice_UpdatesExistingDevice()
    {
        var device = CreateTestDevice("ABCD1234");
        await _db.UpsertDeviceAsync(device);

        device.FriendlyName = "Updated Name";
        device.FirmwareVersion = "20260101";
        await _db.UpsertDeviceAsync(device);

        var result = await _db.GetDeviceAsync("ABCD1234");
        result!.FriendlyName.Should().Be("Updated Name");
        result.FirmwareVersion.Should().Be("20260101");
    }

    [Fact]
    public async Task GetAllDevices_ReturnsAllDevices()
    {
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV1"));
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV2"));

        var devices = await _db.GetAllDevicesAsync();
        devices.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDevice_ReturnsNullForMissing()
    {
        var result = await _db.GetDeviceAsync("NONEXISTENT");
        result.Should().BeNull();
    }

    // --- Signal Sample Tests ---

    [Fact]
    public async Task InsertSamples_BatchInsert_AndQuery()
    {
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV1"));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var samples = Enumerable.Range(0, 10).Select(i => new SignalSample
        {
            DeviceId = "DEV1",
            TunerIndex = 0,
            Channel = "8vsb:7",
            LockType = "8vsb",
            Ss = 80 + i,
            Snq = 90,
            Seq = 100,
            Bps = 19000000,
            Pps = 2400,
            TimestampUnixMs = now + (i * 1000)
        });

        await _db.InsertSamplesAsync(samples);

        var results = await _db.GetSamplesAsync("DEV1", now - 1, now + 100000);
        results.Should().HaveCount(10);
        results[0].Ss.Should().Be(80);
        results[9].Ss.Should().Be(89);
    }

    [Fact]
    public async Task GetSamples_FiltersByTunerIndex()
    {
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV1"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var samples = new List<SignalSample>
        {
            new() { DeviceId = "DEV1", TunerIndex = 0, Channel = "ch0", LockType = "8vsb",
                     Ss = 80, Snq = 90, Seq = 100, Bps = 0, Pps = 0, TimestampUnixMs = now },
            new() { DeviceId = "DEV1", TunerIndex = 1, Channel = "ch1", LockType = "8vsb",
                     Ss = 70, Snq = 85, Seq = 95, Bps = 0, Pps = 0, TimestampUnixMs = now + 1 },
        };
        await _db.InsertSamplesAsync(samples);

        var tuner0 = await _db.GetSamplesAsync("DEV1", now - 1, now + 100, tunerIndex: 0);
        tuner0.Should().HaveCount(1);
        tuner0[0].Channel.Should().Be("ch0");
    }

    [Fact]
    public async Task GetLatestSamples_ReturnsNewest()
    {
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV1"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var samples = Enumerable.Range(0, 5).Select(i => new SignalSample
        {
            DeviceId = "DEV1", TunerIndex = 0, Channel = "ch0", LockType = "8vsb",
            Ss = 80 + i, Snq = 90, Seq = 100, Bps = 0, Pps = 0,
            TimestampUnixMs = now + (i * 1000)
        });
        await _db.InsertSamplesAsync(samples);

        var latest = await _db.GetLatestSamplesAsync("DEV1", 0, 2);
        latest.Should().HaveCount(2);
        latest[0].Ss.Should().Be(84); // Most recent first
        latest[1].Ss.Should().Be(83);
    }

    [Fact]
    public async Task PurgeOldSamples_RemovesExpiredData()
    {
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV1"));
        var oldMs = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeMilliseconds();
        var newMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var samples = new List<SignalSample>
        {
            new() { DeviceId = "DEV1", TunerIndex = 0, Channel = "ch0", LockType = "8vsb",
                     Ss = 80, Snq = 90, Seq = 100, Bps = 0, Pps = 0, TimestampUnixMs = oldMs },
            new() { DeviceId = "DEV1", TunerIndex = 0, Channel = "ch0", LockType = "8vsb",
                     Ss = 85, Snq = 92, Seq = 100, Bps = 0, Pps = 0, TimestampUnixMs = newMs },
        };
        await _db.InsertSamplesAsync(samples);

        await _db.PurgeOldSamplesAsync(30);

        var remaining = await _db.GetSamplesAsync("DEV1", 0, newMs + 1000);
        remaining.Should().HaveCount(1);
        remaining[0].Ss.Should().Be(85); // Only the new one survives
    }

    // --- Channel Tests ---

    [Fact]
    public async Task UpsertChannel_AndGetLineup()
    {
        await _db.UpsertDeviceAsync(CreateTestDevice("DEV1"));

        var channels = new List<ChannelInfo>
        {
            new() { GuideNumber = "7.1", GuideName = "WSVN", Url = "http://x/ch7.1", IsFavorite = true },
            new() { GuideNumber = "10.1", GuideName = "WPLG", Url = "http://x/ch10.1", IsHidden = true },
        };
        await _db.UpsertChannelsAsync("DEV1", channels);

        var lineup = await _db.GetChannelLineupAsync("DEV1");
        lineup.Should().HaveCount(2);
        lineup[0].GuideNumber.Should().Be("7.1"); // Sorted numerically
        lineup[0].IsFavorite.Should().BeTrue();
        lineup[1].IsHidden.Should().BeTrue();
    }

    // --- Settings Tests ---

    [Fact]
    public async Task Settings_DefaultsAreCreatedOnInit()
    {
        var interval = await _db.GetSettingAsync("polling_interval_sec");
        interval.Should().Be("5");

        var retention = await _db.GetSettingAsync("data_retention_days");
        retention.Should().Be("30");
    }

    [Fact]
    public async Task Settings_SetAndGet()
    {
        await _db.SetSettingAsync("custom_key", "custom_value");
        var result = await _db.GetSettingAsync("custom_key");
        result.Should().Be("custom_value");
    }

    [Fact]
    public async Task Settings_OverwriteExisting()
    {
        await _db.SetSettingAsync("polling_interval_sec", "10");
        var result = await _db.GetSettingAsync("polling_interval_sec");
        result.Should().Be("10");
    }

    [Fact]
    public async Task GetAllSettings_ReturnsAll()
    {
        var settings = await _db.GetAllSettingsAsync();
        settings.Should().ContainKey("polling_interval_sec");
        settings.Should().ContainKey("data_retention_days");
        settings.Should().ContainKey("theme");
    }

    [Fact]
    public async Task GetSetting_ReturnsNullForMissing()
    {
        var result = await _db.GetSettingAsync("nonexistent_key");
        result.Should().BeNull();
    }

    // --- Helpers ---

    private static HdHomeRunDevice CreateTestDevice(string id) => new()
    {
        DeviceId = id,
        FriendlyName = "HDHomeRun FLEX 4K",
        ModelNumber = "HDHR5-4K",
        FirmwareName = "hdhomerun5_atsc",
        FirmwareVersion = "20231214",
        TunerCount = 4,
        BaseUrl = $"http://192.168.1.100",
        LineupUrl = $"http://192.168.1.100/lineup.json",
        IpAddress = "192.168.1.100",
        LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
}
