using Microsoft.Data.Sqlite;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Data;

/// <summary>
/// SQLite database service for all persistence operations.
/// Uses WAL mode for concurrent read/write safety.
/// </summary>
public sealed class DatabaseService : IDatabaseService, IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public DatabaseService(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
    }

    private async Task<SqliteConnection> GetConnectionAsync()
    {
        if (_connection is { State: System.Data.ConnectionState.Open })
            return _connection;

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync();

        // Enable WAL mode for concurrent reads during writes
        using var walCmd = _connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await walCmd.ExecuteNonQueryAsync();

        return _connection;
    }

    public async Task InitializeAsync()
    {
        var conn = await GetConnectionAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                device_id           TEXT PRIMARY KEY,
                friendly_name       TEXT,
                model_number        TEXT,
                firmware_name       TEXT,
                firmware_version    TEXT,
                tuner_count         INTEGER,
                base_url            TEXT,
                lineup_url          TEXT,
                ip_address          TEXT,
                last_seen_unix_ms   INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS signal_samples (
                id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id           TEXT NOT NULL,
                tuner_index         INTEGER NOT NULL,
                channel             TEXT,
                lock_type           TEXT,
                ss                  INTEGER,
                snq                 INTEGER,
                seq                 INTEGER,
                bps                 INTEGER,
                pps                 INTEGER,
                timestamp_unix_ms   INTEGER NOT NULL,
                FOREIGN KEY (device_id) REFERENCES devices(device_id)
            );
            CREATE INDEX IF NOT EXISTS idx_samples_device_time
                ON signal_samples(device_id, timestamp_unix_ms);
            CREATE INDEX IF NOT EXISTS idx_samples_channel_time
                ON signal_samples(channel, timestamp_unix_ms);
            CREATE INDEX IF NOT EXISTS idx_samples_device_tuner_time
                ON signal_samples(device_id, tuner_index, timestamp_unix_ms);

            CREATE TABLE IF NOT EXISTS channel_lineup (
                device_id           TEXT NOT NULL,
                guide_number        TEXT NOT NULL,
                guide_name          TEXT,
                stream_url          TEXT,
                tags                TEXT,
                is_favorite         INTEGER DEFAULT 0,
                is_hidden           INTEGER DEFAULT 0,
                last_ss             INTEGER,
                last_snq            INTEGER,
                last_seq            INTEGER,
                last_updated_unix_ms INTEGER,
                PRIMARY KEY (device_id, guide_number),
                FOREIGN KEY (device_id) REFERENCES devices(device_id)
            );

            CREATE TABLE IF NOT EXISTS settings (
                key                 TEXT PRIMARY KEY,
                value               TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS fcc_towers (
                facility_id         INTEGER PRIMARY KEY,
                call_sign           TEXT,
                transmit_channel    INTEGER,
                latitude            REAL,
                longitude           REAL,
                erp_kw              REAL,
                haat_meters         REAL,
                is_nextgen_tv       INTEGER,
                service_type        TEXT,
                city                TEXT,
                state               TEXT
            );

            CREATE TABLE IF NOT EXISTS schema_version (
                version             INTEGER PRIMARY KEY,
                applied_unix_ms     INTEGER NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        // Insert default settings if they don't exist
        await SetDefaultSettingsAsync();
    }

    private async Task SetDefaultSettingsAsync()
    {
        var defaults = new Dictionary<string, string>
        {
            ["polling_interval_sec"] = "5",
            ["data_retention_days"] = "30",
            ["seq_threshold_weak"] = "50",
            ["seq_threshold_good"] = "80",
            ["theme"] = "dark"
        };

        foreach (var (key, value) in defaults)
        {
            var existing = await GetSettingAsync(key);
            if (existing is null)
                await SetSettingAsync(key, value);
        }
    }

    // --- Devices ---

    public async Task UpsertDeviceAsync(HdHomeRunDevice device)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (device_id, friendly_name, model_number, firmware_name,
                firmware_version, tuner_count, base_url, lineup_url, ip_address, last_seen_unix_ms)
            VALUES (@id, @name, @model, @fwName, @fwVer, @tuners, @base, @lineup, @ip, @seen)
            ON CONFLICT(device_id) DO UPDATE SET
                friendly_name = @name, model_number = @model, firmware_name = @fwName,
                firmware_version = @fwVer, tuner_count = @tuners, base_url = @base,
                lineup_url = @lineup, ip_address = @ip, last_seen_unix_ms = @seen;
            """;
        cmd.Parameters.AddWithValue("@id", device.DeviceId);
        cmd.Parameters.AddWithValue("@name", device.FriendlyName);
        cmd.Parameters.AddWithValue("@model", device.ModelNumber);
        cmd.Parameters.AddWithValue("@fwName", device.FirmwareName);
        cmd.Parameters.AddWithValue("@fwVer", device.FirmwareVersion);
        cmd.Parameters.AddWithValue("@tuners", device.TunerCount);
        cmd.Parameters.AddWithValue("@base", device.BaseUrl);
        cmd.Parameters.AddWithValue("@lineup", device.LineupUrl);
        cmd.Parameters.AddWithValue("@ip", device.IpAddress);
        cmd.Parameters.AddWithValue("@seen", device.LastSeenUnixMs);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<HdHomeRunDevice>> GetAllDevicesAsync()
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM devices ORDER BY friendly_name;";

        var devices = new List<HdHomeRunDevice>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            devices.Add(ReadDevice(reader));
        }
        return devices;
    }

    public async Task<HdHomeRunDevice?> GetDeviceAsync(string deviceId)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM devices WHERE device_id = @id;";
        cmd.Parameters.AddWithValue("@id", deviceId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadDevice(reader) : null;
    }

    private static HdHomeRunDevice ReadDevice(SqliteDataReader reader) => new()
    {
        DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
        FriendlyName = reader.GetString(reader.GetOrdinal("friendly_name")),
        ModelNumber = reader.GetString(reader.GetOrdinal("model_number")),
        FirmwareName = reader.GetString(reader.GetOrdinal("firmware_name")),
        FirmwareVersion = reader.GetString(reader.GetOrdinal("firmware_version")),
        TunerCount = reader.GetInt32(reader.GetOrdinal("tuner_count")),
        BaseUrl = reader.GetString(reader.GetOrdinal("base_url")),
        LineupUrl = reader.GetString(reader.GetOrdinal("lineup_url")),
        IpAddress = reader.GetString(reader.GetOrdinal("ip_address")),
        LastSeenUnixMs = reader.GetInt64(reader.GetOrdinal("last_seen_unix_ms"))
    };

    // --- Signal Samples ---

    public async Task InsertSamplesAsync(IEnumerable<SignalSample> samples)
    {
        var conn = await GetConnectionAsync();
        using var transaction = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO signal_samples
                    (device_id, tuner_index, channel, lock_type, ss, snq, seq, bps, pps, timestamp_unix_ms)
                VALUES
                    (@devId, @tuner, @ch, @lock, @ss, @snq, @seq, @bps, @pps, @ts);
                """;

            var pDevId = cmd.Parameters.Add("@devId", SqliteType.Text);
            var pTuner = cmd.Parameters.Add("@tuner", SqliteType.Integer);
            var pCh = cmd.Parameters.Add("@ch", SqliteType.Text);
            var pLock = cmd.Parameters.Add("@lock", SqliteType.Text);
            var pSs = cmd.Parameters.Add("@ss", SqliteType.Integer);
            var pSnq = cmd.Parameters.Add("@snq", SqliteType.Integer);
            var pSeq = cmd.Parameters.Add("@seq", SqliteType.Integer);
            var pBps = cmd.Parameters.Add("@bps", SqliteType.Integer);
            var pPps = cmd.Parameters.Add("@pps", SqliteType.Integer);
            var pTs = cmd.Parameters.Add("@ts", SqliteType.Integer);

            foreach (var s in samples)
            {
                pDevId.Value = s.DeviceId;
                pTuner.Value = s.TunerIndex;
                pCh.Value = s.Channel;
                pLock.Value = s.LockType;
                pSs.Value = s.Ss;
                pSnq.Value = s.Snq;
                pSeq.Value = s.Seq;
                pBps.Value = s.Bps;
                pPps.Value = s.Pps;
                pTs.Value = s.TimestampUnixMs;
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<SignalSample>> GetSamplesAsync(
        string deviceId, long fromUnixMs, long toUnixMs, int? tunerIndex = null)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT * FROM signal_samples
            WHERE device_id = @devId
              AND timestamp_unix_ms BETWEEN @from AND @to
            """;
        if (tunerIndex.HasValue)
            sql += " AND tuner_index = @tuner";
        sql += " ORDER BY timestamp_unix_ms ASC;";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@devId", deviceId);
        cmd.Parameters.AddWithValue("@from", fromUnixMs);
        cmd.Parameters.AddWithValue("@to", toUnixMs);
        if (tunerIndex.HasValue)
            cmd.Parameters.AddWithValue("@tuner", tunerIndex.Value);

        var samples = new List<SignalSample>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            samples.Add(ReadSample(reader));
        }
        return samples;
    }

    public async Task<IReadOnlyList<SignalSample>> GetLatestSamplesAsync(
        string deviceId, int tunerIndex, int count = 1)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM signal_samples
            WHERE device_id = @devId AND tuner_index = @tuner
            ORDER BY timestamp_unix_ms DESC
            LIMIT @count;
            """;
        cmd.Parameters.AddWithValue("@devId", deviceId);
        cmd.Parameters.AddWithValue("@tuner", tunerIndex);
        cmd.Parameters.AddWithValue("@count", count);

        var samples = new List<SignalSample>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            samples.Add(ReadSample(reader));
        }
        return samples;
    }

    public async Task PurgeOldSamplesAsync(int retentionDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeMilliseconds();
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM signal_samples WHERE timestamp_unix_ms < @cutoff;";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync();
    }

    private static SignalSample ReadSample(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("id")),
        DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
        TunerIndex = reader.GetInt32(reader.GetOrdinal("tuner_index")),
        Channel = reader.GetString(reader.GetOrdinal("channel")),
        LockType = reader.GetString(reader.GetOrdinal("lock_type")),
        Ss = reader.GetInt32(reader.GetOrdinal("ss")),
        Snq = reader.GetInt32(reader.GetOrdinal("snq")),
        Seq = reader.GetInt32(reader.GetOrdinal("seq")),
        Bps = reader.GetInt64(reader.GetOrdinal("bps")),
        Pps = reader.GetInt64(reader.GetOrdinal("pps")),
        TimestampUnixMs = reader.GetInt64(reader.GetOrdinal("timestamp_unix_ms"))
    };

    // --- Channel Lineup ---

    public async Task UpsertChannelAsync(string deviceId, ChannelInfo channel)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO channel_lineup
                (device_id, guide_number, guide_name, stream_url, tags, is_favorite, is_hidden,
                 last_ss, last_snq, last_seq, last_updated_unix_ms)
            VALUES
                (@devId, @num, @name, @url, @tags, @fav, @hidden, @ss, @snq, @seq, @updated)
            ON CONFLICT(device_id, guide_number) DO UPDATE SET
                guide_name = @name, stream_url = @url, tags = @tags,
                is_favorite = @fav, is_hidden = @hidden,
                last_ss = @ss, last_snq = @snq, last_seq = @seq,
                last_updated_unix_ms = @updated;
            """;
        AddChannelParameters(cmd, deviceId, channel);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertChannelsAsync(string deviceId, IEnumerable<ChannelInfo> channels)
    {
        var conn = await GetConnectionAsync();
        using var transaction = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO channel_lineup
                    (device_id, guide_number, guide_name, stream_url, tags, is_favorite, is_hidden,
                     last_ss, last_snq, last_seq, last_updated_unix_ms)
                VALUES
                    (@devId, @num, @name, @url, @tags, @fav, @hidden, @ss, @snq, @seq, @updated)
                ON CONFLICT(device_id, guide_number) DO UPDATE SET
                    guide_name = @name, stream_url = @url, tags = @tags;
                """;

            var pDevId = cmd.Parameters.Add("@devId", SqliteType.Text);
            var pNum = cmd.Parameters.Add("@num", SqliteType.Text);
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pUrl = cmd.Parameters.Add("@url", SqliteType.Text);
            var pTags = cmd.Parameters.Add("@tags", SqliteType.Text);
            var pFav = cmd.Parameters.Add("@fav", SqliteType.Integer);
            var pHidden = cmd.Parameters.Add("@hidden", SqliteType.Integer);
            var pSs = cmd.Parameters.Add("@ss", SqliteType.Integer);
            var pSnq = cmd.Parameters.Add("@snq", SqliteType.Integer);
            var pSeq = cmd.Parameters.Add("@seq", SqliteType.Integer);
            var pUpdated = cmd.Parameters.Add("@updated", SqliteType.Integer);

            foreach (var ch in channels)
            {
                pDevId.Value = deviceId;
                pNum.Value = ch.GuideNumber;
                pName.Value = ch.GuideName;
                pUrl.Value = ch.Url;
                pTags.Value = (object?)ch.Tags ?? DBNull.Value;
                pFav.Value = ch.IsFavorite ? 1 : 0;
                pHidden.Value = ch.IsHidden ? 1 : 0;
                pSs.Value = (object?)ch.LastSs ?? DBNull.Value;
                pSnq.Value = (object?)ch.LastSnq ?? DBNull.Value;
                pSeq.Value = (object?)ch.LastSeq ?? DBNull.Value;
                pUpdated.Value = (object?)ch.LastUpdatedUnixMs ?? DBNull.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<ChannelInfo>> GetChannelLineupAsync(string deviceId)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM channel_lineup
            WHERE device_id = @devId
            ORDER BY CAST(guide_number AS REAL);
            """;
        cmd.Parameters.AddWithValue("@devId", deviceId);

        var channels = new List<ChannelInfo>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            channels.Add(new ChannelInfo
            {
                GuideNumber = reader.GetString(reader.GetOrdinal("guide_number")),
                GuideName = reader.GetString(reader.GetOrdinal("guide_name")),
                Url = reader.GetString(reader.GetOrdinal("stream_url")),
                Tags = reader.IsDBNull(reader.GetOrdinal("tags")) ? null : reader.GetString(reader.GetOrdinal("tags")),
                IsFavorite = reader.GetInt32(reader.GetOrdinal("is_favorite")) == 1,
                IsHidden = reader.GetInt32(reader.GetOrdinal("is_hidden")) == 1,
                LastSs = reader.IsDBNull(reader.GetOrdinal("last_ss")) ? null : reader.GetInt32(reader.GetOrdinal("last_ss")),
                LastSnq = reader.IsDBNull(reader.GetOrdinal("last_snq")) ? null : reader.GetInt32(reader.GetOrdinal("last_snq")),
                LastSeq = reader.IsDBNull(reader.GetOrdinal("last_seq")) ? null : reader.GetInt32(reader.GetOrdinal("last_seq")),
                LastUpdatedUnixMs = reader.IsDBNull(reader.GetOrdinal("last_updated_unix_ms"))
                    ? null : reader.GetInt64(reader.GetOrdinal("last_updated_unix_ms"))
            });
        }
        return channels;
    }

    private static void AddChannelParameters(SqliteCommand cmd, string deviceId, ChannelInfo ch)
    {
        cmd.Parameters.AddWithValue("@devId", deviceId);
        cmd.Parameters.AddWithValue("@num", ch.GuideNumber);
        cmd.Parameters.AddWithValue("@name", ch.GuideName);
        cmd.Parameters.AddWithValue("@url", ch.Url);
        cmd.Parameters.AddWithValue("@tags", (object?)ch.Tags ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fav", ch.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@hidden", ch.IsHidden ? 1 : 0);
        cmd.Parameters.AddWithValue("@ss", (object?)ch.LastSs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@snq", (object?)ch.LastSnq ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seq", (object?)ch.LastSeq ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@updated", (object?)ch.LastUpdatedUnixMs ?? DBNull.Value);
    }

    // --- Settings ---

    public async Task<string?> GetSettingAsync(string key)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task SetSettingAsync(string key, string value)
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync()
    {
        var conn = await GetConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings;";

        var settings = new Dictionary<string, string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }
        return settings;
    }

    // --- FCC Data Integration ---

    public async Task ReplaceFccTowersAsync(IEnumerable<FccTower> towers)
    {
        await using var transaction = await _connection.BeginTransactionAsync();
        try
        {
            await using var clearCmd = _connection.CreateCommand();
            clearCmd.CommandText = "DELETE FROM fcc_towers";
            await clearCmd.ExecuteNonQueryAsync();

            await using var insertCmd = _connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO fcc_towers (
                    facility_id, call_sign, transmit_channel, latitude, longitude,
                    erp_kw, haat_meters, is_nextgen_tv, service_type, city, state
                ) VALUES (
                    $fac, $call, $chan, $lat, $lon, $erp, $haat, $nextgen, $type, $city, $state
                )";

            var pFac = insertCmd.Parameters.Add("$fac", SqliteType.Integer);
            var pCall = insertCmd.Parameters.Add("$call", SqliteType.Text);
            var pChan = insertCmd.Parameters.Add("$chan", SqliteType.Integer);
            var pLat = insertCmd.Parameters.Add("$lat", SqliteType.Real);
            var pLon = insertCmd.Parameters.Add("$lon", SqliteType.Real);
            var pErp = insertCmd.Parameters.Add("$erp", SqliteType.Real);
            var pHaat = insertCmd.Parameters.Add("$haat", SqliteType.Real);
            var pNextgen = insertCmd.Parameters.Add("$nextgen", SqliteType.Integer);
            var pType = insertCmd.Parameters.Add("$type", SqliteType.Text);
            var pCity = insertCmd.Parameters.Add("$city", SqliteType.Text);
            var pState = insertCmd.Parameters.Add("$state", SqliteType.Text);

            foreach (var t in towers)
            {
                pFac.Value = t.FacilityId;
                pCall.Value = t.CallSign;
                pChan.Value = t.TransmitChannel;
                pLat.Value = t.Latitude;
                pLon.Value = t.Longitude;
                pErp.Value = t.ErpKw;
                pHaat.Value = t.HaatMeters;
                pNextgen.Value = t.IsNextGenTv ? 1 : 0;
                pType.Value = t.ServiceType;
                pCity.Value = t.City;
                pState.Value = t.State;

                await insertCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<FccTower>> GetAllFccTowersAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM fcc_towers";

        var list = new List<FccTower>();
        await using var reader = await command.ExecuteReaderAsync();
        
        // Ensure we don't crash if an old DB schema without city/state is read
        var hasCityState = reader.FieldCount > 9;

        while (await reader.ReadAsync())
        {
            list.Add(new FccTower
            {
                FacilityId = reader.GetInt32(0),
                CallSign = reader.GetString(1),
                TransmitChannel = reader.GetInt32(2),
                Latitude = reader.GetDouble(3),
                Longitude = reader.GetDouble(4),
                ErpKw = reader.GetDouble(5),
                HaatMeters = reader.GetDouble(6),
                IsNextGenTv = reader.GetInt32(7) != 0,
                ServiceType = reader.GetString(8),
                City = hasCityState && !reader.IsDBNull(9) ? reader.GetString(9) : "",
                State = hasCityState && !reader.IsDBNull(10) ? reader.GetString(10) : ""
            });
        }
        return list;
    }

    public void Dispose()

    {
        _connection?.Dispose();
    }
}
