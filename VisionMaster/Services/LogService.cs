using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UI.Models;

namespace VisionMaster.Services
{
    public class LogService : ILogService
    {
        public event Action<LogItem> OnLogReceived;
        public void Success(params string[] messages)
        {
            PublishLog(LogLevel.Info, CombineMessages(messages));
        }

        public void Error(params Exception[] messages)
        {
            PublishLog(LogLevel.Error, CombineExceptions(messages));
        }

        public void Error(params string[] messages)
        {
            PublishLog(LogLevel.Error, CombineMessages(messages));
        }


        public void Info(params string[] messages)
        {
            PublishLog(LogLevel.Info, CombineMessages(messages));
        }

        public void Warn(params string[] messages)
        {
            PublishLog(LogLevel.Warning, CombineMessages(messages));
        }

        private string GetSource([CallerMemberName] string methodName = null,
                            [CallerFilePath] string filePath = null)
        {
            return $"LogService.{methodName}";
        }
        private string CombineMessages(string[] messages)
        {
            if (messages == null || messages.Length == 0)
                return string.Empty;
            return string.Join(" ", messages);
        }

        private string CombineExceptions(Exception[] exceptions)
        {
            if (exceptions == null || exceptions.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var ex in exceptions)
            {
                if (sb.Length > 0)
                    sb.Append(" | ");
                sb.Append(ex.ToString()); 
            }
            return sb.ToString();
        }
        private void PublishLog(LogLevel level, string message, string source=null)
        {
            var logItem = new LogItem(level, message, source);
            OnLogReceived?.Invoke(logItem);
        }

    }
}
