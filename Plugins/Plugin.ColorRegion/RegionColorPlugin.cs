using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Core.Halcon;
using Core.Halcon.Color;
using Core.Halcon.Models;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;

namespace Plugin.ColorRegion
{
    /// <summary>
    /// 区域颜色检查：**一块区域是什么颜色、与期望比合不合格**。
    ///
    /// 它在"一族颜色算子"里的位置
    /// ---------
    ///   「颜色序列检查」认**顺序**（沿一条线取一串颜色词，逐位比）—— 线序、排线、色带。
    ///   「区域颜色检查」（本算子）认**一块区域**（ROI 内逐点投票取主色 + 占比）——
    ///   指示灯什么颜色、色标对不对、这块区域颜色纯不纯、某颜色占比够不够。
    /// 两者共用内核 Core.Halcon.Color 的**同一套颜色词与判据**，所以"玫红"在哪个算子里答案都一样。
    ///
    /// 关键设计（逐条与用户确认过）
    /// ---------
    ///   ① **逐点分类再投票**，不是"取中位色分类一次" —— 区域里两色混杂时会直接报"不纯（主色只占 41%）"，
    ///      而不是硬给一个词让现场以为很有把握；
    ///   ② ROI 支持**矩形 / 圆 / 椭圆**，形状由用户在图上右键画什么就是什么；采样用 Halcon 区域当掩膜，
    ///      所以**用方框框圆形指示灯时四个角的背景不会被采到**；
    ///   ③ 颜色判据阈值由内核给默认值，本算子带一份**可覆盖的副本**（界面上可调，随方案落盘）；
    ///   ④ 期望为空 → 报错（配置问题）；期望写 `*` → 只报颜色不判定（显式的不检）。
    /// </summary>
    [Display(
        Name = "区域颜色检查",
        GroupName = "图像处理",
        Description = "在采样区里逐点分类投票，取主色与占比，与期望颜色比对（指示灯 / 色标 / 颜色纯度 / 占比）",
        ShortName = "\uf043"
    )]
    public class RegionColorPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ==================================================================
        //  配置项
        // ==================================================================

        /// <summary>
        /// 采样区形状：Rectangle / Circle / Ellipse。
        /// 用字符串而不是枚举：枚举值经步骤的 InputValues 往返会抛 InvalidCastException。
        /// 形状由用户在图上右键选"新建矩形/圆形/椭圆"决定，不需要手工填。
        /// </summary>
        [StepConfig]
        public string RoiShape { get; set; } = RoiShapeNames.Rectangle;

        /// <summary>
        /// 采样区参数，固定 5 个：
        ///   矩形：[中心行, 中心列, 角度, 半长, 半宽]
        ///   圆　：[中心行, 中心列, 半径, 0, 0]
        ///   椭圆：[中心行, 中心列, 角度, 半径1, 半径2]
        /// 统一 5 个是为了让存取、回填、断言都只有一条路径（圆的第 4/5 个占位不用）。
        /// </summary>
        [StepConfig]
        public double[] RoiParams { get; set; } = Array.Empty<double>();

        /// <summary>配置用的示意图路径（只在打开配置界面时读它；运行期一律用上游图像）</summary>
        [StepConfig]
        public string PreviewImagePath { get; set; } = string.Empty;

        /// <summary>期望颜色（多个用逗号分隔，命中任一即可）；写 `*` 表示只报颜色不判定</summary>
        [StepConfig]
        public string ExpectedColors { get; set; } = string.Empty;

        /// <summary>主色最少要占多少才算合格（0~1）</summary>
        [StepConfig]
        public double MinShare { get; set; } = 0.60;

        /// <summary>区域内最多取多少个采样点（点太多时按步长抽稀）</summary>
        [StepConfig]
        public int SamplePoints { get; set; } = 400;

        /// <summary>结果投射到哪个视图窗口（1~9；0 = 不投射）</summary>
        [StepConfig]
        public int DisplayViewIndex { get; set; } = 1;

        // ---- 颜色判据阈值（13 项）：默认值来自内核，本算子带一份可覆盖的副本 ----
        // 属性名必须与共用控件 ColorThresholdEditor 里绑的名字一致（WPF 绑定失败是静默的）。

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

        // ==================================================================
        //  端口
        // ==================================================================

        /// <summary>待检查的图像（运行期由上游连入）</summary>
        public InputPort<HImage> Image { get; } = new("Image", description: "待检查的图像") { IsRequired = false };

        /// <summary>主色（票数最多的颜色词）</summary>
        public OutputPort<string> Color { get; } = new OutputPort<string>("Color", "主色");

        /// <summary>主色占比（0~1）</summary>
        public OutputPort<double> Share { get; } = new OutputPort<double>("Share", "主色占比（0~1）");

        /// <summary>区域中位色（字符串 R,G,B）</summary>
        public OutputPort<string> Rgb { get; } = new OutputPort<string>("Rgb", "区域中位色 R,G,B");

        /// <summary>各颜色词的占比明细（如 红:96%、灰:4%）</summary>
        public OutputPort<string> Table { get; } = new OutputPort<string>("Table", "各颜色占比明细");

        /// <summary>判定结论：合格 / 不合格</summary>
        public OutputPort<bool> Result { get; } = new OutputPort<bool>("Result", "判定结论：合格 true / 不合格 false");

        /// <summary>结论全文（写给人看的那一句）</summary>
        public OutputPort<string> Verdict { get; } = new OutputPort<string>("Verdict", "结论全文");

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

        private string _hint = "点「载入示意图」，然后在右侧图上右键 → 新建矩形/圆形/椭圆，框住要检的那块；再点「试算一下」看结果";

        /// <summary>界面提示（载入结果、错误原因都走它）</summary>
        public string Hint
        {
            get => _hint;
            set => SetProperty(ref _hint, value);
        }

        private string _resultSummary = "尚未试算";

        /// <summary>试算结果摘要（主色、占比、明细、判定）</summary>
        public string ResultSummary
        {
            get => _resultSummary;
            set => SetProperty(ref _resultSummary, value);
        }

        /// <summary>采样区摘要（给界面显示当前框在哪、什么形状）</summary>
        public string RoiSummary
        {
            get
            {
                if (RoiParams == null || RoiParams.Length != 5)
                    return "尚未框选采样区";

                return RoiShape switch
                {
                    RoiShapeNames.Circle =>
                        $"圆形  中心 ({RoiParams[0]:0}, {RoiParams[1]:0})  半径 {RoiParams[2]:0}",
                    RoiShapeNames.Ellipse =>
                        $"椭圆  中心 ({RoiParams[0]:0}, {RoiParams[1]:0})  角度 {RoiParams[2] * 180 / Math.PI:0.#} 度  "
                        + $"半径 {RoiParams[3]:0} × {RoiParams[4]:0}",
                    _ =>
                        $"矩形  中心 ({RoiParams[0]:0}, {RoiParams[1]:0})  角度 {RoiParams[2] * 180 / Math.PI:0.#} 度  "
                        + $"半长 {RoiParams[3]:0}  半宽 {RoiParams[4]:0}",
                };
            }
        }

        /// <summary>
        /// 播种画布期间为真：此时 CanvasRois 的变更来自"回填"，不能再回写参数，
        /// 否则会在打开配置时反复触发。
        /// </summary>
        private bool _seedingCanvas;

        /// <summary>上一次试算出来的主色与占比（「存成期望颜色」按钮用）</summary>
        private string _lastColor = string.Empty;

        public RegionColorPlugin()
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

                // 回填的前提是"参数真的画过一个框"：矩形/椭圆看半长，圆看半径 ——
                // 圆的第 4 个参数本来就是占位 0，用半长判会把圆误挡掉
                bool hasRoi = RoiParams != null && RoiParams.Length == 5
                              && (RoiShape == RoiShapeNames.Circle ? RoiParams[2] > 0 : RoiParams[3] > 0);
                if (hasRoi)
                {
                    CanvasRois.Add(new DrawingObjectInfo(
                        DrawShapeOf(RoiShape),
                        ParamsForCanvas(RoiShape, RoiParams),
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
            return new RegionColorView { DataContext = this };
        }

        /// <summary>视图就绪回调：先把图弄上屏（没图没法框 ROI）</summary>
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

        /// <summary>画布变更（控件新建/删除/清空）→ 回写形状与参数</summary>
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

        /// <summary>拖拽句柄后控件回写 HTuples（INPC）→ 同步参数</summary>
        private void OnRoiTuplesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples)) return;
            if (sender is not DrawingObjectInfo info) return;

            // 只认最后一个：本算子只需一个采样区，用户重画时应以最新的为准
            if (!ReferenceEquals(info, CanvasRois.LastOrDefault())) return;

            WriteBackRoi();
        }

        /// <summary>把画布状态回写成形状 + 参数（取最后一个；画多个只认最新的）</summary>
        private void WriteBackRoi()
        {
            var info = CanvasRois.LastOrDefault();
            if (info == null)
            {
                RoiParams = Array.Empty<double>();
            }
            else
            {
                RoiShape = ShapeNameOf(info.ShapeType);
                RoiParams = PadParams(info);
            }

            OnPropertyChanged(nameof(RoiSummary));
        }

        /// <summary>清空采样区（界面按钮）</summary>
        public void ClearRoi() => CanvasRois.Clear();

        // ==================================================================
        //  形状映射与轮廓
        // ==================================================================

        private static DrawShapeType DrawShapeOf(string shape) => shape switch
        {
            RoiShapeNames.Circle => DrawShapeType.Circle,
            RoiShapeNames.Ellipse => DrawShapeType.Ellipse,
            _ => DrawShapeType.Rectangle,
        };

        private static string ShapeNameOf(DrawShapeType shape) => shape switch
        {
            DrawShapeType.Circle => RoiShapeNames.Circle,
            DrawShapeType.Ellipse => RoiShapeNames.Ellipse,
            _ => RoiShapeNames.Rectangle,
        };

        /// <summary>给画布对象喂参数：圆只要 3 个（多喂会被 HALCON 拒），矩形/椭圆要 5 个</summary>
        private static HTuple[] ParamsForCanvas(string shape, double[] pars)
        {
            int count = shape == RoiShapeNames.Circle ? 3 : 5;
            var list = new HTuple[count];
            for (int i = 0; i < count; i++) list[i] = new HTuple(pars[i]);
            return list;
        }

        /// <summary>把画布对象读回来：统一补齐成 5 个，让存取只有一条路径</summary>
        private static double[] PadParams(DrawingObjectInfo info)
        {
            var tuples = info.HTuples ?? Array.Empty<HTuple>();
            var pars = new double[5];

            // 圆：行、列、半径（只要前 3 个），其余留 0
            if (info.ShapeType == DrawShapeType.Circle)
            {
                for (int i = 0; i < Math.Min(3, tuples.Length); i++) pars[i] = tuples[i].D;
                return pars;
            }

            for (int i = 0; i < Math.Min(5, tuples.Length); i++) pars[i] = tuples[i].D;
            return pars;
        }

        /// <summary>
        /// 采样区轮廓（闭合折线，首尾相接）。投射时用它把 ROI 画出来 ——
        /// 圆/椭圆不能只画一个方框，否则现场看不出到底采的是哪一块。
        /// </summary>
        private static List<(double Row, double Col)> OutlineOf(string shape, double[] pars)
        {
            var points = new List<(double, double)>();
            double row = pars[0], col = pars[1], phi = pars[2];
            double sin = Math.Sin(phi), cos = Math.Cos(phi);

            if (shape == RoiShapeNames.Circle)
            {
                double radius = pars[2];
                for (int i = 0; i <= 32; i++)
                {
                    double a = 2 * Math.PI * i / 32;
                    points.Add((row + radius * Math.Sin(a), col + radius * Math.Cos(a)));
                }
                return points;
            }

            if (shape == RoiShapeNames.Ellipse)
            {
                double radius1 = pars[3], radius2 = pars[4];
                for (int i = 0; i <= 32; i++)
                {
                    double a = 2 * Math.PI * i / 32;
                    double u = radius1 * Math.Cos(a);   // 沿 phi 方向（长轴）
                    double v = radius2 * Math.Sin(a);
                    points.Add((row - sin * u + cos * v, col + cos * u + sin * v));
                }
                return points;
            }

            // 矩形：四个角（Halcon 的 phi=0 时长轴沿列方向）
            double half1 = pars[3], half2 = pars[4];
            foreach (var (u, v) in new[] { (half1, half2), (half1, -half2), (-half1, -half2), (-half1, half2), (half1, half2) })
                points.Add((row - sin * u + cos * v, col + cos * u + sin * v));

            return points;
        }

        // ==================================================================
        //  配置期的试算
        // ==================================================================

        /// <summary>
        /// 用配置界面里的示意图跑一遍，把结论写进 <see cref="ResultSummary"/>。
        ///
        /// **试算只做颜色分析、不做判定** —— 现场通常是先试算出主色、再一键把它填成期望颜色；
        /// 若这里就要求"必须先填期望"，那个一手入口就永远用不上（顺序死锁）。
        /// 判定单独在运行期做（RunAlgorithm），并且必须做。
        /// </summary>
        public void TryPreviewAnalyze()
        {
            if (DisplayImage == null || !DisplayImage.IsInitialized())
            {
                ResultSummary = "先点「载入」把示意图读进来再试算";
                return;
            }

            if (!TryAnalyze(DisplayImage, out var result, out string error))
            {
                ResultSummary = "试算失败：" + error;
                return;
            }

            _lastColor = result!.Dominant;

            string expected = ExpectedColors?.Trim() ?? string.Empty;
            string judgeLine;
            if (expected.Length == 0)
            {
                judgeLine = "尚未配置期望颜色（点下面那个按钮即可把主色填进去）";
            }
            else if (!RegionColorAnalyzer.Judge(result, ParseExpected(), MinShare, out string judgeError))
            {
                judgeLine = "无法判定：" + judgeError;
            }
            else
            {
                judgeLine = $"{(result.Passed ? "合格" : "不合格")}　{result.Verdict}";
            }

            ResultSummary = $"主色：{result.Dominant}（占 {result.Share * 100:0}%）\r\n"
                          + $"中位色：R{result.R} G{result.G} B{result.B}（采样 {result.SampleCount} 点）\r\n"
                          + $"明细：{result.TableText()}\r\n"
                          + $"判定：{judgeLine}";
        }

        /// <summary>
        /// 把上一次试算出来的主色填成期望颜色。
        /// 手打颜色词必须**正好**落在词表里（写成"深红"永远匹配不上），一键填从根上避开这个坑。
        /// </summary>
        public void ApplyPreviewAsExpected()
        {
            if (string.IsNullOrEmpty(_lastColor))
            {
                Hint = "还没有试算结果：先框好采样区并点「试算一下」，再把主色填成期望颜色";
                return;
            }

            ExpectedColors = _lastColor;
            Hint = $"已把期望颜色填成：{_lastColor}";
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

            if (!TryAnalyze(image, out var result, out string error))
            {
                Fail(error);
                return;
            }

            // 运行期**必须**判定：没填期望、期望词写错，都在这里挡下来（配置问题不静默通过）
            if (!RegionColorAnalyzer.Judge(result!, ParseExpected(), MinShare, out error))
            {
                Fail(error);
                return;
            }

            foreach (var warning in result!.Warnings)
                context.Logger.Warn(warning);

            Color.Value = result.Dominant;
            Share.Value = result.Share;
            Rgb.Value = $"{result.R}, {result.G}, {result.B}";
            Table.Value = result.TableText();
            Result.Value = result.Passed;
            Verdict.Value = result.Verdict;

            context.Logger.Info($"区域颜色检查：主色 {result.Dominant}（占 {result.Share * 100:0}%），"
                + $"中位色 R{result.R} G{result.G} B{result.B}，采样 {result.SampleCount} 点");
            context.Logger.Info($"区域颜色检查：明细 {result.TableText()}");
            context.Logger.Info($"区域颜色检查·判定：{result.Verdict}");

            ProjectResult(image, result);
        }

        /// <summary>
        /// 采样 + 分析 + 判定。采样几何失败、颜色未配置、期望词写错，都从这一个口子出来，
        /// 提示语一律写清"怎么补"。
        /// </summary>
        private bool TryAnalyze(HImage image, out RegionColorResult? result, out string error)
        {
            result = null;
            error = string.Empty;

            if (RoiParams == null || RoiParams.Length != 5)
            {
                error = "尚未框选采样区：请在节点配置里于图像上右键 → 新建矩形/圆形/椭圆，框住要检的那块";
                return false;
            }

            int channels = (int)image.CountChannels().D;
            if (channels < 3)
            {
                error = $"区域颜色检查要彩色图，当前图像只有 {channels} 个通道";
                return false;
            }

            // ① ROI → 区域 → 抽样点（内核负责；形状由区域的掩膜保证）
            if (!ColorSampler.TryBuildRoiPoints(image, RoiShape, RoiParams, SamplePoints,
                    out double[] rows, out double[] cols, out error))
                return false;

            // ② 给点取色（内核负责）
            if (!ColorSampler.TryReadPixels(image, rows, cols, out int[][] pixelChannels, out error))
                return false;

            // ③ 逐点分类 → 投票 → 主色与占比（判定不在这里：试算也要能只报颜色）
            if (!RegionColorAnalyzer.Analyze(pixelChannels, BuildThresholds(), out var analyzed, out error))
                return false;

            result = analyzed;
            return true;
        }

        /// <summary>期望颜色：逗号/顿号分隔，去空白</summary>
        private string[] ParseExpected()
            => string.IsNullOrWhiteSpace(ExpectedColors)
                ? Array.Empty<string>()
                : ExpectedColors.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Where(s => s.Length > 0)
                                .ToArray();

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

        /// <summary>
        /// 把结果投到主界面视图窗口：采样区轮廓（圆/椭圆按折线画，不然看不出采的哪一块）、
        /// 主色标签、结论行。颜色约定与「颜色序列检查」完全一致（内核 ColorMarks）：
        /// 合格绿 / 不合格红。
        ///
        /// 推图属于"锦上添花"：投递失败绝不能把一次正常的检测判成失败。
        /// </summary>
        private void ProjectResult(HImage image, RegionColorResult result)
        {
            if (DisplayViewIndex <= 0) return;

            try
            {
                image.GetImageSize(out _, out int height);
                var marks = new List<MeasureAnnotation>();

                string verdictColor = ColorMarks.For(result.Passed);
                var outline = OutlineOf(RoiShape, RoiParams);
                for (int i = 1; i < outline.Count; i++)
                {
                    marks.Add(ColorMarks.Line(
                        outline[i - 1].Row, outline[i - 1].Col,
                        outline[i].Row, outline[i].Col,
                        verdictColor));
                }

                marks.Add(ColorMarks.Text(RoiParams[0], RoiParams[1] + 8,
                    $"{result.Dominant} {result.Share * 100:0}%", verdictColor));

                // 结论行：优先贴采样区上方；贴不下改贴下方；再越界就夹回画面内
                double top = outline.Count > 0 ? outline.Min(p => p.Row) : RoiParams[0];
                double bottom = outline.Count > 0 ? outline.Max(p => p.Row) : RoiParams[0];
                double above = top - 18;
                double textRow = Math.Clamp(above >= 10 ? above : bottom + 18, 10, Math.Max(10, height - 10));
                marks.Add(ColorMarks.Text(textRow, RoiParams[1], result.Verdict, verdictColor));

                this.PublishPreview(image, DisplayViewIndex, marks);
            }
            catch
            {
                // 投递失败只是画面上没有标注，不影响判定结果
            }
        }
    }
}
