using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using VisionMaster.Core;
using VisionMaster.Models;
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
    /// 文件通道：后台队列写入（不阻塞调用线程），按天滚动 <c>{日志目录}\yyyy-MM-dd.log</c>，
    /// 自动清理保留期外的旧文件。写入失败静默忽略，不影响业务。
    ///
    /// <b>双写（2026-10-23）</b>：全局日志（<see cref="LogDirectory"/>）总是写；当前有方案时
    /// 再写一份到 <c>&lt;方案根&gt;\Logs</c>（见 <see cref="SolutionRootProvider"/>）。
    /// 全局那份是排障底稿（启动链/退出链/方案切换只在这里），方案那份随方案文件夹走。
    ///
    /// 【配置从哪来（2026-10-21）】保留天数 / 目录 / 落盘最小级别原先都是本文件里的硬编码，
    /// 现在一律<b>每次现读</b> <see cref="AppConfigService"/> 的 <c>Current</c>：
    ///   · 保留天数：每次清理时读（口径与审计 / 报警落盘端一致：0 或负数 = 永不清理）；
    ///   · 目录：每次打开写入器时读（改完调 <see cref="RequestReopen"/> 立刻换到新目录）；
    ///   · 级别：每行入队时读（改完立刻生效，不需要重开文件）。
    /// 构造函数参数刻意<b>可空</b>：断言 / 单机调试里 <c>new LogService()</c> 仍按默认值工作。
    /// </summary>
    public class LogService : ILogService, IDisposable
    {
        /// <summary>日志文件保留天数的构造兜底值（没有配置来源时用它）</summary>
        public const int DefaultRetentionDays = AppConfigModel.DefaultLogRetentionDays;

        public event Action<LogItem> OnLogReceived;

        // 有界队列：磁盘故障时防止内存无限膨胀（积满后丢弃新日志并计数）
        private readonly BlockingQueue<string>? _fileQueue;
        private readonly Thread? _flushThread;
        private readonly AppSettingsService? _settings;
        private int _droppedCount;

        /// <summary>请求把当前写入器关掉、按最新配置重开（目录改了之后由设置弹窗调用）</summary>
        private volatile bool _reopenRequested;

        /// <summary>
        /// 当前方案根目录的提供口（宿主在启动时挂接）。
        ///
        /// 为什么是"现读"而不是在打开/保存处逐个推送：方案切换入口有四个（新建 / 打开 /
        /// 方案列表 / 启动自动加载），漏一处就会出现"这份方案的日志写进上一份方案的目录"，
        /// 而症状要等到翻日志时才发现。落盘线程每轮读一次当前方案路径，入口再多也不会漏。
        ///
        /// 返回 null / 空 = 当前没有方案（或方案尚未保存）→ 只写全局日志。
        /// </summary>
        private static volatile Func<string?>? _solutionRootProvider;

        /// <summary>方案根目录提供口（宿主挂接；断言里可临时替换再还原）</summary>
        public static Func<string?>? SolutionRootProvider
        {
            get => _solutionRootProvider;
            set => _solutionRootProvider = value;
        }

        public LogService(AppSettingsService? settings = null)
        {
            _settings = settings;
            try
            {
                Directory.CreateDirectory(LogDirectory);
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

        /// <summary>
        /// 全局日志目录：配置为空 = 程序目录下的 <c>Logs</c>（= 历史行为）。
        /// 每次现读——设置弹窗改完目录后不需要重启软件。
        /// 方案自己的日志目录是 <c>&lt;方案根&gt;\Logs</c>，与这里无关（见 <see cref="CurrentSolutionLogDirectory"/>）。
        /// </summary>
        public string LogDirectory
        {
            get
            {
                var configured = _settings?.Current?.LogDirectory;
                return string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(AppContext.BaseDirectory, "Logs")
                    : configured!.Trim();
            }
        }

        /// <summary>保留天数（&lt;= 0 = 永不清理）。每次现读配置</summary>
        public int RetentionDays => _settings?.Current?.LogRetentionDays ?? DefaultRetentionDays;

        /// <summary>
        /// 当前方案的日志目录：<c>&lt;方案根&gt;\Logs</c>；没有方案时 null。
        /// 提供口抛异常（容器已释放 / 退出链）时按"没有方案"处理——日志服务不能反过来把进程带走。
        /// </summary>
        private static string? CurrentSolutionLogDirectory()
        {
            try
            {
                var root = _solutionRootProvider?.Invoke();
                return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root!, SolutionPaths.LogsFolderName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>落盘最小级别（面板不受它影响）</summary>
        private LogFileLevel MinimumFileLevel => _settings?.Current?.LogFileLevel ?? LogFileLevel.Info;

        /// <summary>
        /// 请求重开写入器：目录改了之后由设置弹窗调用，让新日志立刻写到新目录。
        /// 只置标志（落盘线程是唯一写文件的人），不在这里碰 <c>StreamWriter</c>。
        /// </summary>
        public void RequestReopen() => _reopenRequested = true;

        /// <summary>立刻按当前保留期清理一次（设置弹窗"确定"时调用；启动时构造里也会跑一次）</summary>
        public void CleanupNow() => CleanupExpiredFiles();

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

            // 落盘级别过滤（面板不受影响：事件通道在上面已经发过了）
            if (Severity(level) < (int)MinimumFileLevel) return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{LevelTag(level)}] {message}";
            try { _fileQueue.Enqueue(line); }
            catch { /* 队列已满/已完成：丢弃，计数告警由落盘线程输出 */ }
        }

        /// <summary>级别严重度（与 <see cref="LogFileLevel"/> 的整数值对齐）</summary>
        private static int Severity(LogLevel level) => level switch
        {
            LogLevel.Error => (int)LogFileLevel.Error,
            LogLevel.Warning => (int)LogFileLevel.Warning,
            _ => (int)LogFileLevel.Info
        };

        private static string LevelTag(LogLevel level) => level switch
        {
            LogLevel.Error => "ERR ",
            LogLevel.Warning => "WARN",
            _ => "INFO"
        };

        /// <summary>
        /// 后台落盘循环：单线程顺序写，按天切换文件。
        ///
        /// 双通道：全局日志（<see cref="LogDirectory"/>）**总是**写；当前有方案时再写一份到
        /// <c>&lt;方案根&gt;\Logs</c>。两份都要的理由：
        ///  · 全局那份是排障底稿——方案加载前的启动链、退出链、方案切换本身都只在这里；
        ///  · 方案那份随文件夹走——把方案目录拷给售后，日志不用另外找。
        /// 两者解析到同一目录时只写一份（避免同一行落两遍）。
        /// </summary>
        private void FlushLoop()
        {
            var currentDate = DateTime.Now.Date;
            var lastOpenAttempt = DateTime.MinValue;
            StreamWriter? globalWriter = null;
            StreamWriter? solutionWriter = null;
            string? openSolutionDirectory = null;

            try
            {
                while (true)
                {
                    // 方案目录现读：打开/保存/切换方案后，下一轮就换到新目录。
                    var solutionDirectory = CurrentSolutionLogDirectory();
                    if (string.Equals(solutionDirectory, LogDirectory, StringComparison.OrdinalIgnoreCase))
                        solutionDirectory = null; // 同一目录不写两遍

                    if (_reopenRequested)
                    {
                        _reopenRequested = false;
                        lastOpenAttempt = DateTime.MinValue;
                        CloseWriter(ref globalWriter);
                        CloseWriter(ref solutionWriter);
                        openSolutionDirectory = null;
                    }

                    if (!string.Equals(openSolutionDirectory, solutionDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        // 换方案：关掉旧方案的文件，下一段立即按新目录重开（不排队等 5 秒）
                        CloseWriter(ref solutionWriter);
                        openSolutionDirectory = solutionDirectory;
                        lastOpenAttempt = DateTime.MinValue;
                    }

                    // 打开失败重试间隔 5 秒——目录不可写时别把 CPU 烧在重试上。
                    var now = DateTime.UtcNow;
                    var needOpen = globalWriter == null
                                   || (openSolutionDirectory != null && solutionWriter == null);
                    if (needOpen && (now - lastOpenAttempt).TotalSeconds >= 5)
                    {
                        lastOpenAttempt = now;
                        globalWriter ??= TryOpenWriter(LogDirectory, currentDate);
                        if (openSolutionDirectory != null)
                            solutionWriter ??= TryOpenWriter(openSolutionDirectory, currentDate);
                    }

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
                        CloseWriter(ref globalWriter);
                        CloseWriter(ref solutionWriter);
                        globalWriter = TryOpenWriter(LogDirectory, currentDate);
                        if (openSolutionDirectory != null)
                            solutionWriter = TryOpenWriter(openSolutionDirectory, currentDate);
                    }

                    WriteLine(ref globalWriter, line);
                    WriteLine(ref solutionWriter, line);
                }
            }
            finally
            {
                CloseWriter(ref globalWriter);
                CloseWriter(ref solutionWriter);
            }
        }

        /// <summary>写一行；失败（磁盘满/被占用）就关掉这个通道，等下一轮重开，另一通道不受影响</summary>
        private static void WriteLine(ref StreamWriter? writer, string line)
        {
            if (writer == null) return;

            try { writer.WriteLine(line); }
            catch { CloseWriter(ref writer); }
        }

        private static void CloseWriter(ref StreamWriter? writer)
        {
            try { writer?.Flush(); writer?.Dispose(); } catch { }
            writer = null;
        }

        /// <summary>
        /// 打开写入器。目录由调用方给（全局配置目录或当前方案的 Logs 目录），
        /// 并且自己确保目录存在——配置指向一个还没建的目录、方案刚保存还没建 Logs 都是正常用法。
        /// </summary>
        private static StreamWriter OpenWriter(string directory, DateTime date)
        {
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"{date:yyyy-MM-dd}.log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }

        /// <summary>打开失败不许把落盘线程带走：该通道降级为"只丢文件"，其它通道照常</summary>
        private static StreamWriter? TryOpenWriter(string directory, DateTime date)
        {
            try { return OpenWriter(directory, date); }
            catch { return null; }
        }

        /// <summary>清理超过保留期的旧日志文件（0 或负数 = 永不清理，与审计 / 报警同口径）</summary>
        private void CleanupExpiredFiles()
        {
            var retention = RetentionDays;
            if (retention <= 0) return;

            try
            {
                var dir = LogDirectory;
                if (!Directory.Exists(dir)) return;

                var deadline = DateTime.Now.AddDays(-retention);
                foreach (var file in Directory.GetFiles(dir, "*.log"))
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
