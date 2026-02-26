using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 日志服务 - 提供应用程序日志记录功能，支持日志数量限制和过滤
    /// </summary>
    public class LogService
    {
        private readonly List<LogEntry> _logEntries = new();
        private const int MaxLogLines = 1000;

        public event EventHandler<LogEventArgs>? LogUpdated;

        public void AddLog(string message, LogLevel level = LogLevel.Info)
        {
            _logEntries.Add(new LogEntry { Timestamp = DateTime.Now, Level = level, Message = message });
            if (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(0);
            NotifyLogUpdated();
        }

        public void ClearLog()
        {
            _logEntries.Clear();
            NotifyLogUpdated();
        }

        public string GetFullLog(LogLevel? filterLevel = null)
        {
            var entries = filterLevel.HasValue ? _logEntries.Where(e => e.Level == filterLevel.Value) : _logEntries;
            var sb = new StringBuilder();
            foreach (var entry in entries) sb.AppendLine(entry.ToString());
            return sb.ToString();
        }

        public int GetLineCount(LogLevel? filterLevel = null)
            => filterLevel.HasValue ? _logEntries.Count(e => e.Level == filterLevel.Value) : _logEntries.Count;

        private void NotifyLogUpdated()
            => LogUpdated?.Invoke(this, new LogEventArgs
            {
                FullLog = GetFullLog(),
                LineCount = _logEntries.Count,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                LastEntry = _logEntries.LastOrDefault()
            });
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public override string ToString() => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}";
    }

    public enum LogLevel { Info, Warning, Error }

    public class LogEventArgs : EventArgs
    {
        public string FullLog { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public string LastUpdate { get; set; } = string.Empty;
        public LogEntry? LastEntry { get; set; }
    }
}
