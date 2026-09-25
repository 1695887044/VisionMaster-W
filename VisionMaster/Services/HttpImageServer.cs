using Core.Interfaces;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.Models;
using WatsonWebserver;
using WatsonWebserver.Core;

namespace VisionMaster.Services
{
    /// <summary>
    /// HTTP 收图服务端：外部（相机 / PLC / 上位机）把一张图片 POST 进来，
    /// 服务端把它塞进目标流程的图像槽 → 触发该流程单次执行 → 等它跑完 → 把指定输出端口的值回给调用方。
    ///
    /// 请求契约
    /// ---------
    ///   POST http://{host}:{port}/flow/{流程名}?outputs=步骤名.端口名,步骤名.端口名
    ///   Authorization: Bearer {Token}
    ///   Content-Type: application/octet-stream（任意二进制都行，服务端只按图片字节解析）
    ///   body: 图片文件字节（jpg / png / bmp / tif …）
    ///
    ///   outputs 可省略：省略时回传全部步骤的全部输出端口。
    ///
    /// 为什么"收图"和"触发流程"必须绑在一起做
    /// ---------
    /// 客户端发一次请求想要的就是"这一张图的结果"，把收图和触发拆成两个接口（先推图、再触发）
    /// 会引入一个必须自己维护的中间状态（"我推的那张图还在吗？"），而中间状态一旦跨请求，
    /// 就要处理超时、覆盖、并发清理——全是白送的复杂度。绑在一起后，一次请求 = 一帧图 = 一次执行 = 一个结果。
    ///
    /// 为什么图像用"解码后的原始像素"过 Hub 而不是把编码字节直接给插件
    /// ---------
    /// 宿主跑在 WPF 上，BitmapDecoder 一行就能解码且零落盘；插件侧（Core.Interfaces）是
    /// net9.0 无 WPF 的纯托管层，塞解码器等于把 System.Drawing 拖进来。
    /// 所以分工：宿主负责"编码字节 → 原始像素 + 尺寸"（唯一一处解码），
    /// 插件负责"原始像素 → HImage"（一次 GenImage1 / GenImageInterleaved）。
    /// </summary>
    public sealed class HttpImageServer : IDisposable
    {
        private const string AuthHeaderName = "Authorization";
        private const string BearerPrefix = "Bearer ";
        private const string ImageNameHeaderName = "X-Image-Name";

        private readonly ILogService _log;
        private readonly AppSettingsService _settings;
        private readonly IRuntimeManager _runtimeManager;
        private readonly IFlowEngine _flowEngine;
        private readonly FlowCompiler _flowCompiler;
        private readonly IReadOnlyWorkspaceContext _workspace;
        private readonly FlowOutputCollector _collector;

        private Webserver _server;
        private HttpImageServerSettings _config;
        private bool _disposed;

        public HttpImageServer(
            ILogService log,
            AppSettingsService settings,
            IRuntimeManager runtimeManager,
            IFlowEngine flowEngine,
            FlowCompiler flowCompiler,
            IReadOnlyWorkspaceContext workspace,
            FlowOutputCollector collector)
        {
            _log = log;
            _settings = settings;
            _runtimeManager = runtimeManager;
            _flowEngine = flowEngine;
            _flowCompiler = flowCompiler;
            _workspace = workspace;
            _collector = collector;
        }

        /// <summary>当前是否正在监听（供启动自检 / 日志用）</summary>
        public bool IsListening => _server?.IsListening ?? false;

        /// <summary>
        /// 生效中的 HTTP 收图配置快照（供插件配置视图预填"推送测试"的连接参数）
        /// 取值顺序：Start() 缓存的 _config → AppConfig.json 当前值 → 默认值。
        /// 注意 _config 在 Start() 里早于 Enabled 判定就已赋值，所以即便服务未启用，
        /// 这里拿到的也是用户配的 Host/Port/Token，而不是默认值
        /// </summary>
        public HttpImageServerSettings EffectiveSettings =>
            _config ?? _settings.Current?.HttpImageServer ?? new HttpImageServerSettings();

        #region 启动 / 停止

        public void Start()
        {
            _config = _settings.Current?.HttpImageServer ?? new HttpImageServerSettings();

            if (!_config.Enabled)
            {
                _log.Info("[HttpImageServer] 未启用（AppConfig.json 中 HttpImageServer.Enabled=false），本次不监听");
                return;
            }

            var host = string.IsNullOrWhiteSpace(_config.Host) ? HttpImageServerSettings.DefaultHost : _config.Host;
            var port = _config.Port > 0 && _config.Port <= 65535 ? _config.Port : HttpImageServerSettings.DefaultPort;

            _server = new Webserver(new WebserverSettings(host, port), DefaultRoute);
            _server.Routes.AuthenticateApiRequest = Authenticate;
            _server.Post("/flow/{flowName}", HandleFlowPost, auth: true);

            _server.Start();

            _log.Info($"[HttpImageServer] 已监听 http://{host}:{port}/flow/{{流程名}}?outputs=步骤名.端口名");

            // 默认令牌写在源码和文档里，谁都猜得到 —— 它是防误连的，不是安全防线。
            // 不做成"启动失败"：调试阶段本来就要先跑通再收紧，卡在启动上只会让人把整个功能关掉。
            if (_config.IsUsingDefaultToken)
                _log.Warn($"[HttpImageServer] 访问令牌仍是默认占位值「{HttpImageServerSettings.DefaultToken}」，请修改 AppConfig.json 的 HttpImageServer.Token 后再投用");
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
                _log.Warn($"[HttpImageServer] 停止监听失败：{ex.Message}");
            }

            try
            {
                _server?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warn($"[HttpImageServer] 释放服务端失败：{ex.Message}");
            }

            _server = null;
        }

        #endregion

        #region 鉴权与兜底路由

        // 委托类型是 Func<HttpContextBase, Task<AuthResult>>，所以这里返回 Task<AuthResult>。
        // 鉴权是纯内存比较，没有真正的异步操作，用 Task.FromResult 包一层即可。
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

        private async Task<object> HandleFlowPost(ApiRequest req)
        {
            var requestId = Guid.NewGuid().ToString("N").Substring(0, 8);

            // Watson 的路由参数与 query 都不做 URL 解码，中文流程名到这里还是 %E6%B5%81...
            var flowName = Uri.UnescapeDataString(req.Parameters["flowName"] ?? string.Empty).Trim();
            var outputsSpec = Uri.UnescapeDataString(req.Query["outputs"] ?? string.Empty);

            if (flowName.Length == 0)
                return Fail(req, 400, "URL 缺少流程名：应为 /flow/{流程名}");

            var bytes = req.Http.Request.DataAsBytes;
            if (bytes == null || bytes.Length == 0)
                return Fail(req, 400, "请求体为空：请把图片文件的字节作为 body 发送");

            HubImageItem item;
            try
            {
                item = DecodeImage(
                    bytes,
                    requestId,
                    flowName,
                    req.Http.Request.RetrieveHeaderValue(ImageNameHeaderName));
            }
            catch (Exception ex)
            {
                _log.Warn($"[HttpImageServer] 请求 {requestId} 图片解码失败（{bytes.Length} 字节）：{ex.Message}");
                return Fail(req, 400, $"图片解码失败：{ex.Message}");
            }

            // 会话来源：优先复用界面/启动时已编译好的会话，只有在"没有"或"图纸已改过"时才补编译
            var session = _runtimeManager.GetSessionByName(flowName);
            if (session == null || IsOutdated(session, flowName))
            {
                if (!TryBuildSession(flowName, requestId, out session, out var buildError))
                    return Fail(req, 404, buildError);
            }

            // 单次执行抢不到会话锁时会静默吞单（FlowEngineService.TryOccupySession 失败只记 Warn），
            // 所以这里必须先自己判一次，把"没跑"明确告诉客户端，而不是让它收到一个空结果
            if (session.IsRunning)
                return Fail(req, 409, $"流程「{flowName}」正在运行中，本次请求未受理");

            ImageHub.Push(item);

            var stopwatch = Stopwatch.StartNew();
            var runTask = _flowEngine.RunSessionOnceAsync(session);
            var timeout = TimeSpan.FromMilliseconds(Math.Max(1000, _config.RequestTimeoutMs));

            if (await Task.WhenAny(runTask, Task.Delay(timeout)).ConfigureAwait(false) != runTask)
            {
                // 超时不打断流程：单次执行的取消归引擎的 CTS 管，这里抢它的令牌会污染会话状态；
                // 但要挂个观察者把异常吃掉，否则任务最终失败时没人接，会变成未观察异常
                ObserveFault(runTask);
                _log.Warn($"[HttpImageServer] 请求 {requestId} 等待流程「{flowName}」超时（>{timeout.TotalMilliseconds:F0}ms），流程仍在后台继续");
                return Fail(req, 504, $"等待流程「{flowName}」执行超时");
            }

            stopwatch.Stop();

            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error($"[HttpImageServer] 请求 {requestId} 流程「{flowName}」执行抛出异常：{ex.Message}");
                return Fail(req, 500, $"流程「{flowName}」执行异常：{ex.Message}");
            }

            var outputs = _collector.Collect(session, outputsSpec);

            _log.Info($"[HttpImageServer] 请求 {requestId} 流程「{flowName}」完成：{item.Width}x{item.Height}x{item.Channels}，耗时 {stopwatch.ElapsedMilliseconds}ms");

            return new
            {
                success = true,
                requestId,
                flow = flowName,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                image = new { width = item.Width, height = item.Height, channels = item.Channels },
                outputs
            };
        }

        private static object Fail(ApiRequest req, int statusCode, string message)
        {
            req.Http.Response.StatusCode = statusCode;
            return new { success = false, message };
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        #endregion

        #region 会话准备

        private bool IsOutdated(FlowSession session, string flowName)
        {
            var flow = FindFlow(flowName);
            // 图纸改过（Version 涨了）就重编译：否则拿旧图纸跑新图，结果对不上用户看到的设计
            return flow != null && flow.Version > session.CompiledVersion;
        }

        private FlowModel FindFlow(string flowName)
        {
            var flows = _workspace?.CurrentSolution?.Flows;
            if (flows == null) return null;

            // 与 RuntimeManager 的会话查找同口径（Ordinal）：两边判据不一致会出现
            // "找到了流程却找不到会话"这种自己跟自己打架的状态
            return flows.FirstOrDefault(f => string.Equals(f.FlowName, flowName, StringComparison.Ordinal));
        }

        /// <summary>
        /// 按 HTTP 侧需要补编译并注册会话。骨架与 ShellViewModel.RunAllEnabledOnce 完全一致，
        /// 区别只有一处：这里必须传 flowName（与界面侧"编译全部"同口径），
        /// 否则插件 InstanceName 少一截，排查日志时对不上是哪条流程。
        /// </summary>
        private bool TryBuildSession(string flowName, string requestId, out FlowSession session, out string error)
        {
            session = null;
            error = null;

            var flow = FindFlow(flowName);
            if (flow == null)
            {
                error = $"当前方案中没有名为「{flowName}」的流程";
                return false;
            }

            if (!flow.IsEnabled)
            {
                error = $"流程「{flowName}」已被禁用（IsEnabled=false）";
                return false;
            }

            var result = _flowCompiler.Compile(flow.Steps, flow.FlowName);
            if (!result.Success)
            {
                error = $"流程「{flowName}」编译失败：{string.Join("；", result.Errors.Select(e => e.Message))}";
                _log.Error($"[HttpImageServer] 请求 {requestId} {error}");
                return false;
            }

            session = new FlowSession
            {
                FlowName = flow.FlowName,
                ExecutionEngine = result.Data,
                CompiledVersion = flow.Version
            };
            session.AddBlueprintsDeep(flow.Steps);

            // 注意副作用：同名会话已存在时，RegisterSession 会先停旧循环（最长等 3s）再挂新实例。
            // 走到这里说明旧会话要么不存在、要么已过期，停下来是预期行为
            _runtimeManager.RegisterSession(session);
            _log.Info($"[HttpImageServer] 请求 {requestId} 已为流程「{flowName}」补编译并注册会话");
            return true;
        }

        #endregion

        #region 图像解码（编码字节 → 原始像素）

        /// <summary>
        /// 把图片编码字节解码成 HubImageItem。
        /// 解码本身（含"灰度单通道 / 彩色 BGR24 交错"的通道约定）收口在
        /// <see cref="ImageBytesCodec"/>，与网络相机收图链路共用同一份实现。
        /// </summary>
        private static HubImageItem DecodeImage(byte[] bytes, string requestId, string flowName, string sourceName)
        {
            var decoded = ImageBytesCodec.Decode(bytes);

            return new HubImageItem
            {
                RequestId = requestId,
                FlowName = flowName,
                PixelData = decoded.Pixels,
                Width = decoded.Width,
                Height = decoded.Height,
                Channels = decoded.Channels,
                SourceName = sourceName ?? string.Empty
            };
        }

        #endregion
    }
}
