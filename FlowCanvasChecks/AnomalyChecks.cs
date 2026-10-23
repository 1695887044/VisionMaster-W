using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.ML.OnnxRuntime;
using Plugin.DlAnomaly;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 深度学习异常检测插件（Plugin.DlAnomaly）的断言。分四段，各自独立可跑：
    ///
    ///   ① 输出契约嗅探（受控数据，不需要模型）—— 按秩识别 / 拒绝不认识的形状 / 报出真实形状
    ///   ② 会话与运行时守卫（不需要模型）
    ///   ③ 插件壳与端口契约（不需要模型）
    ///   ④ 模型态与预处理口径（不需要模型）
    ///
    /// 为什么①是重点
    /// ---------
    /// 异常检测的 ONNX 输出**没有统一标准**：图像级分数可能是 [1] 或 [1,1]，
    /// 像素级热力图可能是 [1,1,H,W] 或 [1,H,W]。如果按"按顺序取输出"去解码，
    /// 把检测器的 [1,84,8400] 当分数硬算，会得出一个"看着像分数"的数，
    /// **运行一切正常，但判据永远错**。这类错误只能靠"已知形状 → 已知结论"来钉。
    ///
    /// 另一条同族的坑：`NodeMetadata` 上**没有 Name**
    /// （实测 Microsoft.ML.OnnxRuntime 1.x 只有 Dimensions / SymbolicDimensions / ElementType 等），
    /// 输出名只能取 `OutputMetadata` 字典的 Key。只传 Values 就永远拿不到名字，
    /// 加载期认出的契约名与 Run() 返回的名字对不上，运行期静默取不到值。
    /// </summary>
    internal static class AnomalyChecks
    {
        public static void Run()
        {
            Segment("[深度学习异常检测] 输出契约：按秩识别 / 拒绝不认识的形状", RunContractChecks);
            Segment("[深度学习异常检测] 会话与运行时守卫", RunSessionChecks);
            Segment("[深度学习异常检测] 插件壳与端口契约", RunPluginShellChecks);
            Segment("[深度学习异常检测] 模型态与预处理口径", RunModelStateChecks);
        }

        /// <summary>
        /// 跑一段断言；**这一段的任何异常都必须变成一条 [×失败]，不许掀翻整个宿主**。
        /// </summary>
        /// <remarks>
        /// 2026-10-23 实测教训：契约段里用反射写只读属性抛了 ArgumentException，
        /// 没有这层兜底时整个 FlowCanvasChecks 进程 UnhandledException 退出，
        /// 前面已经跑绿的 1793 条结果连汇总行都没落盘——等于这一轮验证白跑。
        /// 断言宿主崩溃 = 整批结果不可信，比某一条红更坏（R38：跳过/异常都要能被反证）。
        /// </remarks>
        private static void Segment(string title, Action body)
        {
            Section(title);
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check($"{title} · 整段执行不抛异常", false,
                    $"{ex.GetType().Name}: {ex.Message}（后续断言未执行，先修这条）");
            }
        }

        // ————————————————————————————————
        // ① 输出契约嗅探
        // ————————————————————————————————

        private static void RunContractChecks()
        {
            // ---- 秩 2 [1,1] = 图像级分数 ----
            KeyValuePair<string, int[]>[] scoreOnly = new[]
            {
                Pair("score", new[] { 1, 1 }),
            };
            bool scoreOk = AnomalyPostProcess.TryResolveContract(scoreOnly, out AnomalyOutputContract? scoreContract, out string scoreErr);
            Check("[Anomaly] 契约：秩 2 的 [1,1] 认成图像级分数",
                scoreOk && scoreContract != null
                    && scoreContract.ScoreOutputName == "score"
                    && scoreContract.HeatmapOutputName == null
                    && !scoreContract.HasHeatmap,
                $"ok={scoreOk} score={scoreContract?.ScoreOutputName} heat={scoreContract?.HeatmapOutputName ?? "null"} err={scoreErr}");

            // ---- 秩 2 [1,1] + 秩 4 [1,1,H,W] ----
            KeyValuePair<string, int[]>[] both = new[]
            {
                Pair("score", new[] { 1, 1 }),
                Pair("heatmap", new[] { 1, 1, 32, 48 }),
            };
            bool bothOk = AnomalyPostProcess.TryResolveContract(both, out AnomalyOutputContract? bothContract, out string bothErr);
            Check("[Anomaly] 契约：秩 2 分数 + 秩 4 热力图同时认出，H/W 取 [2]/[3]",
                bothOk && bothContract != null
                    && bothContract.ScoreOutputName == "score"
                    && bothContract.HeatmapOutputName == "heatmap"
                    && bothContract.HeatmapHeight == 32
                    && bothContract.HeatmapWidth == 48
                    && bothContract.HasHeatmap,
                $"ok={bothOk} h={bothContract?.HeatmapHeight} w={bothContract?.HeatmapWidth} err={bothErr}");

            // ---- 只有秩 4 热力图，没有分数 ----
            KeyValuePair<string, int[]>[] heatOnly = new[]
            {
                // 边长必须 ≥ 32：秩 3 的 [1,H,W] 与检测器 [1,N,C] 同秩同 dims[0]，只靠形状分不开，
                // 所以契约里加了热力图边长下界（见 AnomalyContract.TryReadHeatmapShape 的 remarks）。
                // 这里用 32×32 而不是更小的网格，正是为了让"合法热力图的边界"落到守卫上沿。
                Pair("heatmap", new[] { 1, 1, 32, 32 }),
            };
            bool heatOk = AnomalyPostProcess.TryResolveContract(heatOnly, out AnomalyOutputContract? heatContract, out string heatErr);
            Check("[Anomaly] 契约：只有秩 4 热力图时分数留空（由调用方归约），HasHeatmap=true",
                heatOk && heatContract != null
                    && heatContract.ScoreOutputName == null
                    && heatContract.HeatmapOutputName == "heatmap"
                    && heatContract.HasHeatmap
                    && heatContract.HeatmapHeight == 32
                    && heatContract.HeatmapWidth == 32,
                $"ok={heatOk} score={heatContract?.ScoreOutputName ?? "null"} heat={heatContract?.HeatmapOutputName ?? "null"} err={heatErr}");

            // ---- 秩 3 [1,H,W] 也是合法热力图变体 ----
            KeyValuePair<string, int[]>[] rank3 = new[]
            {
                Pair("heatmap", new[] { 1, 40, 56 }),
            };
            bool rank3Ok = AnomalyPostProcess.TryResolveContract(rank3, out AnomalyOutputContract? rank3Contract, out string rank3Err);
            Check("[Anomaly] 契约：秩 3 的 [1,H,W] 认成热力图，H/W 取 [1]/[2]",
                rank3Ok && rank3Contract != null
                    && rank3Contract.HeatmapOutputName == "heatmap"
                    && rank3Contract.HeatmapHeight == 40
                    && rank3Contract.HeatmapWidth == 56,
                $"ok={rank3Ok} h={rank3Contract?.HeatmapHeight} w={rank3Contract?.HeatmapWidth} err={rank3Err}");

            // ---- 秩 5 必须拒绝，且必须报出真实形状 ----
            KeyValuePair<string, int[]>[] badRank = new[]
            {
                Pair("weird", new[] { 1, 1, 1, 64, 64 }),
            };
            bool badRejected = !AnomalyPostProcess.TryResolveContract(badRank, out _, out string badErr);
            Check("[Anomaly] 【核心】秩 5 的 [1,1,1,64,64] 被拒绝，不拿它当分数或热力图硬算",
                badRejected,
                badErr);
            Check("[Anomaly] 【核心】拒绝时错误串必须报出真实形状（含 秩 与 形状 与输出名）",
                badRejected && badErr.Contains("秩") && badErr.Contains("形状") && badErr.Contains("weird"),
                badErr);

            // ---- 检测器输出 [1,84,8400] 也必须拒绝 ----
            // 秩 3 但 dims[0] != 1，不是 [1,H,W] 形状；若被误认为热力图，
            // H=84 会被当成图高，热力图上屏就会画出一条细线而不是缺陷。
            KeyValuePair<string, int[]>[] detect = new[]
            {
                Pair("output0", new[] { 1, 84, 8400 }),
            };
            bool detectRejected = !AnomalyPostProcess.TryResolveContract(detect, out _, out string detectErr);
            // 秩 3 的两义性：[1,84,8400] 的 dims[0] 也是 1，和 [1,H,W] 热力图只靠秩分不开。
            // 靠尺寸守卫拦：84 < MinHeatmapEdge(32)?  不——84 ≥ 32，所以真正拦住它的是宽度 8400 > MaxHeatmapEdge(8192)。
            Check("[Anomaly] 【核心】检测器输出 [1,84,8400] 被拒绝（秩 3 两义：靠热力图边长守卫挡住）",
                detectRejected,
                detectErr);

            // ---- 空的输出列表 → 拒绝并说清 ----
            bool emptyRejected = !AnomalyPostProcess.TryResolveContract(Array.Empty<KeyValuePair<string, int[]>>(), out _, out string emptyErr);
            Check("[Anomaly] 空输出列表被拒绝且给出可读原因",
                emptyRejected && emptyErr.Length > 0,
                emptyErr);

            // ---- 热力图 H/W 为 0（动态形状）→ 拒绝，不猜 ----
            KeyValuePair<string, int[]>[] zeroDim = new[]
            {
                Pair("heatmap", new[] { 1, 1, 0, 0 }),
            };
            bool zeroRejected = !AnomalyPostProcess.TryResolveContract(zeroDim, out _, out string zeroErr);
            Check("[Anomaly] 热力图 H/W 为 0（动态形状）被拒绝，不猜尺寸",
                zeroRejected,
                zeroErr);

            // ---- Describe 必须把两个名字都写出来 ----
            string described = bothContract?.Describe() ?? "";
            Check("[Anomaly] 契约 Describe 同时提到分数与热力图的名字",
                described.Contains("score") && described.Contains("heatmap") && described.Contains("48"),
                described);
        }

        // ————————————————————————————————
        // ② 会话与运行时守卫
        // ————————————————————————————————

        private static void RunSessionChecks()
        {
            // 空路径
            bool emptyRejected = !AnomalySession.TryAcquire(null, out _, out string emptyErr);
            Check("[Anomaly] 会话守卫：空模型路径被拒绝，文案含「尚未指定模型」",
                emptyRejected && emptyErr.Contains("尚未指定模型"),
                emptyErr);

            // 路径不存在
            string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vm_anomaly_not_exists_" + Guid.NewGuid().ToString("N") + ".onnx");
            bool missingRejected = !AnomalySession.TryAcquire(missing, out _, out string missingErr);
            Check("[Anomaly] 会话守卫：路径不存在被拒绝，文案含「模型文件不存在」与完整路径",
                missingRejected && missingErr.Contains("模型文件不存在") && missingErr.Contains(missing),
                missingErr);

            // 32 位进程守卫：这条只在 32 位进程中可触发，64 位下无法正证。
            // 属于 R38 的"跳过型"断言，必须在理由里写清反证依据：
            //   反证依据 = Environment.Is64BitProcess 为 true 时该分支在源码里不可达，
            //   且分支本体只做错误串赋值（无副作用），删除它不会改变 64 位下的任何行为。
            bool is64 = Environment.Is64BitProcess;
            Check("[Anomaly] 会话守卫：32 位进程拦截（64 位宿主下不可达，跳过）",
                true,
                is64 ? "跳过：当前是 64 位进程，该分支不可达；反证依据见源码 TryAcquire 第三道守卫" : "当前是 32 位进程，此条应为红灯");

            // 引用计数缓存：同一个不存在路径连续调用两次都失败，且不改变缓存条数
            int before = AnomalySession.CachedSessionCount;
            AnomalySession.TryAcquire(missing, out _, out _);
            int after = AnomalySession.CachedSessionCount;
            Check("[Anomaly] 会话守卫：加载失败不污染进程级缓存",
                before == after,
                $"before={before} after={after}");
        }

        // ————————————————————————————————
        // ③ 插件壳与端口契约
        // ————————————————————————————————

        private static void RunPluginShellChecks()
        {
            var plugin = new DlAnomalyPlugin();

            // 显示名四件套
            var display = typeof(DlAnomalyPlugin).GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>();
            Check("[Anomaly] 插件清单：Name=深度学习异常检测",
                display?.Name == "深度学习异常检测",
                display?.Name ?? "null");
            Check("[Anomaly] 插件清单：GroupName=图像处理",
                display?.GroupName == "图像处理",
                display?.GroupName ?? "null");
            Check("[Anomaly] 插件清单：Description 说明「不猜」口径",
                display?.Description?.Contains("不猜") == true,
                display?.Description ?? "null");

            // 端口清单
            string[] inputs = plugin.Inputs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Check("[Anomaly] 输入端口恰好 1 个：Image",
                inputs.Length == 1 && inputs[0] == "Image",
                string.Join(",", inputs));

            // 剔除基类端口：VisionPluginBase 自带 Success / ErrorMessage（所有插件都有），
            // 不属于本插件产出，混进来数个数永远对不上（实测第一次跑就红在这）。
            string[] baseOutputs = { "Success", "ErrorMessage" };
            string[] outputs = plugin.Outputs.Keys
                .Where(k => !baseOutputs.Contains(k, StringComparer.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            string[] expectedOutputs =
            {
                "HasScore", "HeatmapImage", "HeatmapMaxCol", "HeatmapMaxRow",
                "HeatmapMaxValue", "Score", "Summary",
            };
            Check("[Anomaly] 自有输出端口恰好 7 个且名字一致（不含基类 Success/ErrorMessage）",
                outputs.SequenceEqual(expectedOutputs),
                string.Join(",", outputs));

            // 默认参数值
            Check("[Anomaly] 默认参数：模型路径为空、缩放模式 Letterbox、输入尺寸 0",
                string.IsNullOrEmpty(plugin.ModelPath)
                    && plugin.ResizeMode == AnomalyResizeMode.Letterbox
                    && plugin.InputSize == 0,
                $"path='{plugin.ModelPath}' mode={plugin.ResizeMode} size={plugin.InputSize}");
            Check("[Anomaly] 默认参数：热力图上限 0（自适应）、插值 Nearest、预览窗口 1",
                Math.Abs(plugin.HeatmapMax) < 1e-9
                    && plugin.HeatmapInterpolation == AnomalyInterpolation.Nearest
                    && plugin.DisplayViewIndex == 1,
                $"max={plugin.HeatmapMax} interp={plugin.HeatmapInterpolation} view={plugin.DisplayViewIndex}");

            // 夹取钩子
            plugin.InputSize = -5;
            plugin.HeatmapMax = -1;
            plugin.DisplayViewIndex = 42;
            Check("[Anomaly] 参数夹取：InputSize→0、HeatmapMax→0、DisplayViewIndex→9",
                plugin.InputSize == 0 && Math.Abs(plugin.HeatmapMax) < 1e-9 && plugin.DisplayViewIndex == 9,
                $"size={plugin.InputSize} max={plugin.HeatmapMax} view={plugin.DisplayViewIndex}");

            // 无图执行：必须失败且文案点出 Image 端口
            var ctx = NewContext();
            plugin.Execute(ctx);
            Check("[Anomaly] 无上游图像时失败，文案点出 Image 端口",
                plugin.Success.TypedValue is false
                    && (plugin.ErrorMessage.Value as string)?.Contains("Image") == true,
                plugin.ErrorMessage.Value as string ?? "null");

            // 未选模型时：即便给了图，也要在模型这一步失败（而不是去跑推理）
            var gray = MakeGray(64, 64, 128);
            try
            {
                plugin.Image.Value = gray;
                plugin.Execute(NewContext());
                Check("[Anomaly] 有图但未选模型 → 在模型守卫处失败，文案含「尚未选择模型」",
                    plugin.Success.TypedValue is false
                        && (plugin.ErrorMessage.Value as string)?.Contains("尚未选择模型") == true,
                    plugin.ErrorMessage.Value as string ?? "null");
            }
            finally
            {
                gray.Dispose();
            }

            // 轮首复位：上一轮残留的分数不能在失败轮里继续挂出去。
            // 之所以走一次 Execute（无图 → 复位后立刻失败）而不是直接调 RunAlgorithm：
            // Success 只在 Execute 开头被预置 true，直接调 RunAlgorithm 会得到 Success=false，
            // 与本条要钉的"复位"无关，会变成假红（ImageDiffChecks 阶段踩过同一个坑）。
            var resetProbe = new DlAnomalyPlugin();
            resetProbe.Score.Value = 123.456;
            resetProbe.HasScore.Value = true;
            resetProbe.HeatmapMaxRow.Value = 77;
            resetProbe.HeatmapMaxCol.Value = 88;
            resetProbe.Execute(NewContext());
            Check("[Anomaly] 轮首复位：Score→NaN、HasScore→false、HeatmapMaxRow/Col→-1",
                double.IsNaN(resetProbe.Score.TypedValue)
                    && resetProbe.HasScore.Value is false
                    && Math.Abs(resetProbe.HeatmapMaxRow.TypedValue + 1) < 1e-9
                    && Math.Abs(resetProbe.HeatmapMaxCol.TypedValue + 1) < 1e-9,
                $"score={resetProbe.Score.Value} has={resetProbe.HasScore.Value} row={resetProbe.HeatmapMaxRow.Value} col={resetProbe.HeatmapMaxCol.Value}");
        }

        // ————————————————————————————————
        // ④ 模型态与预处理口径
        // ————————————————————————————————

        private static void RunModelStateChecks()
        {
            Check("[Anomaly] 三态枚举：NotLoaded / Ready / Failed 齐全",
                Enum.GetNames(typeof(DlModelState)).SequenceEqual(new[] { "NotLoaded", "Ready", "Failed" }),
                string.Join(",", Enum.GetNames(typeof(DlModelState))));

            var plugin = new DlAnomalyPlugin();
            plugin.RefreshModelState();
            Check("[Anomaly] 未选模型时模型态为 NotLoaded",
                plugin.ModelState == DlModelState.NotLoaded,
                plugin.ModelState.ToString());

            plugin.ModelPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vm_anomaly_missing_" + Guid.NewGuid().ToString("N") + ".onnx");
            plugin.RefreshModelState();
            Check("[Anomaly] 路径指向不存在的文件时模型态为 Failed",
                plugin.ModelState == DlModelState.Failed,
                plugin.ModelState.ToString());

            // 补边灰必须是 114 —— 填 0 会造出训练时没见过的强边缘，
            // 异常模型把这些边缘当异常，分数整体抬高，且不报错。
            Check("[Anomaly] 【核心】补边灰度 = 114（填 0 会让分数整体抬高且不报错）",
                ResizePlan.PadGrayValue == 114,
                ResizePlan.PadGrayValue.ToString());

            // Letterbox 等比缩放取 min，且居中补边
            var plan = ResizePlan.Compute(200, 100, 224, 224, AnomalyResizeMode.Letterbox);
            Check("[Anomaly] 【核心】Letterbox：200x100 → 224x224 时缩放比取 min，得 224x112 且纵向居中",
                plan.ScaledWidth == 224
                    && plan.ScaledHeight == 112
                    && plan.PadX == 0
                    && plan.PadY == 56,
                plan.Describe());

            // Stretch 不补边
            var stretch = ResizePlan.Compute(200, 100, 224, 224, AnomalyResizeMode.Stretch);
            Check("[Anomaly] Stretch：不补边、直接拉满",
                stretch.ScaledWidth == 224 && stretch.ScaledHeight == 224 && stretch.PadX == 0 && stretch.PadY == 0,
                stretch.Describe());
        }

        // ————————————————————————————————
        // 造数据与上下文
        // ————————————————————————————————

        /// <summary>
        /// 造一对「输出名 + 维度」喂给契约嗅探的**纯形状入口**。
        /// </summary>
        /// <remarks>
        /// 曾经这里是造一个假的 <c>NodeMetadata</c>（无构造器 → GetUninitializedObject + 反射写 Dimensions），
        /// 结果运行期抛 <c>ArgumentException: Property set method not found</c>（Dimensions 是只读属性），
        /// 并且因为 RunContractChecks 没有兜住，异常直接掀翻整个断言宿主、前 1793 条结果连汇总行都没了。
        /// 生产代码已补一个只吃 <c>int[]</c> 的重载，这里直接用——契约判定本来就是纯函数。
        /// </remarks>
        private static KeyValuePair<string, int[]> Pair(string name, int[] dims)
            => new(name, dims);

        private static HImage MakeGray(int width, int height, double gray)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", width, height);
            HOperatorSet.GenImageProto(proto, out HObject body, gray);
            proto.Dispose();
            return new HImage(body);
        }

        private static VisionMaster.Services.ExecutionContext NewContext()
            => new(new StubLog(), new FlowSession { FlowName = "异常检测断言" },
                   new WorkspaceContext(), new System.Threading.CancellationTokenSource().Token);
    }
}
