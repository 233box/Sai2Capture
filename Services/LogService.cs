using System;
using System.Text;

namespace Sai2Capture.Services
{
    /// <summary>
    /// 日志服务
    /// 提供应用程序日志记录功能
    /// </summary>
    public class LogService
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private int _logLineCount = 0;

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
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var logEntry = $"[{timestamp}] [{level}] {message}";
            
            _logBuilder.AppendLine(logEntry);
            _logLineCount++;
            
            LogUpdated?.Invoke(this, new LogEventArgs
            {
                FullLog = _logBuilder.ToString(),
                LineCount = _logLineCount,
                LastUpdate = timestamp
            });
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public void ClearLog()
        {
            _logBuilder.Clear();
            _logLineCount = 0;
            
            LogUpdated?.Invoke(this, new LogEventArgs
            {
                FullLog = string.Empty,
                LineCount = 0,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        /// <summary>
        /// 获取完整日志内容
        /// </summary>
        public string GetFullLog() => _logBuilder.ToString();

        /// <summary>
        /// 获取日志行数
        /// </summary>
        public int GetLineCount() => _logLineCount;
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
    }
}
