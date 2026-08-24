using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Models
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class LogItem
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public DateTime Time { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Source { get; set; }
        public string Message { get; set; }

        public LogItem(LogLevel level, string message, string source = "System")
        {
            Level = level;
            Message = message;
            Source = source;
        }
    }
}
