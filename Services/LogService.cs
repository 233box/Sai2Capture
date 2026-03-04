using System.IO;
using System.Text;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 日志服务 - 提供应用程序日志记录功能，支持日志数量限制和过滤
    /// </summary>
    public class LogService
    {
        private readonly List<LogEntry> _logEntries = new();
        private readonly object _lock = new();
        private const int MaxLogLines = 1000;
        private readonly string _logFilePath;

        public LogService()
        {
            // 日志文件路径：程序目录下的 crash.log
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
        }

        public event EventHandler<LogEventArgs>? LogUpdated;

        public void AddLog(string message, LogLevel level = LogLevel.Info)
        {
            lock (_lock)
            {
                _logEntries.Add(new LogEntry { Timestamp = DateTime.Now, Level = level, Message = message });
                if (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(0);
                
                // 同步写入文件（确保崩溃时日志不丢失）
                try
                {
                    File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
                }
                catch
                {
                    // 文件写入失败时不抛出异常，避免影响主流程
                }
            }
            NotifyLogUpdated();
        }

        public void ClearLog()
        {
            lock (_lock)
            {
                _logEntries.Clear();
            }
            NotifyLogUpdated();
        }

        /// <summary>
        /// 获取完整日志（用于 UI 显示）
        /// </summary>
        public string GetFullLog(LogLevel? filterLevel = null)
        {
            lock (_lock)
            {
                var entries = filterLevel.HasValue ? _logEntries.Where(e => e.Level == filterLevel.Value).ToList() : _logEntries.ToList();
                var sb = new StringBuilder();
                foreach (var entry in entries) sb.AppendLine(entry.ToString());
                return sb.ToString();
            }
        }

        /// <summary>
        /// 获取日志行数
        /// </summary>
        public int GetLineCount(LogLevel? filterLevel = null)
        {
            lock (_lock)
            {
                return filterLevel.HasValue ? _logEntries.Count(e => e.Level == filterLevel.Value) : _logEntries.Count;
            }
        }

        /// <summary>
        /// 导出日志到文件
        /// </summary>
        public void ExportLogToFile(string filePath)
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                foreach (var entry in _logEntries) sb.AppendLine(entry.ToString());
                File.WriteAllText(filePath, sb.ToString());
            }
        }

        /// <summary>
        /// 获取日志文件路径
        /// </summary>
        public string GetLogFilePath() => _logFilePath;

        private void NotifyLogUpdated()
        {
            LogEntry? lastEntry;
            string fullLog;
            int lineCount;
            
            lock (_lock)
            {
                lastEntry = _logEntries.LastOrDefault();
                fullLog = GetFullLog();
                lineCount = _logEntries.Count;
            }
            
            LogUpdated?.Invoke(this, new LogEventArgs
            {
                FullLog = fullLog,
                LineCount = lineCount,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                LastEntry = lastEntry
            });
        }
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
