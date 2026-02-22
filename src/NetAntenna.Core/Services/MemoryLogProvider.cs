using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NetAntenna.Core.Services;

public class MemoryLogProvider : ILoggerProvider, IMemoryLogSink
{
    private readonly List<LogEntry> _logs = new();
    private readonly object _lock = new();

    public event EventHandler<LogEntry>? OnLogReceived;

    public ILogger CreateLogger(string categoryName)
    {
        return new MemoryLogger(this, categoryName);
    }

    public IEnumerable<LogEntry> GetRecentLogs(int count = 1000)
    {
        lock (_lock)
        {
            return _logs.TakeLast(count).ToList();
        }
    }

    internal void Log(LogEntry entry)
    {
        lock (_lock)
        {
            _logs.Add(entry);
            // Prune old logs to avoid eating all memory over time
            if (_logs.Count > 10000)
            {
                _logs.RemoveRange(0, 2000);
            }
        }
        OnLogReceived?.Invoke(this, entry);
    }

    public void Dispose()
    {
    }

    private class MemoryLogger : ILogger
    {
        private readonly MemoryLogProvider _provider;
        private readonly string _category;

        public MemoryLogger(MemoryLogProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = logLevel,
                Category = _category,
                Message = formatter(state, exception),
                Exception = exception
            };

            _provider.Log(entry);
        }
    }
}
