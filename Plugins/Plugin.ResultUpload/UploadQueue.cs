using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace Plugin.ResultUpload
{
    /// <summary>入队结果（流程线程据此决定是否按"上报失败阻断"契约失败）。</summary>
    internal enum EnqueueResult { Accepted, QueueFull, Rejected }

    /// <summary>
    /// 一次 HTTP 上报任务（纯数据，不持有插件引用——回调闭包由调用方决定要不要挂）。
    /// </summary>
    internal sealed class UploadJob
    {
        /// <summary>步骤实例名（日志前缀）。</summary>
        public string OwnerName = "";
        public string Url = "";
        /// <summary>单次请求超时（毫秒；≤0 用默认 3000）。</summary>
        public int TimeoutMs;
        /// <summary>自定义请求头（null 允许）。</summary>
        public Dictionary<string, string> Headers;
        /// <summary>请求体（组好的 JSON 文本）。</summary>
        public string Json = "";
        /// <summary>失败后的重试次数（首发失败再试 N 次，总尝试 = N+1）。</summary>
        public int RetryCount;
        /// <summary>完成后回调（队列线程执行）：成功与否 / HTTP 状态码（超时等无响应为 0）/ 响应体或错误描述。</summary>
        public Action<bool, int, string> OnCompleted;

        /// <summary>幂等 ID（自动作为 X-Upload-Id 请求头；重试与断网补传沿用同一 ID，MES 端据此去重）。</summary>
        public string UploadId = "";
        /// <summary>跳过 HTTPS 证书校验（自签证书内网调试用；存在中间人风险）。</summary>
        public bool SkipCertValidation;
        /// <summary>钉钉加签密钥（非空则每次尝试前给 URL 拼 timestamp&amp;sign）。</summary>
        public string SignSecret = "";
        /// <summary>重试穷尽仍失败时是否落盘待补传（测试发送传 false，避免测试数据污染真实账本）。</summary>
        public bool AllowSpool;
    }

    /// <summary>Hub 产生的待投递日志（静态类没有 IExecutionContext，暂存后由插件执行时转交宿主日志）。</summary>
    internal sealed class UploadLogItem
    {
        // 注意必须是属性：XAML 日志回看列表要绑定（WPF Binding 不支持 public 字段）
        public string Level { get; set; }   // INFO / WARN / ERROR
        public string Message { get; set; }
        /// <summary>产生时刻（本机时间；日志回看的显示列）。</summary>
        public DateTime Time { get; set; }
    }

    /// <summary>
    /// 全局上报中枢（插件内静态单例，多步骤实例共享同一份——程序集只加载一次）：
    /// 生产者-消费者——流程线程只做"组 JSON + 入队"（微秒级，不碰网络），
    /// 后台线程发 HTTP + 退避重试，MES 挂了、网线拔了都拖不垮检测节拍。
    ///
    /// 工业纪律内置：
    /// - HttpClient 进程级单例（连接池复用，防高频上报耗尽端口）；
    /// - 单次超时用每请求的 CancellationTokenSource 控制（各任务超时可不同，HttpClient.Timeout 置 Infinite 不干扰）；
    /// - 队列有界：满则拒绝入队，由插件按"阻断/放行"契约处置（背压不压垮内存）；
    /// - 退避重试：0.5s / 1s / 1.5s … 封顶 3s，防 MES 短暂抖动打成风暴；
    /// - 断网续传：重试穷尽仍失败的件落盘 JSONL，空闲时自动补传（实时件永远优先，补传不插队），
    ///   每条自动带 X-Upload-Id 幂等头，补传重发也不怕 MES 记重。
    /// </summary>
    internal static class UploadQueue
    {
        public const int Capacity = 1000;          // 队列上限（条）
        private const int DefaultTimeoutMs = 3000;
        private const int MaxLogItems = 500;       // 防日志堆积

        // —— 断网续传（spool）——
        /// <summary>spool 行数上限：防磁盘被打爆（10 万件断网都兜得住），满了丢最老保最新。</summary>
        public const int MaxSpoolLines = 100000;
        private const int SpoolBatchSize = 20;        // 每轮空闲最多补传条数（一轮不贪多，实时件随到随发）
        private const int SpoolIdleScanSeconds = 30;  // 空闲多久扫一轮 spool（等价于后台 30s Timer）
        private const int MaxDrainFailures = 50;      // 单条累计补传失败上限（防毒记录无限重试）
        private static readonly string SpoolDir = Path.Combine(AppContext.BaseDirectory, "ResultUploadSpool");
        private static readonly string SpoolFilePath = Path.Combine(SpoolDir, "spool.jsonl");
        private static readonly object SpoolLock = new object();   // 文件操作互斥（同步模式失败落盘可能来自流程线程）
        private static int? _spoolCount;                           // 行数缓存（首次访问数一遍，避免每写一条全文扫描）

        private static readonly BlockingCollection<UploadJob> Queue = new BlockingCollection<UploadJob>(Capacity);
        private static readonly ConcurrentQueue<UploadLogItem> Logs = new ConcurrentQueue<UploadLogItem>();
        private const int MaxRecentLogs = 50;      // 日志回看容量（配置面板一屏能看完的量）
        private static readonly object RecentLogLock = new object();
        private static readonly List<UploadLogItem> RecentLogBuffer = new List<UploadLogItem>(); // 回看底账：TakeLogs 转交宿主后这里仍有
        private static int _started;               // 0=线程未启动 1=已启动
        /// <summary>补传累计失败计数（UploadId → 次数；仅消费线程访问，无需加锁）。</summary>
        private static readonly Dictionary<string, int> DrainFailures = new Dictionary<string, int>();

        private static readonly HttpClient Http = CreateClient(false);
        private static readonly HttpClient HttpInsecure = CreateClient(true);

        /// <summary>
        /// 两个 HttpClient 常驻实例：普通 + 跳过证书。HttpClient 的 SslOptions 首次请求后不可变，
        /// 故按 job 标志二选一，而不是运行期改同一个。
        /// </summary>
        private static HttpClient CreateClient(bool skipCertValidation)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10) // 长跑机台：定期换连接防 DNS/路由变更后僵死
            };
            if (skipCertValidation)
            {
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    // 自签证书专用：任何证书都放行（有中间人风险，界面上默认关闭+红字警告）
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                };
            }
            // 总超时置 Infinite：单次超时由每个请求自己的 CTS 控制（TimeoutMs 各任务可不同）
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        private static void EnsureStarted()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            {
                var t = new Thread(ConsumeLoop) { IsBackground = true, Name = "ResultUpload.Worker" };
                t.Start();
            }
        }

        #region 生产者 API（流程线程调用，全部 O(1) 不阻塞）

        /// <summary>一条上报入队（异步模式）。</summary>
        public static EnqueueResult Enqueue(UploadJob job)
        {
            EnsureStarted();
            try
            {
                if (Queue.TryAdd(job, 0))
                    return EnqueueResult.Accepted;
            }
            catch (InvalidOperationException)
            {
                return EnqueueResult.Rejected; // 正在关停，不再接收
            }
            Log("WARN", $"[结果上报] 队列已满（{Capacity} 条），『{job.OwnerName}』一条上报被拒——请检查服务器是否长期不通");
            return EnqueueResult.QueueFull;
        }

        /// <summary>
        /// 同步发送（同步模式与配置面板"测试发送"共用）：含重试退避，阻塞直到出结果。
        /// 只在工作线程调用——流程线程调用时阻塞的就是节拍（同步模式的明示代价）。
        /// </summary>
        public static (bool Success, int StatusCode, string ResponseOrError) SendNow(UploadJob job)
            => SendWithRetry(job);

        /// <summary>取走暂存日志（插件每次执行时转交宿主日志）。</summary>
        public static List<UploadLogItem> TakeLogs()
        {
            var list = new List<UploadLogItem>();
            while (Logs.TryDequeue(out var item))
                list.Add(item);
            return list;
        }

        #endregion

        #region 消费线程：实时优先 + 空闲补传

        private static void ConsumeLoop()
        {
            TryDrainSpool(); // 启动即扫：软件一开就先补历史欠账
            while (true)
            {
                UploadJob job = null;
                try
                {
                    // 实时件优先：只等实时件；等满 30s 还没有 → 视为空闲，巡检一轮 spool
                    if (!Queue.TryTake(out job, TimeSpan.FromSeconds(SpoolIdleScanSeconds)))
                        TryDrainSpool();
                }
                catch (InvalidOperationException)
                {
                    break; // 集合已关停
                }

                if (job != null)
                {
                    RunJob(job);
                    // 实时件清空后的空闲窗口顺手补传（补传永远不排在实时件前面）
                    if (Queue.Count == 0)
                        TryDrainSpool();
                }
            }
        }

        private static void RunJob(UploadJob job)
        {
            try
            {
                SendWithRetry(job);
            }
            catch (Exception ex)
            {
                // SendWithRetry 内部已兜底，这里是最后防线：任何意外都不许杀死消费线程
                Log("ERROR", $"[结果上报]『{job.OwnerName}』上报线程异常：{ex.Message}");
            }
        }

        #endregion

        /// <summary>发送 + 重试退避。完成后调 OnCompleted 并写 Hub 日志；返回 (成功, 状态码, 响应或错误)。</summary>
        private static (bool Success, int StatusCode, string ResponseOrError) SendWithRetry(UploadJob job)
        {
            // 幂等头注入（每条上报一个稳定 ID：首发、重试、断网补传都用同一个，MES 据此去重）
            if (!string.IsNullOrEmpty(job.UploadId) && !HasHeader(job.Headers, "X-Upload-Id"))
            {
                if (job.Headers == null)
                    job.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                job.Headers["X-Upload-Id"] = job.UploadId;
            }

            int maxAttempts = Math.Max(0, job.RetryCount) + 1;
            int timeoutMs = job.TimeoutMs > 0 ? job.TimeoutMs : DefaultTimeoutMs;
            int code = 0;
            string body = null;
            string err = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1)
                    Thread.Sleep(Math.Min(500 * (attempt - 1), 3000)); // 退避 0.5s/1s/1.5s… 封顶 3s

                // 钉钉加签每次尝试重签：长时间重试后旧时间戳可能被服务端判过期
                string url = string.IsNullOrEmpty(job.SignSecret)
                    ? job.Url
                    : SignUrl(job.Url, job.SignSecret, DateTimeOffset.Now.ToUnixTimeMilliseconds());

                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs)))
                    using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        req.Content = new StringContent(job.Json ?? "", Encoding.UTF8, "application/json");
                        if (job.Headers != null)
                        {
                            foreach (var kv in job.Headers)
                            {
                                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                                req.Headers.TryAddWithoutValidation(kv.Key.Trim(), kv.Value ?? "");
                            }
                        }
                        var client = job.SkipCertValidation ? HttpInsecure : Http;
                        using (var resp = client.SendAsync(req, cts.Token).GetAwaiter().GetResult())
                        {
                            code = (int)resp.StatusCode;
                            body = resp.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                            if (resp.IsSuccessStatusCode)
                            {
                                job.OnCompleted?.Invoke(true, code, body);
                                return (true, code, body);
                            }
                            err = $"HTTP {code} {resp.StatusCode}";
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    code = 0;
                    body = null;
                    err = $"超时（{timeoutMs}ms 无响应）";
                }
                catch (Exception ex)
                {
                    // 网络不通 / DNS 解析失败 / URL 非法等都落这里
                    code = 0;
                    body = null;
                    err = ex.Message;
                }
            }

            Log("WARN", $"[结果上报]『{job.OwnerName}』上报失败（已重试 {maxAttempts - 1} 次）：{err}");
            if (job.AllowSpool)
                SpoolAppend(job, err);
            job.OnCompleted?.Invoke(false, code, err);
            return (false, code, err);
        }

        /// <summary>请求头里是否已有同名头（大小写不敏感；用户自填的不覆盖）。</summary>
        private static bool HasHeader(Dictionary<string, string> headers, string name)
        {
            if (headers == null) return false;
            foreach (var k in headers.Keys)
                if (string.Equals(k?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>钉钉加签：sign = UrlEncode(Base64(HMAC-SHA256(secret, "时间戳\nsecret")))，拼到 URL。</summary>
        private static string SignUrl(string url, string secret, long timestampMs)
        {
            string stringToSign = timestampMs + "\n" + secret;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            string sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            return url + (url.Contains('?') ? "&" : "?") + "timestamp=" + timestampMs + "&sign=" + Uri.EscapeDataString(sign);
        }

        #region 断网续传（spool）

        /// <summary>spool 落盘记录（JSONL 一行一件；重试穷尽才写，补传成功即删行）。</summary>
        private sealed class SpoolRecord
        {
            public string Id { get; set; }
            public string Owner { get; set; }
            public string Url { get; set; }
            public int TimeoutMs { get; set; }
            public int RetryCount { get; set; }
            public Dictionary<string, string> Headers { get; set; }
            public string Json { get; set; }
            public string SignSecret { get; set; }
            public bool SkipCert { get; set; }
            public string Time { get; set; }
        }

        private static readonly JsonSerializerOptions SpoolJsonOpts = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 中文可读，排查账本不用解码器
        };

        /// <summary>重试穷尽仍失败 → 落盘待补传（锁内纯文件操作，网络重发在锁外，不互相拖）。</summary>
        private static void SpoolAppend(UploadJob job, string err)
        {
            try
            {
                lock (SpoolLock)
                {
                    Directory.CreateDirectory(SpoolDir);
                    int count = GetSpoolCount();
                    if (count >= MaxSpoolLines)
                    {
                        int drop = Math.Max(1, count / 10);
                        TrimSpoolOldest(drop);
                        count -= drop;
                    }
                    var rec = new SpoolRecord
                    {
                        Id = string.IsNullOrEmpty(job.UploadId) ? Guid.NewGuid().ToString("N") : job.UploadId,
                        Owner = job.OwnerName,
                        Url = job.Url,
                        TimeoutMs = job.TimeoutMs,
                        RetryCount = job.RetryCount,
                        Headers = job.Headers,
                        Json = job.Json,
                        SignSecret = job.SignSecret,
                        SkipCert = job.SkipCertValidation,
                        Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    };
                    File.AppendAllText(SpoolFilePath, JsonSerializer.Serialize(rec, SpoolJsonOpts) + Environment.NewLine);
                    _spoolCount = count + 1;
                }
                Log("WARN", $"[结果上报]『{job.OwnerName}』已存入断网续传队列（{err}），网络恢复后自动补传");
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[结果上报]『{job.OwnerName}』断网续传落盘失败：{ex.Message}");
            }
        }

        /// <summary>空闲巡检：取最早的一批补传重发；成功删行，连续失败即止（网络多半还没恢复）。</summary>
        private static void TryDrainSpool()
        {
            List<SpoolRecord> batch;
            try
            {
                lock (SpoolLock)
                {
                    if (!File.Exists(SpoolFilePath)) return;
                    batch = LoadSpoolBatch();
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[结果上报] 断网续传读取失败：{ex.Message}");
                return;
            }
            if (batch.Count == 0) return;

            var removed = new HashSet<string>(StringComparer.Ordinal);
            int okCount = 0, poisonCount = 0, consecutiveFails = 0;
            foreach (var rec in batch)
            {
                var job = new UploadJob
                {
                    OwnerName = "[补传]" + (rec.Owner ?? ""),
                    Url = rec.Url,
                    TimeoutMs = rec.TimeoutMs,
                    Headers = rec.Headers,
                    Json = rec.Json,
                    RetryCount = rec.RetryCount,
                    UploadId = rec.Id,             // 沿用原 ID：即使本轮没删成、下轮重发，MES 也能去重
                    SignSecret = rec.SignSecret,
                    SkipCertValidation = rec.SkipCert,
                    AllowSpool = false             // 补传失败不重复落盘——原行还在文件里
                };
                var (ok, _, err) = SendWithRetry(job);
                if (ok)
                {
                    removed.Add(rec.Id);
                    DrainFailures.Remove(rec.Id ?? "");
                    okCount++;
                    consecutiveFails = 0;
                }
                else
                {
                    string key = rec.Id ?? "";
                    DrainFailures[key] = (DrainFailures.TryGetValue(key, out var n) ? n : 0) + 1;
                    consecutiveFails++;
                    if (DrainFailures[key] > MaxDrainFailures)
                    {
                        // 毒记录（URL 写错/协议不对永远发不通）：放行删掉，别挡住后面的件
                        removed.Add(rec.Id);
                        poisonCount++;
                        Log("ERROR", $"[结果上报]『{job.OwnerName}』补传累计失败 {MaxDrainFailures} 次已放弃并删除，请检查该步骤的 URL/协议：{err}");
                    }
                    if (consecutiveFails >= 2)
                    {
                        Log("WARN", "[结果上报] 补传连续失败，本轮中止（网络可能仍未恢复，下轮空闲再试）");
                        break;
                    }
                }
            }
            if (removed.Count == 0) return;

            try
            {
                lock (SpoolLock)
                {
                    // 重读全文按 ID 剔除后重写：补传期间其他线程新落的行不受影响
                    var kept = new List<string>();
                    foreach (var line in File.ReadLines(SpoolFilePath))
                    {
                        var id = TryParseId(line);
                        if (id == null) continue;            // 坏行顺手清理
                        if (removed.Contains(id)) continue;  // 本轮已成功/已放弃的
                        kept.Add(line);
                    }
                    File.WriteAllLines(SpoolFilePath, kept);
                    _spoolCount = kept.Count;
                }
                if (okCount > 0)
                    Log("INFO", $"[结果上报] 断网续传补传成功 {okCount} 条");
            }
            catch (Exception ex)
            {
                _spoolCount = null; // 写失败后行数缓存不可信，下次重数
                Log("ERROR", $"[结果上报] 断网续传账本清理失败（成功件下轮会凭幂等 ID 重发，由 MES 去重）：{ex.Message}");
            }
        }

        /// <summary>读最早的一批待补记录（只解析到凑够一批为止，10 万行也只碰前几十行）。</summary>
        private static List<SpoolRecord> LoadSpoolBatch()
        {
            var batch = new List<SpoolRecord>();
            foreach (var line in File.ReadLines(SpoolFilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                SpoolRecord rec = null;
                try { rec = JsonSerializer.Deserialize<SpoolRecord>(line, SpoolJsonOpts); } catch { /* 坏行跳过 */ }
                if (rec == null || string.IsNullOrEmpty(rec.Id)) continue;
                batch.Add(rec);
                if (batch.Count >= SpoolBatchSize) break;
            }
            return batch;
        }

        /// <summary>轻量取行内 Id（只解析顶层一个属性，不反序列化整条记录）。</summary>
        private static string TryParseId(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                return doc.RootElement.ValueKind == JsonValueKind.Object &&
                       doc.RootElement.TryGetProperty("Id", out var p) &&
                       p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>行数（缓存：首次全文件数一遍，之后增删时维护）。</summary>
        private static int GetSpoolCount()
        {
            if (_spoolCount.HasValue) return _spoolCount.Value;
            int n = 0;
            if (File.Exists(SpoolFilePath))
                foreach (var line in File.ReadLines(SpoolFilePath))
                    if (!string.IsNullOrWhiteSpace(line)) n++;
            _spoolCount = n;
            return n;
        }

        /// <summary>丢最老的 n 行（重写文件；保新弃旧——新数据比历史欠账更有业务价值）。</summary>
        private static void TrimSpoolOldest(int removeCount)
        {
            var kept = File.ReadLines(SpoolFilePath).Skip(removeCount).ToList();
            File.WriteAllLines(SpoolFilePath, kept);
            _spoolCount = kept.Count;
            Log("WARN", $"[结果上报] 断网续传积压已达上限 {MaxSpoolLines} 条，丢弃最老的 {removeCount} 条（保新弃旧）");
        }

        #endregion

        private static void Log(string level, string message)
        {
            var item = new UploadLogItem { Level = level, Message = message, Time = DateTime.Now };
            Logs.Enqueue(item);
            while (Logs.Count > MaxLogItems && Logs.TryDequeue(out _)) { } // 防堆积：丢最老的
            lock (RecentLogLock)
            {
                RecentLogBuffer.Add(item);
                if (RecentLogBuffer.Count > MaxRecentLogs) RecentLogBuffer.RemoveAt(0); // 环形：只留最近 50 条
            }
        }

        /// <summary>最近日志快照（新的在后，最多 50 条）：配置面板"日志回看"的数据源。</summary>
        public static List<UploadLogItem> SnapshotRecentLogs()
        {
            lock (RecentLogLock) return new List<UploadLogItem>(RecentLogBuffer);
        }
    }
}
