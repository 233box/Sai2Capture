using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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
        private readonly string _logDirectory;
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly Task _writeTask;
        private volatile bool _isRunning = true;

        public LogService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _logFilePath = Path.Combine(_logDirectory, "crash.log");

            // 确保日志目录存在
            EnsureLogDirectoryExists();

            // 启动后台写入任务
            _writeTask = Task.Run(WriteLogWorker);
        }

        public event EventHandler<LogEventArgs>? LogUpdated;

        public void AddLog(
            string message,
            LogLevel level = LogLevel.Info,
            string? threadType = null,
            [CallerMemberName] string? caller = null)
        {
            lock (_lock)
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    Message = message,
                    ThreadType = threadType ?? (Thread.CurrentThread.IsBackground ? "BG" : "UI"),
                    ThreadId = Environment.CurrentManagedThreadId.ToString(),
                    Caller = caller
                };

                _logEntries.Add(entry);
                if (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(0);

                QueueLogEntry(entry);
            }
            NotifyLogUpdated();
        }

        public void AddLogStructured(
            string message,
            LogLevel level,
            string? threadType = null,
            Dictionary<string, object>? context = null,
            [CallerMemberName] string? caller = null)
        {
            lock (_lock)
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    Message = message,
                    ThreadType = threadType ?? (Thread.CurrentThread.IsBackground ? "BG" : "UI"),
                    ThreadId = Environment.CurrentManagedThreadId.ToString(),
                    Caller = caller,
                    Context = context
                };

                _logEntries.Add(entry);
                if (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(0);

                QueueLogEntry(entry);
            }
            NotifyLogUpdated();
        }

#if DEBUG
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Debug(string message, string? threadType = null, [CallerMemberName] string? caller = null)
        {
            AddLog(message, LogLevel.Debug, threadType, caller);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DebugStructured(string message, Dictionary<string, object>? context = null, string? threadType = null, [CallerMemberName] string? caller = null)
        {
            AddLogStructured(message, LogLevel.Debug, threadType, context, caller);
        }
#else
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Debug(string message, string? threadType = null, [CallerMemberName] string? caller = null) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DebugStructured(string message, Dictionary<string, object>? context = null, string? threadType = null, [CallerMemberName] string? caller = null) { }
#endif

        private void EnsureLogDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch
            {
                // 目录创建失败时静默处理
            }
        }

        private void QueueLogEntry(LogEntry entry)
        {
            _logQueue.Enqueue(entry.ToFileString() + Environment.NewLine);
        }

        private async Task WriteLogWorker()
        {
            var batch = new List<string>();
            while (_isRunning || !_logQueue.IsEmpty)
            {
                // 批量收集日志条目
                batch.Clear();
                while (_logQueue.TryDequeue(out string? line) && batch.Count < 100)
                {
                    if (line != null) batch.Add(line);
                }

                if (batch.Count > 0)
                {
                    try
                    {
                        await File.AppendAllTextAsync(_logFilePath, string.Concat(batch));
                    }
                    catch
                    {
                        // 文件写入失败时静默处理，避免影响主流程
                    }
                }
                else
                {
                    // 没有日志时短暂等待，减少 CPU 占用
                    await Task.Delay(100);
                }
            }
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

        /// <summary>
        /// 停止日志服务，等待所有日志写入完成
        /// </summary>
        public async Task ShutdownAsync()
        {
            _isRunning = false;
            try
            {
                await _writeTask;
            }
            catch
            {
                // 忽略关闭时的异常
            }
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ThreadType { get; set; }
        public string? ThreadId { get; set; }
        public string? Caller { get; set; }
        public Dictionary<string, object>? Context { get; set; }

        public override string ToString() => $"[{Timestamp:HH:mm:ss}] [{ThreadType ?? "MAIN",-4}] [{Level,-7}] {Message}";

        public string ToFileString()
        {
            var baseStr = $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{ThreadType ?? "MAIN"}:{ThreadId ?? "?"}] [{Level}] {Message}";
            if (Context != null && Context.Count > 0)
            {
                var contextStr = string.Join(", ", Context.Select(kv => $"{kv.Key}={kv.Value}"));
                return $"{baseStr} | {{{contextStr}}}";
            }
            return baseStr;
        }
    }

    public enum LogLevel { Debug, Info, Warning, Error }

    public class LogEventArgs : EventArgs
    {
        public string FullLog { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public string LastUpdate { get; set; } = string.Empty;
        public LogEntry? LastEntry { get; set; }
    }
}
