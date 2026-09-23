using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VisionMaster.Lifetime
{
    /// <summary>
    /// 崩溃现场留档（S13-f 打包与交付）。
    ///
    /// 做什么：异常退出时把「异常全文 + 当时现场」写成一份独立文件放到程序目录
    /// <c>crash_reports\crash-yyyyMMdd-HHmmss-fff.txt</c>；下次启动由
    /// <see cref="TakeLatestUnacknowledged"/> 取出来提示用户，并把该份标记为"已提示"
    /// （改扩展名为 <c>.acked</c>），避免同一份报告每次启动都弹一次。
    ///
    /// <b>为什么不走 ILogService</b>
    /// ---------
    /// ① 崩溃路径上日志服务可能已经死了（后台落盘线程异常、有界队列积满），
    ///    而崩溃那一刻恰恰是最需要留下证据的时刻；
    /// ② 日志是"按天一个文件、几千行"的滚动结构，崩溃现场会被淹没在里面，
    ///    现场排障要的是"一眼看到这一次的异常 + 当时开着哪个方案"。
    /// 所以这里走一条不依赖任何其他服务的最短路径：直接 File.WriteAllText。
    /// 由此得出本类的一条纪律：<b>所有方法都自行吞异常</b>——崩溃处理过程中再抛一次，
    /// 用户看到的就是"程序没有任何提示地消失了"。
    ///
    /// 与 <c>AppLifetimeService</c> 的分工：分级、日志、退出链都在那边；
    /// 本类只负责"把现场写成一份文件"，不做任何决策。
    /// </summary>
    public static class CrashReportWriter
    {
        /// <summary>崩溃报告目录名（程序目录下，与运行期落盘约定一致）</summary>
        public const string DirectoryName = "crash_reports";

        /// <summary>已提示标记：报告文件改挂这个扩展名即为"用户已知晓"</summary>
        public const string AcknowledgedExtension = ".acked";

        /// <summary>保留的报告份数上限（含已提示的），超出丢最早的</summary>
        public const int DefaultKeep = 20;

        private const string FilePrefix = "crash-";

        /// <summary>默认目录：程序目录\crash_reports</summary>
        public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, DirectoryName);

        /// <summary>
        /// 写一份崩溃报告，返回文件完整路径。
        /// 任何失败（磁盘满/无权限/目录被占）都返回 null——调用方无需处理，也拿不到异常。
        /// </summary>
        /// <param name="ex">未处理异常；可为 null（只留现场不留异常，如"启动自检失败退出"）</param>
        /// <param name="source">异常来源（"严重异常"/"AppDomain"/"Task"…），写进报告便于分类</param>
        /// <param name="scene">现场文本（当前方案、是否有未保存改动等），由宿主侧提供</param>
        /// <param name="directory">测试用：指定目录；null 用 <see cref="DirectoryPath"/></param>
        public static string? Write(Exception? ex, string source, string? scene = null, string? directory = null)
        {
            try
            {
                var dir = Resolve(directory);
                System.IO.Directory.CreateDirectory(dir);

                var file = Path.Combine(dir, $"{FilePrefix}{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
                File.WriteAllText(file, Compose(ex, source, scene), Encoding.UTF8);

                PruneOldReports(DefaultKeep, dir);
                return file;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>列出"未提示过"的崩溃报告（新 → 旧）；目录不存在返回空集合</summary>
        public static IReadOnlyList<string> ListReports(string? directory = null)
        {
            try
            {
                var dir = Resolve(directory);
                if (!System.IO.Directory.Exists(dir)) return Array.Empty<string>();

                // 文件名里是定宽时间戳（yyyyMMdd-HHmmss-fff），按名倒序即按时间倒序，
                // 不依赖文件系统时间戳（拷贝/同步会改写它）
                return System.IO.Directory
                    .GetFiles(dir, FilePrefix + "*.txt")
                    .Where(f => !f.EndsWith(AcknowledgedExtension, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 取最新一份未提示的崩溃报告并标记为已提示（改名挂 <see cref="AcknowledgedExtension"/>）；
        /// 没有则返回 null。改名失败也照常返回路径——宁可下次再提示一次，也不能把崩溃吞掉。
        /// </summary>
        public static string? TakeLatestUnacknowledged(string? directory = null)
        {
            var latest = ListReports(directory).FirstOrDefault();
            if (latest == null) return null;

            try { File.Move(latest, latest + AcknowledgedExtension); }
            catch { /* 已被占用等：下次启动再提示一次 */ }

            return latest;
        }

        /// <summary>只保留最近 keep 份报告（含已提示的），返回删除份数</summary>
        public static int PruneOldReports(int keep = DefaultKeep, string? directory = null)
        {
            try
            {
                var dir = Resolve(directory);
                if (!System.IO.Directory.Exists(dir)) return 0;

                var all = System.IO.Directory.GetFiles(dir, FilePrefix + "*")
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .ToList();

                var removed = 0;
                for (var i = Math.Max(keep, 0); i < all.Count; i++)
                {
                    try { File.Delete(all[i]); removed++; }
                    catch { /* 单个删不掉不影响其余 */ }
                }
                return removed;
            }
            catch
            {
                return 0;
            }
        }

        private static string Resolve(string? directory)
            => string.IsNullOrWhiteSpace(directory) ? DirectoryPath : directory!;

        private static string Compose(Exception? ex, string source, string? scene)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=========== 崩溃现场 ===========");
            sb.AppendLine($"时间      : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"来源      : {source}");
            sb.AppendLine($"应用      : {AppDomain.CurrentDomain.FriendlyName}");
            sb.AppendLine($"程序目录  : {AppContext.BaseDirectory}");
            sb.AppendLine($"操作系统  : {Environment.OSVersion}");
            sb.AppendLine($"运行时    : {Environment.Version}");
            sb.AppendLine($"进程ID    : {Process.GetCurrentProcess().Id}");
            sb.AppendLine($"工作集    : {Environment.WorkingSet / 1024 / 1024} MB");

            sb.AppendLine();
            sb.AppendLine("----------- 现场 -----------");
            sb.AppendLine(string.IsNullOrWhiteSpace(scene) ? "(未提供)" : scene);

            sb.AppendLine();
            sb.AppendLine("----------- 异常 -----------");
            sb.AppendLine(ex == null ? "(无异常对象：本次为非异常路径退出)" : ex.ToString());

            return sb.ToString();
        }
    }
}
