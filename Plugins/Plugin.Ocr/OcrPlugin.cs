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
    /// 只输出"认到了什么"（文本 + 逐字符置信度），合格与否交给下游的比较节点 ——
    /// 与 Plugin.ColorCheck 立的规矩一致："没有合格与否的输出，判定请继续用现有方案"。
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
    public class OcrPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ==================================================================
        //  常用参数（默认展开）
        // ==================================================================

        /// <summary>识别区形状参数，按 Halcon Rectangle 约定：[中心行, 中心列, 角度(弧度), 半长, 半宽]</summary>
        [StepConfig]
        public double[] RoiParams { get; set; } = Array.Empty<double>();

        /// <summary>
        /// 配置用的示意图路径。只在"打开配置界面"时读它 —— 那会儿还没有上游图像，
        /// 不给人看一眼图就没法框识别区、更没法试算。运行期一律用上游连进来的图像。
        /// </summary>
        [StepConfig]
        public string PreviewImagePath { get; set; } = string.Empty;

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

        /// <summary>true = 按列优先排序（竖排文本），false = 按行优先（横排）</summary>
        [StepConfig]
        public bool ColumnFirst { get; set; }

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待识别的图像（运行期由上游连入）</summary>
        public InputPort<HImage> Image { get; } = new("Image", null, "待识别的图像") { IsRequired = false };

        /// <summary>识别出的文本</summary>
        public OutputPort<string> Text { get; } = new("Text", "识别文本");

        /// <summary>字符数</summary>
        public OutputPort<int> Count { get; } = new("Count", "字符数");

        /// <summary>平均置信度（0~1）。低置信度表示本次结果不可信</summary>
        public OutputPort<double> Confidence { get; } = new("Confidence", "平均置信度");

        /// <summary>逐字符明细：序号 + 字符 + 置信度；拒识字显示为 ?</summary>
        public OutputPort<string> Detail { get; } = new("Detail", "逐字符明细（序号+字符+置信度，?=拒识）");

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

        private string _hint = "点「载入示意图」，然后在右侧图上右键 → 新建矩形，框住整行字符；再点「试算一下」看识别结果";

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

        /// <summary>视图就绪回调（视图 code-behind 只留这一个信号）：把示意图读进来</summary>
        public void OnViewLoaded() => LoadPreviewImage();

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
        public void ClearRoi() => CanvasRois.Clear();

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
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryRecognize(DisplayImage, out var outcome, out string error))
            {
                ResultSummary = "试算失败：" + error;
                return;
            }

            ResultSummary =
                $"识别结果：'{outcome!.Text}'\r\n"
                + $"字符 {outcome.Count} 个（拒识 {outcome.RejectedCount} 个）　均值置信 {outcome.AverageConfidence:0.00}　"
                + $"实际阈值 {outcome.UsedThreshold:0}\r\n"
                + $"逐字符：{outcome.Detail}";
        }

        // ==================================================================
        //  运行期
        // ==================================================================

        public override void RunAlgorithm(IExecutionContext context)
        {
            var image = Image.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("上游没有图像");
                return;
            }

            if (!TryRecognize(image, out var outcome, out string error))
            {
                Fail(error);
                return;
            }

            Text.Value = outcome!.Text;
            Count.Value = outcome.Count;
            Confidence.Value = outcome.AverageConfidence;
            Detail.Value = outcome.Detail;

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
        private bool TryRecognize(HImage image, out OcrOutcome? outcome, out string error)
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
                if (!OcrEngine.TryResolveModel(ModelPath, out string modelPath, out error))
                    return false;

                if (!_engine.TryRecognize(segment, modelPath, out var recognition, out error) || recognition == null)
                    return false;

                outcome = new OcrOutcome
                {
                    Text = recognition.Text,
                    Count = recognition.Count,
                    RejectedCount = recognition.RejectedCount,
                    AverageConfidence = recognition.AverageConfidence,
                    Detail = recognition.Detail,
                    UsedThreshold = segment.UsedThreshold,
                };
                return true;
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
