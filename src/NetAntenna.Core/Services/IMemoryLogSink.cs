using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace NetAntenna.Core.Services;

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public interface IMemoryLogSink
{
    event EventHandler<LogEntry> OnLogReceived;
    IEnumerable<LogEntry> GetRecentLogs(int count = 1000);
    void Clear();
}
