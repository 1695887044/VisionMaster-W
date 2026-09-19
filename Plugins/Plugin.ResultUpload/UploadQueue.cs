using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
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
    }

    /// <summary>Hub 产生的待投递日志（静态类没有 IExecutionContext，暂存后由插件执行时转交宿主日志）。</summary>
    internal sealed class UploadLogItem
    {
        public string Level;   // INFO / WARN / ERROR
        public string Message;
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
    /// - 退避重试：0.5s / 1s / 1.5s … 封顶 3s，防 MES 短暂抖动打成风暴。
    /// </summary>
    internal static class UploadQueue
    {
        public const int Capacity = 1000;          // 队列上限（条）
        private const int DefaultTimeoutMs = 3000;
        private const int MaxLogItems = 500;       // 防日志堆积

        private static readonly BlockingCollection<UploadJob> Queue = new BlockingCollection<UploadJob>(Capacity);
        private static readonly ConcurrentQueue<UploadLogItem> Logs = new ConcurrentQueue<UploadLogItem>();
        private static int _started;               // 0=线程未启动 1=已启动

        private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10) // 长跑机台：定期换连接防 DNS/路由变更后僵死
        })
        {
            // 总超时置 Infinite：单次超时由每个请求自己的 CTS 控制（TimeoutMs 各任务可不同）
            Timeout = Timeout.InfiniteTimeSpan
        };

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

        private static void ConsumeLoop()
        {
            foreach (var job in Queue.GetConsumingEnumerable())
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
        }

        /// <summary>发送 + 重试退避。完成后调 OnCompleted 并写 Hub 日志；返回 (成功, 状态码, 响应或错误)。</summary>
        private static (bool Success, int StatusCode, string ResponseOrError) SendWithRetry(UploadJob job)
        {
            int maxAttempts = Math.Max(0, job.RetryCount) + 1;
            int code = 0;
            string body = null;
            string err = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1)
                    Thread.Sleep(Math.Min(500 * (attempt - 1), 3000)); // 退避 0.5s/1s/1.5s… 封顶 3s
                try
                {
                    int timeoutMs = job.TimeoutMs > 0 ? job.TimeoutMs : DefaultTimeoutMs;
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs)))
                    using (var req = new HttpRequestMessage(HttpMethod.Post, job.Url))
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
                        using (var resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult())
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
                    err = $"超时（{Math.Max(1, job.TimeoutMs)}ms 无响应）";
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
            job.OnCompleted?.Invoke(false, code, err);
            return (false, code, err);
        }

        private static void Log(string level, string message)
        {
            Logs.Enqueue(new UploadLogItem { Level = level, Message = message });
            while (Logs.Count > MaxLogItems && Logs.TryDequeue(out _)) { } // 防堆积：丢最老的
        }
    }
}
