using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 日志服务
    /// 提供应用程序日志记录功能，支持日志数量限制和过滤
    /// </summary>
    public class LogService
    {
        private readonly List<LogEntry> _logEntries = new List<LogEntry>();
        private const int MaxLogLines = 1000; // 最大保留日志行数

        /// <summary>
        /// 日志更新事件
        /// </summary>
        public event EventHandler<LogEventArgs>? LogUpdated;

        /// <summary>
        /// 添加日志消息
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别</param>
        public void AddLog(string message, LogLevel level = LogLevel.Info)
        {
            var timestamp = DateTime.Now;
            var entry = new LogEntry
            {
                Timestamp = timestamp,
                Level = level,
                Message = message
            };
            
            _logEntries.Add(entry);
            
            // 限制日志数量，保留最新的日志
            if (_logEntries.Count > MaxLogLines)
            {
                _logEntries.RemoveAt(0);
            }
            
            NotifyLogUpdated();
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public void ClearLog()
        {
            _logEntries.Clear();
            NotifyLogUpdated();
        }

        /// <summary>
        /// 获取完整日志内容
        /// </summary>
        public string GetFullLog(LogLevel? filterLevel = null)
        {
            var entries = filterLevel.HasValue
                ? _logEntries.Where(e => e.Level == filterLevel.Value)
                : _logEntries;

            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                sb.AppendLine(entry.ToString());
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取日志行数
        /// </summary>
        public int GetLineCount(LogLevel? filterLevel = null)
        {
            return filterLevel.HasValue 
                ? _logEntries.Count(e => e.Level == filterLevel.Value)
                : _logEntries.Count;
        }

        /// <summary>
        /// 通知日志更新
        /// </summary>
        private void NotifyLogUpdated()
        {
            LogUpdated?.Invoke(this, new LogEventArgs
            {
                FullLog = GetFullLog(),
                LineCount = _logEntries.Count,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                LastEntry = _logEntries.LastOrDefault()
            });
        }
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}";
        }
    }

    /// <summary>
    /// 日志级别枚举
    /// </summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 日志事件参数
    /// </summary>
    public class LogEventArgs : EventArgs
    {
        public string FullLog { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public string LastUpdate { get; set; } = string.Empty;
        public LogEntry? LastEntry { get; set; }
    }
}
