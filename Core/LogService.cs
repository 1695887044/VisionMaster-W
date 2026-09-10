using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using VisionMaster.Core;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UI.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 日志服务：事件推送（UI 日志面板）+ 文件落盘双通道。
    /// 文件通道：后台队列写入（不阻塞调用线程），按天滚动 Logs\yyyy-MM-dd.log，
    /// 自动清理保留期（默认 30 天）外的旧文件。写入失败静默忽略，不影响业务。
    /// </summary>
    public class LogService : ILogService, IDisposable
    {
        /// <summary>日志文件保留天数</summary>
        private const int RetentionDays = 30;

        public event Action<LogItem> OnLogReceived;

        // 有界队列：磁盘故障时防止内存无限膨胀（积满后丢弃新日志并计数）
        private readonly BlockingQueue<string>? _fileQueue;
        private readonly Thread? _flushThread;
        private readonly string _logDir;
        private int _droppedCount;

        public LogService()
        {
            _logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
            try
            {
                Directory.CreateDirectory(_logDir);
                CleanupExpiredFiles();
                _fileQueue = new BlockingQueue<string>(capacity: 100_000);
                _flushThread = new Thread(FlushLoop)
                {
                    IsBackground = true,
                    Name = "LogService.Flush"
                };
                _flushThread.Start();
            }
            catch
            {
                _fileQueue = null; // 文件通道初始化失败：仅保留事件通道
            }
        }

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
        private void PublishLog(LogLevel level, string message, string source = null)
        {
            var logItem = new LogItem(level, message, source);
            OnLogReceived?.Invoke(logItem);
            EnqueueToFile(level, message);
        }

        /// <summary>入队文件通道（时间戳在入队时固定，避免写盘延迟导致乱序）</summary>
        private void EnqueueToFile(LogLevel level, string message)
        {
            if (_fileQueue == null) return;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelTag(level)}] {message}";
            try { _fileQueue.Enqueue(line); }
            catch { /* 队列已满/已完成：丢弃，计数告警由落盘线程输出 */ }
        }

        private static string LevelTag(LogLevel level) => level switch
        {
            LogLevel.Error => "ERR ",
            LogLevel.Warning => "WARN",
            _ => "INFO"
        };

        /// <summary>后台落盘循环：单线程顺序写，按天切换文件</summary>
        private void FlushLoop()
        {
            var currentDate = DateTime.Now.Date;
            StreamWriter? writer = OpenWriter(currentDate);
            try
            {
                while (true)
                {
                    if (!_fileQueue!.TryDequeue(out var line))
                    {
                        // 队列已完成写入且取空：收尾退出
                        if (_fileQueue.IsCompleted)
                            break;
                        Thread.Sleep(200);
                        continue;
                    }

                    var today = DateTime.Now.Date;
                    if (today != currentDate)
                    {
                        currentDate = today;
                        writer?.Flush(); writer?.Dispose();
                        writer = OpenWriter(currentDate);
                    }

                    try { writer?.WriteLine(line); }
                    catch { writer = null; } // 写失败（磁盘满/被占用）：本次丢弃，下次重开
                }
            }
            finally
            {
                try { writer?.Flush(); writer?.Dispose(); } catch { }
            }
        }

        private StreamWriter OpenWriter(DateTime date)
        {
            var path = Path.Combine(_logDir, $"{date:yyyy-MM-dd}.log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }

        /// <summary>清理超过保留期的旧日志文件</summary>
        private void CleanupExpiredFiles()
        {
            try
            {
                var deadline = DateTime.Now.AddDays(-RetentionDays);
                foreach (var file in Directory.GetFiles(_logDir, "*.log"))
                {
                    if (File.GetLastWriteTime(file) < deadline)
                        File.Delete(file);
                }
            }
            catch { /* 清理失败不影响启动 */ }
        }

        public void Dispose()
        {
            try { _fileQueue?.Complete(); } catch { }
            try { _flushThread?.Join(3000); } catch { }
        }
    }
}
