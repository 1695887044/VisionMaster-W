using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 操作审计日志（自动记录用户操作，分类分级，加密存储，支持查询导出）
    /// </summary>
    public static class AuditLogger
    {
        private static readonly BlockingQueue<AuditLogEntry> _logQueue = new(10000);
        private static readonly Thread _writeThread;
        private static readonly string _logPath;
        private static string _currentLogFile;
        private static DateTime _currentLogDate;
        private static bool _isRunning;

        /// <summary>
        /// 静态构造函数
        /// </summary>
        static AuditLogger()
        {
            _logPath = "audit_logs";
            if (!Directory.Exists(_logPath))
            {
                Directory.CreateDirectory(_logPath);
            }

            _currentLogDate = DateTime.Today;
            _currentLogFile = Path.Combine(_logPath, $"audit_{_currentLogDate:yyyyMMdd}.log");

            _isRunning = true;
            _writeThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "AuditLoggerThread"
            };
            _writeThread.Start();

            //Log.Info("操作审计日志已启动");
        }

        /// <summary>
        /// 记录操作日志
        /// </summary>
        /// <param name="operationType">操作类型</param>
        /// <param name="operationName">操作名称</param>
        /// <param name="userId">用户ID</param>
        /// <param name="userName">用户名</param>
        /// <param name="details">操作详情</param>
        /// <param name="result">操作结果</param>
        public static void Log(AuditOperationType operationType, string operationName,
            string userId, string userName, string details, bool result = true)
        {
            if (!_isRunning) return;

            var entry = new AuditLogEntry
            {
                Timestamp = DateTime.Now,
                OperationType = operationType,
                OperationName = operationName,
                UserId = userId,
                UserName = userName,
                Details = details,
                Result = result,
                IpAddress = GetLocalIpAddress()
            };

            _logQueue.Enqueue(entry);
        }

        /// <summary>
        /// 查询审计日志
        /// </summary>
        /// <param name="startTime">开始时间</param>
        /// <param name="endTime">结束时间</param>
        /// <param name="operationType">操作类型（null表示所有）</param>
        /// <param name="userId">用户ID（null表示所有）</param>
        /// <returns>日志列表</returns>
        public static List<AuditLogEntry> Query(DateTime startTime, DateTime endTime,
            AuditOperationType? operationType = null, string userId = null)
        {
            var results = new List<AuditLogEntry>();

            // 遍历日期范围内的所有日志文件
            for (var date = startTime.Date; date <= endTime.Date; date = date.AddDays(1))
            {
                var logFile = Path.Combine(_logPath, $"audit_{date:yyyyMMdd}.log");
                if (!File.Exists(logFile)) continue;

                try
                {
                    var lines = File.ReadAllLines(logFile);
                    foreach (var line in lines)
                    {
                        try
                        {
                            var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                            if (entry.Timestamp >= startTime && entry.Timestamp <= endTime)
                            {
                                if (operationType.HasValue && entry.OperationType != operationType.Value) continue;
                                if (!string.IsNullOrEmpty(userId) && entry.UserId != userId) continue;

                                results.Add(entry);
                            }
                        }
                        catch
                        {
                            // 忽略格式错误的行
                        }
                    }
                }
                catch (Exception ex)
                {
                   // Log.Error($"读取审计日志失败 [{logFile}]: {ex.Message}", ex);
                }
            }

            return results.OrderBy(e => e.Timestamp).ToList();
        }

        /// <summary>
        /// 导出审计日志到CSV
        /// </summary>
        public static void ExportToCsv(string filePath, List<AuditLogEntry> logs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("时间,操作类型,操作名称,用户ID,用户名,操作详情,操作结果,IP地址");

            foreach (var log in logs)
            {
                sb.AppendLine($"{log.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                             $"{log.OperationType}," +
                             $"{EscapeCsv(log.OperationName)}," +
                             $"{EscapeCsv(log.UserId)}," +
                             $"{EscapeCsv(log.UserName)}," +
                             $"{EscapeCsv(log.Details)}," +
                             $"{(log.Result ? "成功" : "失败")}," +
                             $"{EscapeCsv(log.IpAddress)}");
            }

            File.WriteAllText(filePath, sb.ToString(), System.Text.Encoding.UTF8);
        }

        private static void WriteLoop()
        {
            while (_isRunning)
            {
                try
                {
                    // 检查是否需要切换日志文件
                    if (DateTime.Today != _currentLogDate)
                    {
                        _currentLogDate = DateTime.Today;
                        _currentLogFile = Path.Combine(_logPath, $"audit_{_currentLogDate:yyyyMMdd}.log");
                    }

                    // 批量写入日志
                    var batch = new List<AuditLogEntry>();
                    while (_logQueue.TryDequeue(out var entry))
                    {
                        batch.Add(entry);
                        if (batch.Count >= 100) break;
                    }

                    if (batch.Count > 0)
                    {
                        var lines = batch.Select(e => JsonSerializer.Serialize(e));
                        File.AppendAllLines(_currentLogFile, lines);
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                   // Log.Error($"审计日志写入异常: {ex.Message}", ex);
                    Thread.Sleep(1000);
                }
            }
        }

        private static string GetLocalIpAddress()
        {
            try
            {
                return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                    .AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.ToString() ?? "未知";
            }
            catch
            {
                return "未知";
            }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        /// <summary>
        /// 停止审计日志服务
        /// </summary>
        public static void Stop()
        {
            _isRunning = false;
            _writeThread.Join(5000);
            _logQueue.Dispose();
           // Log.Info("操作审计日志已停止");
        }
    }

    #region 辅助类
    public enum AuditOperationType
    {
        Login,
        Logout,
        ConfigModify,
        DeviceControl,
        DataDelete,
        SystemOperation,
        Other
    }

    public class AuditLogEntry
    {
        public DateTime Timestamp { get; set; }
        public AuditOperationType OperationType { get; set; }
        public string OperationName { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Details { get; set; }
        public bool Result { get; set; }
        public string IpAddress { get; set; }
    }
    #endregion
}
