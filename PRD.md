# Net Antenna — Product Requirements Document (PRD)

**Version:** 1.0  
**Last Updated:** 2026-02-22  
**License:** MIT  
**Repository:** `github.com/LstDtchMn/Net-Antenna`

---

## 1. Product Overview

Net Antenna is an open-source, diagnostic-first OTA (Over-The-Air) TV antenna management and signal profiling tool. It interfaces with local HDHomeRun tuners to scan, monitor, and profile broadcast channels — bridging the gap between physical RF realities and local network streaming.

### Core Value Proposition

> **Before Net Antenna:** "My recording failed last night and I don't know why."  
> **After Net Antenna:** "Signal dropped to 42% SEQ at 2:17 AM during heavy rain. Channel 7.1 needs a better line-of-sight to the tower at 247°."

### Target Users

- Cord-cutters using HDHomeRun tuners for OTA TV
- Homelab enthusiasts running Plex/Jellyfin DVR setups
- Anyone troubleshooting OTA signal issues (dropouts, interference, antenna aiming)

---

## 2. System Architecture

### "Standalone First, Companion Optional"

```
┌─────────────────────────────────────────────────┐
│              NET ANTENNA DESKTOP                 │
│         (Avalonia UI / .NET 10 LTS)             │
│                                                  │
│  ┌─────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │Discovery│ │ HTTP     │ │  Signal Logger   │ │
│  │ Service │ │ Client   │ │  (Background)    │ │
│  │ UDP     │ │ REST     │ │  Poll → SQLite   │ │
│  └────┬────┘ └────┬─────┘ └────────┬─────────┘ │
│       │           │                │             │
│       ▼           ▼                ▼             │
│  ┌──────────────────────────────────────────┐   │
│  │           SQLite Database               │   │
│  │         (WAL mode, local file)           │   │
│  └──────────────────────────────────────────┘   │
│                                                  │
│  ┌─────────┐ ┌──────────┐ ┌──────────────────┐ │
│  │Dashboard│ │ Channel  │ │   Settings       │ │
│  │  View   │ │ Manager  │ │   View           │ │
│  └─────────┘ └──────────┘ └──────────────────┘ │
└───────────────────┬─────────────────────────────┘
                    │ (optional)
                    │ REST + WebSocket
                    ▼
┌─────────────────────────────────────────────────┐
│          COMPANION CONTAINER (Phase 4)          │
│              (Go / Docker)                       │
│                                                  │
│  • 24/7 background signal logging                │
│  • M3U/XMLTV API generation                      │
│  • Home Assistant MQTT bridge                    │
│  • Plex DVR Pre-Flight API                       │
└─────────────────────────────────────────────────┘
```

### Technology Stack

| Component | Technology | Rationale |
|---|---|---|
| **Runtime** | .NET 10 LTS | Long-term support through Nov 2028 |
| **UI Framework** | Avalonia UI (FluentTheme) | Cross-platform (Win/Mac/Linux), WinUI 3 look-and-feel, mature ecosystem |
| **MVVM** | CommunityToolkit.Mvvm | Microsoft-backed, source generators, minimal boilerplate |
| **Charting** | ScottPlot (Avalonia) | Fastest rendering for large datasets, native signal/scientific plotting |
| **Database** | SQLite via Microsoft.Data.Sqlite | Zero-config, single-file, WAL mode for concurrent read/write |
| **Maps (Phase 2)** | WebView2 + Leaflet.js | OpenStreetMap tiles (free), full-featured, no API key required |
| **Weather (Phase 2)** | NWS API | Free, US-only, official NOAA data |
| **Docker Companion** | Go | Low memory footprint, high concurrency, tiny binary |
| **Companion Protocol** | REST + WebSocket | REST for config/queries, WebSocket for live telemetry push |
| **Companion Discovery** | mDNS + manual fallback | Auto-discover on LAN, manual IP entry as fallback |
| **License** | MIT | Maximum permissive |
| **Distribution** | GitHub Releases | `.exe` installer via InnoSetup or platform-native packages |

---

## 3. HDHomeRun API Reference

All features are built on these local HTTP/UDP endpoints exposed by HDHomeRun hardware:

### Discovery

| Method | Endpoint | Purpose |
|---|---|---|
| UDP Broadcast | Port 65001 | Discover devices on local network |
| HTTP Fallback | `http://my.hdhomerun.com/discover` | Secondary discovery (may not be live) |

### Device HTTP API

| Method | Endpoint | Returns |
|---|---|---|
| `GET` | `/discover.json` | `DeviceID`, `DeviceAuth`, `FriendlyName`, `ModelNumber`, `FirmwareVersion`, `TunerCount`, `BaseURL`, `LineupURL` |
| `GET` | `/lineup.json` | Array of `{GuideNumber, GuideName, URL, Tags}` |
| `GET` | `/lineup.m3u` | M3U playlist of all channels |
| `GET` | `/tuner{n}/status` | Key-value pairs: `ch`, `lock`, `ss`, `snq`, `seq`, `bps`, `pps` |
| `POST` | `/lineup.post` | Modify channel visibility (hide/show/favorite) |

### Signal Metrics Glossary

| Metric | Field | Range | Meaning |
|---|---|---|---|
| Signal Strength | `ss` | 0–100 | Raw power level at antenna |
| Signal-to-Noise Quality | `snq` | 0–100 | Digital clarity after demodulation |
| Symbol Quality | `seq` | 0–100 | % of symbols decoded correctly (most important for recording quality) |

> [!IMPORTANT]
> **SEQ is the critical metric.** A channel can have high SS but low SEQ (strong but noisy signal). SEQ < 80% typically causes visible artifacts. SEQ < 50% will cause recording failures.

---

## 4. Feature Requirements by Phase

### Phase 1 — Core MVP

The minimum viable product that delivers the core value loop: **discover → poll → log → visualize → manage**.

#### F1.1 HDHomeRun Auto-Discovery

- Send UDP broadcast to port 65001, parse responses to extract device IPs
- Call `/discover.json` on each discovered device for full metadata
- Support multiple concurrent HDHomeRun devices from day one
- Re-scan on demand and periodic background re-scan (every 60s)
- Persist discovered devices to SQLite with `last_seen` timestamp

**Acceptance Criteria:**
- [ ] Discovers all HDHomeRun devices on the same subnet within 5 seconds
- [ ] Handles 0 devices gracefully (empty state UI with troubleshooting tips)
- [ ] Handles 1–4 devices simultaneously
- [ ] Manual IP entry as fallback when UDP broadcast fails (VLAN scenarios)

#### F1.2 Continuous Signal Logging

- Background service polls `/tuner{n}/status` for all active tuners
- Configurable polling interval: 1–60 seconds (default: 5 seconds)
- Writes `SignalSample` records to SQLite in batched transactions
- Data retention: user-configurable, default 30 days, with periodic cleanup
- Raises real-time events for UI update (`SignalSampleReceived`)

**Acceptance Criteria:**
- [ ] Logs SS/SNQ/SEQ for every active tuner at the configured interval
- [ ] SQLite writes are batched (every 10 samples or 30 seconds)
- [ ] Does not consume more than 50MB RAM while logging 4 tuners at 1s intervals
- [ ] Gracefully handles tuner disconnection mid-logging
- [ ] Old data is automatically purged per retention policy

#### F1.3 Dashboard — Live Telemetry

**Top Pane — Signal Tiles:**
- Three large indicator tiles: SS, SNQ, SEQ
- Color-coded: Green (>80), Yellow (50–80), Red (<50)
- Current channel info, lock type, bitrate (bps), packets/sec (pps)
- Tuner selector (when device has multiple tuners)

**Bottom Pane — Time-Series Charts (ScottPlot):**
- Three overlaid signal lines (SS, SNQ, SEQ) plotted over time
- Zoomable/pannable X-axis
- Auto-scrolling live mode with freeze toggle
- Time window presets: Last 1h / 6h / 24h / All
- Crosshair with tooltip showing exact values at hover point

**Acceptance Criteria:**
- [ ] Tiles update within 1 second of a new sample
- [ ] Chart renders 86,400 data points (24h at 1s intervals) without lag
- [ ] Chart auto-scrolls smoothly during live mode
- [ ] Switching tuners instantly updates both tiles and chart

#### F1.4 Channel Manager

- `DataGrid` or equivalent showing all channels from `/lineup.json`
- Columns: Guide Number, Channel Name, Favorite ⭐, Hidden 👁, Last SS/SNQ/SEQ
- Individual toggle for favorite/hidden per channel
- **"Hide Weak Channels"** bulk action: hides channels with avg SEQ below configurable threshold
- Changes pushed to HDHomeRun via `POST /lineup.post`
- Import/Export channel configuration as JSON backup

**Acceptance Criteria:**
- [ ] Full channel list loads within 2 seconds
- [ ] Hide/Favorite toggles take effect on the HDHomeRun within 1 second
- [ ] "Hide Weak Channels" correctly filters using signal data from the logging period
- [ ] Export/Import preserves all hide/favorite states

#### F1.5 Settings

- **Devices:** List of discovered devices, manual IP entry, rescan button
- **Polling:** Interval slider (1s–60s), data retention slider (7d–365d)
- **Antenna Profile:** Type dropdown (directional UHF, omnidirectional VHF/UHF, attic-mount, outdoor, indoor), height AGL input — stored for Phase 2 prediction engine
- **Appearance:** Theme (dark/light/system), accent color
- **About:** Version, GitHub link, open-source licenses

**Acceptance Criteria:**
- [ ] All settings persist across app restart (SQLite `settings` table)
- [ ] Changing polling interval takes effect immediately without restart
- [ ] Antenna profile fields are stored but not yet used (Phase 2 prep)

---

### Phase 2 — Intelligence

#### F2.1 FCC Data Integration & Tower Map

- Download and index FCC LMS broadcast tower database locally
- Use FCC Elevation/HAAT APIs for terrain-aware calculations
- Interactive map via WebView2 + Leaflet.js:
  - Plot all nearby broadcast towers with distance and azimuth
  - Color-code towers based on real-time signal quality from logged data
  - User location marker with adjustable position
- Tower list table as a non-map fallback

> [!NOTE]
> **RabbitEars.info has no public API.** The prediction engine must be built on raw FCC data. Reference RabbitEars' methodology but do not depend on their service.

#### F2.2 RF Prediction Engine

- Cross-reference user location, antenna profile, and FCC tower data
- Account for terrain elevation using FCC Elevation API
- Predict expected signal strength per channel before scanning
- Output: ranked list of receivable channels with confidence scores

#### F2.3 Antenna Aiming Assistant

- Compass rose overlay showing optimal antenna bearing
- Calculate best single-direction compromise when multiple towers are in different directions
- Live signal feedback while user rotates antenna

#### F2.4 Channel Spectrum Overview

- Per-channel signal quality heatmap across the UHF band (channels 14–51)
- Color-coded bars: green (strong), yellow (marginal), red (weak), gray (empty)
- Overlay marking the 600MHz 5G interference danger zone (T-Mobile n71 band)
- Iterates through all known TV frequencies and records metrics per-channel

> [!CAUTION]
> **This is NOT a spectrum waterfall.** The HDHomeRun is a TV tuner, not an SDR. It cannot perform broadband frequency sweeps or detect non-broadcast emissions. This feature scans channel-by-channel and presents results as a heatmap.

#### F2.5 Weather Correlation

- Integrate with NWS API (free, US-only) for precipitation/humidity data
- Overlay weather events on signal time-series charts
- Annotate signal drops with matching weather conditions
- Help users distinguish weather-related dropouts from hardware/antenna issues

---

### Phase 3 — Diagnostics

#### F3.1 Network & VLAN Diagnostic Toolkit

- Test UDP broadcast reachability on port 65001
- Test mDNS reflection (`.local` hostname resolution)
- Test IGMP snooping behavior
- Generate diagnostic report with pass/fail results and fix recommendations
- Specific guidance for Ubiquiti/UniFi network setups (most common pain point)

#### F3.2 Smart Scan Recommendations

- Perform a rapid 60-second baseline sweep across all channels
- Analyze initial signal volatility (standard deviation of SEQ over the sweep)
- Recommend a data-gathering timeframe:
  - Low volatility → 1-hour quick log
  - Medium volatility → 6-hour extended log
  - High volatility → 24-hour overnight soak test
- Provide reasoning for the recommendation

#### F3.3 ATSC 3.0 PLP Monitoring (Beta)

- Track Physical Layer Pipe (PLP) data for ATSC 3.0 broadcasts when available
- Display PLP robustness levels and modulation info
- Flag DRM-encrypted channels vs. unencrypted

> [!WARNING]
> **ATSC 3.0 DRM is still in flux.** SiliconDust became an ATSC 3.0 Certificate Authority in late 2025, but encrypted channel support may not be stable. This feature should be labeled Beta and gracefully degrade when PLP data is unavailable.

#### F3.4 Diagnostic Export

- One-click export of a diagnostic bundle as ZIP:
  - Signal logs (last 48h)
  - Device info and firmware versions
  - Network diagnostic results
  - App configuration (sanitized)
- Designed for attaching to GitHub issues or forum posts

---

### Phase 4 — Ecosystem Integrations

#### F4.1 Docker Companion Container (Go)

- Headless container for 24/7 background operation
- Continuous signal logging independent of desktop app
- REST API for configuration and historical data queries
- WebSocket endpoint for real-time telemetry streaming
- Auto-discovery via mDNS (`_netantenna._tcp.local`), manual IP fallback
- Simple API key authentication (generated on first run)
- `docker-compose.yml` with Traefik-compatible labels for reverse proxy

**Communication Protocol:**

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v1/devices` | `GET` | List discovered HDHomeRun devices |
| `/api/v1/devices/{id}/status` | `GET` | Current tuner status for a device |
| `/api/v1/samples` | `GET` | Query historical signal samples (time range, device, channel filters) |
| `/api/v1/config` | `GET/PUT` | Read/write companion configuration |
| `/ws/v1/telemetry` | WebSocket | Subscribe to real-time signal samples |

#### F4.2 Plex DVR Pre-Flight API

- Exposes local endpoint that Plex can ping before scheduled recordings
- Checks SEQ of the target channel 5 minutes before recording start
- Returns pass/warn/fail status with current signal metrics
- Logs weather-related signal failures before a corrupted recording is generated

#### F4.3 Home Assistant Integration (HACS)

- Docker companion publishes tuner metrics via REST for HA polling
- Custom HACS component providing:
  - Signal strength sensors (SS, SNQ, SEQ per tuner)
  - Binary sensor for "signal good" / "signal bad"
  - Device tracker for HDHomeRun online/offline status
- Automation triggers: "if SEQ drops below X, send notification"

#### F4.4 Automated Channel Pruning

- Analyze signal data from the logging period
- Recommend channels to hide based on configurable quality thresholds
- Show before/after comparison of channel list
- One-click apply via `POST /lineup.post`
- Reversible: export config before applying, one-click restore

---

## 5. Data Model

### SQLite Schema

```sql
-- Enable WAL mode for concurrent reads during writes
PRAGMA journal_mode=WAL;

-- Discovered HDHomeRun devices
CREATE TABLE IF NOT EXISTS devices (
    device_id       TEXT PRIMARY KEY,
    friendly_name   TEXT,
    model_number    TEXT,
    firmware_version TEXT,
    tuner_count     INTEGER,
    base_url        TEXT,
    ip_address      TEXT,
    last_seen_unix_ms INTEGER NOT NULL
);

-- Time-series signal samples (core data)
CREATE TABLE IF NOT EXISTS signal_samples (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id       TEXT NOT NULL,
    tuner_index     INTEGER NOT NULL,
    channel         TEXT,
    lock_type       TEXT,
    ss              INTEGER,
    snq             INTEGER,
    seq             INTEGER,
    bps             INTEGER,
    pps             INTEGER,
    timestamp_unix_ms INTEGER NOT NULL,
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);
CREATE INDEX IF NOT EXISTS idx_samples_device_time
    ON signal_samples(device_id, timestamp_unix_ms);
CREATE INDEX IF NOT EXISTS idx_samples_channel_time
    ON signal_samples(channel, timestamp_unix_ms);

-- Channel lineup per device
CREATE TABLE IF NOT EXISTS channel_lineup (
    device_id       TEXT NOT NULL,
    guide_number    TEXT NOT NULL,
    guide_name      TEXT,
    stream_url      TEXT,
    is_favorite     INTEGER DEFAULT 0,
    is_hidden       INTEGER DEFAULT 0,
    last_ss         INTEGER,
    last_snq        INTEGER,
    last_seq        INTEGER,
    last_updated_unix_ms INTEGER,
    PRIMARY KEY (device_id, guide_number),
    FOREIGN KEY (device_id) REFERENCES devices(device_id)
);

-- Key-value settings store
CREATE TABLE IF NOT EXISTS settings (
    key             TEXT PRIMARY KEY,
    value           TEXT NOT NULL
);

-- Schema versioning for migrations
CREATE TABLE IF NOT EXISTS schema_version (
    version         INTEGER PRIMARY KEY,
    applied_unix_ms INTEGER NOT NULL
);
```

### Default Settings

| Key | Default | Description |
|---|---|---|
| `polling_interval_sec` | `5` | Signal polling frequency |
| `data_retention_days` | `30` | Auto-purge data older than this |
| `seq_threshold_weak` | `50` | SEQ below this = "weak" channel |
| `seq_threshold_good` | `80` | SEQ above this = "good" channel |
| `theme` | `dark` | UI theme preference |
| `antenna_type` | `null` | User's antenna type (Phase 2) |
| `antenna_height_ft` | `null` | Antenna height AGL (Phase 2) |
| `user_lat` | `null` | User latitude (Phase 2) |
| `user_lng` | `null` | User longitude (Phase 2) |

---

## 6. Project Structure

```
Net-Antenna/
├── NetAntenna.sln
├── PRD.md                                  # This document
├── LICENSE                                 # MIT
├── README.md
├── .gitignore
│
├── src/
│   ├── NetAntenna.Core/                    # Platform-agnostic class library
│   │   ├── NetAntenna.Core.csproj          # Target: net10.0
│   │   ├── Models/
│   │   │   ├── HdHomeRunDevice.cs
│   │   │   ├── TunerStatus.cs
│   │   │   ├── ChannelInfo.cs
│   │   │   └── SignalSample.cs
│   │   ├── Services/
│   │   │   ├── IDeviceDiscovery.cs
│   │   │   ├── DeviceDiscoveryService.cs   # UDP 65001 + HTTP fallback
│   │   │   ├── ITunerClient.cs
│   │   │   ├── TunerHttpClient.cs          # /discover.json, /lineup.json, /tuner{n}/status
│   │   │   ├── ISignalLogger.cs
│   │   │   └── SignalLoggerService.cs      # Background polling → SQLite
│   │   └── Data/
│   │       ├── IDatabaseService.cs
│   │       ├── DatabaseService.cs          # SQLite CRUD + migrations
│   │       └── Migrations.cs
│   │
│   └── NetAntenna.Desktop/                 # Avalonia UI desktop app
│       ├── NetAntenna.Desktop.csproj       # Target: net10.0
│       ├── App.axaml / App.axaml.cs        # DI container, app lifecycle
│       ├── MainWindow.axaml / .cs          # Shell with NavigationView sidebar
│       ├── Program.cs                      # Entry point
│       ├── Assets/
│       │   └── app-icon.ico
│       ├── Styles/
│       │   └── AppTheme.axaml             # Dark theme overrides, color tokens
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── DashboardViewModel.cs
│       │   ├── ChannelManagerViewModel.cs
│       │   └── SettingsViewModel.cs
│       └── Views/
│           ├── DashboardView.axaml / .cs
│           ├── ChannelManagerView.axaml / .cs
│           └── SettingsView.axaml / .cs
│
├── tests/
│   └── NetAntenna.Core.Tests/
│       ├── NetAntenna.Core.Tests.csproj    # xUnit + Moq + FluentAssertions
│       ├── Services/
│       │   ├── DeviceDiscoveryServiceTests.cs
│       │   ├── TunerHttpClientTests.cs
│       │   └── SignalLoggerServiceTests.cs
│       └── Data/
│           └── DatabaseServiceTests.cs
│
└── companion/                              # Phase 4 — Go Docker companion
    ├── Dockerfile
    ├── docker-compose.yml
    ├── go.mod
    ├── main.go
    └── ...
```

---

## 7. UI/UX Specification

### Design Language

- **Framework:** Avalonia FluentTheme (matches Windows 11 / WinUI 3 aesthetic)
- **Default Theme:** Dark mode
- **Typography:** Segoe UI Variable (Windows), Inter (cross-platform fallback)
- **Data Density:** High — this is a diagnostic tool, not a consumer app. Show data, not whitespace.
- **Accent Color:** Signal-themed — default cyan/teal (#00BCD4)

### Navigation

```
┌───────────────────────────────────────────────────────┐
│  [📡 Net Antenna]  [Device: HDHR-1234ABCD ▼] [🟢 Connected] [⚡ Quick Scan]  │
├──────────┬────────────────────────────────────────────┤
│          │                                            │
│ 📊 Dash  │   ┌──────────────────────────────────────┐│
│          │   │  SS: 85%   SNQ: 92%   SEQ: 100%     ││
│ 📺 Chan  │   │  [GREEN]   [GREEN]    [GREEN]        ││
│          │   │  Ch: 7.1 WABC | Lock: 8vsb | 19.4Mbps││
│ ⚙ Set   │   ├──────────────────────────────────────┤│
│          │   │                                      ││
│          │   │  ┌─── ScottPlot Time-Series ───────┐ ││
│          │   │  │ SS  ─── SNQ ─── SEQ ───         │ ││
│          │   │  │                                  │ ││
│          │   │  │  100% ┤                          │ ││
│          │   │  │       │    ~~~~~~~~~~~~~~~~~~~~  │ ││
│          │   │  │   50% ┤                          │ ││
│          │   │  │       │                          │ ││
│          │   │  │    0% ┤──────────────────────── │ ││
│          │   │  │       12:00  13:00  14:00  15:00 │ ││
│          │   │  └─────────────────────────────────┘ ││
│          │   │  [1h] [6h] [24h] [All]  [▶ Live]    ││
│          │   └──────────────────────────────────────┘│
└──────────┴────────────────────────────────────────────┘
```

### Color Coding Thresholds

| Signal Level | SEQ Range | Color | Hex |
|---|---|---|---|
| Excellent | 80–100% | Green | `#4CAF50` |
| Marginal | 50–79% | Yellow/Amber | `#FFC107` |
| Poor | 0–49% | Red | `#F44336` |
| No Signal | N/A | Gray | `#757575` |

---

## 8. Non-Functional Requirements

### Performance

| Metric | Target |
|---|---|
| App cold start | < 3 seconds |
| Device discovery | < 5 seconds on same subnet |
| Signal polling overhead | < 5% CPU per tuner at 1s intervals |
| Memory usage (4 tuners, 1s polling) | < 100 MB |
| Chart rendering (86,400 points) | < 500ms |
| SQLite write batch | < 50ms per batch of 10 samples |
| Data retention cleanup | Runs nightly, < 5 seconds |

### Reliability

- Signal logger must survive tuner disconnection and auto-reconnect
- SQLite must use WAL mode to prevent corruption from unexpected shutdown
- All HTTP calls to HDHomeRun must have timeout (5 second default)
- App must not crash if HDHomeRun firmware returns unexpected responses

### Security

- All SQLite queries use parameterized statements (no string concatenation)
- Docker companion API: API key authentication (generated on first run)
- No data leaves the local network unless user explicitly configures external access
- No telemetry, no analytics, no phone-home

---

## 9. Development Phases & Milestones

| Phase | Scope | Key Deliverables |
|---|---|---|
| **Phase 1: Core MVP** | Discovery, logging, charts, channel manager | Shippable `.exe`, SQLite storage, real-time dashboard |
| **Phase 2: Intelligence** | FCC data, tower map, prediction engine, weather, spectrum overview | Map view, antenna aiming, weather correlation |
| **Phase 3: Diagnostics** | VLAN toolkit, smart scan, ATSC 3.0 beta, diagnostic export | Network troubleshooting, diagnostic ZIP export |
| **Phase 4: Ecosystem** | Docker companion, Plex pre-flight, Home Assistant HACS | Go container, REST+WebSocket API, HACS component |

---

## 10. NuGet Dependencies (Phase 1)

### NetAntenna.Core

| Package | Purpose |
|---|---|
| `Microsoft.Data.Sqlite` | SQLite database access |
| `System.Text.Json` | JSON serialization (built-in with .NET 10) |

### NetAntenna.Desktop

| Package | Purpose |
|---|---|
| `Avalonia` | UI framework |
| `Avalonia.Desktop` | Desktop platform support |
| `Avalonia.Themes.Fluent` | WinUI 3 look-and-feel theme |
| `CommunityToolkit.Mvvm` | MVVM base classes + source generators |
| `ScottPlot.Avalonia` | Time-series charting control |
| `Microsoft.Extensions.DependencyInjection` | DI container |

### NetAntenna.Core.Tests

| Package | Purpose |
|---|---|
| `xunit` | Test framework |
| `xunit.runner.visualstudio` | VS/CLI test runner |
| `Moq` | Interface mocking |
| `FluentAssertions` | Readable test assertions |
| `Microsoft.NET.Test.Sdk` | Test SDK |

---

## 11. Agent Implementation Notes

> [!NOTE]
> This section provides guidance for AI coding agents implementing this PRD.

### Key Interfaces to Implement First

Build these interfaces before any implementation — they define the entire service boundary:

```csharp
// Device discovery — UDP + HTTP
public interface IDeviceDiscovery
{
    Task<IReadOnlyList<HdHomeRunDevice>> DiscoverDevicesAsync(
        TimeSpan timeout, CancellationToken ct = default);
    Task<HdHomeRunDevice?> GetDeviceByIpAsync(
        string ipAddress, CancellationToken ct = default);
}

// Tuner HTTP client — all HDHomeRun REST calls
public interface ITunerClient
{
    Task<HdHomeRunDevice> GetDeviceInfoAsync(string baseUrl, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelInfo>> GetLineupAsync(string baseUrl, CancellationToken ct = default);
    Task<TunerStatus> GetTunerStatusAsync(string baseUrl, int tunerIndex, CancellationToken ct = default);
    Task SetChannelVisibilityAsync(string baseUrl, string guideNumber, bool visible, CancellationToken ct = default);
}

// Signal logger — background polling orchestrator
public interface ISignalLogger
{
    event EventHandler<SignalSample>? SignalSampleReceived;
    Task StartAsync(string deviceId, TimeSpan interval, CancellationToken ct = default);
    Task StopAsync();
    bool IsRunning { get; }
}

// Database — all persistence
public interface IDatabaseService
{
    Task InitializeAsync();
    Task UpsertDeviceAsync(HdHomeRunDevice device);
    Task InsertSamplesAsync(IEnumerable<SignalSample> samples);
    Task<IReadOnlyList<SignalSample>> GetSamplesAsync(string deviceId, long fromUnixMs, long toUnixMs);
    Task<IReadOnlyList<ChannelInfo>> GetChannelLineupAsync(string deviceId);
    Task UpsertChannelAsync(string deviceId, ChannelInfo channel);
    Task<string?> GetSettingAsync(string key);
    Task SetSettingAsync(string key, string value);
    Task PurgeOldSamplesAsync(int retentionDays);
}
```

### HDHomeRun Response Parsing

**`/discover.json` returns JSON:**
```json
{
  "FriendlyName": "HDHomeRun FLEX 4K",
  "ModelNumber": "HDHR5-4K",
  "FirmwareName": "hdhomerun5_atsc",
  "FirmwareVersion": "20231214",
  "DeviceID": "1234ABCD",
  "DeviceAuth": "abc123def456",
  "TunerCount": 4,
  "BaseURL": "http://192.168.1.100:80",
  "LineupURL": "http://192.168.1.100:80/lineup.json"
}
```

**`/lineup.json` returns JSON array:**
```json
[
  {"GuideNumber": "7.1", "GuideName": "WABC", "URL": "http://192.168.1.100:5004/auto/v7.1"},
  {"GuideNumber": "7.2", "GuideName": "LAFF", "URL": "http://192.168.1.100:5004/auto/v7.2", "Tags": "favorite"}
]
```

**`/tuner0/status` returns key-value text (NOT JSON):**
```
ch=8vsb:7
lock=8vsb
ss=85
snq=92
seq=100
bps=19392712
pps=2412
```

> [!IMPORTANT]
> The tuner status endpoint returns **plain text key=value pairs**, not JSON. Parse accordingly with string splitting, not JSON deserialization.

### Multi-Device Data Model

The data model must support multiple HDHomeRun devices from day one. Every table that stores per-device data uses `device_id` as a foreign key. The UI device selector dropdown filters all views by the selected device.

### Avalonia Specifics

- Avalonia uses `.axaml` (not `.xaml`) for markup files
- Use `FluentTheme` with `DarkMode` for WinUI 3 appearance
- ScottPlot Avalonia control: `<ScottPlot:AvaPlot Name="SignalChart"/>`
- ViewModels should inherit from `ObservableObject` (CommunityToolkit.Mvvm)
- Use `[ObservableProperty]` and `[RelayCommand]` source generators
