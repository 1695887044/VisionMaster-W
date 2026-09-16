using HalconDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Plugin.DataRecord
{
    /// <summary>入队结果（流程线程据此决定是否按"写失败阻断"契约失败）。</summary>
    internal enum EnqueueResult { Accepted, QueueFull, Rejected }

    /// <summary>
    /// 一个记录目标 = 某步骤实例按当前配置解析出的"账本"。
    /// 插件每次执行前重建；Hub 只按引用分组，配置变化自然走新目标，互不干扰。
    /// </summary>
    internal sealed class RecordingTarget
    {
        /// <summary>步骤实例名（日志前缀 + {StepName} 占位符）</summary>
        public string OwnerName = "";
        /// <summary>目录模板（可含 {yyyy-MM} 等日期占位符，跨月自动建新目录）</summary>
        public string DirectoryTemplate = "";
        /// <summary>文件名模板（含 .csv，通常 {yyyy-MM-dd}.csv，按天滚动）</summary>
        public string FileNamePattern = "";
        /// <summary>图片目录绝对路径（仅保留期清理扫描用；可为空）</summary>
        public string ImageDirectory = "";
        /// <summary>表头列名（与行值数组一一对齐）</summary>
        public string[] Header = new string[0];
        /// <summary>新文件是否写表头</summary>
        public bool WriteHeader = true;
        /// <summary>每行落盘（断电保真，牺牲节拍）</summary>
        public bool FlushEveryRow = false;
        /// <summary>保留天数（0=永不清理——追溯数据的删除必须显式决定）</summary>
        public int KeepDays = 0;
    }

    /// <summary>数据队列元素：一行账（值已在流程线程格式化成字符串）。</summary>
    internal sealed class DataEntry
    {
        public RecordingTarget Target;
        public long Seq;
        public DateTime Time;
        public string[] Values;
    }

    /// <summary>图片队列元素：一张待落盘的图（Image 是入队方深拷贝出的独立副本，Hub 落盘后负责 Dispose）。</summary>
    internal sealed class ImageEntry
    {
        public HImage Image;
        public string PathNoExt;
        public string Format;
        public string Owner;
    }

    /// <summary>Hub 产生的待投递日志（静态类没有 IExecutionContext，暂存后由插件执行时转交宿主日志）。</summary>
    internal sealed class LogItem
    {
        public string Level;   // INFO / WARN / ERROR
        public string Message;
    }

    /// <summary>
    /// 全局记录中枢（插件内静态单例，多步骤实例共享同一份——程序集只加载一次）：
    /// 生产者-消费者双队列——流程线程只做"组字典 + 入队"（微秒级，不碰磁盘），
    /// CSV 写线程按 500ms 批量落盘，图片写线程逐张低频落盘，节拍与 IO 彻底解耦。
    ///
    /// 工业纪律内置：
    /// - CSV：UTF-8 带 BOM（Excel 双击不乱码）、按天滚动、字段转义；
    /// - 目标被 Excel 占用：IOException 重试 3 次 → 旁路 *_conflictN.csv，宁旁路不丢账；
    /// - 队列有界：满则拒绝入队，由插件按"阻断/放行"契约处置（背压不压垮内存）；
    /// - 全局序列号：_seq.json 边车持久化，重启续号防图片撞名；
    /// - 磁盘水位：每 60 秒抽查一次剩余空间，低于 500MB 报警；
    /// - 进程退出：ProcessExit 尽力冲刷队列（3 秒预算）。
    /// </summary>
    internal static class RecordingHub
    {
        public const int DataCapacity = 200000;   // 数据队列上限（行）
        public const int ImageCapacity = 2000;    // 图片队列上限（张）
        private const int MaxBatch = 5000;        // 单批最多出队行数
        private const long MinFreeBytes = 500L * 1024 * 1024; // 磁盘水位 500MB

        private static readonly BlockingCollection<DataEntry> DataQueue = new BlockingCollection<DataEntry>(DataCapacity);
        private static readonly BlockingCollection<ImageEntry> ImageQueue = new BlockingCollection<ImageEntry>(ImageCapacity);
        private static readonly ConcurrentQueue<LogItem> Logs = new ConcurrentQueue<LogItem>();

        /// <summary>序列号边车（放在程序目录：全局唯一账本序号，跨步骤实例/跨重启单调）</summary>
        private static readonly string SeqFilePath = Path.Combine(AppContext.BaseDirectory, "DataRecord_seq.json");
        private static long _sequence;            // 当前全局序号
        private static long _persistedSeq;        // 上次落盘的序号
        private static int _seqWarned;            // 边车写失败只警告一次

        private static int _started;              // 0=线程未启动 1=已启动
        private static volatile bool _stopping;   // 退出信号

        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

        static RecordingHub()
        {
            try
            {
                if (File.Exists(SeqFilePath) &&
                    long.TryParse(File.ReadAllText(SeqFilePath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) &&
                    v > 0)
                {
                    _sequence = v;
                    _persistedSeq = v;
                }
            }
            catch
            {
                // 边车读不了：从 0 继续，首次落盘失败时经日志提醒用户
            }
        }

        #region 生产者 API（流程线程调用，全部 O(1) 不阻塞）

        /// <summary>取全局序列号（仅在账本/图片名确实需要时才取，避免空耗）。</summary>
        public static long NextSequence()
        {
            EnsureStarted(); // 取过号就必须挂好退出冲刷钩子：防止"只取号未入队"的进程退出时边车不落盘，重启序号回绕撞名
            return Interlocked.Increment(ref _sequence);
        }

        /// <summary>一行账入队。values 须与 target.Header 对齐（转义由 Hub 完成）。</summary>
        public static EnqueueResult Enqueue(RecordingTarget target, string[] values, long seq)
        {
            EnsureStarted();
            try
            {
                if (DataQueue.TryAdd(new DataEntry { Target = target, Values = values, Seq = seq, Time = DateTime.Now }, 0))
                    return EnqueueResult.Accepted;
            }
            catch (InvalidOperationException)
            {
                return EnqueueResult.Rejected; // 正在关停，不再接收
            }

            Log("WARN", $"[数据记录] 写入队列已满（{DataCapacity} 行），来自『{target.OwnerName}』的一条记录被拒——请检查磁盘是否过慢/目标文件长期被占用");
            return EnqueueResult.QueueFull;
        }

        /// <summary>
        /// 一张图入队。image 必须是调用方深拷贝出的独立副本（CopyImage），
        /// 本方法接管其生命周期：落盘后或入队失败时由 Hub Dispose。
        /// </summary>
        public static bool EnqueueImage(HImage image, string pathNoExt, string format, string owner)
        {
            EnsureStarted();
            try
            {
                if (ImageQueue.TryAdd(new ImageEntry { Image = image, PathNoExt = pathNoExt, Format = format, Owner = owner }, 0))
                    return true;
            }
            catch (InvalidOperationException) { }

            try { image.Dispose(); } catch { }
            Log("WARN", $"[数据记录] 图片队列已满，放弃存图：{pathNoExt}");
            return false;
        }

        /// <summary>
        /// 同步写一行（视图"测试写一行"按钮用）：绕过队列立即追加到目标文件，
        /// 让用户当场看到账本长什么样。返回 false 时 error 带原因。
        /// </summary>
        public static bool TryWriteNow(RecordingTarget target, string[] values, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;
            try
            {
                var now = DateTime.Now;
                fullPath = ResolveCsvPath(target, now);
                WriteRows(target, fullPath, new List<DataEntry>
                {
                    new DataEntry { Target = target, Values = values, Seq = 0, Time = now }
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        #endregion

        #region 消费者（两条后台线程）

        private static void EnsureStarted()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;

            var csvThread = new Thread(DataLoop) { IsBackground = true, Name = "DataRecord.CsvWriter" };
            var imgThread = new Thread(ImageLoop) { IsBackground = true, Name = "DataRecord.ImageLayouter" };
            csvThread.Start();
            imgThread.Start();

            AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();
        }

        private static void DataLoop()
        {
            var batch = new List<DataEntry>(MaxBatch);
            var cleanupDone = new Dictionary<RecordingTarget, string>(); // 目标 → 已做过清理的日期
            long batches = 0;

            try
            {
                while (true)
                {
                    DataEntry first = null;
                    try { DataQueue.TryTake(out first, 500, CancellationToken.None); } // 500ms 攒批窗口：节拍快时合并、空闲时不空转（TryTake 超时返回 null，队列关死时抛 InvalidOperationException）
                    catch (InvalidOperationException) { break; }

                    if (first != null)
                    {
                        batch.Add(first);
                        while (batch.Count < MaxBatch && DataQueue.TryTake(out var more)) batch.Add(more);
                    }

                    if (batch.Count > 0)
                    {
                        try { ProcessBatch(batch, cleanupDone); }
                        catch (Exception ex) { Log("ERROR", "[数据记录] 批量写入异常: " + ex.Message); }
                        batch.Clear();
                        batches++;
                        if (batches % 20 == 0) PersistSeq();
                    }

                    if (_stopping && DataQueue.Count == 0) break;
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "[数据记录] 写线程异常退出: " + ex.Message);
            }

            // 关停前把残留全部冲刷
            while (DataQueue.TryTake(out var rest)) batch.Add(rest);
            if (batch.Count > 0)
            {
                try { ProcessBatch(batch, cleanupDone); } catch { }
            }
            PersistSeq(true);
        }

        private static void ProcessBatch(List<DataEntry> batch, Dictionary<RecordingTarget, string> cleanupDone)
        {
            // 两级分组：目标 → 日期滚动后的具体文件 → 行（跨天批次也能各归各账）
            var byTarget = new Dictionary<RecordingTarget, Dictionary<string, List<DataEntry>>>();
            foreach (var e in batch)
            {
                if (!byTarget.TryGetValue(e.Target, out var byFile))
                {
                    byFile = new Dictionary<string, List<DataEntry>>();
                    byTarget[e.Target] = byFile;
                }
                string path = ResolveCsvPath(e.Target, e.Time);
                if (!byFile.TryGetValue(path, out var list))
                {
                    list = new List<DataEntry>();
                    byFile[path] = list;
                }
                list.Add(e);
            }

            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var kvT in byTarget)
            {
                var t = kvT.Key;

                // 保留期清理：每个目标每天至多扫一次（静默失败，绝不影响记账）
                if (t.KeepDays > 0 && (!cleanupDone.TryGetValue(t, out var d) || d != today))
                {
                    cleanupDone[t] = today;
                    try { RunCleanup(t); }
                    catch (Exception ex) { Log("WARN", $"[数据记录] 清理过期文件失败（跳过，不影响记录）: {ex.Message}"); }
                }

                CheckDiskWatermark(t);

                foreach (var kvF in kvT.Value)
                {
                    if (t.FlushEveryRow)
                    {
                        foreach (var one in kvF.Value)
                            WriteRows(t, kvF.Key, new List<DataEntry> { one });
                    }
                    else
                    {
                        WriteRows(t, kvF.Key, kvF.Value);
                    }
                }
            }
        }

        private static void WriteRows(RecordingTarget t, string path, List<DataEntry> entries)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            var sb = new StringBuilder(128 * entries.Count);
            if (isNew && t.WriteHeader && t.Header != null && t.Header.Length > 0)
                sb.AppendLine(JoinRow(t.Header));
            foreach (var e in entries)
                sb.AppendLine(JoinRow(e.Values));
            string text = sb.ToString();

            // 每次批量开→写→关：句柄只持有几毫秒，最大限度给 Excel 让路
            IOException last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs, Utf8Bom))
                    {
                        sw.Write(text);
                        sw.Flush();
                    }
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                    Thread.Sleep(120 * attempt); // 递增退避，给占用方释放的机会
                }
            }

            // 三次仍失败（多半是 Excel 独占打开）→ 旁路文件，宁旁路不丢账
            string conflict = FindConflictPath(path);
            try
            {
                using (var fs = new FileStream(conflict, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, Utf8Bom))
                {
                    sw.Write(text);
                }
                Log("WARN", $"[数据记录] 目标被占用，{entries.Count} 行已改写入旁路 {conflict}（原因: {last?.Message}）——请关闭 Excel 中打开的 {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[数据记录] 旁路文件也写入失败，丢失 {entries.Count} 行: {ex.Message}");
            }
        }

        private static void ImageLoop()
        {
            var pending = new List<ImageEntry>();
            try
            {
                while (true)
                {
                    ImageEntry e = null;
                    try { ImageQueue.TryTake(out e, 500, CancellationToken.None); }
                    catch (InvalidOperationException) { break; }

                    if (e != null)
                    {
                        pending.Add(e);
                        while (ImageQueue.TryTake(out var more)) pending.Add(more);
                    }
                    if (pending.Count > 0)
                    {
                        foreach (var it in pending) SaveImage(it);
                        pending.Clear();
                    }
                    if (_stopping && ImageQueue.Count == 0) break;
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "[数据记录] 图片写线程异常退出: " + ex.Message);
            }

            while (ImageQueue.TryTake(out var rest)) pending.Add(rest);
            foreach (var it in pending) SaveImage(it);
        }

        private static void SaveImage(ImageEntry e)
        {
            try
            {
                string dir = Path.GetDirectoryName(e.PathNoExt);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                e.Image.WriteImage(e.Format, 0, e.PathNoExt); // HALCON 落盘（自动按格式加扩展名）
            }
            catch (Exception ex)
            {
                // 共识：图片失败不回改已写出的账本，只留 WARN 供人工补账
                Log("WARN", $"[数据记录] 存图失败（账本路径已写出，需人工留意）: {e.PathNoExt} | {ex.Message}");
            }
            finally
            {
                try { e.Image.Dispose(); } catch { } // 释放独立副本（入队方已 CopyImage，与上游生命周期无关）
            }
        }

        private static void Shutdown()
        {
            if (_started == 0) return;
            _stopping = true;
            var deadline = DateTime.Now.AddSeconds(3); // 退出冲刷预算 3 秒，超时放弃不死等
            while ((DataQueue.Count > 0 || ImageQueue.Count > 0) && DateTime.Now < deadline)
                Thread.Sleep(50);
            PersistSeq(true);
        }

        #endregion

        #region CSV 行拼接（RFC 4180 转义）

        /// <summary>把一格行的字段拼成 CSV 行；含逗号/引号/换行的字段用双引号包裹，内部引号翻倍。</summary>
        private static string JoinRow(string[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(EscapeCsv(cells[i] ?? ""));
            }
            return sb.ToString();
        }

        private static string EscapeCsv(string s)
        {
            if (s.Length == 0) return s;
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        #endregion

        #region 路径 / 清理 / 磁盘 / 序列号

        /// <summary>把 {StepName} 与日期占位符展开为实际路径片段（长 token 优先替换）。</summary>
        public static string ExpandTemplate(string template, DateTime t, string stepName)
        {
            if (string.IsNullOrEmpty(template)) return "";
            string s = template.Replace("{StepName}", stepName ?? "");
            s = s.Replace("{yyyy-MM-dd HH-mm-ss}", t.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
            s = s.Replace("{yyyy-MM-dd}", t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            s = s.Replace("{yyyy-MM}", t.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            s = s.Replace("{yyyy}", t.ToString("yyyy", CultureInfo.InvariantCulture));
            s = s.Replace("{MM}", t.ToString("MM", CultureInfo.InvariantCulture));
            s = s.Replace("{dd}", t.ToString("dd", CultureInfo.InvariantCulture));
            return s;
        }

        private static string ResolveCsvPath(RecordingTarget t, DateTime time)
        {
            string dir = ExpandTemplate(t.DirectoryTemplate, time, t.OwnerName);
            string name = ExpandTemplate(t.FileNamePattern, time, t.OwnerName);
            if (string.IsNullOrWhiteSpace(name)) name = "{yyyy-MM-dd}.csv";
            return Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, name));
        }

        /// <summary>旁路文件名：xxx.csv → xxx_conflict1.csv（找第一个不存在的序号）。</summary>
        private static string FindConflictPath(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; i <= 99; i++)
            {
                string cand = Path.Combine(dir, $"{name}_conflict{i}{ext}");
                if (!File.Exists(cand)) return cand;
            }
            return Path.Combine(dir, $"{name}_conflict{ext}");
        }

        private static readonly Dictionary<string, DateTime> DiskCheckedAt = new Dictionary<string, DateTime>();

        private static void CheckDiskWatermark(RecordingTarget t)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(t.DirectoryTemplate ?? "."));
                if (string.IsNullOrEmpty(root)) return;
                if (DiskCheckedAt.TryGetValue(root, out var last) && (DateTime.Now - last).TotalSeconds < 60) return;
                DiskCheckedAt[root] = DateTime.Now;

                var di = new DriveInfo(root);
                if (di.IsReady && di.AvailableFreeSpace < MinFreeBytes)
                    Log("ERROR", $"[数据记录] 磁盘 {root} 剩余 {di.AvailableFreeSpace / 1024 / 1024}MB，低于 500MB 水位——请立即清理或更换记录目录！");
            }
            catch { }
        }

        private static readonly Regex DateInName = new Regex(@"\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);

        private static void RunCleanup(RecordingTarget t)
        {
            string dir = Path.GetFullPath(ExpandTemplate(t.DirectoryTemplate, DateTime.Now, t.OwnerName));
            var cutoff = DateTime.Now.AddDays(-t.KeepDays);
            int deleted = 0;

            if (Directory.Exists(dir))
            {
                // 只认"文件名里带日期"的 csv（按天滚动的标准命名），名字无日期的文件一律不动——保守不出错
                foreach (var f in Directory.GetFiles(dir, "*.csv"))
                {
                    var m = DateInName.Match(Path.GetFileName(f));
                    if (!m.Success) continue;
                    if (!DateTime.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fd)) continue;
                    if (fd < cutoff)
                    {
                        try { File.Delete(f); deleted++; } catch { }
                    }
                }
            }

            // 图片目录按最后写入时间判旧（图片名未必含日期）
            if (!string.IsNullOrEmpty(t.ImageDirectory) && Directory.Exists(t.ImageDirectory))
            {
                foreach (var f in Directory.GetFiles(t.ImageDirectory, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < cutoff) { File.Delete(f); deleted++; }
                    }
                    catch { }
                }
                foreach (var sub in Directory.GetDirectories(t.ImageDirectory))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(sub).Length == 0) Directory.Delete(sub); // 收掉空日期文件夹
                    }
                    catch { }
                }
            }

            if (deleted > 0)
                Log("INFO", $"[数据记录] 保留期清理（{t.KeepDays} 天）：已删除 {deleted} 个过期文件");
        }

        private static void PersistSeq(bool force = false)
        {
            long cur = Interlocked.Read(ref _sequence);
            long persisted = Interlocked.Read(ref _persistedSeq);
            if (!force && cur - persisted < 50) return; // 步进落盘：每 50 号写一次边车，开销可忽略
            if (cur == persisted) return;
            try
            {
                File.WriteAllText(SeqFilePath, cur.ToString(CultureInfo.InvariantCulture));
                Interlocked.Exchange(ref _persistedSeq, cur);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _seqWarned, 1) == 0)
                    Log("WARN", $"[数据记录] 序列号边车 {SeqFilePath} 无法写入（{ex.Message}）——重启后序号将从 0 重来，图片可能撞名，请检查程序目录写权限");
            }
        }

        #endregion

        #region 日志暂存与投递

        private static void Log(string level, string msg)
        {
            Logs.Enqueue(new LogItem { Level = level, Message = msg });
            while (Logs.Count > 2000 && Logs.TryDequeue(out _)) { } // 防御：无人消费时丢弃最旧
        }

        /// <summary>插件执行时取走全部暂存日志转交宿主（同白名单审计的投递模式）。</summary>
        public static List<LogItem> TakeLogs()
        {
            var list = new List<LogItem>();
            while (Logs.TryDequeue(out var it)) list.Add(it);
            return list;
        }

        #endregion
    }
}
