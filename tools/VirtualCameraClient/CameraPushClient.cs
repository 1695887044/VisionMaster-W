using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
// 说明：UseWPF 打开后，隐式 using 里同时存在 System.IO.Path 和 System.Windows.Shapes.Path，
// 直接用 Path 会产生二义性，这里显式指定用 IO 的那个。
using Path = System.IO.Path;

namespace VirtualCameraClient
{
    /// <summary>推一帧的结果：HTTP 状态 + 服务端 JSON 关键字段。</summary>
    public sealed class FramePushResult
    {
        public bool Ok { get; set; }              // HTTP 2xx 且服务端 success=true
        public int StatusCode { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Serial { get; set; }
        public int FrameId { get; set; }
        public int Received { get; set; }
        public int Pending { get; set; }
        public int Overflow { get; set; }
        public long Bytes { get; set; }
        public string RawJson { get; set; }
        public string Summary { get; set; }       // 拼成一行，供界面与日志显示
    }

    /// <summary>心跳一次的结果。</summary>
    public sealed class HeartbeatResult
    {
        public bool Ok { get; set; }
        public int StatusCode { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Serial { get; set; }
        public string State { get; set; }
        public int Received { get; set; }
        public int Pending { get; set; }
        public int Overflow { get; set; }
        public int Dropped { get; set; }
        public string RawJson { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>
    /// 虚拟相机的 HTTP 客户端：只做两件事——推一帧、发心跳。不持有任何界面对象，纯传输层。
    ///
    /// 协议契约（与主软件约定，不可自行更改）
    /// ---------
    ///   推帧   POST {base}/camera/{serial}/frame
    ///          Authorization: Bearer {token}
    ///          X-Image-Name: {文件名}
    ///          Content-Type: application/octet-stream，body = 图片原始字节
    ///   心跳   POST {base}/camera/{serial}/heartbeat   （无 body，每 1 秒一次）
    ///
    /// 为什么 HttpClient 必须复用同一个实例（而不是每次请求 new 一个）
    /// ---------
    /// 1) 每次 new HttpClient 都会新建独立的连接池与 Socket 句柄；连续推图（5 次/秒）+每秒心跳
    ///    这种高频场景下，短命实例会让大量连接停留在 TIME_WAIT，最终耗尽本地端口，抛 SocketException。
    /// 2) HttpClient 内部持有 timer、连接池等资源，是"设计给长期存活、反复使用"的类型。
    /// 因此本工具全程只持有一个 _http，连接参数变化时只改字段，绝不重建实例。
    /// 本项目里唯一一次释放发生在窗口关闭（Dispose）。
    /// </summary>
    public sealed class CameraPushClient : IDisposable
    {
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 19100;
        private const string DefaultSerial = "VIRTUAL-001";

        private readonly HttpClient _http;

        private string _baseUrl = $"http://{DefaultHost}:{DefaultPort}";
        private string _serial = DefaultSerial;
        private string _token = "visionmaster";

        private bool _disposed;

        public CameraPushClient()
        {
            // 超时 5 秒：本机通信正常应在毫秒级；给 5 秒是留给"服务端在跑但偶发卡顿"的余量，
            // 同时保证一次失败不会把等待拖太长（推图与心跳共用这个超时）。
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        /// <summary>把界面上的连接参数同步到客户端（可反复调用，只改字段）。</summary>
        public void Configure(string host, int port, string serial, string token)
        {
            var h = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
            var p = port > 0 && port <= 65535 ? port : DefaultPort;
            _baseUrl = $"http://{h}:{p}";
            _serial = string.IsNullOrWhiteSpace(serial) ? DefaultSerial : serial.Trim();
            _token = token ?? string.Empty;
        }

        #region 推一帧

        /// <summary>
        /// 把一张图片文件按协议推到服务端。约定：任何"业务失败"都不抛异常，只通过返回值表达，
        /// 这样连续推图的循环不会因单帧失败而中断。只有"用户取消"才抛 OperationCanceledException。
        /// </summary>
        public async Task<FramePushResult> PushFrameAsync(string filePath, CancellationToken ct)
        {
            var result = new FramePushResult();
            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

                var url = $"{_baseUrl}/camera/{Uri.EscapeDataString(_serial)}/frame";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
                req.Headers.TryAddWithoutValidation("X-Image-Name", Path.GetFileName(filePath));

                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                req.Content = content;

                using var resp = await _http
                    .SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                result.StatusCode = (int)resp.StatusCode;
                result.RawJson = json;
                ParseFrameJson(json, result);

                // 只有"HTTP 2xx" 且 "服务端 success=true" 才算真的推成功；
                // 4xx/5xx 即便 body 里没有 success 字段，也按失败处理。
                result.Ok = resp.IsSuccessStatusCode && result.Success;
                if (string.IsNullOrEmpty(result.Message) && !resp.IsSuccessStatusCode)
                    result.Message = $"HTTP {(int)resp.StatusCode}";

                result.Summary = result.Success
                    ? $"success=true, serial={result.Serial}, frameId={result.FrameId}, received={result.Received}, pending={result.Pending}, overflow={result.Overflow}, bytes={result.Bytes}"
                    : $"success=false, HTTP {result.StatusCode}, message={result.Message}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户主动停止 / 关窗：这是正常流程，交给上层循环安静退出
                throw;
            }
            catch (Exception ex)
            {
                // 网络异常、连接被拒、超时（TaskCanceledException 但其 ct 未取消）等一律转成失败结果
                result.Ok = false;
                result.Success = false;
                result.Message = $"{ex.GetType().Name}: {ex.Message}";
                result.Summary = $"请求异常：{result.Message}";
            }

            return result;
        }

        #endregion

        #region 心跳

        /// <summary>
        /// 发一次心跳。
        ///
        /// 心跳的语义（务必理解）：
        /// 主软件靠"多久没收到心跳"判断本客户端是否还在线，心跳停 3 秒即判掉线。
        /// 所以心跳必须由独立的定时循环驱动，绝不能挂在推图节拍上——
        /// 用户一旦"暂停推图"，若心跳跟着停，主软件 3 秒后就会把相机判为掉线，
        /// 再点"开始推图"时服务端已经是"相机未打开"状态，推帧全失败。
        /// </summary>
        public async Task<HeartbeatResult> SendHeartbeatAsync(CancellationToken ct)
        {
            var result = new HeartbeatResult();
            try
            {
                var url = $"{_baseUrl}/camera/{Uri.EscapeDataString(_serial)}/heartbeat";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
                // 心跳无 body；显式给一个空内容，避免个别实现把"无 body 的 POST"当异常请求
                req.Content = new ByteArrayContent(Array.Empty<byte>());

                using var resp = await _http
                    .SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                    .ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                result.StatusCode = (int)resp.StatusCode;
                result.RawJson = json;
                ParseHeartbeatJson(json, result);

                result.Ok = resp.IsSuccessStatusCode && result.Success;
                if (string.IsNullOrEmpty(result.Message) && !resp.IsSuccessStatusCode)
                    result.Message = $"HTTP {(int)resp.StatusCode}";

                result.Summary = result.Success
                    ? $"success=true, state={result.State}, received={result.Received}, pending={result.Pending}, overflow={result.Overflow}, dropped={result.Dropped}"
                    : $"success=false, HTTP {result.StatusCode}, message={result.Message}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Success = false;
                result.Message = $"{ex.GetType().Name}: {ex.Message}";
                result.Summary = $"请求异常：{result.Message}";
            }

            return result;
        }

        #endregion

        #region 响应解析

        // 用手写 JsonDocument 解析而不是直接反序列化成 DTO：
        // 服务端返回体可能带额外字段、也可能在异常时返回纯文本（如 404 not found），
        // 手写解析能"缺字段不报错、非 JSON 不崩"，比强类型反序列化更耐操。

        private static void ParseFrameJson(string json, FramePushResult r)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                r.Success = GetBool(root, "success");
                r.Message = GetString(root, "message");
                r.Serial = GetString(root, "serial");
                r.FrameId = GetInt(root, "frameId");
                r.Received = GetInt(root, "received");
                r.Pending = GetInt(root, "pending");
                r.Overflow = GetInt(root, "overflow");
                r.Bytes = GetLong(root, "bytes");
            }
            catch (JsonException)
            {
                // 非 JSON 响应：保留原文，Summary 里带 HTTP 码，便于排查"是不是打到了别的服务"
            }
        }

        private static void ParseHeartbeatJson(string json, HeartbeatResult r)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                r.Success = GetBool(root, "success");
                r.Message = GetString(root, "message");
                r.Serial = GetString(root, "serial");
                r.State = GetString(root, "state");
                r.Received = GetInt(root, "received");
                r.Pending = GetInt(root, "pending");
                r.Overflow = GetInt(root, "overflow");
                r.Dropped = GetInt(root, "dropped");
            }
            catch (JsonException)
            {
            }
        }

        private static string GetString(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static bool GetBool(JsonElement root, string name)
            => root.TryGetProperty(name, out var v)
               && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
               && v.GetBoolean();

        private static int GetInt(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
               && v.TryGetInt32(out var i) ? i : 0;

        private static long GetLong(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
               && v.TryGetInt64(out var i) ? i : 0L;

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // HttpClient 实现了 IDisposable，进程退出前释放，避免句柄泄漏（也便于宿主复用时干净收尾）
            _http?.Dispose();
        }
    }
}
