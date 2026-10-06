using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Newtonsoft.Json;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 胶路检测插件（方案说明书 §1.3 / §5）：参考路径 + 平面可变形对齐 + bead 检测。
    ///
    /// 做的事：按配方选参考数据 → 对齐（三模式）→ apply_bead_inspection_model
    /// → 按 MinErrorLength 过滤碎段 → 统计「缺胶/太细/太粗/位置偏移」四类计数与 OK/NG
    /// → mm 换算（可选）→ 离屏渲染标注图 → 发布预览。
    ///
    /// 判定语义（§2 决策 8，与 BlobDetect 同口径）：NG 是正常结果——Success 保持 true、
    /// 结果与标注图照常输出、NgReason 留痕；只有「执行失败」（图空/无配方/对齐未命中/
    /// 路径退化/未学习/单位换算缺来源）才 Fail，话术按 §5.2 逐条给「下一步」。
    ///
    /// 模型生命周期（§5.3）：HALCON 的 bead / planar 模型都不可序列化——配方只存「输入」，
    /// 句柄在运行期按参数指纹惰性重建并缓存（planar 重建实测 100~360ms，必须缓存；
    /// 换配方/指纹失效/Dispose 时 Clear 并释放）。
    ///
/// 自定义配置界面见 BeadInspectPluginConfigView.cs（partial 第二部分）：
/// 配方列表 + 画布逐点拾取 + 自动提取 + 学习，全部隔离在配置实例上（§6）。
    /// </summary>
    [Display(
        Name = "胶路检测",
        GroupName = "缺陷检测",
        Description = "参考路径 + 平面可变形对齐 + bead 检测：输出缺胶/太细/太粗/位置偏移分段判定、逐段长度与标注图，支持多配方与 mm 输出",
        ShortName = "\uf1ce"
    )]
    public partial class BeadInspectPlugin : VisionPluginBase
    {
        // ==================================================================
        //  配置项（[StepConfig] 三选一；键 = 属性名，改名 = 静默丢配置）
        // ==================================================================

        /// <summary>配方库整体序列化落盘（照抄 Matching 的 TemplateLibraryJson 范式）</summary>
        [StepConfig]
        public string RecipeLibraryJson { get; set; } = "[]";

        /// <summary>默认配方名：RecipeName 端口未连接/为空时使用（界面「设为默认」写入）</summary>
        [StepConfig]
        public string DefaultRecipeName { get; set; } = string.Empty;

        /// <summary>对齐策略：None / PoseFromMatching / PlanarDeformable（§2 决策 4）</summary>
        [StepConfig]
        public BeadAlignMode AlignMode { get; set; } = BeadAlignMode.PlanarDeformable;

        /// <summary>胶的明暗极性（配方未单独指定时使用；HALCON 硬约束只能二选一）</summary>
        [StepConfig]
        public BeadPolarity Polarity { get; set; } = BeadPolarity.Dark;

        /// <summary>
        /// 目标胶宽（像素）。配方 TargetWidth ≤ 0 时使用。
        /// 下限 6 是硬约束：P10 实测 create_bead_inspection_model 在 target_thickness &lt; 6 时
        /// 报 HALCON #3716（英文错误，现场看不懂）——在赋值处夹进合法域，而不是等算子炸。
        /// </summary>
        [StepConfig, DefaultValue(15.0)]
        public partial double TargetWidth { get; set; }

        partial void OnTargetWidthChanging(ref double value) => value = Math.Clamp(value, 6.0, 10000.0);

        /// <summary>胶宽容差（像素）。配方未单独指定时使用。P12：须 ≥ 提取路径偏差上界 8.39px，默认 8</summary>
        [StepConfig]
        public double WidthTolerance { get; set; } = 8.0;

        /// <summary>位置容差（像素）。P9：待现场良品样本标定，默认保守值 30</summary>
        [StepConfig]
        public double PositionTolerance { get; set; } = 30.0;

        /// <summary>错误段长度下限（像素）：短于此的碎段不计数（滤小误报）</summary>
        [StepConfig]
        public double MinErrorLength { get; set; } = 5.0;

        /// <summary>平面匹配最低分数 0~1（低于它的对齐判失败）</summary>
        [StepConfig, DefaultValue(0.4)]
        public partial double MinScore { get; set; }

        partial void OnMinScoreChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        /// <summary>平面匹配金字塔层数（find 用，1~10；范例/探针实测 5）</summary>
        [StepConfig, DefaultValue(5)]
        public partial int PlanarNumLevels { get; set; }

        partial void OnPlanarNumLevelsChanging(ref int value) => value = Math.Clamp(value, 1, 10);

        /// <summary>
        /// 平面匹配起始角（**度**，仓库约定：插件面向用户用度，只在调 HALCON 处换算弧度）。
        /// 默认 -22.35° ≈ 范例 -0.39rad；换工位/相机角度范围不同时调大。
        /// </summary>
        [StepConfig, DefaultValue(-22.35)]
        public partial double FindAngleStartDeg { get; set; }

        /// <summary>平面匹配角度范围（**度**）。默认 44.69° ≈ 范例 0.78rad；与起始角合起来确定搜索窗口</summary>
        [StepConfig, DefaultValue(44.69)]
        public partial double FindAngleExtentDeg { get; set; }

        /// <summary>平面匹配行向最小缩放（1 = 不允许缩放；相机远近/工件大小有变化时设成如 0.9~1.1）</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double FindScaleRMin { get; set; }

        /// <summary>平面匹配行向最大缩放</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double FindScaleRMax { get; set; }

        /// <summary>平面匹配列向最小缩放</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double FindScaleCMin { get; set; }

        /// <summary>平面匹配列向最大缩放</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double FindScaleCMax { get; set; }

        /// <summary>运行显示窗口：把标注图发布到主界面几号视图窗口（1~9），0 = 不发布</summary>
        [StepConfig]
        public int DisplayViewIndex { get; set; } = 1;

        /// <summary>是否输出对齐后的图（AlignedImage 输出端口；关闭时输出空）</summary>
        [StepConfig]
        public bool OutputAlignedImage { get; set; } = true;

        /// <summary>长度输出单位（mm 换算只发生在输出层，见 §5.4）</summary>
        [StepConfig]
        public BeadUnit UnitOutput { get; set; } = BeadUnit.Pixel;

        /// <summary>像素当量 mm/px：UnitOutput=Mm 且未接 Transform 端口时的兜底来源</summary>
        [StepConfig]
        public double PixelSizeMm { get; set; }

        // ── 配方库（运行时状态；落盘经 RecipeLibraryJson） ──

        /// <summary>配方库（下一批配置界面 ListBox 直接绑定；断言可直接 Add 条目）</summary>
        public ObservableCollection<BeadRecipeEntry> Library { get; } = new();

        // ── 重建计数器（断言 9「模型缓存」用；也是诊断面） ──

        /// <summary>bead 模型实际重建次数（同参数连续两轮不重建 = 缓存生效）</summary>
        public int BeadModelRebuildCount { get; private set; }

        /// <summary>planar 模型实际重建次数（P5：重建 100~360ms，必须指纹缓存）</summary>
        public int PlanarModelRebuildCount { get; private set; }

        private readonly OffscreenRenderer _renderer = new();

        /// <summary>度→弧度换算（仓库约定：插件面向用户用「度」，只在调 HALCON 算子处换算弧度）</summary>
        private const double DegToRad = Math.PI / 180.0;

        // ==================================================================
        //  端口（§3 契约：端口名一经发布即契约）
        // ==================================================================

        /// <summary>待检测图像（必填）</summary>
        public InputPort<HImage> SrcImage { get; } =
            new("SrcImage", description: "待检测图像");

        /// <summary>配方名/产品型号：未连接或为空时用「默认配方」，仍无则用库首条</summary>
        public InputPort<string> RecipeName { get; } =
            new("RecipeName", description: "配方名/产品型号") { IsRequired = false };

        /// <summary>
        /// 对齐后的图（仅 PoseFromMatching 模式使用：接匹配插件的 AlignedImage 输出）。
        /// 属性名带 In 后缀是因为同名端口在输出侧还有一个（§3 契约两者都叫 AlignedImage），
        /// C# 属性不能同名——端口名以构造参数为准，连线不受影响。
        /// </summary>
        public InputPort<HImage> AlignedImageIn { get; } =
            new("AlignedImage", description: "对齐后的图（仅 PoseFromMatching 模式使用，接匹配插件的 AlignedImage）")
            { IsRequired = false };

        /// <summary>可选检测范围（reduce_domain 提速）；未连接 = 整图。坐标不因它改变（§3.1 注）</summary>
        public InputPort<HRegion> RoiRegion { get; } =
            new("RoiRegion", description: "可选检测范围（reduce_domain 提速）；未连接 = 整图") { IsRequired = false };

        /// <summary>mm 换算首选来源（接标定插件的 Transform 输出；PixelScale 只取 MmPerPixel 不用矩阵）</summary>
        public InputPort<CalibrationTransform?> Transform { get; } =
            new("Transform", description: "mm 换算首选来源（接标定插件的 Transform 输出）") { IsRequired = false };

        public OutputPort<bool> IsOk { get; } = new("IsOk", "判定结果（true = 合格）");

        public OutputPort<string> NgReason { get; } = new("NgReason", "NG 原因（如「缺胶 2 段、太细 1 段」）；OK 时为空串");

        public OutputPort<HImage> AnnotatedImage { get; } =
            new("AnnotatedImage", "标注图（对齐图 + 参考路径 + 左右轮廓 + 错误段红标 + 判定文字）");

        public OutputPort<int> ErrorCount { get; } = new("ErrorCount", "错误段总数（已按 MinErrorLength 过滤）");

        public OutputPort<HTuple> ErrorTypes { get; } = new("ErrorTypes", "各错误段类型字符串数组（no bead / too thin / too thick / incorrect position）");

        public OutputPort<HTuple> ErrorLengths { get; } = new("ErrorLengths", "各错误段长度（像素），与 ErrorTypes 逐项对齐");

        public OutputPort<HTuple> ErrorRows { get; } = new("ErrorRows", "各错误段中心 Row，与 ErrorTypes 逐项对齐");

        public OutputPort<HTuple> ErrorCols { get; } = new("ErrorCols", "各错误段中心 Col，与 ErrorTypes 逐项对齐");

        public OutputPort<double> TotalErrorLength { get; } =
            new("TotalErrorLength", "错误段总长度（按 UnitOutput 单位：像素或毫米）");

        public OutputPort<int> NoBeadCount { get; } = new("NoBeadCount", "「缺胶」段数");

        public OutputPort<int> TooThinCount { get; } = new("TooThinCount", "「太细」段数");

        public OutputPort<int> TooThickCount { get; } = new("TooThickCount", "「太粗」段数");

        public OutputPort<int> MispositionCount { get; } = new("MispositionCount", "「位置偏移」段数");

        public OutputPort<HXLD> ErrorSegments { get; } = new("ErrorSegments", "错误段轮廓集合（可直接给下游/存图）");

        public OutputPort<HXLD> BeadContours { get; } = new("BeadContours", "检出的左右胶路轮廓（2 个对象；OK 时也可用于复检）");

        public OutputPort<HImage> AlignedImage { get; } =
            new("AlignedImage", "对齐后的图（供下游复用；OutputAlignedImage 关闭时输出空）");

        // ==================================================================
        //  配方库解析 / 落盘（照抄 Matching 的 ParseLibraryAndMigrate 范式）
        // ==================================================================

        /// <summary>
        /// 统一灌值（配置界面与流程编译共用）：库 JSON 的解析必须在这里——
        /// 流程编译器对运行实例只调 ApplyConfigValues（不调 Initialize），
        /// 解析不放这里的话，运行内核面对的是空库（Matching 实测过的坑）。
        /// </summary>
        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            base.ApplyConfigValues(stepData); // 先灌 [StepConfig]（库 JSON / 默认名 / 各参数）

            // 释放旧条目的模型句柄，防止反复解析库时泄漏（HALCON 句柄是非托管资源）
            foreach (var e in Library.ToArray())
            {
                ReleaseBeadModel(e);
                ReleasePlanarModel(e);
            }
            Library.Clear();
            try
            {
                var list = JsonConvert.DeserializeObject<List<BeadRecipeEntry>>(RecipeLibraryJson ?? "[]");
                if (list != null)
                    foreach (var e in list)
                        Library.Add(e);
            }
            catch
            {
                // 库 JSON 损坏：按空库处理（用户重学），不让整个方案加载失败
            }
        }

        /// <summary>确认：把库序列化成 JSON 落盘</summary>
        public override void OnConfirm(IStepConfigData stepData)
        {
            RecipeLibraryJson = JsonConvert.SerializeObject(Library);
            base.OnConfirm(stepData);
        }

        // ==================================================================
        //  运行内核
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 轮首重置标量/元组端口（基类只自动回收 IDisposable；脏标量会骗下游一整轮）
            IsOk.Value = false;
            NgReason.Value = string.Empty;
            ErrorCount.Value = 0;
            ErrorTypes.Value = new HTuple();
            ErrorLengths.Value = new HTuple();
            ErrorRows.Value = new HTuple();
            ErrorCols.Value = new HTuple();
            TotalErrorLength.Value = 0;
            NoBeadCount.Value = 0;
            TooThinCount.Value = 0;
            TooThickCount.Value = 0;
            MispositionCount.Value = 0;

            var temp = new List<HObject>();
            try
            {
                // ── 1. 取图 ──
                var src = SrcImage.ActualValue;
                if (src == null || !src.IsInitialized())
                {
                    Fail("输入图像为空或未初始化");
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }

                // ── 2. 选配方 ──
                var entry = ResolveEntry(out string? resolveError);
                if (entry == null)
                {
                    Fail(resolveError ?? "未找到可用配方：请在配置界面新建配方并学习参考路径");
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }

                // ── 3. 参考路径预检（点数/退化，§5.2 话术在 TryBuildContour 里）──
                if (!BeadRecipeEntry.TryParsePoints(entry.RefPointsJson, out var pathRows, out var pathCols, out var parseError))
                {
                    Fail(parseError);
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }
                if (!BeadInspectHalcon.TryBuildContour(pathRows, pathCols, out var probeContour, out var pathError))
                {
                    Fail(pathError);
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }
                probeContour?.Dispose(); // 仅预检用；正式的参考路径 XLD 归条目所有（EnsureBeadModel 里建）

                // ── 4. 对齐（按 AlignMode 分支）──
                HObject aligned;
                if (AlignMode == BeadAlignMode.PoseFromMatching)
                {
                    var upstream = AlignedImageIn.ActualValue;
                    if (upstream == null || !upstream.IsInitialized())
                    {
                        Fail("对齐模式为「匹配位姿」但未连接 AlignedImage：请连接匹配插件的对齐图输出，或改用其它对齐模式");
                        context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                        return;
                    }
                    HOperatorSet.CopyImage(upstream, out HObject copy);
                    temp.Add(copy);
                    aligned = copy;
                }
                else if (AlignMode == BeadAlignMode.PlanarDeformable)
                {
                    if (!EnsurePlanarModel(entry, out string planarError, context.Logger))
                    {
                        Fail(planarError);
                        context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                        return;
                    }
                    if (!BeadInspectHalcon.AlignImage(
                            src, entry.RuntimePlanarModel!, entry.RuntimeRowT, entry.RuntimeColT,
                            MinScore, PlanarNumLevels,
                            FindAngleStartDeg * DegToRad, FindAngleExtentDeg * DegToRad,
                            FindScaleRMin, FindScaleRMax, FindScaleCMin, FindScaleCMax,
                            out HObject? alignedImg, out double score, out string alignError))
                    {
                        Fail(alignError);
                        context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                        return;
                    }
                    temp.Add(alignedImg!);
                    aligned = alignedImg!;
                    context.Logger?.Info($"{InstanceName} 配方「{entry.Name}」平面匹配 score={score:0.000}");
                }
                else
                {
                    aligned = src; // None：固定相机/治具，不搬图（引用复用，不复制）
                }

                // ── 5. 可选检测范围（reduce_domain 保持原坐标系，§3.1 注）──
                HObject detectImage = aligned;
                var roi = RoiRegion.ActualValue;
                if (roi != null && roi.IsInitialized())
                {
                    HOperatorSet.ReduceDomain(aligned, roi, out HObject reduced);
                    temp.Add(reduced);
                    detectImage = reduced;
                }

                // ── 6. 取/建 bead 模型（指纹不符才重建，§5.3）──
                // 有效参数：配方自带值优先（每个产品分开调），空/0 回落插件级
                double effTarget = entry.TargetWidth > 0 ? entry.TargetWidth : TargetWidth;
                double effTol = entry.WidthTolerance > 0 ? entry.WidthTolerance : WidthTolerance;
                double effPos = entry.PositionTolerance > 0 ? entry.PositionTolerance : PositionTolerance;
                string effPolarity = !string.IsNullOrWhiteSpace(entry.Polarity)
                    ? entry.Polarity.Trim()
                    : (Polarity == BeadPolarity.Light ? "light" : "dark");

                if (!EnsureBeadModel(entry, effTarget, effTol, effPos, effPolarity, out string modelError))
                {
                    Fail(modelError);
                    context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                    return;
                }

                // ── 7. 检测 + 过滤 + 统计 ──
                BeadInspectHalcon.ApplyBead(
                    detectImage, entry.RuntimeBeadModel!,
                    out HObject left, out HObject right, out HObject errSeg, out HTuple errType);
                temp.Add(left);
                temp.Add(right);
                temp.Add(errSeg);

                HOperatorSet.CountObj(errSeg, out HTuple segCount);
                int segN = segCount.I;
                int segTypes = errType.Length;

                var keptTypes = new HTuple();
                var keptLengths = new HTuple();
                var keptRows = new HTuple();
                var keptCols = new HTuple();
                HObject? keptSegs = null;
                double totalLength = 0;
                int noBead = 0, tooThin = 0, tooThick = 0, misposition = 0;

                for (int i = 1; i <= segN && i <= segTypes; i++)
                {
                    HOperatorSet.SelectObj(errSeg, out HObject seg, i); // 对象序号是第 3 参（探针踩过）
                    HOperatorSet.LengthXld(seg, out HTuple segLen);
                    double len = segLen.D;
                    if (len < MinErrorLength)
                    {
                        seg.Dispose(); // 碎段丢弃：不进 keptSegs，就地释放
                        continue;
                    }

                    string type = errType[i - 1].S ?? string.Empty;
                    keptTypes = keptTypes.TupleConcat(type);
                    keptLengths = keptLengths.TupleConcat(len);
                    HOperatorSet.AreaCenterPointsXld(seg, out _, out HTuple cRow, out HTuple cCol);
                    keptRows = keptRows.TupleConcat(cRow.D);
                    keptCols = keptCols.TupleConcat(cCol.D);
                    totalLength += len;

                    // 拼接保留下来的错误段（keptSegs 的所有权归本方法，最后进 temp 统一释放）
                    if (keptSegs == null)
                    {
                        keptSegs = seg;
                    }
                    else
                    {
                        HOperatorSet.ConcatObj(keptSegs, seg, out HObject combined);
                        keptSegs.Dispose();
                        seg.Dispose();
                        keptSegs = combined;
                    }

                    switch (type)
                    {
                        case "no bead": noBead++; break;
                        case "too thin": tooThin++; break;
                        case "too thick": tooThick++; break;
                        case "incorrect position": misposition++; break;
                    }
                }
                if (keptSegs != null)
                    temp.Add(keptSegs);

                int errorCount = keptTypes.Length;
                bool ok = errorCount == 0;

                // ── 8. mm 换算（§5.4：Transform 优先，失配必须失败，绝不静默按像素输出）──
                double totalOut = totalLength;
                if (UnitOutput == BeadUnit.Mm)
                {
                    HOperatorSet.GetImageSize(src, out HTuple widthT, out HTuple heightT);
                    if (!ResolveMmPerPixel(widthT.I, heightT.I, out double mmPerPixel, out string mmError))
                    {
                        Fail(mmError);
                        context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                        return;
                    }
                    totalOut = totalLength * mmPerPixel;
                }

                // ── 9. NG 原因（OK 时为空串）──
                var parts = new List<string>(4);
                if (noBead > 0) parts.Add($"缺胶 {noBead} 段");
                if (tooThin > 0) parts.Add($"太细 {tooThin} 段");
                if (tooThick > 0) parts.Add($"太粗 {tooThick} 段");
                if (misposition > 0) parts.Add($"位置偏移 {misposition} 段");
                string ngReason = ok ? string.Empty : string.Join("、", parts);

                // ── 10. 输出端口赋值（NG 也照常输出：下游要拿结果与标注图）──
                ErrorTypes.Value = keptTypes;
                ErrorLengths.Value = keptLengths;
                ErrorRows.Value = keptRows;
                ErrorCols.Value = keptCols;
                ErrorCount.Value = errorCount;
                NoBeadCount.Value = noBead;
                TooThinCount.Value = tooThin;
                TooThickCount.Value = tooThick;
                MispositionCount.Value = misposition;
                TotalErrorLength.Value = totalOut;
                IsOk.Value = ok;
                NgReason.Value = ngReason;

                if (keptSegs != null)
                    ErrorSegments.TypedValue = new HXLDCont(keptSegs);

                // 左右轮廓拼成 2 对象集合（任一为空则只给有货的那半）
                HObject? beadPair = null;
                HOperatorSet.CountObj(left, out HTuple leftN);
                HOperatorSet.CountObj(right, out HTuple rightN);
                if (leftN.I > 0 && rightN.I > 0)
                {
                    HOperatorSet.ConcatObj(left, right, out HObject both);
                    temp.Add(both);
                    beadPair = both;
                }
                else if (leftN.I > 0)
                    beadPair = left;
                else if (rightN.I > 0)
                    beadPair = right;
                if (beadPair != null)
                    BeadContours.TypedValue = new HXLDCont(beadPair);

                if (OutputAlignedImage)
                    AlignedImage.TypedValue = new HImage(aligned);

                // ── 11. 标注图（离屏渲染；渲染失败不影响端口数据）──
                string[] lines = ok
                    ? new[] { $"胶路 OK（配方「{entry.Name}」，错误段 0）" }
                    : new[] { $"NG：{ngReason}", $"配方「{entry.Name}」，错误段 {errorCount} 段" };
                var annotated = _renderer.Render(aligned, entry.RuntimeContour, beadPair, keptSegs, lines, ok);
                if (annotated != null)
                    AnnotatedImage.TypedValue = annotated;

                // ── 12. 发布预览（0 = 不发布；总线无订阅者时是 no-op）──
                if (DisplayViewIndex > 0 && AnnotatedImage.Value is HImage publishable)
                    this.PublishPreview(publishable, DisplayViewIndex);

                context.Logger?.Info(
                    $"{InstanceName} 配方「{entry.Name}」错误段={errorCount}（缺胶{noBead}/太细{tooThin}/太粗{tooThick}/偏移{misposition}）"
                    + $" 总长={totalOut:0.#}{(UnitOutput == BeadUnit.Mm ? "mm" : "px")}"
                    + $" 判定={(ok ? "OK" : "NG")}");
            }
            catch (Exception ex)
            {
                Fail($"胶路检测失败：{ex.Message}");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
            }
            finally
            {
                foreach (var o in temp)
                {
                    try { o?.Dispose(); }
                    catch { /* 中间对象释放失败不阻断 */ }
                }
            }
        }

        /// <summary>
        /// 选配方：RecipeName 端口（按名字）→ 默认配方 → 库里第一条。
        /// 名字给了但不在库里 → null 且 error 给出库里现有的名字（产线口径：不静默、不猜）。
        /// </summary>
        private BeadRecipeEntry? ResolveEntry(out string? error)
        {
            error = null;
            var wanted = RecipeName.GetTypedValue();
            wanted = string.IsNullOrWhiteSpace(wanted) ? null : wanted.Trim();
            if (wanted != null)
            {
                var hit = Library.FirstOrDefault(e => e.Name == wanted);
                if (hit == null)
                {
                    error = Library.Count > 0
                        ? $"未知配方：{wanted} 没有对应胶路配方（库里有：{string.Join("，", Library.Select(e => e.Name))}）"
                        : "未找到可用配方：请在配置界面新建配方并学习参考路径";
                    return null;
                }
                return hit;
            }

            if (!string.IsNullOrWhiteSpace(DefaultRecipeName))
            {
                var hit = Library.FirstOrDefault(e => e.Name == DefaultRecipeName);
                if (hit != null)
                    return hit;
            }

            if (Library.Count == 0)
                error = "未找到可用配方：请在配置界面新建配方并学习参考路径";
            return Library.FirstOrDefault();
        }

        // ==================================================================
        //  mm 换算（§5.4：口径与 PoseTransform 严格对齐，失配必须明确失败）
        // ==================================================================

        /// <summary>
        /// 解析 mm/px 当量：Transform 端口优先（校验图像尺寸失配），回退 PixelSizeMm，
        /// 两者皆无 → Fail（不静默按像素输出）。PixelScale 标定只取 MmPerPixel，不用其矩阵
        /// （该模式矩阵除对角线外为 0，无机械坐标含义）。
        /// </summary>
        private bool ResolveMmPerPixel(int imageWidth, int imageHeight, out double mmPerPixel, out string error)
        {
            mmPerPixel = 0;
            error = string.Empty;

            var t = Transform.ActualValue;
            if (t != null)
            {
                if (t.MmPerPixel <= 0)
                {
                    error = "标定变换的像素当量无效（MmPerPixel ≤ 0）：请重新运行标定";
                    return false;
                }
                if (t.SourceImageWidth > 0 && t.SourceImageHeight > 0
                    && (t.SourceImageWidth != imageWidth || t.SourceImageHeight != imageHeight))
                {
                    error = $"标定失配：当前图 {imageWidth}×{imageHeight} ≠ 标定图 {t.SourceImageWidth}×{t.SourceImageHeight}，"
                            + "换分辨率/换相机后需重新标定";
                    return false;
                }
                mmPerPixel = t.MmPerPixel;
                return true;
            }

            if (PixelSizeMm > 0)
            {
                mmPerPixel = PixelSizeMm;
                return true;
            }

            error = "输出单位设为毫米，但既未连接标定变换也未填写像素当量：请补其一";
            return false;
        }

        // ==================================================================
        //  模型生命周期（§5.3：存输入、按指纹惰性重建、Clear 释放）
        // ==================================================================

        /// <summary>
        /// 取/建 bead 模型：指纹 = 点列 + 有效参数（胶宽/容差/位置容差/极性）。
        /// 指纹相符直接复用句柄（断言 9）；不符才 Clear 旧句柄并重建。
        /// 重建出的参考路径 XLD（RuntimeContour）归条目所有，随句柄一起释放。
        /// </summary>
        private bool EnsureBeadModel(
            BeadRecipeEntry entry,
            double targetWidth,
            double widthTolerance,
            double positionTolerance,
            string polarity,
            out string error)
        {
            error = string.Empty;
            // 指纹格式唯一出处 = BeadRecipeEntry.BuildBeadSignature（P2-2：配置界面的「已学习」
            // 标记同走这里，两处手写会漂移）
            string signature = entry.BuildBeadSignature(targetWidth, widthTolerance, positionTolerance, polarity);
            if (entry.RuntimeBeadModel != null && entry.RuntimeBeadSource == signature)
                return true; // 缓存命中：同参数连续两轮不重建

            ReleaseBeadModel(entry);
            if (!BeadRecipeEntry.TryParsePoints(entry.RefPointsJson, out var rows, out var cols, out var parseError))
            {
                error = parseError;
                return false;
            }
            if (!BeadInspectHalcon.TryBuildContour(rows, cols, out HObject? contour, out var contourError))
            {
                error = contourError;
                return false;
            }
            try
            {
                entry.RuntimeBeadModel = BeadInspectHalcon.CreateBeadModel(
                    contour, targetWidth, widthTolerance, positionTolerance, polarity);
            }
            catch (Exception ex)
            {
                try { contour.Dispose(); } catch { }
                error = $"创建胶路检测模型失败：{ex.Message}";
                return false;
            }
            entry.RuntimeContour = contour;
            entry.RuntimeBeadSource = signature;
            BeadModelRebuildCount++;
            return true;
        }

        /// <summary>
        /// 取/建 planar 模型：指纹 = 参考图路径 + 文件长度 + 写入时间（UTC Ticks）+ 矫正四点
        /// （P2-3：时间戳兜底「同路径覆盖文件」——覆盖后长度可能碰巧不变，mtime 不会）。
        /// 重建较贵（P5 实测 100~360ms）——只在换配方/换参考图/覆盖参考图/改矫正四点时发生；
        /// 锚点 (RowT, ColT) 与平面区随模型一起缓存。
        /// </summary>
        private bool EnsurePlanarModel(BeadRecipeEntry entry, out string error, ILogService? log = null)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(entry.RefImagePath))
            {
                error = "配方未学习：请先在配置界面载入参考图并点击「学习」";
                return false;
            }
            if (!File.Exists(entry.RefImagePath))
            {
                error = $"参考图不存在：{entry.RefImagePath}（换路径后需重新学习）";
                return false;
            }

            long refLength = 0;
            long refWriteTicks = 0;
            try
            {
                var refInfo = new FileInfo(entry.RefImagePath);
                refLength = refInfo.Length;
                refWriteTicks = refInfo.LastWriteTimeUtc.Ticks;
            }
            catch { }
            string signature = $"planar|{entry.RefImagePath}|{refLength}|{refWriteTicks}|{entry.RectifyQuadJson}";
            if (entry.RuntimePlanarModel != null && entry.RuntimePlanarSource == signature)
                return true; // 缓存命中

            ReleasePlanarModel(entry, log);
            if (!BeadRecipeEntry.TryParseRectifyQuad(
                    entry.RectifyQuadJson, out var quadRows, out var quadCols, out var dstRows, out var dstCols, out var quadError))
            {
                error = quadError;
                return false;
            }

            try
            {
                HOperatorSet.ReadImage(out HObject refImage, entry.RefImagePath);
                try
                {
                    bool hasQuad = quadRows.Length == 4;
                    if (!BeadInspectHalcon.PrepareAlignment(
                            refImage,
                            hasQuad ? quadRows : null,
                            hasQuad ? quadCols : null,
                            dstRows, dstCols,
                            out var planarModel, out var rowT, out var colT,
                            out var rectifiedRef, out var plane, out error))
                    {
                        return false;
                    }
                    entry.RuntimePlanarModel = planarModel;
                    entry.RuntimePlanarSource = signature;
                    entry.RuntimeRowT = rowT;
                    entry.RuntimeColT = colT;
                    entry.RuntimeRectifiedRef = rectifiedRef;
                    entry.RuntimePlane = plane;
                    PlanarModelRebuildCount++;
                    return true;
                }
                finally
                {
                    try { refImage.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                error = $"载入参考图失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>释放 bead 模型句柄 + 参考路径 XLD（换配方/Dispose 时必须调，否则句柄泄漏）</summary>
        private static void ReleaseBeadModel(BeadRecipeEntry entry)
        {
            if (entry.RuntimeBeadModel != null)
            {
                try { HOperatorSet.ClearBeadInspectionModel(entry.RuntimeBeadModel); }
                catch { /* 句柄可能已被 Clear，释放失败不阻断 */ }
                entry.RuntimeBeadModel = null;
            }
            entry.RuntimeBeadSource = null;
            try { entry.RuntimeContour?.Dispose(); } catch { }
            entry.RuntimeContour = null;
        }

        /// <summary>
        /// 释放 planar 模型句柄 + 矫正参考图 + 平面区。
        /// 释放失败不再静默（P2-5）：句柄泄漏必须留痕——运行链路传 <paramref name="log"/>
        /// （RunAlgorithm 的 context.Logger）；Dispose/配置态没有日志通道，落
        /// System.Diagnostics.Debug.WriteLine（仓库既有插件 Dispose 期日志的同款通道）。
        /// </summary>
        private void ReleasePlanarModel(BeadRecipeEntry entry, ILogService? log = null)
        {
            if (entry.RuntimePlanarModel != null)
            {
                // planar uncalib deformable 模型的释放走 clear_deformable_model 家族
                //（本机 halcondotnet 23.05 无 ClearPlanarUncalibDeformableModel 入口，反射已核实）
                try { HOperatorSet.ClearDeformableModel(entry.RuntimePlanarModel); }
                catch (Exception ex)
                {
                    string releaseError =
                        $"{InstanceName} 释放配方「{entry.Name}」的 planar 模型句柄失败：{ex.Message}";
                    if (log != null)
                        log.Error(releaseError);
                    else
                        System.Diagnostics.Debug.WriteLine(releaseError);
                }
                entry.RuntimePlanarModel = null;
            }
            entry.RuntimePlanarSource = null;
            try { entry.RuntimeRectifiedRef?.Dispose(); } catch { }
            entry.RuntimeRectifiedRef = null;
            try { entry.RuntimePlane?.Dispose(); } catch { }
            entry.RuntimePlane = null;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        public override void Dispose()
        {
            foreach (var e in Library)
            {
                ReleaseBeadModel(e);
                ReleasePlanarModel(e);
            }
            Library.Clear();
            DisposeConfigState(); // 配置态的画布底图/预览图/行 VM（partial 第二部分）
            _renderer.Dispose();
            base.Dispose();
        }

        // ==================================================================
        //  离屏标注渲染（BlobDetect 的 AnnotationRenderer 同款：缓存 buffer 窗口防 #9302）
        // ==================================================================

        /// <summary>
        /// 把「对齐图 + 参考路径（橙） + 左右轮廓（绿） + 错误段（红，线宽 2） + 判定文字」
        /// 合成一张 HImage。HALCON 写文字必须依附窗口字库，故走 open_window('buffer') 离屏渲染；
        /// 窗口按尺寸缓存复用（HALCON 反复 open/close 窗口会 #9302 死锁，仓库既有结论）。
        /// </summary>
        private sealed class OffscreenRenderer : IDisposable
        {
            private readonly object _gate = new();
            private HTuple? _window;
            private int _width;
            private int _height;
            private bool _disposed;

            public HImage? Render(
                HObject baseImage,
                HObject? referencePath,
                HObject? beadContours,
                HObject? errorSegments,
                string[] lines,
                bool ok)
            {
                lock (_gate)
                {
                    if (_disposed || baseImage == null || !baseImage.IsInitialized())
                        return null;
                    try
                    {
                        HOperatorSet.GetImageSize(baseImage, out HTuple widthT, out HTuple heightT);
                        int w = widthT.I, h = heightT.I;
                        if (w <= 0 || h <= 0)
                            return null;

                        EnsureWindow(w, h);
                        var win = _window!;

                        // 显式钉住窗口 part（不设的话 part 会被撑到全幅，底图缩在角落——Matching 实测）
                        HOperatorSet.SetPart(win, 0, 0, h - 1, w - 1);
                        HOperatorSet.DispObj(baseImage, win);

                        if (HasContent(referencePath))
                        {
                            HOperatorSet.SetColor(win, "orange");
                            HOperatorSet.SetLineWidth(win, 1);
                            HOperatorSet.DispObj(referencePath!, win);
                        }
                        if (HasContent(beadContours))
                        {
                            HOperatorSet.SetColor(win, "green");
                            HOperatorSet.SetLineWidth(win, 1);
                            HOperatorSet.DispObj(beadContours!, win);
                        }
                        if (HasContent(errorSegments))
                        {
                            HOperatorSet.SetColor(win, "red");
                            HOperatorSet.SetLineWidth(win, 2);
                            HOperatorSet.DispObj(errorSegments!, win);
                            HOperatorSet.SetLineWidth(win, 1); // 还原，别把线宽带去下一张
                        }

                        if (lines is { Length: > 0 })
                        {
                            TrySetFont(win, 16);
                            HOperatorSet.DispText(
                                win, new HTuple(lines), "window", 12, 12, ok ? "green" : "red",
                                new HTuple("box_color"), new HTuple("white"));
                        }

                        // dump 回读：new HImage(shot) 是独立句柄，shot 立刻释放（引用计数语义，
                        // 卡尺插件实测 500 次不释放约漏 160MB）
                        HOperatorSet.DumpWindowImage(out HObject shot, win);
                        var result = new HImage(shot);
                        shot.Dispose();
                        return result;
                    }
                    catch
                    {
                        return null; // 标注渲染失败不影响端口数据（错误段/判定照常输出）
                    }
                }
            }

            private void EnsureWindow(int w, int h)
            {
                if (_window != null && w == _width && h == _height)
                    return;
                CloseWindow();
                HOperatorSet.OpenWindow(0, 0, w, h, "black", "buffer", "local", out HTuple win);
                _window = win;
                _width = w;
                _height = h;
            }

            private static void TrySetFont(HTuple win, int size)
            {
                try { HOperatorSet.SetFont(win, $"-Consolas-{size}-*-0-*-*-1-"); }
                catch { /* 字号是锦上添花，失败用默认 */ }
            }

            private static bool HasContent(HObject? o)
            {
                if (o == null || !o.IsInitialized())
                    return false;
                HOperatorSet.CountObj(o, out HTuple n);
                return n.Length > 0 && n[0].I > 0;
            }

            private void CloseWindow()
            {
                if (_window == null)
                    return;
                try { HOperatorSet.CloseWindow(_window); } catch { /* 关窗失败不阻断 */ }
                _window = null;
                _width = 0;
                _height = 0;
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    _disposed = true;
                    CloseWindow();
                }
            }
        }
    }
}
