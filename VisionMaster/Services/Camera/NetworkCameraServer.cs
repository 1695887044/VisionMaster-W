using Core.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using VisionMaster.Models;
using WatsonWebserver;
using WatsonWebserver.Core;

namespace VisionMaster.Services
{
    /// <summary>
    /// 网络相机收图服务：相机客户端按"相机该有的样子"持续把帧推过来（+ 每秒一次心跳），
    /// 服务端解出原始像素后交对应设备入队，流程再从队列里消费。
    ///
    /// 请求契约
    /// ---------
    ///   推帧：POST http://{host}:{port}/camera/{序列号}/frame
    ///         Authorization: Bearer {Token}
    ///         X-Image-Name: {文件名，仅用于日志与界面}
    ///         Content-Type: application/octet-stream
    ///         body: 图片文件字节（jpg / png / bmp / tif …）
    ///
    ///   心跳：POST http://{host}:{port}/camera/{序列号}/heartbeat（无 body）
    ///
    /// 一句话说清它与 HttpImageServer 的区别
    /// ---------
    /// HttpImageServer 是"推一张图 → 立刻跑一次流程 → 同步回结果"，一请求一帧一次执行；
    /// 本服务是"相机持续出图"，一帧进来只做一件事：入队。流程什么时候取、取几帧，由流程自己决定。
    /// 所以两者的收图侧语义完全不同（只留最新帧 vs 消费 + 环形缓冲 + 溢出计数），不能互相复用。
    ///
    /// 心跳为什么必须独立于推帧
    /// ---------
    /// 只有心跳才能区分"相机在线但这一刻没出图"（正常）与"相机没了"（故障）。
    /// 若只靠"多久没收到帧"来判，用户一按暂停、或图片放完，界面就会报"相机掉线"，
    /// 把运维引到一个根本不存在的故障上。
    /// </summary>
    public sealed class NetworkCameraServer : IDisposable
    {
        private const string AuthHeaderName = "Authorization";
        private const string BearerPrefix = "Bearer ";
        private const string ImageNameHeaderName = "X-Image-Name";

        private readonly ILogService _log;
        private readonly AppSettingsService _settings;
        private readonly ICameraProvider _cameras;

        /// <summary>
        /// 各相机的"上一条拒绝原因"。
        ///
        /// 为什么要它：客户端是按节拍持续推的（33 帧/秒很常见）。若每帧都写一条"相机未打开"，
        /// 一秒就能把日志刷满，真正有用的第一条反而被淹没。这里只在**原因发生变化**时记一条，
        /// 既能看见"什么时候开始被拒的"，又不会刷屏；推帧成功后清除，下次再拒会重新记。
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _lastRejectReason = new(StringComparer.OrdinalIgnoreCase);

        private Webserver _server;
        private NetworkCameraServerSettings _config;
        private bool _disposed;

        public NetworkCameraServer(ILogService log, AppSettingsService settings, ICameraProvider cameras)
        {
            _log = log;
            _settings = settings;
            _cameras = cameras ?? NullCameraProvider.Instance;
        }

        /// <summary>当前是否正在监听（供启动自检 / 日志用）</summary>
        public bool IsListening => _server?.IsListening ?? false;

        /// <summary>
        /// 生效中的配置快照（供界面显示"客户端该往哪个地址推"）。
        /// 取值顺序：Start() 缓存的 _config → AppConfig.json 当前值 → 默认值。
        /// _config 在 Start() 里早于 Enabled 判定就已赋值，所以即便服务未启用，
        /// 这里拿到的也是用户配的 Host/Port/Token。
        /// </summary>
        public NetworkCameraServerSettings EffectiveSettings =>
            _config ?? _settings.Current?.NetworkCameraServer ?? new NetworkCameraServerSettings();

        #region 启动 / 停止

        public void Start()
        {
            _config = _settings.Current?.NetworkCameraServer ?? new NetworkCameraServerSettings();

            if (!_config.Enabled)
            {
                _log.Info("[NetworkCameraServer] 未启用（AppConfig.json 中 NetworkCameraServer.Enabled=false），本次不监听");
                return;
            }

            var host = string.IsNullOrWhiteSpace(_config.Host) ? NetworkCameraServerSettings.DefaultHost : _config.Host;
            var port = _config.Port > 0 && _config.Port <= 65535 ? _config.Port : NetworkCameraServerSettings.DefaultPort;

            _server = new Webserver(new WebserverSettings(host, port), DefaultRoute);
            _server.Routes.AuthenticateApiRequest = Authenticate;
            _server.Post("/camera/{serial}/frame", HandleFramePost, auth: true);
            _server.Post("/camera/{serial}/heartbeat", HandleHeartbeatPost, auth: true);

            _server.Start();

            _log.Info($"[NetworkCameraServer] 已监听 http://{host}:{port}/camera/{{序列号}}/frame 与 /heartbeat");

            if (_config.IsUsingDefaultToken)
                _log.Warn($"[NetworkCameraServer] 访问令牌仍是默认占位值「{NetworkCameraServerSettings.DefaultToken}」，请修改 AppConfig.json 的 NetworkCameraServer.Token 后再投用");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_server?.IsListening == true)
                    _server.Stop();
            }
            catch (Exception ex)
            {
                _log.Warn($"[NetworkCameraServer] 停止监听失败：{ex.Message}");
            }

            try
            {
                _server?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warn($"[NetworkCameraServer] 释放服务端失败：{ex.Message}");
            }

            _server = null;
        }

        #endregion

        #region 鉴权与兜底路由

        // 委托类型是 Func<HttpContextBase, Task<AuthResult>>，鉴权是纯内存比较，用 Task.FromResult 包一层即可
        private Task<AuthResult> Authenticate(HttpContextBase ctx)
        {
            var header = ctx.Request.RetrieveHeaderValue(AuthHeaderName) ?? string.Empty;
            var expected = _config?.Token ?? string.Empty;

            var token = header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? header.Substring(BearerPrefix.Length).Trim()
                : string.Empty;

            if (token.Length > 0 && string.Equals(token, expected, StringComparison.Ordinal))
            {
                return Task.FromResult(new AuthResult
                {
                    AuthenticationResult = AuthenticationResultEnum.Success,
                    AuthorizationResult = AuthorizationResultEnum.Permitted
                });
            }

            return Task.FromResult(new AuthResult
            {
                AuthenticationResult = AuthenticationResultEnum.NotFound,
                AuthorizationResult = AuthorizationResultEnum.DeniedImplicit
            });
        }

        private static Task DefaultRoute(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 404;
            return ctx.Response.Send("not found");
        }

        #endregion

        #region 主路由

        /// <summary>
        /// 推一帧。郑重说明：本方法**不做任何等待**——解出像素、入队、立刻回包。
        /// 客户端不等流程出结果，这正是相机与"网络推送"最本质的区别，
        /// 也是为什么这里没有 HttpImageServer 里那一整套会话获取 / 超时 / 结果收集逻辑。
        /// </summary>
        private Task<object> HandleFramePost(ApiRequest req)
        {
            if (!TryResolveCamera(req, out var serial, out var device, out var fail))
                return fail;

            var bytes = req.Http.Request.DataAsBytes;
            if (bytes == null || bytes.Length == 0)
                return Task.FromResult(Fail(req, 400, "请求体为空：请把图片文件的字节作为 body 发送"));

            ImageBytesCodec.DecodedImage decoded;
            try
            {
                decoded = ImageBytesCodec.Decode(bytes);
            }
            catch (Exception ex)
            {
                _log.Warn($"[NetworkCameraServer] 序列号「{serial}」图片解码失败（{bytes.Length} 字节）：{ex.Message}");
                return Task.FromResult(Fail(req, 400, $"图片解码失败：{ex.Message}"));
            }

            var frame = new CameraFrame
            {
                PixelData = decoded.Pixels,
                Width = decoded.Width,
                Height = decoded.Height,
                Channels = decoded.Channels,
                Timestamp = DateTime.Now,
                SourceName = req.Http.Request.RetrieveHeaderValue(ImageNameHeaderName) ?? string.Empty
            };

            if (!device.PushFrame(frame, out var error))
            {
                ReportReject(serial, error);
                return Task.FromResult(Fail(req, 409, error));
            }

            // 推成功即清除拒绝记录：下次再被拒时才会重新记一条（见 _lastRejectReason 的说明）
            _lastRejectReason.TryRemove(serial, out _);

            return Task.FromResult<object>(new
            {
                success = true,
                serial,
                frameId = frame.FrameId,
                received = device.ReceivedFrameCount,
                pending = device.PendingFrameCount,
                overflow = device.OverflowCount,
                bytes = bytes.Length
            });
        }

        /// <summary>
        /// 心跳：只表示"客户端还活着"，不产生任何一帧图像。
        /// 设备据此把"在线但没送帧"与"掉线"区分开（见 ICameraDevice.NotifyAlive）。
        /// </summary>
        private Task<object> HandleHeartbeatPost(ApiRequest req)
        {
            if (!TryResolveCamera(req, out var serial, out var device, out var fail))
                return fail;

            device.NotifyAlive();
            _lastRejectReason.TryRemove(serial, out _);

            return Task.FromResult<object>(new
            {
                success = true,
                serial,
                state = device.State.ToString(),
                received = device.ReceivedFrameCount,
                pending = device.PendingFrameCount,
                overflow = device.OverflowCount,
                dropped = device.DroppedFrameCount
            });
        }

        /// <summary>
        /// 按 URL 里的序列号定位相机。找不到时给出可操作的指引——
        /// 序列号打错一个字符是这个链路里最高频的接入失败原因，
        /// 只回一句"404"会让现场去翻协议文档。
        /// </summary>
        private bool TryResolveCamera(ApiRequest req, out string serial, out ICameraDevice device, out Task<object> fail)
        {
            device = null;
            fail = null;

            // Watson 的路由参数不做 URL 解码，中文/特殊字符到这里还是 %XX
            serial = Uri.UnescapeDataString(req.Parameters["serial"] ?? string.Empty).Trim();

            if (serial.Length == 0)
            {
                fail = Task.FromResult(Fail(req, 400, "URL 缺少序列号：应为 /camera/{序列号}/frame"));
                return false;
            }

            if (!_cameras.TryGetDeviceBySerial(serial, out device) || device == null)
            {
                var known = string.Join("、", System.Linq.Enumerable.Select(_cameras.Cameras, c => c.SerialNo));
                fail = Task.FromResult(Fail(
                    req,
                    404,
                    $"未找到序列号为「{serial}」的相机。请先在「系统 → 相机设置」添加该相机，"
                    + $"并确保序列号完全一致（当前已配置：{(known.Length > 0 ? known : "无")}）"));
                return false;
            }

            return true;
        }

        /// <summary>拒绝原因变化时才记一条日志（避免按节拍推图时刷屏）</summary>
        private void ReportReject(string serial, string reason)
        {
            var message = reason ?? "未知原因";
            if (_lastRejectReason.TryGetValue(serial, out var last) &&
                string.Equals(last, message, StringComparison.Ordinal))
                return;

            _lastRejectReason[serial] = message;
            _log.Warn($"[NetworkCameraServer] 序列号「{serial}」的帧被拒绝：{message}");
        }

        private static object Fail(ApiRequest req, int statusCode, string message)
        {
            req.Http.Response.StatusCode = statusCode;
            return new { success = false, message };
        }

        #endregion
    }
}
