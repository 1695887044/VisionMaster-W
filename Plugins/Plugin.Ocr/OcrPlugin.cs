using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;

namespace Plugin.Ocr
{
    /// <summary>
    /// OCR 文本识别插件：在矩形识别区里切分字符，用内置 MLP 分类器认字。
    ///
    /// 定位（最不可逆的一刀）
    /// ---------
    /// 做的是**通用「OCR 文本识别」**而不是某个专用场景（如"线序字符识别"）：
    /// 料号、日期码、批次号、铭牌都用同一个节点。理由与 Plugin.ColorCheck 的取舍同源 ——
    /// 将来要收窄成专用很容易，反过来拆不回去。
    ///
    /// 但 OCR 与传统视觉算子有个本质差别，它决定了本插件的形状：
    /// **分割是成败的 90%，识别只是一行调用**。所以"分割策略"是一等公民
    /// （见 <see cref="CharSegmenter"/> 的注释），参数按"常用可见 / 高级折叠"两层摆放。
    ///
    /// 判定不归本插件管
    /// ---------
    /// 只输出"认到了什么"（文本 + 逐字符类别/置信度/字符框），合格与否交给下游的比较节点 ——
    /// 与 Plugin.ColorCheck 立的规矩一致："没有合格与否的输出，判定请继续用现有方案"。
    /// 逐字符三件套（<see cref="Classes"/>/<see cref="Confidences"/>/<see cref="CharBoxes"/>）
    /// 按下标逐项对齐、顺序 = 阅读顺序，就是为了让下游能"按位"拼判定条件（第 3 位必须是 B、
    /// 字符间距超限即报警之类），而不必再去解析 <see cref="Detail"/> 的字符串。
    ///
    /// 认不准必须看得出来
    /// ---------
    /// OCR 最危险的失败不是"认错"，而是"给出一串看似正常的错字符却没有任何提示"，
    /// 那种结果会被下游当成成功识别直接放行。所以本插件在两种情况下主动写 Warn：
    /// 出现拒识字（字符不在分类器字符集内，或字形不可辨）、以及均值置信度低于可靠线。
    /// 这条机制的直接来源是实测：低对比点阵图上分类器会大面积拒识、置信度崩到 0.7 上下。
    ///
    /// 数据流（与 Plugin.CreateRoi / Plugin.ColorCheck 同一套约定）
    /// ---------
    ///   控件右键新建/删除 → 改动 CanvasRois → CollectionChanged 回写 RoiParams
    ///   拖拽句柄        → 控件回写 info.HTuples（INPC）→ 回写 RoiParams
    ///   打开配置        → Initialize 按 RoiParams 播种 CanvasRois → 控件上屏
    /// </summary>
    [Display(
        Name = "OCR 文本识别",
        GroupName = "图像处理",
        Description = "在识别区里切分字符并用内置分类器识别（0-9 / A-Z，带拒识）",
        ShortName = "\uf031"
    )]
    [ParallelSafe] // 二期真并行：纯视觉计算
    public class OcrPlugin : VisionPluginBase, IPluginCustomViewProvider, IAssetIdConfig
    {
        // ==================================================================
        //  常用参数（默认展开）
        // ==================================================================

        /// <summary>识别区形状参数，按 Halcon Rectangle 约定：[中心行, 中心列, 角度(弧度), 半长, 半宽]</summary>
        [StepConfig]
        public double[] RoiParams { get; set; } = Array.Empty<double>();

        /// <summary>极性：暗字亮底（工业最常见的打码方式）还是亮字暗底</summary>
        [StepConfig]
        public bool TextIsDark { get; set; } = true;

        /// <summary>字符最小/最大高度（像素）。按实际字符尺寸收紧能挡掉背景纹理与脏点</summary>
        [StepConfig]
        public int MinCharHeight { get; set; } = 8;

        [StepConfig]
        public int MaxCharHeight { get; set; } = 300;

        /// <summary>字符最小/最大宽度（像素）</summary>
        [StepConfig]
        public int MinCharWidth { get; set; } = 4;

        [StepConfig]
        public int MaxCharWidth { get; set; } = 300;

        // ==================================================================
        //  高级参数（配置界面里折叠）
        // ==================================================================

        /// <summary>用固定阈值（true）还是自动阈值（false）。光照均匀时自动即可</summary>
        [StepConfig]
        public bool UseFixedThreshold { get; set; }

        /// <summary>固定阈值：暗字时取灰度 &lt;= 本值，亮字时取 &gt;= 本值</summary>
        [StepConfig]
        public int FixedThreshold { get; set; } = 128;

        [StepConfig]
        public int MinArea { get; set; } = 20;

        [StepConfig]
        public int MaxArea { get; set; } = 5000;

        /// <summary>
        /// 闭运算半径（点阵字必备）：把散点连成笔画，同时不让字形变胖。
        /// 默认 0 —— 实心印刷字不需要；点阵喷印从 1.5 起试。
        /// </summary>
        [StepConfig]
        public double ClosingRadius { get; set; }

        /// <summary>腐蚀半径：印刷字粘连时先腐蚀断开。默认 0</summary>
        [StepConfig]
        public double ErosionRadius { get; set; }

        /// <summary>膨胀半径：笔画过细时加粗（会同时外扩字形，点阵字请优先用闭运算）。默认 0</summary>
        [StepConfig]
        public double DilationRadius { get; set; }

        /// <summary>外部模型路径。留空 = 用内置模型（0-9 / A-Z）；填了则用指定模型（如中文/自训练）</summary>
        [StepConfig]
        public string ModelPath { get; set; } = string.Empty;

        /// <summary>方案模型资产 Id（优先于 ModelPath；运行期由宿主解析成绝对路径）。</summary>
        [StepConfig]
        public string ModelAssetId { get; set; } = string.Empty;

        public string AssetKind => "model";
        public string AssetId
        {
            get => ModelAssetId;
            set => ModelAssetId = value ?? string.Empty;
        }

        /// <summary>true = 按列优先排序（竖排文本），false = 按行优先（横排）</summary>
        [StepConfig]
        public bool ColumnFirst { get; set; }

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待识别的图像（运行期由上游连入）</summary>
        public InputPort<HImage> Image { get; } = new("Image", default!, "待识别的图像") { IsRequired = false };

        /// <summary>识别出的文本</summary>
        public OutputPort<string> Text { get; } = new("Text", "识别文本");

        /// <summary>字符数</summary>
        public OutputPort<int> Count { get; } = new("Count", "字符数");

        /// <summary>平均置信度（0~1）。低置信度表示本次结果不可信</summary>
        public OutputPort<double> Confidence { get; } = new("Confidence", "平均置信度");

        /// <summary>逐字符明细：序号 + 字符 + 置信度；拒识字显示为 ?</summary>
        public OutputPort<string> Detail { get; } = new("Detail", "逐字符明细（序号+字符+置信度，?=拒识）");

        // ---- 结构化输出（只给数据，不判 OK/NG；判定交给下游通用判定算子）----
        // 与 Text/Count/Confidence/Detail 的关系：同一份识别结果的不同切面，
        // Classes/Confidences/CharBoxes 三者**按下标逐项对齐**，下标顺序 = 阅读顺序。
        // 类型口径跟仓内先例：类别名数组用 string[]（同 CodeReader.Codes），
        // 数值数组用 HTuple（同 BlobDetect 的 CenterRows/DefectAreas 等）。

        /// <summary>
        /// 逐字符原始类别名（**不替换拒识占位符**：分类器认不出时就是 U+001A，原样给出）。
        /// 与 <see cref="Confidences"/> / <see cref="CharBoxes"/> 逐项对齐，顺序 = 阅读顺序。
        /// </summary>
        public OutputPort<string[]> Classes { get; } =
            new("Classes", "逐字符原始类别名（与 Confidences/CharBoxes 逐项对齐；拒识为 U+001A）");

        /// <summary>逐字符置信度（0~1），长度 = <see cref="Count"/></summary>
        public OutputPort<HTuple> Confidences { get; } =
            new("Confidences", "逐字符置信度（0~1），与 Classes/CharBoxes 逐项对齐");

        /// <summary>拒识字符数（= <see cref="Classes"/> 里 U+001A 的个数）</summary>
        public OutputPort<int> RejectedCount { get; } =
            new("RejectedCount", "拒识字符数（= Classes 里 U+001A 的个数）");

        /// <summary>
        /// 逐字符外接框，扁平 stride 4：<c>[row1,col1,width,height, row2,...]</c>。
        /// 轴对齐最小外接矩形（<c>smallest_rectangle1</c>），宽高按像素**含端点**（<c>col2-col1+1</c>），
        /// 顺序与 <see cref="Classes"/> 一致。0 字符时是空元组。
        /// </summary>
        public OutputPort<HTuple> CharBoxes { get; } =
            new("CharBoxes", "逐字符外接框，扁平 stride 4：[row1,col1,width,height, ...]（像素，宽高含端点）");

        // ==================================================================
        //  运行期状态
        // ==================================================================

        /// <summary>
        /// 分类器引擎。构造即建、只加载句柄时才有开销，所以不必懒初始化 ——
        /// 懒初始化反而要处理"配置界面试算"与"流程运行"并发时的创建竞态。
        /// </summary>
        private readonly OcrEngine _engine = new();

        /// <summary>
        /// 均值置信度低于此值时主动写 Warn。
        /// 取常量而不是配置项：它只影响"要不要提醒"，不参与判定（判定归下游），
        /// 多一个旋钮只会让现场多一处要调的东西。
        /// </summary>
        private const double LowConfidenceThreshold = 0.9;

        /// <summary>一次识别的完整结果（运行期与配置期试算共用）</summary>
        public sealed class OcrOutcome
        {
            public string Text { get; init; } = string.Empty;
            public int Count { get; init; }
            public int RejectedCount { get; init; }
            public double AverageConfidence { get; init; }
            public string Detail { get; init; } = string.Empty;
            public double UsedThreshold { get; init; }

            /// <summary>逐字符原始类别名（拒识保留 U+001A，不替换成 '?'），顺序 = 阅读顺序</summary>
            public string[] Classes { get; init; } = Array.Empty<string>();

            /// <summary>逐字符置信度（0~1），与 <see cref="Classes"/> 逐项对齐</summary>
            public double[] Confidences { get; init; } = Array.Empty<double>();

            /// <summary>逐字符外接框，扁平 stride 4：[row1,col1,width,height, ...]（像素，宽高含端点）</summary>
            public double[] CharBoxes { get; init; } = Array.Empty<double>();
        }

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

        private string _hint = "先在「图像输入」里点 🔗 手动绑定图像来源（选上游端口或变量），再在右侧图上右键 → 新建矩形，框住整行字符；然后「试算一下」看识别结果";

        /// <summary>界面提示（载入结果、错误原因都走它）</summary>
        public string Hint
        {
            get => _hint;
            set => SetProperty(ref _hint, value);
        }

        private string _resultSummary = "尚未试算";

        /// <summary>试算结果摘要（认到了什么、每个字的置信度）</summary>
        public string ResultSummary
        {
            get => _resultSummary;
            set => SetProperty(ref _resultSummary, value);
        }

        /// <summary>识别区摘要（给界面显示当前框在哪）</summary>
        public string RoiSummary
        {
            get
            {
                if (RoiParams == null || RoiParams.Length != 5)
                    return "尚未框选识别区";

                double degrees = RoiParams[2] * 180.0 / Math.PI;
                return $"中心 ({RoiParams[0]:0}, {RoiParams[1]:0})  角度 {degrees:0.#} 度  "
                     + $"半长 {RoiParams[3]:0}  半宽 {RoiParams[4]:0}";
            }
        }

        /// <summary>
        /// 播种画布期间为真：此时 CanvasRois 的变更来自"回填"，不能再回写 RoiParams，
        /// 否则会把形状参数的写法绕回自身、在打开配置时反复触发。
        /// </summary>
        private bool _seedingCanvas;

        public OcrPlugin()
        {
            CanvasRois.CollectionChanged += OnCanvasRoisChanged;

            // 试运行时上游图是**通过给端口赋值**桥接进配置实例的（PluginTestRunner.BridgeInputs），
            // 所以盯着端口的变化就能把上游图搬到预览上 ——
            // 否则配置界面里只有"填示意图路径"这一条路，用户会觉得"图像绑不上上游"
            //（与 RegionColor/ColorCheck 同一模式）
            Image.PropertyChanged += OnImagePortChanged;
        }

        private void OnImagePortChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IInputPort.Value)) return;
            ShowUpstreamImage();
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
                        "识别区"));
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
            return new OcrView { DataContext = this };
        }

        /// <summary>
        /// 视图就绪回调：把上游图显示到预览。
        /// 图只有一个来源 —— 上面的「图像输入」端口（见构造函数里对端口变化的订阅）。
        /// </summary>
        public void OnViewLoaded() => ShowUpstreamImage();

        /// <summary>
        /// 把上游图显示到预览。有则返回 true。
        /// 显示的是**自己拷的一份**：上游那张图的句柄归框架管，我们只负责自己的副本。
        /// </summary>
        public bool ShowUpstreamImage()
        {
            var upstream = Image.GetTypedValue();
            if (upstream == null || !upstream.IsInitialized()) return false;

            try
            {
                DisplayImage?.Dispose();
                DisplayImage = new HImage(upstream);
                Hint = "已显示上游图像（点「执行」试运行带进来的）：可直接点「试算一下」看识别效果";
                return true;
            }
            catch (Exception ex)
            {
                Hint = "上游图像显示失败：" + ex.Message;
                return false;
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

            // 只认最后一个：本插件只需一个识别区，用户重画时应以最新的为准
            if (!ReferenceEquals(info, CanvasRois.LastOrDefault())) return;

            RoiParams = ToParams(info);
            OnPropertyChanged(nameof(RoiSummary));
        }

        /// <summary>
        /// 把画布状态回写成形状参数。取集合里最后一个 —— 用户画了多个只认最新的那个，
        /// 界面上另有「清空识别区」可以收拾。
        /// </summary>
        private void WriteBackRoi()
        {
            RoiParams = ToParams(CanvasRois.LastOrDefault());
            OnPropertyChanged(nameof(RoiSummary));
        }

        private static double[] ToParams(DrawingObjectInfo? info)
        {
            if (info?.HTuples == null || info.HTuples.Length == 0)
                return Array.Empty<double>();

            return info.HTuples.Select(t => t.D).ToArray();
        }

        /// <summary>清空识别区（界面按钮）</summary>
        public void ClearRoi()
        {
            CanvasRois.Clear();
            ResultSummary = "尚未试算";
            Hint = "识别区已清空：在右侧画布右键 → 新建矩形重新框住整行字符";
        }

        // ==================================================================
        //  配置期的试算
        // ==================================================================

        /// <summary>
        /// 用配置界面里的示意图跑一遍"分割 + 识别"，把结论写进 <see cref="ResultSummary"/>。
        ///
        /// 为什么必须在界面上留这个口子：分割的成败取决于"这幅图的直方图与字符尺寸长什么样"，
        /// 光看参数值想象不出来。能在框完识别区的当下看到"切出几个字、各自认成什么、置信度多少"，
        /// 才有依据去调「高级参数」。没有试算的 OCR 插件等于不可用。
        /// </summary>
        public void TryPreviewRecognize()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先在「图像输入」绑定上游图像（或点「执行」试运行把图带进来）再试算";
                Hint = "还没有图像：先在「图像输入」绑定上游图像，或点宿主「执行」把图带进来，再试算";
                return;
            }

            if (!TryRecognize(DisplayImage, null, out var outcome, out string error))
            {
                ResultSummary = "试算失败：" + error;
                Hint = "试算失败：先确认识别区是否框住整行字符、极性是否选对，再调整字符尺寸或阈值";
                return;
            }

            ResultSummary =
                $"识别结果：'{outcome!.Text}'\r\n"
                + $"字符 {outcome.Count} 个（拒识 {outcome.RejectedCount} 个）　均值置信 {outcome.AverageConfidence:0.00}　"
                + $"实际阈值 {outcome.UsedThreshold:0}\r\n"
                + $"逐字符：{outcome.Detail}";

            Hint = outcome.RejectedCount > 0
                ? $"识别完成：认出 {outcome.Count} 个字符（其中 {outcome.RejectedCount} 个拒识），平均置信度 {outcome.AverageConfidence:0.00}"
                : $"识别完成：认出 {outcome.Count} 个字符，平均置信度 {outcome.AverageConfidence:0.00}";
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 开轮重置所有"基类不会回收"的输出端口：基类的 AutoDisposeRoundOutputs 只处理 IDisposable
            // （HImage/HRegion），string/int/double/HTuple 端口的上一轮值会原样留下 ——
            // 本轮若失败，下游会读到上一轮的脏数据（BlobDetect 同款镜像 bug，见其 RunAlgorithm 开轮注释）。
            // 四个结构化端口与原有四个一起清：否则失败轮会出现"Classes 为空但 Count 是上一轮的值"，
            // 逐项对齐的契约当场自相矛盾。
            Text.Value = string.Empty;
            Count.Value = 0;
            Confidence.Value = 0;
            Detail.Value = string.Empty;
            Classes.Value = Array.Empty<string>();
            Confidences.Value = new HTuple();
            RejectedCount.Value = 0;
            CharBoxes.Value = new HTuple();

            var image = Image.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("上游没有图像");
                return;
            }

            if (!TryRecognize(image, context, out var outcome, out string error))
            {
                Fail(error);
                return;
            }

            Text.Value = outcome!.Text;
            Count.Value = outcome.Count;
            Confidence.Value = outcome.AverageConfidence;
            Detail.Value = outcome.Detail;

            // 结构化输出：只给数据，不判 OK/NG。HTuple 由 double[] 逐项构造（空数组 = 空元组）
            Classes.Value = outcome.Classes;
            Confidences.Value = ToTuple(outcome.Confidences);
            RejectedCount.Value = outcome.RejectedCount;
            CharBoxes.Value = ToTuple(outcome.CharBoxes);

            context.Logger.Info(
                $"OCR 识别：'{outcome.Text}'（{outcome.Count} 个字符，均值置信 {outcome.AverageConfidence:0.00}，"
                + $"拒识 {outcome.RejectedCount} 个，阈值 {outcome.UsedThreshold:0}）"
            );

            // ---- 下面两条 Warn 是本插件的"诚实机制"：认不准必须让现场知道 ----
            // 直接来源是实测：低对比点阵图（瓶盖曲面、细纹理纸面）上分类器会大面积拒识、
            // 均值置信度掉到 0.7 上下。此时 Text 仍然是一个"看着正常"的字符串，
            // 若没有任何提示，下游会把它当成成功结果直接放行。
            if (outcome.RejectedCount > 0)
            {
                context.Logger.Warn(
                    $"OCR 有 {outcome.RejectedCount} 个字符被拒识（不在分类器字符集内或字形不可辨），"
                    + $"识别结果不完整：{outcome.Detail}"
                );
            }

            if (outcome.AverageConfidence < LowConfidenceThreshold)
            {
                context.Logger.Warn(
                    $"OCR 均值置信度偏低（{outcome.AverageConfidence:0.00} < {LowConfidenceThreshold:0.00}），"
                    + $"本次识别结果不可信，请勿直接用于放行：{outcome.Detail}"
                );
            }
        }

        /// <summary>
        /// 分割 + 识别。运行期与配置期试算共用同一条路径 ——
        /// 若试算与运行走两套代码，界面上看着对、跑起来不对，那种问题最难查。
        /// </summary>
        private bool TryRecognize(HImage image, IExecutionContext? context, out OcrOutcome? outcome, out string error)
        {
            outcome = null;
            error = string.Empty;

            var options = new CharSegmenter.Options
            {
                TextIsDark = TextIsDark,
                UseFixedThreshold = UseFixedThreshold,
                FixedThreshold = FixedThreshold,
                MinCharHeight = MinCharHeight,
                MaxCharHeight = MaxCharHeight,
                MinCharWidth = MinCharWidth,
                MaxCharWidth = MaxCharWidth,
                MinArea = MinArea,
                MaxArea = MaxArea,
                ClosingRadius = ClosingRadius,
                ErosionRadius = ErosionRadius,
                DilationRadius = DilationRadius,
                ColumnFirst = ColumnFirst,
            };

            if (!CharSegmenter.TrySegment(image, RoiParams, options, out var segment, out error) || segment == null)
                return false;

            using (segment)
            {
                if (!ProjectAssetPathResolver.TryResolve(context, ModelAssetId, ModelPath, "model", out var configuredPath, out error))
                    return false;

                if (!OcrEngine.TryResolveModel(configuredPath, out string modelPath, out error))
                    return false;

                if (!_engine.TryRecognize(segment, modelPath, out var recognition, out error) || recognition == null)
                    return false;

                // 字符框与类别/置信度必须同源、同序：都在 segment.Chars 的阅读顺序上按下标对齐
                if (!TryBuildCharBoxes(segment, recognition.Count, out double[] charBoxes, out error))
                    return false;

                outcome = new OcrOutcome
                {
                    Text = recognition.Text,
                    Count = recognition.Count,
                    RejectedCount = recognition.RejectedCount,
                    AverageConfidence = recognition.AverageConfidence,
                    Detail = recognition.Detail,
                    UsedThreshold = segment.UsedThreshold,
                    Classes = recognition.Classes,
                    Confidences = recognition.Confidences,
                    CharBoxes = charBoxes,
                };
                return true;
            }
        }

        /// <summary>
        /// <c>double[]</c> → <c>HTuple</c>。空数组走 <c>new HTuple()</c>：
        /// "0 字符"这条路径必须稳定给出空元组，不依赖"用空数组构造 HTuple 会不会抛"这种实现细节。
        /// </summary>
        private static HTuple ToTuple(double[] values) =>
            values.Length == 0 ? new HTuple() : new HTuple(values);

        /// <summary>
        /// 逐字符算轴对齐外接框（<c>smallest_rectangle1</c>），扁平 stride 4：<c>[row1,col1,width,height, ...]</c>。
        ///
        /// 为什么宽高要 +1（含端点）：<c>smallest_rectangle1</c> 给的是 row1..row2 / col1..col2 的**闭区间**，
        /// 一个只占单像素宽的字 col1==col2，直接相减得 0 —— 下游拿 0 当"框不存在"会把这种字整条丢掉。
        /// 按 <c>col2-col1+1</c> 算出来才是"覆盖了几个像素"，与 BlobDetect 的 DefectWidths 口径一致。
        ///
        /// 为什么逐对象 SelectObj 而不是一次 SmallestRectangle1 全算：后者返回的是元组，
        /// 与 <c>segment.Chars</c> 的下标对齐全靠 HALCON 的输出顺序，而顺序契约没有任何断言守着；
        /// 逐对象取 i+1 号对象则把"第 i 个框 ↔ 第 i 个类别"写死在代码里。
        ///
        /// 0 字符时输出空数组、不报错：识别不到字不是错误，是"这一帧没有字"。
        /// </summary>
        private static bool TryBuildCharBoxes(
            CharSegmenter.SegmentResult segment,
            int count,
            out double[] charBoxes,
            out string error)
        {
            error = string.Empty;

            if (count <= 0)
            {
                charBoxes = Array.Empty<double>();
                return true;
            }

            charBoxes = new double[count * 4];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    // SelectObj 的索引是 1 基；取出的单个区域必须自己释放（HALCON 对象是进程级非托管资源）
                    HOperatorSet.SelectObj(segment.Chars, out HObject single, i + 1);
                    try
                    {
                        HOperatorSet.SmallestRectangle1(
                            single, out HTuple row1, out HTuple col1, out HTuple row2, out HTuple col2);

                        charBoxes[i * 4 + 0] = row1.D;
                        charBoxes[i * 4 + 1] = col1.D;
                        charBoxes[i * 4 + 2] = col2.D - col1.D + 1.0; // 宽：像素含端点
                        charBoxes[i * 4 + 3] = row2.D - row1.D + 1.0; // 高：像素含端点
                    }
                    finally
                    {
                        single.Dispose();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                charBoxes = Array.Empty<double>();
                error = $"计算字符外接框失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 释放分类器句柄（Halcon 的 read_ocr_class_mlp 是进程级非托管资源）。
        /// 基类会释放输出端口承载的非托管值，但管不到这里，所以必须重写。
        /// </summary>
        public override void Dispose()
        {
            _engine.Dispose();
            DisplayImage?.Dispose();
            DisplayImage = null;
            base.Dispose();
        }
    }
}
