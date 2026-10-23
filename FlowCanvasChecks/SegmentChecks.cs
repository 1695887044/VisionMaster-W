using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using HalconDotNet;
using Plugin.DlSegment;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 深度学习分割插件（Plugin.DlSegment）的断言。分四段，各自独立可跑：
    ///
    ///   ① 输出契约嗅探（受控数据，不需要模型）—— 只认 4 维 [1,C,H,W]，C ≥ 2；其余拒绝并报出真实形状
    ///   ② 会话与运行时守卫（不需要模型）
    ///   ③ 插件壳与端口契约（不需要模型）
    ///   ④ 模型态与预处理几何（不需要模型）
    ///
    /// 为什么①是重点
    /// ---------
    /// 语义分割的输出形状分歧比异常检测小，但坑同样静默：
    ///   · 检测器的 `[1,84,8400]` 是 **3 维**，不是分割；秩 4 但 `C == 1` 的是**单类掩膜**
    ///     （`[1,1,H,W]`，用阈值或 Blob 更合适），不是语义分割；
    ///   · 秩 4 但 `dims[0] != 1` 的是**批量**，本插件的类别图只有一张，必须拒；
    ///   · H/W 超过 8192 的多半是被误当特征的权重张量。
    /// 这些一律"明确拒绝 + 把读到的真实形状报出来"，因为猜错的后果是
    /// **运行一切正常，但每一帧的面积都是错的**。
    ///
    /// 另一个必须钉住的点：`ClassMap` 是**序号图**（0 = 背景，1..C-1 = 类别），
    /// 不是 0/255 掩膜。图上看着"黑底 + 灰块"很像掩膜，一旦有人按 255 去判断前景，
    /// 类别序号 1 的像素会被当成"几乎没有"，判据整体错位。
    /// </summary>
    internal static class SegmentChecks
    {
        public static void Run()
        {
            Segment("[深度学习分割] 输出契约：只认 [1,C,H,W] / 拒绝不认识的形状", RunContractChecks);
            Segment("[深度学习分割] 会话与运行时守卫", RunSessionChecks);
            Segment("[深度学习分割] 插件壳与端口契约", RunPluginShellChecks);
            Segment("[深度学习分割] 模型态与预处理几何", RunModelStateChecks);
        }

        /// <summary>
        /// 跑一段断言；**这一段的任何异常都必须变成一条 [×失败]，不许掀翻整个宿主**。
        /// </summary>
        /// <remarks>
        /// 同 AnomalyChecks 的教训：断言宿主崩溃 = 整批结果不可信，比某一条红更坏（R38）。
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

        // ───────────────────────── ① 输出契约 ─────────────────────────

        private static void RunContractChecks()
        {
            // 正规语义分割：3 类（含背景），特征图 32×48
            KeyValuePair<string, int[]>[] semantic =
                { Pair("logits", new[] { 1, 3, 32, 48 }) };
            bool semanticOk = SegmentPostProcess.TryResolveContract(semantic, out SegmentOutputContract? semanticContract, out string semanticError);
            Check("[Segment] 【核心】[1,3,32,48] 认成语义分割：C=3 / H=32 / W=48",
                semanticOk && semanticContract != null
                    && semanticContract.ClassCount == 3
                    && semanticContract.Height == 32
                    && semanticContract.Width == 48,
                semanticOk ? semanticContract!.Describe() : semanticError);

            // 多输出时取第一个合法的那份，而不是"按顺序取第一个"
            KeyValuePair<string, int[]>[] mixed =
            {
                Pair("aux", new[] { 1, 84, 8400 }),
                Pair("main", new[] { 1, 5, 64, 64 }),
            };
            bool mixedOk = SegmentPostProcess.TryResolveContract(mixed, out SegmentOutputContract? mixedContract, out string mixedError);
            Check("[Segment] 【核心】多输出时挑出合法的那份（检测器 [1,84,8400] 被跳过）",
                mixedOk && mixedContract?.ClassCount == 5 && mixedContract.Height == 64 && mixedContract.Width == 64,
                mixedOk ? mixedContract!.Describe() : mixedError);

            // C == 1：单类掩膜，不是语义分割
            KeyValuePair<string, int[]>[] singleChannel =
                { Pair("mask", new[] { 1, 1, 64, 64 }) };
            bool singleRejected = !SegmentPostProcess.TryResolveContract(singleChannel, out _, out string singleError);
            // 判据只认"拒绝了 + 文案指到了两处真因"，不认字面 "C=1"：
            // 真实拒绝文案写的是「只认 4 维 [1,C,H,W] 且 C ≥ 2」并点名「二值掩膜 [1,1,H,W] 都不在本插件契约内」，
            // 拼接出的形状串是 "[1, 1, 64, 64]"（带空格）。钉字面 "C=1" 只会让文案一改写就假红。
            Check("[Segment] 【核心】C=1 的 [1,1,64,64] 被拒绝（单类掩膜应走阈值/Blob）",
                singleRejected
                && (singleError.Contains("C ≥ 2") || singleError.Contains("[1, 1, 64, 64]")),
                singleError);

            // 秩 3：检测器输出
            KeyValuePair<string, int[]>[] detect =
                { Pair("output0", new[] { 1, 84, 8400 }) };
            bool detectRejected = !SegmentPostProcess.TryResolveContract(detect, out _, out string detectError);
            Check("[Segment] 【核心】检测器输出 [1,84,8400]（秩 3）被拒绝",
                detectRejected && detectError.Contains("秩"),
                detectError);

            // 秩 5
            KeyValuePair<string, int[]>[] badRank =
                { Pair("weird", new[] { 1, 1, 1, 64, 64 }) };
            bool badRankRejected = !SegmentPostProcess.TryResolveContract(badRank, out _, out string badRankError);
            Check("[Segment] 秩 5 的 [1,1,1,64,64] 被拒绝，且错误串带真实秩",
                badRankRejected && badRankError.Contains("秩"),
                badRankError);

            // 批量 dims[0] != 1
            KeyValuePair<string, int[]>[] batched =
                { Pair("logits", new[] { 2, 3, 32, 32 }) };
            bool batchedRejected = !SegmentPostProcess.TryResolveContract(batched, out _, out string batchedError);
            Check("[Segment] 批量 [2,3,32,32] 被拒绝（dims[0] 必须为 1）",
                batchedRejected && batchedError.Length > 0,
                batchedError);

            // 超长边
            KeyValuePair<string, int[]>[] huge =
                { Pair("logits", new[] { 1, 3, 4, 20000 }) };
            bool hugeRejected = !SegmentPostProcess.TryResolveContract(huge, out _, out string hugeError);
            Check("[Segment] 超长边 [1,3,4,20000] 被拒绝（多半是权重张量）",
                hugeRejected && hugeError.Length > 0,
                hugeError);

            // 空输出列表
            bool emptyRejected = !SegmentPostProcess.TryResolveContract(
                Array.Empty<KeyValuePair<string, int[]>>(), out _, out string emptyError);
            Check("[Segment] 空输出列表被拒绝", emptyRejected && emptyError.Length > 0, emptyError);

            // 拒绝文案必须能被用户看懂：含名字 + 真实形状 + "不会替你猜"
            Check("[Segment] 拒绝文案含输出名字、真实形状与「不会替你猜」",
                detectError.Contains("output0") && detectError.Contains("8400") && detectError.Contains("不会替你猜"),
                detectError);

            // 任务标签守卫
            bool taskOk = SegmentPostProcess.IsSupportedTask("segment", out _);
            bool taskBlankOk = SegmentPostProcess.IsSupportedTask(null, out _);
            bool taskBad = !SegmentPostProcess.IsSupportedTask("detect", out string taskError);
            Check("[Segment] 任务标签：segment 放行 / 空白放行 / detect 拒绝",
                taskOk && taskBlankOk && taskBad && taskError.Length > 0,
                $"segment={taskOk} blank={taskBlankOk} detectReject={taskBad} {taskError}");

            // 类别名解析
            IReadOnlyList<string> parsed = SegmentPostProcess.ParseClassNames("ok,scratch,dent", 3);
            IReadOnlyList<string> fallback = SegmentPostProcess.ParseClassNames("only-two", 3);
            Check("[Segment] 类别名：数量对得上就用元数据，对不上回落 class_N",
                parsed.Count == 3 && parsed[1] == "scratch"
                    && fallback.Count == 3 && fallback[0] == "class_0",
                $"parsed=[{string.Join(",", parsed)}] fallback=[{string.Join(",", fallback)}]");
        }

        // ───────────────────────── ② 会话守卫 ─────────────────────────

        private static void RunSessionChecks()
        {
            bool empty = !SegmentSession.TryAcquire("", out SegmentSession? emptySession, out string emptyError);
            emptySession?.Dispose();
            Check("[Segment] 空路径：明确拒绝并提示选模型",
                empty && emptyError.Contains("尚未指定模型"), emptyError);

            string missing = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "vm-no-such-segment-model.onnx");
            bool missingOk = !SegmentSession.TryAcquire(missing, out SegmentSession? missingSession, out string missingError);
            missingSession?.Dispose();
            Check("[Segment] 文件不存在：拒绝并回显完整路径",
                missingOk && missingError.Contains(missing), missingError);

            // 32 位守卫：分支体只赋错误串、无副作用，64 位宿主下不可达。
            // 反证依据：Environment.Is64BitProcess 为 true 时 TryAcquire 的第三道守卫不会进入。
            if (Environment.Is64BitProcess)
            {
                Check("[Segment] 32 位进程拦截",
                    true,
                    "跳过：当前是 64 位进程，该分支不可达；反证依据见源码 TryAcquire 的第三道守卫");
            }
            else
            {
                Check("[Segment] 32 位进程拦截", false, "当前是 32 位进程 —— 该分支应已生效，此断言需重写");
            }

            int before = SegmentSession.CachedSessionCount;
            SegmentSession.TryAcquire(missing, out SegmentSession? failSession, out _);
            failSession?.Dispose();
            Check("[Segment] 加载失败不污染会话缓存",
                SegmentSession.CachedSessionCount == before,
                $"before={before} after={SegmentSession.CachedSessionCount}");
        }

        // ───────────────────────── ③ 插件壳与端口 ─────────────────────────

        private static void RunPluginShellChecks()
        {
            var plugin = new DlSegmentPlugin();

            var display = (System.ComponentModel.DataAnnotations.DisplayAttribute?)Attribute.GetCustomAttribute(
                typeof(DlSegmentPlugin), typeof(System.ComponentModel.DataAnnotations.DisplayAttribute));
            Check("[Segment] [Display]：Name=深度学习分割 / GroupName=图像处理",
                display?.Name == "深度学习分割" && display?.GroupName == "图像处理",
                $"Name={display?.Name} GroupName={display?.GroupName}");
            Check("[Segment] [Display] 说明里写清「不猜」这条纪律",
                display?.Description?.Contains("不猜") == true, display?.Description ?? "null");

            Check("[Segment] 输入端口恰好 Image 一个",
                plugin.Inputs.Count == 1 && plugin.Inputs.ContainsKey("Image"),
                string.Join(",", plugin.Inputs.Keys));

            // 自有输出端口（剔除基类 Success / ErrorMessage）
            string[] baseOutputs = { "Success", "ErrorMessage" };
            string[] expectedOutputs =
            {
                "ClassAreas", "ClassMap", "ClassRatios", "DominantClassIndex",
                "DominantClassName", "ForegroundRatio", "Region", "RegionCount",
                "RegionMask", "Summary",
            };
            string[] outputs = plugin.Outputs.Keys
                .Where(k => !baseOutputs.Contains(k, StringComparer.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            Check("[Segment] 自有输出端口恰好 10 个且名字一致（不含基类 Success/ErrorMessage）",
                outputs.SequenceEqual(expectedOutputs), string.Join(",", outputs));

            Check("[Segment] 默认参数：Letterbox / InputSize=0 / Nearest / AllClasses / ClassIndex=1 / View=1",
                plugin.ResizeMode == SegmentResizeMode.Letterbox
                    && plugin.InputSize == 0
                    && plugin.Interpolation == SegmentInterpolation.Nearest
                    && plugin.RegionMode == SegmentRegionMode.AllClasses
                    && plugin.ClassIndex == 1
                    && plugin.DisplayViewIndex == 1,
                $"{plugin.ResizeMode}/{plugin.InputSize}/{plugin.Interpolation}/{plugin.RegionMode}/{plugin.ClassIndex}/{plugin.DisplayViewIndex}");
            Check("[Segment] 模型路径默认空串（不许内置任何模型）",
                plugin.ModelPath.Length == 0, plugin.ModelPath);

            // 夹取
            plugin.InputSize = -8;
            plugin.DisplayViewIndex = 42;
            plugin.ClassIndex = 999;
            Check("[Segment] 夹取：InputSize→0 / DisplayViewIndex→9 / ClassIndex→255",
                plugin.InputSize == 0 && plugin.DisplayViewIndex == 9 && plugin.ClassIndex == 255,
                $"InputSize={plugin.InputSize} View={plugin.DisplayViewIndex} ClassIndex={plugin.ClassIndex}");
            plugin.Dispose();

            // 无图执行 → 明确失败
            var noImage = new DlSegmentPlugin();
            noImage.Execute(NewContext());
            Check("[Segment] 上游无图：失败且文案指向 Image 端口",
                noImage.Success.TypedValue is false
                    && (noImage.ErrorMessage.Value as string)?.Contains("Image") == true,
                (noImage.ErrorMessage.Value as string) ?? "null");
            noImage.Dispose();

            // 有图但没选模型 → 明确失败
            var noModel = new DlSegmentPlugin();
            using (HImage gray = MakeGray(64, 64, 128))
            {
                noModel.Image.Value = gray;
                noModel.Execute(NewContext());
            }
            Check("[Segment] 有图无模型：失败且文案是「尚未选择模型」",
                noModel.Success.TypedValue is false
                    && (noModel.ErrorMessage.Value as string)?.Contains("尚未选择模型") == true,
                (noModel.ErrorMessage.Value as string) ?? "null");
            noModel.Dispose();

            // 轮首复位：必须走 Execute（Success 只在 Execute 开头预置 true）
            var resetProbe = new DlSegmentPlugin();
            resetProbe.Execute(NewContext());
            Check("[Segment] 轮首复位：RegionCount=0 / ForegroundRatio=NaN / DominantClassIndex=-1",
                resetProbe.RegionCount.TypedValue == 0
                    && double.IsNaN(resetProbe.ForegroundRatio.TypedValue)
                    && resetProbe.DominantClassIndex.TypedValue == -1,
                $"count={resetProbe.RegionCount.TypedValue} ratio={resetProbe.ForegroundRatio.TypedValue} dominant={resetProbe.DominantClassIndex.TypedValue}");
            resetProbe.Dispose();
        }

        // ───────────────────────── ④ 模型态与几何 ─────────────────────────

        private static void RunModelStateChecks()
        {
            Check("[Segment] DlModelState 三态齐全",
                Enum.GetNames(typeof(DlModelState)).SequenceEqual(new[] { "NotLoaded", "Ready", "Failed" }),
                string.Join(",", Enum.GetNames(typeof(DlModelState))));

            var plugin = new DlSegmentPlugin();
            Check("[Segment] 未选模型时模型态 = NotLoaded",
                plugin.ModelState == DlModelState.NotLoaded, plugin.ModelState.ToString());
            plugin.Dispose();

            var broken = new DlSegmentPlugin
            {
                ModelPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vm-no-such-segment.onnx"),
            };
            Check("[Segment] 路径不存在时模型态 = Failed",
                broken.ModelState == DlModelState.Failed, broken.ModelState.ToString());
            broken.Dispose();

            Check("[Segment] 补边灰度 = 114（黑边是模型没见过的强边缘）",
                SegmentResizePlan.PadGrayValue == 114,
                SegmentResizePlan.PadGrayValue.ToString());

            SegmentResizePlan letterbox = SegmentResizePlan.Compute(200, 100, 224, 224, SegmentResizeMode.Letterbox);
            Check("[Segment] Letterbox 200×100 → 224×224：缩放 224×112、补边 (0,56)",
                letterbox.ScaledWidth == 224 && letterbox.ScaledHeight == 112
                    && letterbox.PadX == 0 && letterbox.PadY == 56,
                letterbox.Describe());

            SegmentResizePlan stretch = SegmentResizePlan.Compute(200, 100, 224, 224, SegmentResizeMode.Stretch);
            Check("[Segment] Stretch 200×100 → 224×224：无补边",
                stretch.ScaledWidth == 224 && stretch.ScaledHeight == 224
                    && stretch.PadX == 0 && stretch.PadY == 0,
                stretch.Describe());
        }

        // ───────────────────────── 工具 ─────────────────────────

        private static KeyValuePair<string, int[]> Pair(string name, int[] dims) => new(name, dims);

        private static HImage MakeGray(int width, int height, int gray)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", width, height);
            try
            {
                HOperatorSet.GenImageProto(proto, out HObject body, gray);
                var image = new HImage(body);
                body.Dispose();
                return image;
            }
            finally
            {
                proto.Dispose();
            }
        }

        private static ExecutionContext NewContext() =>
            new(new StubLog(),
                new FlowSession { FlowName = "深度学习分割断言" },
                new WorkspaceContext(),
                new CancellationTokenSource().Token);
    }
}
