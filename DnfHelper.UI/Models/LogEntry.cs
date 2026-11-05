using System;

namespace DnfHelper.UI.Models
{
    /// <summary>日志条目。Level 与内部日志级别对齐：0 信息 / 1 警告 / 2 错误。</summary>
    public sealed class LogEntry
    {
        public LogEntry(int level, string message)
        {
            Level = level;
            Message = message ?? string.Empty;
            Time = DateTime.Now;
        }

        public int Level { get; }
        public string Message { get; }
        public DateTime Time { get; }

        public string TimeText => Time.ToString("HH:mm:ss");

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case 1:
                        return "警告";
                    case 2:
                        return "错误";
                    default:
                        return "信息";
                }
            }
        }
    }
}
