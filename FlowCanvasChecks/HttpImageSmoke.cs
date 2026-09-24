using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「HTTP 收图 → 视觉处理 → HTTP 反馈」端到端冒烟。
    ///
    /// 为什么要在一个无 GUI 的控制台宿主里起真服务端
    /// ---------
    /// 这条链路上最容易出错的不是算法，而是**跨程序集的运行时契约**：
    ///   ① 宿主 HttpImageServer 与插件 ImageAcquisitionPlugin 分属两个程序集，
    ///      插件由 Assembly.LoadFrom 装进默认 ALC，两边的 ImageHub 必须解析到同一份
    ///      Core.Interfaces —— 否则 Push 进 A 的队列、TryPop 从 B 的队列取，永远取不到图；
    ///   ② Watson 的路由参数/query 不做 URL 解码，中文流程名必须靠业务自己 Unescape；
    ///   ③ 编码字节 → 原始像素（宿主 WPF 解码）→ HImage（插件 GenImage）这条转换链，
    ///      任何一环尺寸/通道算错，表现都是"图能出来但结果不对"，静态读代码看不出来。
    /// 这三条都只有"真发一次 HTTP、真跑一遍流程"才能证伪，所以冒烟必须起真服务端。
    ///
    /// 为什么能在这个宿主里跑（而不是必须开主程序）
    /// ---------
    ///   · GlobalEventBus.PublishOnUIThread 在 Application.Current == null 时降级为同步直调，
    ///     且无订阅者时整段是 no-op —— 插件里的 PublishPreview 不会炸；
    ///   · FlowEngineService 的 IPerformanceMonitor 允许传 null（ExecutionChecks 已有先例）；
    ///   · WorkspaceContext 同一个实例既满足 IWorkspaceManager（喂引擎）又满足
    ///     IReadOnlyWorkspaceContext（喂服务端）；
    ///   · AppSettingsService 的配置路径不可注入（private static readonly，落在
    ///     AppDomain.BaseDirectory），所以换端口/令牌的唯一办法是**先往 bin 目录写一份 AppConfig.json**。
    ///
    /// 为什么测试图是现场生成的而不是读仓库里的 png
    /// ---------
    /// 断言要验的是"尺寸/通道有没有算错"，所以期望值必须是自己写进去的那个数。
    /// 用现成 png 就得先知道它是灰度还是彩色、多大，等于把不确定性引进断言。
    /// 现场用 PngBitmapEncoder 编一张 Gray8、一张 Bgr24，尺寸通道全由本文件确定。
    /// </summary>
    internal static class HttpImageSmoke
    {
        private const string FlowName = "收图冒烟流程";
        private const string StepName = "图像采集_0";
        private const int Port = 19123;

        // 故意用默认占位令牌：既能让请求通过鉴权，又能顺带验证"默认令牌 → 启动 Warn"
        private const string Token = "visionmaster";

        private const int ImgWidth = 64;
        private const int ImgHeight = 48;

        public static void Run()
        {
            Section("[S] HTTP 收图 → 视觉处理 → HTTP 反馈 端到端冒烟");

            // ---- S1 配置：先落盘 AppConfig.json，再构造 AppSettingsService ----
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configPath = Path.Combine(baseDir, "AppConfig.json");
            File.WriteAllText(configPath, BuildAppConfigJson(), new UTF8Encoding(false));

            var settings = new AppSettingsService();
            var cfg = settings.Current?.HttpImageServer;
            Check("[S1] AppConfig.json 加载（HttpImageServer 节）",
                cfg != null && cfg.Enabled && cfg.Port == Port && cfg.Token == Token,
                cfg == null ? "HttpImageServer 为 null" : $"Enabled={cfg.Enabled} Host={cfg.Host} Port={cfg.Port} Token={cfg.Token}");
            Check("[S1] 默认令牌判定（IsUsingDefaultToken）",
                cfg?.IsUsingDefaultToken == true,
                $"IsUsingDefaultToken={cfg?.IsUsingDefaultToken}");

            // ---- S2 插件装配：FlowCanvasChecks 的 bin 里没有 Plugin.*.dll，必须自己装进默认 ALC ----
            var modulesDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Modules"));
            var pluginDll = Path.Combine(modulesDir, "Plugin.ImageAcquisition.dll");
            Type pluginType = null;
            try
            {
                var asm = Assembly.LoadFrom(pluginDll);
                pluginType = asm.GetType("Plugin.ImageAcquisition.ImageAcquisitionPlugin", throwOnError: false);
            }
            catch (Exception ex)
            {
                Check("[S2] 加载 Plugin.ImageAcquisition.dll", false, ex.Message);
            }

            Check("[S2] 加载 Plugin.ImageAcquisition.dll 并解析插件类型",
                pluginType != null,
                pluginType == null ? $"未找到类型（{pluginDll}）" : pluginType.AssemblyQualifiedName);

            // 这条断言是整条链路的地基：插件 Assembly.LoadFrom 进默认 ALC 后，它引用的
            // Core.Interfaces 必须命中宿主已加载的那一份（同标识 → 同一实例）。
            // 标识不一致时 ImageHub 会变成两个类型、两份静态状态，Push 与 TryPop 永远错开。
            var hostCore = typeof(ImageHub).Assembly.GetName();
            var pluginRef = pluginType?.Assembly.GetReferencedAssemblies()
                .FirstOrDefault(a => a.Name == "Core.Interfaces");
            Check("[S2] 宿主与插件指向同一份 Core.Interfaces",
                pluginRef != null
                && pluginRef.Version == hostCore.Version
                && pluginRef.GetPublicKeyToken()?.SequenceEqual(hostCore.GetPublicKeyToken() ?? Array.Empty<byte>()) == true,
                pluginRef == null
                    ? "插件未引用 Core.Interfaces"
                    : $"宿主 {hostCore.Name} {hostCore.Version} / 插件引用 {pluginRef.Version}");

            if (pluginType == null)
            {
                Check("[S3~S12] 后续链路", false, "插件类型不可用，后续断言无法执行");
                return;
            }

            // ---- S3 工作区：单流程单步骤（多步流程会因 ROI/预处理算子输入口未连线而编译失败） ----
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "收图冒烟方案" };
            solution.Flows.Clear();

            var flow = new FlowModel { FlowName = FlowName, Description = "HTTP 收图冒烟", Version = 1, IsEnabled = true };

            var step = new ActionStep("", "图像采集", pluginType.AssemblyQualifiedName, StepName);
            var modeType = pluginType.GetProperty("Mode")?.PropertyType;
            // 2 = AcquisitionMode.Hub（"网络推送"）。用 Enum.ToObject 而不是裸 int：
            // 走的就是界面上选"网络推送"后存进 InputValues 的那个枚举值。
            step.SetInputValue("Mode", Enum.ToObject(modeType, 2));
            // 保持产品默认值 1：会让插件调 PublishPreview，顺带验证无 WPF 宿主下的事件降级
            step.SetInputValue("DisplayViewIndex", 1);
            flow.Steps.Add(step);

            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            Check("[S3] 工作区就绪（当前方案 1 条流程 / 1 个步骤）",
                workspace.CurrentSolution?.Flows.Count == 1 && flow.Steps.Count == 1,
                $"Flows={workspace.CurrentSolution?.Flows.Count} Steps={flow.Steps.Count} ModeType={modeType?.Name}");

            // ---- S4 装配服务端并启动 ----
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var locks = new ResourceLockService();
            var compiler = new FlowCompiler(workspace);
            var engine = new FlowEngineService(runtime, log, workspace, null, locks);
            var collector = new FlowOutputCollector();
            var server = new HttpImageServer(log, settings, runtime, engine, compiler, workspace, collector);

            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                Check("[S4] HttpImageServer.Start()", false, ex.Message);
                return;
            }

            Check("[S4] 服务端已开始监听", server.IsListening, $"IsListening={server.IsListening}");
            Check("[S4] 启动日志含监听地址",
                log.Infos.Any(i => i.Contains("已监听")),
                string.Join(" | ", log.Infos));
            Check("[S4] 默认令牌 → 启动 Warn",
                log.HasWarn("访问令牌仍是默认占位值"),
                string.Join(" | ", log.Warns));

            // ---- S5~S12 真发 HTTP ----
            try
            {
                RunHttpChecks(server, runtime, flow).GetAwaiter().GetResult();
            }
            finally
            {
                server.Dispose();
                Check("[S12] Dispose 后停止监听", !server.IsListening, $"IsListening={server.IsListening}");
            }

            // ---- S13 阻塞等待语义（不依赖服务端，放在 finally 之后跑） ----
            RunWaitChecks(runtime, engine, log, flow);
        }

        /// <summary>
        /// [S13] 网络推送：槽空阻塞等图 / 等待中被"停止流程"打断。
        ///
        /// 为什么这两条必须在"真跑一遍流程"这一层断言
        /// ---------
        /// ImageHub.WaitPop 单独测很好写，但这次要证的恰恰是**插件 + 引擎合起来**的行为：
        ///   · 槽里没图时，采集步骤必须卡住不往下走，直到有新图推来
        ///     —— 旧实现把它报成失败，用户看到的就是"没图却一路跑过去了"；
        ///   · 等待中点"停止流程" → 本步未成功 + 一条 Warn，会话正常收尾（不是 Faulted）。
        /// 只断言 WaitPop 的返回值，插件里接错分支这两条都会被放过。
        ///
        /// 为什么不会挂死、也不靠赌时序
        /// ---------
        /// 断言的同步点是**插件自己打的那条 Info**（"槽内暂无图像，开始等待网络推送"）：
        /// 它出现即代表执行线程真的进了等待点，此后"流程尚未完成"才是硬结论。
        /// 再往后由本方法主动 Push 一帧 / Cancel 令牌来打破等待，两条路径都必然退出。
        /// </summary>
        private static void RunWaitChecks(RuntimeManager runtime, FlowEngineService engine, StubLog log, FlowModel flow)
        {
            Section("[S13] 网络推送：槽空阻塞等图 / 停止打断");

            var session = runtime.GetSessionByName(FlowName);
            if (session == null)
            {
                Check("[S13] 会话已注册（复用 S1~S12 那条）", false, "GetSessionByName 返回 null");
                return;
            }

            // ---- S13a 槽空 → 阻塞 → 推入一帧后继续 ----
            ImageHub.Clear(FlowName);
            Check("[S13a] 起始槽为空（保证走到等待分支）",
                ImageHub.Count(FlowName) == 0, $"Count={ImageHub.Count(FlowName)}");

            // 同步点用"条数增量"而不是"是否存在"：S13a 跑完后日志里已经留下一条同样的 Info，
            // S13b 若按"存在即通过"判断会立刻假通过，所以每段都先取基线再等增量
            var baseWaitLogA = CountLog(log, "槽内暂无图像");
            var runA = engine.RunSessionOnceAsync(session);
            var enteredA = WaitForLog(log, "槽内暂无图像", baseWaitLogA, 5000);
            Check("[S13a] 采集步骤进入等待（插件打出等待 Info）",
                enteredA, $"Infos={Clip(string.Join(" | ", log.Infos.ToArray()))}");

            if (enteredA)
            {
                // 等一会儿再判：确认它是"卡住"而不是"恰好还没跑完"
                Thread.Sleep(200);
                Check("[S13a] 槽空期间流程确实被卡在采集步骤（未完成）",
                    !runA.IsCompleted, $"IsCompleted={runA.IsCompleted}");
            }

            ImageHub.Push(BuildHubItem(FlowName, gray: true));
            runA.GetAwaiter().GetResult();

            Check("[S13a] 推入一帧后流程继续并成功",
                flow.Steps[0].State == StepState.Success && ImageHub.Count(FlowName) == 0,
                $"State={flow.Steps[0].State} Count={ImageHub.Count(FlowName)}");

            // ---- S13b 等待中点"停止流程" → 未成功 + Warn，会话正常收尾 ----
            ImageHub.Clear(FlowName);
            var warnBefore = log.Warns.ToArray().Length;

            var baseWaitLogB = CountLog(log, "槽内暂无图像");
            var runB = engine.RunSessionOnceAsync(session);
            var enteredB = WaitForLog(log, "槽内暂无图像", baseWaitLogB, 5000);
            Check("[S13b] 采集步骤进入等待",
                enteredB, $"Infos={Clip(string.Join(" | ", log.Infos.ToArray()))}");

            engine.StopSession(session);
            runB.GetAwaiter().GetResult();

            Check("[S13b] 停止打断等待后本步按「未成功」收尾",
                flow.Steps[0].State == StepState.Failed,
                $"State={flow.Steps[0].State}");
            Check("[S13b] 插件打了一条「已停止等待网络图像」的 Warn",
                log.Warns.ToArray().Skip(warnBefore).Any(w => w.Contains("已停止等待网络图像")),
                $"Warns={Clip(string.Join(" | ", log.Warns.ToArray()))}");
            Check("[S13b] 会话正常收尾（Stopped，不是 Faulted）",
                session.State == SessionState.Stopped, $"State={session.State}");
            Check("[S13b] 停止后槽仍空（没被凭空塞进一帧）",
                ImageHub.Count(FlowName) == 0, $"Count={ImageHub.Count(FlowName)}");
        }

        /// <summary>
        /// 轮询等待"含某片段的日志条数超过基线"。
        ///
        /// 为什么要用日志当同步点：本用例要证的"流程卡住了"只有在"执行线程已进到等待点"之后才是硬结论。
        /// 用固定 Sleep 猜时长要么不够稳（机器慢就假失败），要么白等（机器快就拖时间）；
        /// 插件进等待点时会打一条 Info，拿它当信号既准确又零额外延迟。
        ///
        /// 为什么比对"条数"而不是"是否存在"：S13a / S13b 打的是同一条文案，
        /// 按"存在"判断第二段会立刻假通过，等于没测。
        ///
        /// 为什么用 ToArray() 取快照而不是直接枚举 List：日志由执行线程在写、这里在读，
        /// List 的枚举器带版本校验，撞上并发写会抛 InvalidOperationException。
        /// </summary>
        private static bool WaitForLog(StubLog log, string fragment, int baseline, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (CountLog(log, fragment) > baseline) return true;
                Thread.Sleep(20);
            }
            return false;
        }

        private static int CountLog(StubLog log, string fragment)
        {
            return log.Infos.ToArray().Count(i => i.Contains(fragment))
                 + log.Warns.ToArray().Count(w => w.Contains(fragment));
        }

        private static async Task RunHttpChecks(HttpImageServer server, RuntimeManager runtime, FlowModel flow)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var url = $"http://127.0.0.1:{Port}/flow/{Uri.EscapeDataString(FlowName)}";

            // ---- S5 鉴权 ----
            var noAuth = await Post(http, url, BuildPng(gray: true), token: null);
            Check("[S5] 无令牌 → 拒绝（非 200）",
                noAuth.Status != 200,
                $"HTTP {noAuth.Status} body={Clip(noAuth.Body)}");

            var badAuth = await Post(http, url, BuildPng(gray: true), token: "wrong-token");
            Check("[S5] 错误令牌 → 拒绝（非 200）",
                badAuth.Status != 200,
                $"HTTP {badAuth.Status} body={Clip(badAuth.Body)}");

            // ---- S6 未知流程 → 404 ----
            var unknown = await Post(http,
                $"http://127.0.0.1:{Port}/flow/{Uri.EscapeDataString("不存在的流程")}",
                BuildPng(gray: true), Token);
            // 回包是 System.Text.Json 序列化的，中文会被转义成 \uXXXX ——
            // 直接对原文 Contains 中文必然落空，必须先解析再取值比对
            Check("[S6] 未知流程 → 404",
                unknown.Status == 404
                    && GetString(TryParse(unknown.Body), "message")?.Contains("不存在的流程") == true,
                $"HTTP {unknown.Status} body={Clip(unknown.Body)}");

            // ---- S7 空 body → 400 ----
            var emptyBody = await Post(http, url, Array.Empty<byte>(), Token);
            Check("[S7] 空 body → 400",
                emptyBody.Status == 400
                    && GetString(TryParse(emptyBody.Body), "message")?.Contains("请求体为空") == true,
                $"HTTP {emptyBody.Status} body={Clip(emptyBody.Body)}");

            // ---- S8 灰度图 happy path ----
            // outputs 里额外带上 Success / ErrorMessage：插件失败时这两个端口会直接说明
            // "是哪种失败"（取图槽为空 / 文件路径为空 / 异常），比只看 Image=null 有用得多
            var grayPng = BuildPng(gray: true);
            var grayResp = await Post(http, url + "?outputs=" + Uri.EscapeDataString(
                    $"{StepName}.Image,{StepName}.CurrentPath,{StepName}.TotalFiles,{StepName}.Success,{StepName}.ErrorMessage"),
                grayPng, Token, imageName: "smoke-gray.png");
            Check("[S8] 灰度图 POST → 200 且 success=true",
                grayResp.Status == 200 && JsonFlag(TryParse(grayResp.Body), "success"),
                $"HTTP {grayResp.Status} body={Clip(grayResp.Body)}");

            var grayDoc = TryParse(grayResp.Body);
            Check("[S8] 回包 image 尺寸/通道 = 源图 64x48x1",
                grayDoc != null && GetInt(grayDoc, "image", "width") == ImgWidth
                    && GetInt(grayDoc, "image", "height") == ImgHeight
                    && GetInt(grayDoc, "image", "channels") == 1,
                DescribeImage(grayDoc));

            // HImage 的尺寸来自 HALCON 侧 GenImage1，能对上源图尺寸才说明字节→HImage 没算错
            Check("[S8] 输出端口 Image 是 HImage 且尺寸 64x48",
                GetString(grayDoc, "outputs", $"{StepName}.Image", "type") == "image"
                    && GetInt(grayDoc, "outputs", $"{StepName}.Image", "width") == ImgWidth
                    && GetInt(grayDoc, "outputs", $"{StepName}.Image", "height") == ImgHeight,
                GetRaw(grayDoc, "outputs", $"{StepName}.Image"));

            // CurrentPath = 请求头里的 X-Image-Name → 说明宿主解码时确实带上了来源名
            Check("[S8] CurrentPath 回传 X-Image-Name",
                GetString(grayDoc, "outputs", $"{StepName}.CurrentPath") == "smoke-gray.png",
                GetRaw(grayDoc, "outputs", $"{StepName}.CurrentPath"));

            Check("[S8] TotalFiles = 1",
                GetInt(grayDoc, "outputs", $"{StepName}.TotalFiles") == 1,
                GetRaw(grayDoc, "outputs", $"{StepName}.TotalFiles"));

            // 诊断口：插件自报的执行结果。Success=false 时 ErrorMessage 会直接指出失败原因
            Check("[S8] 插件自报 Success=true",
                GetRaw(grayDoc, "outputs", $"{StepName}.Success") == "true",
                $"Success={GetRaw(grayDoc, "outputs", $"{StepName}.Success")} "
                    + $"ErrorMessage={GetString(grayDoc, "outputs", $"{StepName}.ErrorMessage")}");

            // ---- S9 彩色图 happy path（走 GenImageInterleaved 的 bgr 分支） ----
            var colorResp = await Post(http, url + "?outputs=" + Uri.EscapeDataString($"{StepName}.Image"),
                BuildPng(gray: false), Token);
            var colorDoc = TryParse(colorResp.Body);
            Check("[S9] 彩色图 POST → 200 且通道=3",
                colorResp.Status == 200 && GetInt(colorDoc, "image", "channels") == 3,
                $"HTTP {colorResp.Status} {DescribeImage(colorDoc)} body={Clip(colorResp.Body)}");
            Check("[S9] 彩色图 HImage 尺寸 64x48",
                GetString(colorDoc, "outputs", $"{StepName}.Image", "type") == "image"
                    && GetInt(colorDoc, "outputs", $"{StepName}.Image", "width") == ImgWidth
                    && GetInt(colorDoc, "outputs", $"{StepName}.Image", "height") == ImgHeight,
                GetRaw(colorDoc, "outputs", $"{StepName}.Image"));

            // ---- S10 不带 X-Image-Name → CurrentPath 回落成 hub://流程名 ----
            // 这条同时反证了"图确实是从 Hub 槽里取走的"：走文件通路时这里会是个磁盘路径
            var hubResp = await Post(http, url + "?outputs=" + Uri.EscapeDataString($"{StepName}.CurrentPath"),
                BuildPng(gray: true), Token);
            var hubDoc = TryParse(hubResp.Body);
            Check("[S10] 无来源名 → CurrentPath = hub://流程名（证明走的是网络槽通路）",
                GetString(hubDoc, "outputs", $"{StepName}.CurrentPath") == $"hub://{FlowName}",
                $"HTTP {hubResp.Status} {GetRaw(hubDoc, "outputs", $"{StepName}.CurrentPath")}");

            // ---- S11 运行中 → 409（会话已由前面的请求补编译并注册，这里直接置位造窗口） ----
            var session = runtime.GetSessionByName(FlowName);
            Check("[S11] 前序请求已注册会话",
                session != null,
                session == null ? "GetSessionByName 返回 null" : $"SessionID={session.SessionID} CompiledVersion={session.CompiledVersion}");

            if (session != null)
            {
                session.IsRunning = true;
                try
                {
                    var busy = await Post(http, url, BuildPng(gray: true), Token);
                    Check("[S11] 流程运行中 → 409",
                        busy.Status == 409
                            && GetString(TryParse(busy.Body), "message")?.Contains("正在运行中") == true,
                        $"HTTP {busy.Status} body={Clip(busy.Body)}");
                }
                finally
                {
                    session.IsRunning = false;
                }
            }

            // ---- S12 未指定 outputs → 全量返回 ----
            var allResp = await Post(http, url, BuildPng(gray: true), Token);
            var allDoc = TryParse(allResp.Body);
            Check("[S12] 省略 outputs → 回传全部输出端口",
                allResp.Status == 200
                    && GetRaw(allDoc, "outputs", $"{StepName}.Image") != null
                    && GetRaw(allDoc, "outputs", $"{StepName}.CurrentIndex") != null
                    && GetRaw(allDoc, "outputs", $"{StepName}.TotalFiles") != null,
                $"HTTP {allResp.Status} keys={(allDoc == null ? "(解析失败)" : string.Join(",", allDoc.Value.GetProperty("outputs").EnumerateObject().Select(p => p.Name)))}");

            // Hub 槽不应有残留（每帧都被取走）
            Check("[S12] 流程结束后 Hub 槽已排空",
                ImageHub.Count(FlowName) == 0,
                $"Count={ImageHub.Count(FlowName)}");
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        private static string BuildAppConfigJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"HttpImageServer\": {");
            sb.AppendLine("    \"Enabled\": true,");
            sb.AppendLine($"    \"Host\": \"127.0.0.1\",");
            sb.AppendLine($"    \"Port\": {Port},");
            sb.AppendLine($"    \"Token\": \"{Token}\",");
            sb.AppendLine("    \"RequestTimeoutMs\": 30000");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>现场编一张 PNG：gray=true → Gray8 单通道；false → Bgr24 三通道</summary>
        private static byte[] BuildPng(bool gray)
        {
            var channels = gray ? 1 : 3;
            var stride = ImgWidth * channels;
            var pixels = new byte[stride * ImgHeight];

            // 填个可辨认的图案：灰度用横向渐变，彩色用三通道各自不同的渐变
            for (int y = 0; y < ImgHeight; y++)
            {
                for (int x = 0; x < ImgWidth; x++)
                {
                    var i = y * stride + x * channels;
                    if (gray)
                    {
                        pixels[i] = (byte)(x * 4);
                    }
                    else
                    {
                        pixels[i + 0] = (byte)(x * 4);   // B
                        pixels[i + 1] = (byte)(y * 5);   // G
                        pixels[i + 2] = (byte)(255 - x * 4); // R
                    }
                }
            }

            var format = gray ? PixelFormats.Gray8 : PixelFormats.Bgr24;
            var source = BitmapSource.Create(ImgWidth, ImgHeight, 96, 96, format, null, pixels, stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        /// <summary>现场造一帧 Hub 图像（供 [S13] 直接 Push 打破阻塞等待，不走 HTTP）</summary>
        private static HubImageItem BuildHubItem(string flowName, bool gray)
        {
            var channels = gray ? 1 : 3;
            var stride = ImgWidth * channels;
            var pixels = new byte[stride * ImgHeight];

            for (int y = 0; y < ImgHeight; y++)
            {
                for (int x = 0; x < ImgWidth; x++)
                {
                    var i = y * stride + x * channels;
                    pixels[i] = (byte)(x * 4);
                    if (!gray)
                    {
                        pixels[i + 1] = (byte)(y * 5);
                        pixels[i + 2] = (byte)(255 - x * 4);
                    }
                }
            }

            return new HubImageItem
            {
                FlowName = flowName,
                PixelData = pixels,
                Width = ImgWidth,
                Height = ImgHeight,
                Channels = channels,
                SourceName = "s13-push.png"
            };
        }

        private static async Task<(int Status, string Body)> Post(
            HttpClient http, string url, byte[] body, string token, string imageName = null)
        {
            using var msg = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, url);
            if (token != null)
                msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (imageName != null)
                msg.Headers.TryAddWithoutValidation("X-Image-Name", imageName);

            var content = new ByteArrayContent(body ?? Array.Empty<byte>());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            msg.Content = content;

            using var resp = await http.SendAsync(msg).ConfigureAwait(false);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private static JsonElement? TryParse(string body)
        {
            try
            {
                return JsonDocument.Parse(body).RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        private static bool JsonFlag(JsonElement? root, string name)
        {
            if (root == null) return false;
            return root.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        }

        private static int GetInt(JsonElement? root, params string[] path)
        {
            var v = Navigate(root, path);
            return v != null && v.Value.ValueKind == JsonValueKind.Number ? v.Value.GetInt32() : int.MinValue;
        }

        private static string GetString(JsonElement? root, params string[] path)
        {
            var v = Navigate(root, path);
            return v != null && v.Value.ValueKind == JsonValueKind.String ? v.Value.GetString() : null;
        }

        private static string GetRaw(JsonElement? root, params string[] path)
        {
            var v = Navigate(root, path);
            return v?.GetRawText();
        }

        private static JsonElement? Navigate(JsonElement? root, string[] path)
        {
            if (root == null) return null;
            var cur = root.Value;
            foreach (var key in path)
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(key, out var next))
                    return null;
                cur = next;
            }
            return cur;
        }

        private static string DescribeImage(JsonElement? root)
        {
            var w = GetInt(root, "image", "width");
            var h = GetInt(root, "image", "height");
            var c = GetInt(root, "image", "channels");
            return w == int.MinValue ? "image=(缺失)" : $"image={w}x{h}x{c}";
        }

        private static string Clip(string s)
            => string.IsNullOrEmpty(s) ? "(空)" : (s.Length > 200 ? s.Substring(0, 200) + "…" : s);
    }
}
