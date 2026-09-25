using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Core.Halcon;
using Core.Halcon.Color;
using Core.Halcon.Models;
using Core.Interfaces;
using Core.Events;
using HalconDotNet;

namespace Plugin.ColorCheck
{
    /// <summary>
    /// 颜色序列检查插件。
    ///
    /// 设计意图
    /// ---------
    /// 把「线序颜色检查」从脚本抽象成插件，抽象层级选的是**通用「颜色序列检查」**：
    ///   沿一条采样带取一维颜色序列 → 与配方比对
    /// 这样线序、色带、排线、色块标签都能用同一个节点 —— 插件不认识「线」这个词。
    ///
    /// 已经切下的两刀
    /// ---------
    ///   第一刀（采样区）：在图上框一个矩形，框住线束。画框能力来自 Core.Halcon 的 ImageEdit，
    ///     本插件只做「控件里的框 ↔ 配置里的形状参数」这条双向通道。
    ///     之所以先切它，是因为它最不可逆：要换成多段线，上面几层全得返工。
    ///   第二刀（算法层）：从采样区算出颜色序列。几何约定与自适应策略见
    ///     <see cref="ColorSequenceAnalyzer"/> 的注释；颜色词归到哪一类见 <see cref="ColorClassifier"/>。
    ///
    /// 还没做
    /// ---------
    ///   ④ 颜色层（色卡可调 / 参考色示教）、⑤ 配方表、⑥ 结果投射。现在颜色判据是内置色卡占位，
    ///   也没有"合格与否"的输出 —— 判定请继续用现有的脚本方案。
    ///
    /// 采样几何（与通行做法一致的约定）
    /// ---------
    ///   矩形**长轴**判为线束走向，采样方向与之垂直（即沿短轴扫）；
    ///   短轴的范围就是采样范围，长轴的范围只用来判"哪边是线束走向"。
    ///   5 条采样线在长轴方向只偏移几个像素（与脚本版左右各取 3 列同源），按同一短轴位置
    ///   逐通道取中位合成**一条中位剖面** —— 抗单点噪声，但不摊开（摊开会把扇形散开的线束洗掉）。
    ///   序列沿短轴排列，索引 0 是坐标较小的那一端；要反过来看就勾「序列反向」。
    ///
    /// 数据流（与 Plugin.CreateRoi 同一套约定）
    /// ---------
    ///   控件右键新建/删除 → 改动 CanvasRois → CollectionChanged 回写 RoiParams
    ///   拖拽句柄        → 控件回写 info.HTuples（INPC）→ 回写 RoiParams
    ///   打开配置        → Initialize 按 RoiParams 播种 CanvasRois → 控件上屏
    /// </summary>
    [Display(
        Name = "颜色序列检查",
        GroupName = "图像处理",
        Description = "在采样区里取一维颜色序列（线序 / 色带 / 排线通用），后续与配方比对",
        ShortName = "\uf53f"
    )]
    public class ColorCheckPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ==================================================================
        //  配置项
        // ==================================================================

        /// <summary>采样区形状参数，按 Halcon Rectangle 约定：[中心行, 中心列, 角度(弧度), 半长, 半宽]</summary>
        [StepConfig]
        public double[] RoiParams { get; set; } = System.Array.Empty<double>();

        /// <summary>
        /// 配置用的示意图路径。
        /// 只在"打开配置界面"时读它 —— 那会儿还没有上游图像，不给人看一眼图就没法框 ROI。
        /// 运行期一律用上游连进来的图像，不读这个字段。
        /// </summary>
        [StepConfig]
        public string PreviewImagePath { get; set; } = string.Empty;

        /// <summary>序列反向：勾上则把采样剖面的首尾对调（线束摆放方向与上次相反时用）</summary>
        [StepConfig]
        public bool ReverseSequence { get; set; }

        /// <summary>手动指定线数；0 或负数 = 由算法自动反推</summary>
        [StepConfig]
        public int WireCount { get; set; }

        /// <summary>等间距规整化：开 = 按估出的线距铺满整束（能补出看不见的白线）；关 = 只报看得见的线</summary>
        [StepConfig]
        public bool Regularize { get; set; } = true;

        /// <summary>
        /// 暗线判据幅度（占「众数亮度到最暗像素」这段跨度的百分比）。
        /// 判定线 = 众数亮度 减去 这个百分比 × 跨度，也就是「明显比线间缝还暗」才算可靠暗线。
        /// 实测两张样本图的安全窗口是 100~160，默认 35% 给出 154 与 114。
        /// </summary>
        [StepConfig]
        public double DarkSpanRatio { get; set; } = 35;

        /// <summary>白线百分位：亮度高于该百分位的算可靠线像素</summary>
        [StepConfig]
        public double BrightPercentile { get; set; } = 92;

        /// <summary>最小段长：连着这么多行都可靠才算一根线</summary>
        [StepConfig]
        public int MinRun { get; set; } = 4;

        /// <summary>最大段长比例：长过剖面长度这个比例的可靠段按背景丢弃</summary>
        [StepConfig]
        public double MaxRunRatio { get; set; } = 0.33;

        /// <summary>规整补色时的窗口半宽（线距的比例）</summary>
        [StepConfig]
        public double RuleWindowRatio { get; set; } = 0.30;

        /// <summary>端点外推：允许在检出范围两端各多探一根</summary>
        [StepConfig]
        public bool ExtendEnds { get; set; } = true;

        // ==================================================================
        //  颜色判据阈值（13 项）
        //
        //  默认值来自内核 Core.Halcon.Color.ColorThresholds，**本算子带一份可覆盖的副本**：
        //  现场在「高级参数 → 颜色判据阈值」里调，随方案落盘。
        //  为什么每个算子各存一份而不是全局一份：不同工位/产品的打光与材质不一样，
        //  "玫红从哪里算起"本来就可能不同；全局一份会变成束缚。
        //  含义逐条见内核那 13 个属性的注释（界面上也有 ToolTip）。
        // ==================================================================

        /// <summary>无彩/有彩分界（饱和度）</summary>
        [StepConfig]
        public double ColorlessSaturation { get; set; } = 0.45;

        /// <summary>无彩且明度低于它 → 黑</summary>
        [StepConfig]
        public double BlackValue { get; set; } = 80;

        /// <summary>白要求饱和度低于它</summary>
        [StepConfig]
        public double WhiteSaturation { get; set; } = 0.15;

        /// <summary>无彩且明度高于它 → 白</summary>
        [StepConfig]
        public double WhiteValue { get; set; } = 195;

        /// <summary>无彩但偏暖（红比绿蓝都高出这么多）→ 棕</summary>
        [StepConfig]
        public int BrownWarmth { get; set; } = 12;

        /// <summary>棕的明度上限</summary>
        [StepConfig]
        public double BrownValue { get; set; } = 175;

        /// <summary>玫红起点（色相，度）</summary>
        [StepConfig]
        public double RoseHueFrom { get; set; } = 330;

        /// <summary>红起点（色相，度）</summary>
        [StepConfig]
        public double RedHueFrom { get; set; } = 350;

        /// <summary>红止点（色相，度）</summary>
        [StepConfig]
        public double RedHueTo { get; set; } = 20;

        /// <summary>橙止点（色相，度）</summary>
        [StepConfig]
        public double OrangeHueTo { get; set; } = 45;

        /// <summary>黄止点（色相，度）</summary>
        [StepConfig]
        public double YellowHueTo { get; set; } = 70;

        /// <summary>绿止点（色相，度）</summary>
        [StepConfig]
        public double GreenHueTo { get; set; } = 160;

        /// <summary>蓝止点（色相，度；比它高就是紫）</summary>
        [StepConfig]
        public double BlueHueTo { get; set; } = 230;

        /// <summary>
        /// 配方表：一行一条「产品名 = 颜色1,颜色2,...」，井号开头是注释。
        /// 运行时按实测根数匹配（期望序列的长度就是这条配方要求几根）。
        /// </summary>
        [StepConfig]
        public string RecipeText { get; set; } = string.Empty;

        /// <summary>结果投射到哪个视图窗口（1~9；0 = 不投射）</summary>
        [StepConfig]
        public int DisplayViewIndex { get; set; } = 1;

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待检查的图像（运行期由上游连入）</summary>
        public InputPort<HImage> Image { get; } = new("Image", description: "待检查的图像") { IsRequired = false };

        /// <summary>
        /// 期望序列（可选）：填了就以此为准，不再查配方表 —— 留给"配方从数据库/工单取"的场景。
        /// 是 string 而不是枚举：枚举端口经 InputValues 往返会抛 InvalidCastException。
        /// </summary>
        public InputPort<string> ExpectedSequence { get; } = new("ExpectedSequence", string.Empty, "期望序列（留空则按根数查配方表）")
        {
            IsRequired = false
        };

        /// <summary>判定结论：合格 / 不合格</summary>
        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "判定结论：合格 true / 不合格 false");

        /// <summary>第一位不符的位置（从 1 开始）；0 = 没有不符的位或根数就不对</summary>
        public OutputPort<int> BadIndex { get; } = new OutputPort<int>("BadIndex", "第几位不符（1 起）；0 = 全对或根数不符");

        /// <summary>结论全文（写给人看的那一句）</summary>
        public OutputPort<string> Verdict { get; } = new OutputPort<string>("Verdict", "结论全文");

        /// <summary>本次比对用的期望序列（回显，便于排查"到底比的哪条配方"）</summary>
        public OutputPort<string> Expected { get; } = new OutputPort<string>("Expected", "本次比对用的期望序列");

        /// <summary>根数</summary>
        public OutputPort<int> Count { get; } = new OutputPort<int>("Count", "根数");

        /// <summary>颜色序列（逗号分隔）</summary>
        public OutputPort<string> Sequence { get; } = new OutputPort<string>("Sequence", "颜色序列（逗号分隔）");

        /// <summary>逐根明细：序号 + 颜色 + 来源标记（检 = 来自检出段，补 = 靠等间距规整补出）</summary>
        public OutputPort<string> Detail { get; } = new OutputPort<string>("Detail", "逐根明细：序号+颜色+来源（检/补）");

        /// <summary>每根线中心的图像行号（逗号分隔），结果投射时用来贴标签</summary>
        public OutputPort<string> Rows { get; } = new OutputPort<string>("Rows", "每根线中心的图像行号（逗号分隔）");

        // ==================================================================
        //  配置视图用的状态
        // ==================================================================

        /// <summary>画布集合：直接绑到 ImageEdit.DrawObjectList，控件负责新建/删除/拖拽</summary>
        public ObservableCollection<DrawingObjectInfo> CanvasRois { get; } = new();

        private DrawingObjectInfo? _canvasActiveRoi;

        /// <summary>画布上正在编辑的 ROI（与控件 ActiveRoi 双向绑定）</summary>
        public DrawingObjectInfo? CanvasActiveRoi
        {
            get => _canvasActiveRoi;
            set => SetProperty(ref _canvasActiveRoi, value);
        }

        private HImage? _displayImage;

        /// <summary>配置界面里显示的图像（只做显示与试算，运行期不依赖它）</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set => SetProperty(ref _displayImage, value);
        }

        private string _hint = "点「载入示意图」，然后在右侧图上右键 → 新建矩形，框住线束；再点「试算一下」看结果";

        /// <summary>界面提示（载入结果、错误原因都走它）</summary>
        public string Hint
        {
            get => _hint;
            set => SetProperty(ref _hint, value);
        }

        private string _resultSummary = "尚未试算";

        /// <summary>试算结果摘要（几根、颜色序列是什么、每根从哪来）</summary>
        public string ResultSummary
        {
            get => _resultSummary;
            set => SetProperty(ref _resultSummary, value);
        }

        /// <summary>采样区摘要（给界面显示当前框在哪）</summary>
        public string RoiSummary
        {
            get
            {
                if (RoiParams == null || RoiParams.Length != 5)
                    return "尚未框选采样区";

                double degrees = RoiParams[2] * 180.0 / System.Math.PI;
                return $"中心 ({RoiParams[0]:0}, {RoiParams[1]:0})  角度 {degrees:0.#} 度  "
                     + $"半长 {RoiParams[3]:0}  半宽 {RoiParams[4]:0}";
            }
        }

        /// <summary>
        /// 播种画布期间为真：此时 CanvasRois 的变更来自"回填"，不能再回写 RoiParams，
        /// 否则会把形状参数的写法（double[] → HTuple[]）绕回自身、在打开配置时反复触发。
        /// </summary>
        private bool _seedingCanvas;

        public ColorCheckPlugin()
        {
            CanvasRois.CollectionChanged += OnCanvasRoisChanged;

            // 试运行时上游图是**通过给端口赋值**桥接进配置实例的（PluginTestRunner.BridgeInputs），
            // 所以盯着端口的变化就能把上游图搬到画布上 ——
            // 否则配置界面里只有"填示意图路径"这一条路，用户会觉得"图像绑不上上游"
            Image.PropertyChanged += OnImagePortChanged;
        }

        private void OnImagePortChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IInputPort.Value)) return;
            ShowUpstreamImage();
        }

        /// <summary>
        /// 把上游图显示到画布上。有则返回 true。
        /// 显示的是**自己拷的一份**：上游那张图的句柄归框架管，我们只负责自己的副本，
        /// 免得以后谁先 Dispose 把对方句柄弄坏。
        /// </summary>
        public bool ShowUpstreamImage()
        {
            var upstream = Image.GetTypedValue();
            if (upstream == null || !upstream.IsInitialized()) return false;

            try
            {
                DisplayImage?.Dispose();
                DisplayImage = new HImage(upstream);
                Hint = "已显示上游图像（试运行带进来的）：可以直接在图上右键框采样区";
                return true;
            }
            catch (Exception ex)
            {
                Hint = "上游图像显示失败：" + ex.Message;
                return false;
            }
        }

        public override void Initialize() { }

        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);

            _seedingCanvas = true;
            try
            {
                foreach (var info in CanvasRois.ToList())
                    info.PropertyChanged -= OnRoiTuplesChanged;

                CanvasRois.Clear();
                if (RoiParams != null && RoiParams.Length == 5)
                {
                    CanvasRois.Add(new DrawingObjectInfo(
                        DrawShapeType.Rectangle,
                        RoiParams.Select(p => new HTuple(p)).ToArray(),
                        "采样区"));
                }
            }
            finally
            {
                _seedingCanvas = false;
            }

            OnPropertyChanged(nameof(RoiSummary));
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new ColorCheckView { DataContext = this };
        }

        /// <summary>视图就绪回调（视图 code-behind 只留这一个信号）：先把图弄上屏</summary>
        public void OnViewLoaded()
        {
            // 优先上游图（试运行桥接进来的）；没有才退回示意图路径
            if (!ShowUpstreamImage()) LoadPreviewImage();
        }

        /// <summary>读示意图。路径空/不存在/读失败都给中文提示，不抛异常</summary>
        public void LoadPreviewImage()
        {
            if (string.IsNullOrWhiteSpace(PreviewImagePath))
            {
                Hint = "还没有示意图路径：填一个图像路径后再点「载入」";
                return;
            }

            if (!File.Exists(PreviewImagePath))
            {
                Hint = $"示意图不存在：{PreviewImagePath}";
                return;
            }

            try
            {
                HOperatorSet.ReadImage(out HObject raw, PreviewImagePath);
                try
                {
                    DisplayImage?.Dispose();
                    DisplayImage = new HImage(raw);
                }
                finally
                {
                    raw?.Dispose();
                }

                Hint = $"已载入示意图 {Path.GetFileName(PreviewImagePath)}";
            }
            catch (Exception ex)
            {
                Hint = "示意图载入失败：" + ex.Message;
            }
        }

        /// <summary>画布变更（控件新建/删除/清空）→ 回写形状参数</summary>
        private void OnCanvasRoisChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_seedingCanvas) return;

            if (e.NewItems != null)
                foreach (var info in e.NewItems.OfType<DrawingObjectInfo>())
                    info.PropertyChanged += OnRoiTuplesChanged;

            if (e.OldItems != null)
                foreach (var info in e.OldItems.OfType<DrawingObjectInfo>())
                    info.PropertyChanged -= OnRoiTuplesChanged;

            WriteBackRoi();
        }

        /// <summary>拖拽句柄后控件回写 HTuples（INPC）→ 同步形状参数</summary>
        private void OnRoiTuplesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples)) return;
            if (sender is not DrawingObjectInfo info) return;

            // 只认最后一个：本插件只需一个采样区，用户重画时应以最新的为准
            if (!ReferenceEquals(info, CanvasRois.LastOrDefault())) return;

            RoiParams = ToParams(info);
            OnPropertyChanged(nameof(RoiSummary));
        }

        /// <summary>
        /// 把画布状态回写成形状参数。取集合里最后一个 —— 用户画了多个只认最新的那个，
        /// 界面上另有「清空采样区」可以收拾。
        /// </summary>
        private void WriteBackRoi()
        {
            RoiParams = ToParams(CanvasRois.LastOrDefault());
            OnPropertyChanged(nameof(RoiSummary));
        }

        private static double[] ToParams(DrawingObjectInfo? info)
        {
            if (info?.HTuples == null || info.HTuples.Length == 0)
                return System.Array.Empty<double>();

            return info.HTuples.Select(t => t.D).ToArray();
        }

        /// <summary>清空采样区（界面按钮）</summary>
        public void ClearRoi() => CanvasRois.Clear();

        // ==================================================================
        //  配置期的试算
        // ==================================================================

        /// <summary>
        /// 用配置界面里的示意图跑一遍算法，把结论写进 <see cref="ResultSummary"/>。
        ///
        /// 为什么要在界面上留这个口子：自适应算法的表现取决于"这幅图的直方图长什么样"，
        /// 光看参数值想象不出来。能在框完 ROI 的当下看到"几根、什么颜色、哪几根是补的"，
        /// 才有依据去调「高级参数」。
        /// </summary>
        public void TryPreviewAnalyze()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryAnalyze(DisplayImage, out var result, out var judgement, out string error))
            {
                ResultSummary = "试算失败：" + error;
                return;
            }

            string seq = string.Join(",", result!.Names);
            string detail = string.Join(",", Enumerable.Range(0, result.Count)
                .Select(i => $"{i + 1}{result.Names[i]}{(result.FromSegment[i] ? "检" : "补")}"));

            ResultSummary = $"找到 {result.Count} 根：{seq}\r\n线距 {result.Pitch:0.0} 像素"
                          + $"（分段估 {result.PitchFromSegments:0.0} / 自相关 {result.PitchFromAutocorrelation:0.0}）"
                          + $"\r\n阈值：暗 < {result.DarkThreshold:0}　彩 > {result.SaturationThreshold:0}　亮 > {result.BrightThreshold:0}"
                          + $"\r\n来源：{detail}"
                          + $"\r\n期望：{judgement!.ExpectedText}（配方「{judgement.RecipeName}」）"
                          + $"\r\n判定：{(judgement.Passed ? "合格" : "不合格")}　{judgement.Verdict}";

            // 记住这次实测序列，供「把试算结果存成配方」一键生成配方
            _lastMeasured = result.Names;
        }

        /// <summary>上一次试算出来的颜色序列（「存成配方」按钮用）</summary>
        private string[] _lastMeasured = System.Array.Empty<string>();

        /// <summary>
        /// 把上一次试算的结果追加成一条配方。
        ///
        /// 为什么要有这个按钮：手打颜色词必须**正好**落在分类器的词表里
        /// （黑/白/灰/紫/蓝/绿/黄/红/玫红/橙），写成"深红"这种它永远匹配不上。
        /// 拿一张已知良品的图点一下试算、再点一下这个按钮，就不会犯这个错。
        /// </summary>
        public void SavePreviewAsRecipe()
        {
            if (_lastMeasured.Length == 0)
            {
                Hint = "还没有试算结果：先框好采样区并点「试算一下」，再把结果存成配方";
                return;
            }

            string line = $"{_lastMeasured.Length} 芯 = {string.Join(",", _lastMeasured)}";
            if ((RecipeText ?? string.Empty).Contains(line, StringComparison.Ordinal))
            {
                Hint = $"配方表里已经有这一条了：{line}";
                return;
            }

            string text = RecipeText ?? string.Empty;
            if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += "\r\n";
            RecipeText = text + line;
            Hint = $"已存成配方：{line}";
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            var image = Image.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("上游没有图像：请先在流程里把本节点的 Image 连到上游"
                   + "（画布上从上游拖一条线过来，或右键 → 变量绑定选上游的 Image 输出）。"
                   + "只想先框采样区的话，点「执行」一次，上游图像会自动显示在配置界面右侧");
                return;
            }

            if (RoiParams == null || RoiParams.Length != 5)
            {
                Fail("尚未框选采样区：请在节点配置里于图像上右键 → 新建矩形，框住线束");
                return;
            }

            int channels = (int)image.CountChannels().D;
            if (channels < 3)
            {
                Fail($"颜色序列检查要彩色图，当前图像只有 {channels} 个通道");
                return;
            }

            if (!TryAnalyze(image, out var result, out var judgement, out string error))
            {
                Fail(error);
                return;
            }

            foreach (var warning in result!.Warnings)
                context.Logger.Warn(warning);

            string seq = string.Join(",", result.Names);
            string detail = string.Join(",", Enumerable.Range(0, result.Count)
                .Select(i => $"{i + 1}{result.Names[i]}{(result.FromSegment[i] ? "检" : "补")}"));

            Count.Value = result.Count;
            Sequence.Value = seq;
            Detail.Value = detail;
            Rows.Value = string.Join(",", result.Positions
                .Select(p => System.Math.Round(ToImageCoord(result.RowsOfProfile, p)).ToString("0")));
            Result.Value = judgement!.Passed;
            BadIndex.Value = judgement.BadIndex;
            Verdict.Value = judgement.Verdict;
            Expected.Value = judgement.ExpectedText;

            context.Logger.Info($"颜色序列检查：找到 {result.Count} 根：{seq}");
            context.Logger.Info($"颜色序列检查：线距 {result.Pitch:0.0} 像素"
                + $"（分段估 {result.PitchFromSegments:0.0} / 自相关 {result.PitchFromAutocorrelation:0.0}），"
                + $"阈值 暗<{result.DarkThreshold:0} 彩>{result.SaturationThreshold:0} 亮>{result.BrightThreshold:0}，"
                + $"可靠段 {result.SegmentCount} 段，端点外推 {result.ExtendedEnds} 根");
            context.Logger.Info($"颜色序列检查·逐根来源：{detail}");
            context.Logger.Info($"颜色序列检查·判定：{judgement.Verdict}");

            ProjectResult(image, result, judgement);
        }

        /// <summary>
        /// 把结果投到主界面视图窗口。画三样：采样线（那条中位线本身）、逐根颜色标签、结论行。
        ///
        /// 颜色约定：结论与采样线随判定（合格绿 / 不合格红）；**不符的那一根红字**标出来；
        /// **「规整补出来」的那几根橙字** —— 那是 Detail 里「补」标记的画面版，
        /// 让现场一眼看出哪几根是算出来的、不是看到的。
        ///
        /// 推图属于"锦上添花"：投递失败绝不能把一次正常的检测判成失败（与 Plugin.PreProcessing 同一口径）。
        /// </summary>
        private void ProjectResult(HImage image, SequenceResult result, Judgement judgement)
        {
            if (DisplayViewIndex <= 0) return;
            if (result.ColsOfProfile.Length == 0 || result.RowsOfProfile.Length == 0) return;

            try
            {
                image.GetImageSize(out _, out int height);

                var marks = new List<MeasureAnnotation>();
                string verdictColor = ColorMarks.For(judgement.Passed);

                // 采样线：剖面本身就是"那条采样线"，把两端连起来
                marks.Add(ColorMarks.Line(
                    result.RowsOfProfile[0], result.ColsOfProfile[0],
                    result.RowsOfProfile[result.RowsOfProfile.Length - 1],
                    result.ColsOfProfile[result.ColsOfProfile.Length - 1],
                    verdictColor));

                double pitch = System.Math.Max(8.0, result.Pitch);
                for (int i = 0; i < result.Count; i++)
                {
                    double row = ToImageCoord(result.RowsOfProfile, result.Positions[i]);
                    double col = ToImageCoord(result.ColsOfProfile, result.Positions[i]);
                    marks.Add(ColorMarks.Text(row, col + 10, $"{i + 1}.{result.Names[i]}", LabelColor(i, result, judgement)));
                }

                // 结论行：优先贴采样区上方；贴不下改贴下方；再越界就夹回画面内
                double pitchGap = System.Math.Max(16, pitch);
                double above = result.RowsOfProfile[0] - pitchGap;
                double textRow = above >= 10
                    ? above
                    : result.RowsOfProfile[result.RowsOfProfile.Length - 1] + pitchGap;
                textRow = System.Math.Clamp(textRow, 10, System.Math.Max(10, height - 10));

                marks.Add(ColorMarks.Text(textRow, result.ColsOfProfile[0] + 10, judgement.Verdict, verdictColor));

                this.PublishPreview(image, DisplayViewIndex, marks);
            }
            catch
            {
                // 投递失败只是画面上没有标注，不影响判定结果
            }
        }

        /// <summary>某一根标签用什么颜色：不符的那根红字，规整补出来的橙字，其余绿字</summary>
        private static string LabelColor(int index, SequenceResult result, Judgement judgement)
        {
            if (!judgement.Passed && judgement.BadIndex == index + 1) return ColorMarks.Fail;
            return result.FromSegment[index] ? ColorMarks.Pass : ColorMarks.Inferred;
        }

        /// <summary>
        /// 采样 + 分析 + 判定。采样几何失败（"框画歪了/超出图了"这类）、分析失败（"找不出线距"这类）、
        /// 判定失败（"配方表里没有这个芯数"这类）都从这一个口子出来，提示语一律写清"怎么补"。
        /// </summary>
        private bool TryAnalyze(HImage image, out SequenceResult? result, out Judgement? judgement, out string error)
        {
            result = null;
            judgement = null;
            error = string.Empty;

            if (RoiParams == null || RoiParams.Length != 5)
            {
                error = "尚未框选采样区：请在节点配置里于图像上右键 → 新建矩形，框住线束";
                return false;
            }

            if (!TryBuildProfile(image, out var profile, out error)) return false;

            var settings = new SequenceSettings
            {
                ForceCount = WireCount,
                Regularize = Regularize,
                DarkSpanRatio = DarkSpanRatio,
                BrightPercentile = BrightPercentile,
                MinRun = MinRun,
                MaxRunRatio = MaxRunRatio,
                RuleWindowRatio = RuleWindowRatio,
                ExtendEnds = ExtendEnds,
                Colors = BuildThresholds(),
            };

            if (!ColorSequenceAnalyzer.Analyze(profile!.Profile, settings, out var analyzed, out error)) return false;

            if (ReverseSequence)
            {
                int length = profile!.Profile.Length;
                analyzed.Names = analyzed.Names.Reverse().ToArray();
                analyzed.FromSegment = analyzed.FromSegment.Reverse().ToArray();
                analyzed.Positions = analyzed.Positions.Reverse().Select(p => length - 1 - p).ToArray();
            }

            // 剖面索引换算成图像坐标，供日志与投射用；换算表跟着结果一起走
            analyzed.RowsOfProfile = profile.Rows;
            analyzed.ColsOfProfile = profile.Cols;
            result = analyzed;

            return TryJudge(analyzed, out judgement, out error);
        }

        /// <summary>把界面上那 13 个阈值收成一个判据对象交给算法（内核只认这个对象）</summary>
        private ColorThresholds BuildThresholds() => new()
        {
            ColorlessSaturation = this.ColorlessSaturation,
            BlackValue = this.BlackValue,
            WhiteSaturation = this.WhiteSaturation,
            WhiteValue = this.WhiteValue,
            BrownWarmth = this.BrownWarmth,
            BrownValue = this.BrownValue,
            RoseHueFrom = this.RoseHueFrom,
            RedHueFrom = this.RedHueFrom,
            RedHueTo = this.RedHueTo,
            OrangeHueTo = this.OrangeHueTo,
            YellowHueTo = this.YellowHueTo,
            GreenHueTo = this.GreenHueTo,
            BlueHueTo = this.BlueHueTo,
        };

        /// <summary>剖面坐标（可能是小数）换算成图像坐标：在换算表上线性插值</summary>
        private static double ToImageCoord(double[] table, double position)
        {
            if (table == null || table.Length == 0) return position;

            int i0 = (int)System.Math.Floor(position);
            if (i0 < 0) return table[0];
            if (i0 >= table.Length - 1) return table[table.Length - 1];

            double t = position - i0;
            return table[i0] * (1 - t) + table[i0 + 1] * t;
        }

        // ==================================================================
        //  判定（配方 → 逐位比对 → 结论）
        // ==================================================================

        /// <summary>判定结果：用了哪条配方、对不对、第一位不符在哪</summary>
        private sealed class Judgement
        {
            internal string RecipeName { get; set; } = string.Empty;
            internal string[] ExpectedColors { get; set; } = System.Array.Empty<string>();
            internal string ExpectedText { get; set; } = string.Empty;
            internal bool Passed { get; set; }
            internal int BadIndex { get; set; }
            internal string Verdict { get; set; } = string.Empty;
        }

        /// <summary>
        /// 拿期望序列与实测序列比。
        ///
        /// 期望序列从哪来：**「期望序列」端口填了就以端口为准**（留给"配方从数据库/工单取"的场景），
        /// 没填才去查配方表（按实测根数匹配，期望序列的长度就是芯数）。
        ///
        /// 配方缺失（根数对不上任何一条）时**直接报错让流程停住**，不判成 NG：
        /// 配方没配是工程配置问题，不是产品不良 —— 判成 NG 的话，一条配错的线会永远把良品报成 NG，
        /// 而现场只会以为"这批产品有问题"。这一点与脚本版（走 Else 判 NG）是**有意不同**的。
        /// </summary>
        private bool TryJudge(SequenceResult result, out Judgement? judgement, out string error)
        {
            judgement = null;
            error = string.Empty;

            string portValue = ExpectedSequence.GetTypedValue() ?? string.Empty;
            string[] expected;
            string recipeName;

            if (!string.IsNullOrWhiteSpace(portValue))
            {
                expected = RecipeTable.SplitColors(portValue);
                recipeName = "输入端口";
            }
            else
            {
                var recipes = RecipeTable.Parse(RecipeText, out string parseError);
                if (recipes == null) { error = parseError; return false; }

                var recipe = RecipeTable.MatchByWireCount(recipes, result.Count, result.Warnings);
                if (recipe == null)
                {
                    string known = recipes.Count == 0
                        ? "配方表是空的"
                        : "配方表里有 " + string.Join("、", recipes.Select(r => $"{r.Name}（{r.WireCount} 芯）"));
                    error = $"未知产品：{result.Count} 芯没有对应配方（{known}）："
                          + "请补一条配方，或用「期望序列」输入端口直接把期望序列给进来";
                    return false;
                }

                expected = recipe.Colors;
                recipeName = recipe.Name;
            }

            var verdict = new Judgement
            {
                RecipeName = recipeName,
                ExpectedColors = expected,
                ExpectedText = string.Join(",", expected),
                Passed = RecipeTable.Compare(result.Names, expected, out int badIndex),
                BadIndex = badIndex,
            };
            verdict.Verdict = BuildVerdict(result, verdict);
            judgement = verdict;
            return true;
        }

        /// <summary>写给人看的那一句结论（日志、投射、界面共用同一句，避免三处口径不一致）</summary>
        private static string BuildVerdict(SequenceResult result, Judgement judgement)
        {
            string actual = string.Join(",", result.Names);

            if (result.Count != judgement.ExpectedColors.Length)
                return $"根数不符：实测 {result.Count} 根（{actual}），配方「{judgement.RecipeName}」要求 {judgement.ExpectedColors.Length} 根";

            if (judgement.Passed)
                return $"线序正确，{result.Count} 芯：{actual}（配方「{judgement.RecipeName}」）";

            int i = judgement.BadIndex - 1;
            return $"线序错误：第 {judgement.BadIndex} 根应为 {judgement.ExpectedColors[i]}、实测 {result.Names[i]}"
                 + $"（配方「{judgement.RecipeName}」；实测 {actual}）";
        }

        /// <summary>一次采样的几何：合并后的三通道剖面，以及每个剖面索引对应的图像坐标</summary>
        private sealed class ProfileBundle
        {
            internal ColorProfile Profile { get; set; } = null!;
            internal double[] Rows { get; set; } = System.Array.Empty<double>();
            internal double[] Cols { get; set; } = System.Array.Empty<double>();
        }

        /// <summary>
        /// 沿采样区短轴取一条中位剖面。
        ///
        /// 取点走元组版 GetGrayval：一次调用拿回整批点的灰阶，比逐点调用快两个数量级；
        /// 也不必把通道包成 HImage 对象去管 Halcon 句柄（那条路上踩过 HALCON #4056 object-ID is NULL）。
        /// </summary>
        private bool TryBuildProfile(HImage image, out ProfileBundle? bundle, out string error)
        {
            bundle = null;
            error = string.Empty;

            double row = RoiParams[0], col = RoiParams[1], phi = RoiParams[2];
            double half1 = RoiParams[3], half2 = RoiParams[4];
            if (half1 <= 0 || half2 <= 0)
            {
                error = "采样区的半长/半宽必须大于 0：请重新画一个框";
                return false;
            }

            // Halcon 的 phi=0 时长轴沿列方向；因为图像行坐标向下，故长轴单位向量是 (-sin, cos)
            double sin = System.Math.Sin(phi), cos = System.Math.Cos(phi);
            double axisRow = -sin, axisCol = cos;
            double sideRow = cos, sideCol = sin;

            // 长轴自动判为线束走向：半长大的那条轴。用户把框竖着画也能用
            bool firstIsLong = half1 >= half2;
            double alongRow = firstIsLong ? axisRow : sideRow;
            double alongCol = firstIsLong ? axisCol : sideCol;
            double acrossRow = firstIsLong ? sideRow : axisRow;
            double acrossCol = firstIsLong ? sideCol : axisCol;
            double acrossHalf = System.Math.Min(half1, half2);

            int sampleCount = (int)System.Math.Round(2 * acrossHalf) + 1;
            if (sampleCount < 8)
            {
                error = $"采样区的短边只有 {sampleCount} 个像素，太窄了：把框沿线束方向之外的那一边拉宽些";
                return false;
            }

            image.GetImageSize(out int width, out int height);

            // 5 条采样线在长轴方向只偏移几个像素 —— 与脚本版「左右各取 3 列、取中位」同源。
            //
            // 为什么不沿长轴摊开（比如按 10/30/50/70/90 百分比铺满采样区）：
            // 实测样本图里的线束是**扇形散开**的（连接器在一端，线向另一端散开），
            // 同一根线在不同列上的行号能差十几像素；摊开后按行号合并，
            // 会把只在个别列上明显的边界线洗成背景 —— 实测第 10 根棕线就是这样丢的，根数直接少两根。
            // 抗单点噪声的诉求，靠"几条邻近采样线取中位"已经满足，不需要摊开。
            double[] lineOffsets = { -4, -2, 0, 2, 4 };
            const int lineCount = 5;
            int pointCount = lineCount * sampleCount;

            var rows = new double[pointCount];
            var cols = new double[pointCount];

            // 线序在前（line-major），后面按同一短轴位置把 5 条线的值并起来取中位
            for (int li = 0; li < lineCount; li++)
                for (int k = 0; k < sampleCount; k++)
                {
                    double s = -acrossHalf + k;
                    double t = lineOffsets[li];
                    double r = row + alongRow * t + acrossRow * s;
                    double c = col + alongCol * t + acrossCol * s;

                    if (r < 0 || r > height - 1 || c < 0 || c > width - 1)
                    {
                        error = $"采样区超出图像范围（图像 {width}x{height}，采样点取到 ({r:0}, {c:0})）：请把框画在图像内";
                        return false;
                    }

                    rows[li * sampleCount + k] = r;
                    cols[li * sampleCount + k] = c;
                }

            // 取色交给内核：与「区域颜色检查」走同一条路，免得两处各写一遍 GetGrayval
            if (!ColorSampler.TryReadPixels(image, rows, cols, out int[][] channel, out error)) return false;
            if (channel.Length < 3)
            {
                error = $"颜色序列检查要彩色图，当前图像只有 {channel.Length} 个通道";
                return false;
            }

            // 同一短轴位置上的 5 个像素取中位 —— 单点取值容易被反光/脏点骗
            var r2 = new int[sampleCount];
            var g2 = new int[sampleCount];
            var b2 = new int[sampleCount];
            var bucket = new int[lineCount];
            for (int k = 0; k < sampleCount; k++)
            {
                r2[k] = MedianOfFive(channel[0], k, sampleCount, bucket);
                g2[k] = MedianOfFive(channel[1], k, sampleCount, bucket);
                b2[k] = MedianOfFive(channel[2], k, sampleCount, bucket);
            }

            var profileRows = new double[sampleCount];
            var profileCols = new double[sampleCount];
            int middleLine = lineCount / 2;
            for (int k = 0; k < sampleCount; k++)
            {
                profileRows[k] = rows[middleLine * sampleCount + k];
                profileCols[k] = cols[middleLine * sampleCount + k];
            }

            bundle = new ProfileBundle
            {
                Profile = new ColorProfile(r2, g2, b2),
                Rows = profileRows,
                Cols = profileCols,
            };
            return true;
        }

        /// <summary>取同一短轴位置上 5 条采样线灰阶的中位数</summary>
        private static int MedianOfFive(int[] buffer, int k, int sampleCount, int[] bucket)
        {
            for (int li = 0; li < bucket.Length; li++) bucket[li] = buffer[li * sampleCount + k];
            System.Array.Sort(bucket);
            return bucket[bucket.Length / 2];
        }
    }
}
