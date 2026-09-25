using Core.Commands;
using Core.Interfaces;
using HalconDotNet;
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Threading;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// Blob 缺陷检测插件（流程节点 + 配置界面 ViewModel 合一，与仓库既有插件同一套写法）。
    ///
    /// 它做的事：把输入图像转灰度 → 按所选方式二值化成"可疑区域" → 形态学去毛刺/连断桥
    /// → 拆连通域 → 按面积下限丢噪点 → 统计个数与最大面积 → 与规格比对给出 OK/NG
    /// → 叠一张"原图 + 缺陷红圈 + 判定文字"的标注图。
    ///
    /// 角色说明（务必看清）：
    ///  · 作为流程节点：只干 RunAlgorithm 一件事——跑算法、给 7 个输出端口赋值；
    ///  · 作为配置界面 ViewModel：承载参数、预览图、状态栏，负责"改参数即时看到标注图"。
    /// 主程序会给"配置"和"编译执行"各造一个实例，两者共用一份代码不会有状态串扰。
    /// </summary>
    [Display(
        Name = "Blob 缺陷检测",
        GroupName = "缺陷检测",
        Description = "阈值分割 + 连通域分析，检出划痕/暗斑等缺陷并按个数与单缺陷面积判定 OK/NG；支持固定阈值、自动阈值(max_separability)、动态阈值(var_threshold)",
        ShortName = "\uf002"
    )]
    public class BlobDetectPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 输入 / 输出端口（名字即连线名，编译期就存在，供按名连线）

        /// <summary>待检测的输入图像（可链接上游；未连线时按既有插件的惯例给中文提示后判失败）</summary>
        public InputPort<HImage> SrcImage { get; } = new("SrcImage", description: "待检测的输入图像");

        /// <summary>标注图：原图上把缺陷圈红 + 左上角写判定结果与个数</summary>
        public OutputPort<HImage> DefectImage { get; } = new("DefectImage", "标注图（原图 + 缺陷红圈 + 判定文字）");

        /// <summary>缺陷区域对象集合（可直接给下游做进一步分析）</summary>
        public OutputPort<HRegion> Defects { get; } = new("Defects", "缺陷区域对象集合");

        /// <summary>缺陷个数</summary>
        public OutputPort<int> DefectCount { get; } = new("DefectCount", "缺陷个数");

        /// <summary>最大单缺陷面积（像素）</summary>
        public OutputPort<double> MaxArea { get; } = new("MaxArea", "最大单缺陷面积（像素）");

        /// <summary>判定结果（true = OK 合格）</summary>
        public OutputPort<bool> IsOk { get; } = new("IsOk", "判定结果（true = OK 合格）");

        /// <summary>各缺陷中心行坐标数组（顺序由「输出排序」参数决定，与 CenterCols / DefectAreas 逐项对齐）</summary>
        public OutputPort<HTuple> CenterRows { get; } = new("CenterRows", "各缺陷中心行坐标数组");

        /// <summary>各缺陷中心列坐标数组（顺序同上）</summary>
        public OutputPort<HTuple> CenterCols { get; } = new("CenterCols", "各缺陷中心列坐标数组");

        // ── 以下为功能补全新增端口（只增不改：既有 7 个端口的名字/类型/顺序一律不动，老方案连线不受影响）──

        /// <summary>各缺陷面积数组（像素，顺序同上）。只有"最大面积"时无法做分布统计/分级，故补齐逐项面积</summary>
        public OutputPort<HTuple> DefectAreas { get; } = new("DefectAreas", "各缺陷面积数组（像素）");

        /// <summary>各缺陷外接矩形宽（像素，顺序同上）</summary>
        public OutputPort<HTuple> DefectWidths { get; } = new("DefectWidths", "各缺陷外接矩形宽（像素）");

        /// <summary>各缺陷外接矩形高（像素，顺序同上）</summary>
        public OutputPort<HTuple> DefectHeights { get; } = new("DefectHeights", "各缺陷外接矩形高（像素）");

        /// <summary>缺陷总面积（像素）。"最大单缺陷合格但整片背景脏污"只有总面积能兜住</summary>
        public OutputPort<double> TotalArea { get; } = new("TotalArea", "缺陷总面积（像素）");

        /// <summary>最大单缺陷面积（mm²）= MaxArea(px) × 像素当量²</summary>
        public OutputPort<double> MaxAreaMm2 { get; } = new("MaxAreaMm2", "最大单缺陷面积（mm²）");

        /// <summary>缺陷总面积（mm²）= TotalArea(px) × 像素当量²</summary>
        public OutputPort<double> TotalAreaMm2 { get; } = new("TotalAreaMm2", "缺陷总面积（mm²）");

        #endregion

        #region 配置参数（[StepConfig] 由基类随 InputValues 一并存盘/灌值）

        #region 出厂默认值（唯一真相）

        // 为什么要把默认值抽成常量、并让字段初值引用它们：
        // "按当前图像自适应"只在参数仍等于出厂默认值时才允许自动触发。判定"是否等于默认值"
        // 必须有一处权威基准，否则默认值一改（字段初值与判断基准各写一份）就会悄悄失配，
        // 导致"用户没改过却判成改过"或反之。这里集中一处，字段初值也只认这一处。
        private const ThresholdMode DefaultThresholdMode = ThresholdMode.Fixed;
        private const DetectTarget DefaultDetectTarget = DetectTarget.Dark;
        private const double DefaultMinGray = 0;
        private const double DefaultMaxGray = 128;   // 8 位灰度图中位；见 MaxGray 注释里的位深说明
        private const int DefaultVarMaskWidth = 15;
        private const int DefaultVarMaskHeight = 15;
        private const double DefaultVarStdDevScale = 0.4;
        private const double DefaultVarAbsThreshold = 2;
        private const double DefaultMinArea = 30;
        private const double DefaultOpenRadius = 0;
        private const double DefaultCloseRadius = 0;
        private const int DefaultMaxDefectCount = 0;
        private const double DefaultMaxSingleArea = 100;

        // ── 功能补全新增参数的出厂默认值（一律取"不生效/不改变既有行为"，保证老方案加载后结果不变）──
        private const double DefaultMaxBlobArea = 0;      // 0 = 不限（上限自动取图像像素总数）
        private const double DefaultMinCircularity = 0;   // 0 = 不按圆度筛
        private const double DefaultMaxAspectRatio = 0;   // 0 = 不按长宽比筛
        private const int DefaultExcludeBorderPx = 0;     // 0 = 不排除触边
        private const bool DefaultFillHoles = false;      // 默认不填孔（保持既有语义）
        private const double DefaultMergeRadius = 0;      // 0 = 不合并相邻缺陷
        private const int DefaultMinDefectCount = 0;      // 0 = 不做存在性判定
        private const double DefaultMaxTotalArea = 0;     // 0 = 不限总面积
        private const double DefaultPixelSizeMm = 1.0;    // 1.0 = 输出即像素值，不假设标定（与卡尺插件同口径）
        private const DefectSortMode DefaultSortMode = DefectSortMode.None;

        /// <summary>浮点相等判断的容差（自适应换算会产生小数，不能用 == 比）</summary>
        private const double DefaultTolerance = 1e-6;

        /// <summary>
        /// 阈值自适应分位：暗缺陷取灰度范围靠暗侧的这一段比例，亮缺陷对称地取靠亮侧的一段。
        ///
        /// 依据：缺陷（划痕/脏污/亮点）的灰度通常落在直方图两端极值区，而背景占绝大多数。
        /// 取最暗（最亮）的 25% 作为保守带——既覆盖极值尾部，又不会把中位附近的背景整体吞进来。
        /// 这是"可用起点"而非"正确值"：现场仍需按预览微调（这正是直方图存在的意义）。
        /// </summary>
        private const double DarkSideFraction = 0.25;
        private const double BrightSideFraction = 0.25;

        /// <summary>
        /// 面积自适应的基准分辨率。默认面积（30 / 100 像素）是按 640×480 这一常见小画幅给的，
        /// 换到大画幅必须按面积比例放大，否则同一物理缺陷在大图上会被"缩成噪点"或被误判。
        /// </summary>
        private const double BaselineWidth = 640;
        private const double BaselineHeight = 480;

        #endregion

        #region ① 二值化

        private ThresholdMode _thresholdMode = DefaultThresholdMode;
        /// <summary>二值化方式（切换时界面只显示该方式自己的参数）</summary>
        [StepConfig]
        public ThresholdMode ThresholdMode
        {
            get => _thresholdMode;
            set
            {
                if (!SetProperty(ref _thresholdMode, value)) return;
                // 三个分区 + "检测目标"的显隐都跟着方式走，一次性把相关通知补全
                OnPropertyChanged(nameof(IsFixedThreshold));
                OnPropertyChanged(nameof(IsAutoThreshold));
                OnPropertyChanged(nameof(IsDynamicThreshold));
                OnPropertyChanged(nameof(ShowDetectTarget));
                SchedulePreview();
            }
        }

        private DetectTarget _detectTarget = DefaultDetectTarget;
        /// <summary>检测目标：亮缺陷 / 暗缺陷（统一映射到各方式的极性参数）</summary>
        [StepConfig]
        public DetectTarget DetectTarget
        {
            get => _detectTarget;
            set
            {
                var old = _detectTarget;
                if (!SetProperty(ref _detectTarget, value)) return;

                // 明暗极性一变，之前按"最暗 25%"算出的阈值区间立刻失效（会停在暗侧、与意图相反）。
                // 但"是否出厂默认值"的守卫会因为 DetectTarget 变了而跳过自动适配，所以这里记一笔补做。
                // 前提是阈值仍是上次适配出来的值——用户手工调过就不动他的。
                if (old != value && _hasAdapted && IsThresholdAtLastAdaptedValue())
                    _pendingAdaptOnTargetChange = true;

                SchedulePreview();
            }
        }

        /// <summary>阈值是否仍停留在上次适配出来的那组值（用于判断"用户有没有手调过"）</summary>
        private bool IsThresholdAtLastAdaptedValue()
            => NearlyEqual(MinGray, _lastAdaptedMinGray) && NearlyEqual(MaxGray, _lastAdaptedMaxGray);

        private double _minGray = DefaultMinGray;
        /// <summary>固定阈值：下限灰度</summary>
        [StepConfig]
        public double MinGray
        {
            get => _minGray;
            set { if (SetProperty(ref _minGray, value)) SchedulePreview(); }
        }

        private double _maxGray = DefaultMaxGray;
        /// <summary>
        /// 固定阈值：上限灰度。
        ///
        /// 默认 128 = 8 位灰度图的中位，是一个"不针对任何特定产品"的通用起点 ——
        /// 本插件是通用工业视觉平台的算子，不该假装知道操作员的图该切在哪，调参靠右侧实时预览。
        ///（早期版本用的 140 是按仓库自带划痕样图 102~206 调出来的，属"拿验证样图当调参目标"，已改掉。）
        ///
        /// 注意这是绝对灰度值、隐含 8 位图假设：图像若是 uint2（12/16 位，0~4095），
        /// 128 会落在极暗处，需按位深换算（12 位图约取 2048）。
        /// </summary>
        [StepConfig]
        public double MaxGray
        {
            get => _maxGray;
            set { if (SetProperty(ref _maxGray, value)) SchedulePreview(); }
        }

        private int _varMaskWidth = DefaultVarMaskWidth;
        /// <summary>动态阈值：局部窗口宽（像素）</summary>
        [StepConfig]
        public int VarMaskWidth
        {
            get => _varMaskWidth;
            set { if (SetProperty(ref _varMaskWidth, value)) SchedulePreview(); }
        }

        private int _varMaskHeight = DefaultVarMaskHeight;
        /// <summary>动态阈值：局部窗口高（像素）</summary>
        [StepConfig]
        public int VarMaskHeight
        {
            get => _varMaskHeight;
            set { if (SetProperty(ref _varMaskHeight, value)) SchedulePreview(); }
        }

        private double _varStdDevScale = DefaultVarStdDevScale;
        /// <summary>动态阈值：标准差权重</summary>
        [StepConfig]
        public double VarStdDevScale
        {
            get => _varStdDevScale;
            set { if (SetProperty(ref _varStdDevScale, value)) SchedulePreview(); }
        }

        private double _varAbsThreshold = DefaultVarAbsThreshold;
        /// <summary>动态阈值：绝对灰度偏移量</summary>
        [StepConfig]
        public double VarAbsThreshold
        {
            get => _varAbsThreshold;
            set { if (SetProperty(ref _varAbsThreshold, value)) SchedulePreview(); }
        }

        #endregion

        #region ② 特征筛选（噪声清理 + 形状 + 触边）

        private double _minArea = DefaultMinArea;
        /// <summary>面积下限：小于它的连通域当噪点丢弃</summary>
        [StepConfig]
        public double MinArea
        {
            get => _minArea;
            set { if (SetProperty(ref _minArea, value)) SchedulePreview(); }
        }

        private double _maxBlobArea = DefaultMaxBlobArea;
        /// <summary>
        /// 单缺陷面积上限（筛选用，0 = 不限，此时上限自动取"图像像素总数"）。
        ///
        /// 为什么要把它与判定用的 MaxSingleArea 分开：一个是"这个东西算不算缺陷"（筛选，超了直接不计数），
        /// 一个是"这个缺陷合不合格"（判定，超了判 NG）。早期版本只有后者、且把 select_shape 的筛选上限写死 1e7，
        /// 结果是大画幅（>1000 万像素）上整块背景被 1e7 静默滤掉——既不计数也不 NG，等于漏检还报 OK。
        /// </summary>
        [StepConfig]
        public double MaxBlobArea
        {
            get => _maxBlobArea;
            set { if (SetProperty(ref _maxBlobArea, value)) SchedulePreview(); }
        }

        private double _minCircularity = DefaultMinCircularity;
        /// <summary>
        /// 圆度下限（0 = 不筛）。取值 0~1：1 = 正圆，细长划痕约 0.02~0.1，方块约 0.67。
        /// 用来把"圆形斑点"与"细长划痕"分开——这是现场最常见的两类缺陷区分需求。
        /// </summary>
        [StepConfig]
        public double MinCircularity
        {
            get => _minCircularity;
            set { if (SetProperty(ref _minCircularity, value)) SchedulePreview(); }
        }

        private double _maxAspectRatio = DefaultMaxAspectRatio;
        /// <summary>
        /// 长宽比上限（0 = 不筛）。长宽比 = 外接矩形 长边/短边，越大越细长（正方形 = 1）。
        ///
        /// 为什么不直接用 HALCON 的 'elongation' 特征：实测本仓库 HALCON 版本（23.05）不认识该特征名
        /// （select_shape / region_features 均报 #3101 Unknown feature；仓库脚本模板里教用户写 'elongation' 是错的）。
        /// 这里改用 smallest_rectangle1 自己算，语义与 elongation 一致且在任何版本都成立。
        /// </summary>
        [StepConfig]
        public double MaxAspectRatio
        {
            get => _maxAspectRatio;
            set { if (SetProperty(ref _maxAspectRatio, value)) SchedulePreview(); }
        }

        private int _excludeBorderPx = DefaultExcludeBorderPx;
        /// <summary>
        /// 排除触边缺陷的边界带宽（像素，0 = 不排除）。
        /// 缺陷外接矩形只要碰到图像（或 ROI 外接矩形）最外 N 像素，就当"打光/裁切边缘效应"丢弃。
        ///
        /// 为什么必须有这一项：打光边缘效应是固定的误检源（自带样图上就有不少缺陷落在右缘/底缘），
        /// 而上游 ROI 只能整体裁形状、没法"往里缩一圈"，所以只能在本插件里按"是否触边"过滤。
        /// </summary>
        [StepConfig]
        public int ExcludeBorderPx
        {
            get => _excludeBorderPx;
            set { if (SetProperty(ref _excludeBorderPx, value)) SchedulePreview(); }
        }

        #endregion

        #region ③ 形态学

        private double _openRadius = DefaultOpenRadius;
        /// <summary>开运算半径（0 = 不做开运算）</summary>
        [StepConfig]
        public double OpenRadius
        {
            get => _openRadius;
            set { if (SetProperty(ref _openRadius, value)) SchedulePreview(); }
        }

        private double _closeRadius = DefaultCloseRadius;
        /// <summary>闭运算半径（0 = 不做闭运算）</summary>
        [StepConfig]
        public double CloseRadius
        {
            get => _closeRadius;
            set { if (SetProperty(ref _closeRadius, value)) SchedulePreview(); }
        }

        private bool _fillHoles = DefaultFillHoles;
        /// <summary>
        /// 是否填孔（fill_up）。缺陷内部若有亮斑/反光会把一个缺陷"挖成环形"：
        /// 面积偏小、甚至被拆成多个区域。勾选后按外轮廓计算面积。
        /// </summary>
        [StepConfig]
        public bool FillHoles
        {
            get => _fillHoles;
            set { if (SetProperty(ref _fillHoles, value)) SchedulePreview(); }
        }

        private double _mergeRadius = DefaultMergeRadius;
        /// <summary>
        /// 相邻缺陷的合并半径（像素，0 = 不合并）。把间距小于 2×半径 的几段连成一个缺陷。
        ///
        /// 用在哪：一条划痕常常被打光/噪声断成三四段，个数虚高好几倍，判定必然 NG。
        /// 做法：union1（先合成一个区域集）→ closing_circle → connection。
        /// 注意必须先 union1：直接对区域数组做形态学，HALCON 是逐对象处理、合不到一起。
        /// </summary>
        [StepConfig]
        public double MergeRadius
        {
            get => _mergeRadius;
            set { if (SetProperty(ref _mergeRadius, value)) SchedulePreview(); }
        }

        #endregion

        #region ④ 判定规格

        private int _maxDefectCount = DefaultMaxDefectCount;
        /// <summary>缺陷个数上限（超过即 NG）</summary>
        [StepConfig]
        public int MaxDefectCount
        {
            get => _maxDefectCount;
            set { if (SetProperty(ref _maxDefectCount, value)) SchedulePreview(); }
        }

        private int _minDefectCount = DefaultMinDefectCount;
        /// <summary>
        /// 缺陷个数下限（少于即 NG，0 = 不做这项判定）。
        ///
        /// 用在哪：Blob 在产线上大量用于"存在性检测"——这个特征/这个标记有没有。
        /// 此时"一个都没检到"恰恰是坏消息，而只靠上限的话 0 个会判 OK。
        /// </summary>
        [StepConfig]
        public int MinDefectCount
        {
            get => _minDefectCount;
            set { if (SetProperty(ref _minDefectCount, value)) SchedulePreview(); }
        }

        private double _maxSingleArea = DefaultMaxSingleArea;
        /// <summary>单个缺陷面积上限（超过即 NG）</summary>
        [StepConfig]
        public double MaxSingleArea
        {
            get => _maxSingleArea;
            set { if (SetProperty(ref _maxSingleArea, value)) SchedulePreview(); }
        }

        private double _maxTotalArea = DefaultMaxTotalArea;
        /// <summary>
        /// 缺陷总面积上限（超过即 NG，0 = 不做这项判定）。
        /// 用在哪：每个缺陷单独看都合格、但整片脏污/麻点密布——只有总面积能兜住这种情况。
        /// </summary>
        [StepConfig]
        public double MaxTotalArea
        {
            get => _maxTotalArea;
            set { if (SetProperty(ref _maxTotalArea, value)) SchedulePreview(); }
        }

        #endregion

        #region ⑤ 输出

        private double _pixelSizeMm = DefaultPixelSizeMm;
        /// <summary>
        /// 像素当量（mm/px）。默认 1.0 = 输出的 mm² 数值等于像素值，不假设任何标定；
        /// 现场做完标定后填真实值，MaxAreaMm2 / TotalAreaMm2 即为物理量。
        /// 与仓库 Plugin.CaliperMeasure 的 PixelSizeMm 同一口径（那边注释写明"项目暂无标定模块"）。
        /// 注意：判定仍按像素做，像素当量只影响 mm² 输出，避免"改了当量就改判定结果"这种隐式耦合。
        /// </summary>
        [StepConfig]
        public double PixelSizeMm
        {
            get => _pixelSizeMm;
            set { if (SetProperty(ref _pixelSizeMm, value)) SchedulePreview(); }
        }

        private DefectSortMode _sortMode = DefaultSortMode;
        /// <summary>
        /// 输出排序。CenterRows / CenterCols / DefectAreas / DefectWidths / DefectHeights 与 Defects 端口
        /// 一律按此顺序逐项对齐，下游按同一个索引取即可对上号。
        /// </summary>
        [StepConfig]
        public DefectSortMode SortMode
        {
            get => _sortMode;
            set { if (SetProperty(ref _sortMode, value)) SchedulePreview(); }
        }

        #endregion

        #endregion

        #region 视图辅助属性（下拉数据源 / 分区显隐）

        /// <summary>二值化方式下拉数据源</summary>
        public ThresholdMode[] ThresholdModes { get; } = (ThresholdMode[])Enum.GetValues(typeof(ThresholdMode));

        /// <summary>检测目标下拉数据源</summary>
        public DetectTarget[] DetectTargets { get; } = (DetectTarget[])Enum.GetValues(typeof(DetectTarget));

        /// <summary>输出排序下拉数据源</summary>
        public DefectSortMode[] SortModes { get; } = (DefectSortMode[])Enum.GetValues(typeof(DefectSortMode));

        public bool IsFixedThreshold => ThresholdMode == ThresholdMode.Fixed;
        public bool IsAutoThreshold => ThresholdMode == ThresholdMode.Auto;
        public bool IsDynamicThreshold => ThresholdMode == ThresholdMode.Dynamic;

        /// <summary>
        /// 是否显示"检测目标"。
        /// 固定阈值下灰度区间本身已经把极性表达清楚（区间取哪一段就是找什么），
        /// 再摆一个不生效的开关只会让人困惑，所以这一项固定阈值时隐藏。
        /// </summary>
        public bool ShowDetectTarget => ThresholdMode != ThresholdMode.Fixed;

        /// <summary>
        /// 「按当前图像重新适配」命令。
        ///
        /// 与"自动适配"的区别：自动适配只在参数仍是出厂默认值时才动（绝不覆盖用户调过的值），
        /// 而这个是用户主动按下的，任何时候都能重来一次——用户改乱了参数想回到"按这张图算出的起点"时用。
        /// </summary>
        public RelayCommand AdaptToImageCommand { get; }

        #endregion

        #region 预览（配置态：调参即时刷新标注图，做法照搬既有 PreProcessing 插件）

        private readonly AnnotationRenderer _renderer = new();

        private HImage? _previewImage;
        /// <summary>
        /// 预览图（标注图）。换图即弃旧：SetProperty 成功后释放旧实例，避免非托管内存堆积；
        /// 顺序是"先切绑定、后释放旧图"，确保 UI 已经不看旧图了再回收。
        /// </summary>
        public HImage? PreviewImage
        {
            get => _previewImage;
            set
            {
                var old = _previewImage;
                if (ReferenceEquals(old, value)) return;
                if (SetProperty(ref _previewImage, value))
                    old?.Dispose();
            }
        }

        private string _statusMessage = string.Empty;
        /// <summary>信息栏文字（请走 SetStatus 写入：文字与级别必须成对更新）</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;
        /// <summary>信息栏级别：决定文字颜色，由视图 DataTrigger 消费</summary>
        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        /// <summary>信息栏唯一写入口：文字和级别成对更新，避免"失败变红后成功仍停红"</summary>
        private void SetStatus(string message, StatusLevel level = StatusLevel.Info)
        {
            StatusMessage = message;
            StatusLevel = level;
        }

        #region 灰度直方图（配置态展示：让"阈值该切在哪"有据可依，而不是靠猜）

        private GrayHistogram? _histogram;
        /// <summary>当前预览图像的灰度直方图（null = 无数据，控件画占位提示）</summary>
        public GrayHistogram? Histogram
        {
            get => _histogram;
            private set => SetProperty(ref _histogram, value);
        }

        private string _histogramRangeText = string.Empty;
        /// <summary>直方图旁第一行文字：灰度范围（min~max）</summary>
        public string HistogramRangeText
        {
            get => _histogramRangeText;
            private set => SetProperty(ref _histogramRangeText, value);
        }

        private string _histogramTypeText = string.Empty;
        /// <summary>直方图旁第二行文字：图像类型/位深（如 byte（8 位））</summary>
        public string HistogramTypeText
        {
            get => _histogramTypeText;
            private set => SetProperty(ref _histogramTypeText, value);
        }

        private string _histogramError = string.Empty;
        /// <summary>直方图不可用时的原因（空 = 正常）。界面据此显示提示，信息栏也会带上</summary>
        public string HistogramError
        {
            get => _histogramError;
            private set => SetProperty(ref _histogramError, value);
        }

        private bool _histogramVisible;
        /// <summary>是否显示直方图（有数据才显示）</summary>
        public bool HistogramVisible
        {
            get => _histogramVisible;
            private set => SetProperty(ref _histogramVisible, value);
        }

        private bool _histogramErrorVisible;
        /// <summary>是否显示"直方图不可用"提示（无数据且有原因时显示）</summary>
        public bool HistogramErrorVisible
        {
            get => _histogramErrorVisible;
            private set => SetProperty(ref _histogramErrorVisible, value);
        }

        /// <summary>
        /// 本次刷新中"自适应发生过"的说明文字，一次性交给信息栏显示。
        /// 为什么用字段暂存而不是直接 SetStatus：自动适配发生在 RefreshPreview 内部（状态还没算完），
        /// 手动适配发生在按钮点击时（真正的检测状态要等 200ms 后的防抖刷新才算得出来）。
        /// 两种情况都把它并进那一次信息栏输出，用户才看得到"软件到底做了什么"。
        /// </summary>
        private string? _adaptNote;

        /// <summary>自适应写参数期间置 true：屏蔽这些赋值触发的预览排队（避免多算一遍全图）</summary>
        private bool _suppressPreviewSchedule;

        /// <summary>
        /// 待执行的"按检测目标重新适配"。
        ///
        /// 为什么需要它：DetectTarget 从"暗缺陷"改到"亮缺陷"时，阈值区间应该从"最暗那段"翻到"最亮那段"。
        /// 但"是否出厂默认值"的守卫把 DetectTarget 也算进去了——一改就判成"用户调过参数"，于是永远不再自动适配，
        /// 结果阈值停在暗侧、与用户意图正好相反。这里在切换目标时记一笔，下次刷新时补做一次适配。
        /// 前提是阈值仍等于上次适配出来的值（用户若手工改过阈值，就不动他的）。
        /// </summary>
        private bool _pendingAdaptOnTargetChange;

        /// <summary>上次适配出的阈值区间（用于判断"用户是否还停留在适配值上"）</summary>
        private double _lastAdaptedMinGray;
        private double _lastAdaptedMaxGray;
        private bool _hasAdapted;

        #endregion

        /// <summary>
        /// 预览防抖定时器（惰性创建）。
        ///
        /// 为什么不能在字段初始化器里直接 new：DispatcherTimer 会归属"创建它的那个线程"的 Dispatcher。
        /// 后台线程（如 HTTP 收图链路在线程池上编译流程，见 VisionMaster/Services/HttpImageServer.cs）
        /// 上 new 出来的定时器寄生在一个永不泵消息的 Dispatcher 上，Tick 永远不触发——
        /// 表现为"预览静默失效"，且不留日志、不报错、无法归因。故改为首次使用时显式绑定到 UI 线程 Dispatcher。
        /// </summary>
        private DispatcherTimer? _previewDebounce;

        /// <summary>
        /// 是否为"配置态实例"（宿主为打开配置界面而创建的那个）。运行实例一律 false。
        ///
        /// 为什么必须区分：预览与"按图像自适应"都是给人调参用的配置态能力。放在运行实例上会——
        /// ① 产线每帧在 UI 线程多跑一遍全图算法 + 直方图 + 离屏渲染；
        /// ② 运行实例按"首帧图像"静默改写 MinGray/MaxGray/MinArea/MaxSingleArea，改完就不再变，
        ///    现场表现为"参数自己变了"。工业软件里这比"参数不理想"严重得多。
        /// </summary>
        private bool _isConfigInstance;

        public BlobDetectPlugin()
        {
            AdaptToImageCommand = new RelayCommand(_ => AdaptToCurrentImage());
        }

        /// <summary>视图就绪信号（视图 Loaded 时调用）：切到配置态并取输入图做预览底图</summary>
        public void OnViewLoaded()
        {
            MarkAsConfigInstance();
            RefreshPreview();
        }

        /// <summary>
        /// 标记为配置态实例（幂等）。只有配置态才订阅输入图变化——
        /// 运行实例不该被"上游每帧新图"唤醒去做预览。
        /// </summary>
        private void MarkAsConfigInstance()
        {
            if (_isConfigInstance) return;
            _isConfigInstance = true;
            // 输入图像变了（换图/接上上游变量）也刷新预览，否则选了图还得再动一下参数才看得到
            SrcImage.ValueChanged += OnSrcImageValueChanged;
        }

        private void OnSrcImageValueChanged(object? sender, EventArgs e) => SchedulePreview();

        private void SchedulePreview()
        {
            // 运行实例不跑预览（理由见 _isConfigInstance 的注释）
            if (!_isConfigInstance) return;

            // 自适应正在写参数时不再排队：否则"改 4 个参数"会额外触发一次完整的全图重算
            if (_suppressPreviewSchedule) return;

            var dispatcher = UiDispatcher;
            // 没有 Application（离线跑流程 / 单元测试）：就地同步跑一次，不排队
            if (dispatcher == null)
            {
                RefreshPreview();
                return;
            }

            _previewDebounce ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(200),          // 参数逐字符刷新，全图算法不便宜，200ms 防抖
                DispatcherPriority.Normal,
                OnPreviewTick,
                dispatcher);                              // 显式绑 UI 线程，杜绝"寄生在后台线程"

            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private void OnPreviewTick(object? sender, EventArgs e)
        {
            _previewDebounce?.Stop();
            RefreshPreview();
        }

        /// <summary>UI 线程调度器；没有 Application（单元测试/离线跑流程）时为 null，此时按"就在 UI 线程"处理</summary>
        private static Dispatcher? UiDispatcher => System.Windows.Application.Current?.Dispatcher;

        /// <summary>
        /// 重算预览：UI 线程同步执行，因此不存在"图被流程线程释放掉"的竞态。
        /// 顺序：自动适配（仅默认值时）→ 直方图 → 检测 → 信息栏。
        /// </summary>
        public void RefreshPreview()
        {
            // 运行实例不跑预览（配置态能力的隔离，理由见 _isConfigInstance 注释）
            if (!_isConfigInstance) return;

            var src = SrcImage.ActualValue;

            if (src == null || !src.IsInitialized())
            {
                PreviewImage = null;
                ClearHistogram();
                SetStatusWithAdaptNote("输入图像为空：请为上方“输入图像”指定图片，或连接上游图像端口", StatusLevel.Warning);
                return;
            }

            // 自动适配：仅当参数仍等于出厂默认值时才执行。
            // 这是工业软件的硬要求——用户调过的值一旦被静默改掉，会被当成软件故障；
            // 所以判据就是"全部参数 == 出厂默认值"，只要有一项被改过就完全跳过、一个字节都不动。
            if (AreAllParametersAtFactoryDefault() && TryAdaptCore(src, out string adaptNote))
                _adaptNote = adaptNote;

            // 切换"检测目标"后的补适配：仅在阈值仍停留在上次适配值时才做，绝不覆盖用户手调的阈值
            if (_pendingAdaptOnTargetChange)
            {
                _pendingAdaptOnTargetChange = false;
                if (IsThresholdAtLastAdaptedValue() && TryAdaptCore(src, out string reAdaptNote))
                    _adaptNote = reAdaptNote;
            }

            // 直方图：跟随本次预览刷新计算（不另起一套），失败只降级、不影响下面的检测
            ComputeHistogram(src);

            BlobResult result;
            try
            {
                result = ExecuteBlobCore(src, null);
            }
            catch (Exception ex)
            {
                // 预览不弹框、不抛：把原因写在信息栏，用户改参数重试即可。
                // 旧图必须清掉——否则信息栏报红、右边却摆着上一张看着正常的标注图，用户会以为报错是假的
                PreviewImage = null;
                SetStatusWithAdaptNote($"预览失败：{ex.Message}", StatusLevel.Error);
                return;
            }

            if (result.Failed)
            {
                result.AnnotatedImage?.Dispose();
                result.Defects?.Dispose();
                PreviewImage = null;   // 同上：失败就别留旧图误导
                SetStatusWithAdaptNote(result.FailMessage, StatusLevel.Error);
                return;
            }

            // 预览只关心"看着对不对"，缺陷区域对象没处放，用完即弃
            result.Defects?.Dispose();
            result.Defects = null;

            PreviewImage = result.AnnotatedImage;   // setter 负责释放上一张预览图

            var message = result.IsOk
                ? $"OK：缺陷数 {result.DefectCount}，最大缺陷面积 {result.MaxArea:0.#} px（在规格内）"
                : $"NG：{result.NgReason}";
            if (HistogramErrorVisible)
                message += $"（{HistogramError}）";

            SetStatusWithAdaptNote(message, result.IsOk ? StatusLevel.Info : StatusLevel.Error);
        }

        /// <summary>信息栏写入口（带自适应说明）：把本次"适配过"的说明并进这一条，显示一次后清空</summary>
        private void SetStatusWithAdaptNote(string message, StatusLevel level)
        {
            if (!string.IsNullOrEmpty(_adaptNote))
            {
                message = _adaptNote + "\n" + message;
                _adaptNote = null;
            }
            SetStatus(message, level);
        }

        /// <summary>清空直方图相关状态（无图或计算失败时用）</summary>
        private void ClearHistogram(string? error = null)
        {
            Histogram = null;
            HistogramRangeText = string.Empty;
            HistogramTypeText = string.Empty;
            HistogramError = error ?? string.Empty;
            HistogramVisible = false;
            HistogramErrorVisible = !string.IsNullOrEmpty(error);
        }

        /// <summary>
        /// 计算并发布直方图。任何失败都只降级成"不显示直方图 + 记原因"，绝不抛到界面外。
        /// </summary>
        private void ComputeHistogram(HImage src)
        {
            var temp = new List<HObject>();
            try
            {
                if (!TryToGrayImage(src, temp, out HObject gray, out string grayError))
                {
                    ClearHistogram($"无法计算直方图：{grayError}");
                    return;
                }

                if (!GrayHistogram.TryCompute(gray, out var histogram, out string error) || histogram == null)
                {
                    ClearHistogram($"无法计算直方图：{error}");
                    return;
                }

                Histogram = histogram;
                HistogramRangeText = $"灰度范围：{histogram.RangeText}";
                HistogramTypeText = $"图像类型：{histogram.TypeText}";
                HistogramError = string.Empty;
                HistogramVisible = true;
                HistogramErrorVisible = false;
            }
            catch (Exception ex)
            {
                ClearHistogram($"无法计算直方图：{ex.Message}");
            }
            finally
            {
                foreach (var o in temp)
                {
                    try { o.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
                }
            }
        }

        #region 按当前图像自适应（阈值 + 面积）

        /// <summary>
        /// 手动适配入口（按钮）。任何时候都能执行，不检查"是否默认值"。
        /// </summary>
        private void AdaptToCurrentImage()
        {
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                SetStatus("没有可用的输入图像，无法适配：请先指定图片或连接上游图像端口", StatusLevel.Warning);
                return;
            }

            if (!TryAdaptCore(src, out string note))
            {
                SetStatus($"适配失败：{note}", StatusLevel.Error);
                return;
            }

            _adaptNote = note;
            SchedulePreview();   // 走既有防抖刷新，让信息栏连同新的检测结果一起显示
        }

        /// <summary>
        /// 判断是否所有参数都还是出厂默认值。用于"自动适配只在默认值时触发"这条硬规则。
        /// 浮点用容差比较：自适应换算会产生小数，用 == 比会永远判成"改过"。
        /// </summary>
        private bool AreAllParametersAtFactoryDefault()
        {
            return ThresholdMode == DefaultThresholdMode
                && DetectTarget == DefaultDetectTarget
                && NearlyEqual(MinGray, DefaultMinGray)
                && NearlyEqual(MaxGray, DefaultMaxGray)
                && VarMaskWidth == DefaultVarMaskWidth
                && VarMaskHeight == DefaultVarMaskHeight
                && NearlyEqual(VarStdDevScale, DefaultVarStdDevScale)
                && NearlyEqual(VarAbsThreshold, DefaultVarAbsThreshold)
                && NearlyEqual(MinArea, DefaultMinArea)
                && NearlyEqual(MaxBlobArea, DefaultMaxBlobArea)
                && NearlyEqual(MinCircularity, DefaultMinCircularity)
                && NearlyEqual(MaxAspectRatio, DefaultMaxAspectRatio)
                && ExcludeBorderPx == DefaultExcludeBorderPx
                && FillHoles == DefaultFillHoles
                && NearlyEqual(OpenRadius, DefaultOpenRadius)
                && NearlyEqual(CloseRadius, DefaultCloseRadius)
                && NearlyEqual(MergeRadius, DefaultMergeRadius)
                && MaxDefectCount == DefaultMaxDefectCount
                && MinDefectCount == DefaultMinDefectCount
                && NearlyEqual(MaxSingleArea, DefaultMaxSingleArea)
                && NearlyEqual(MaxTotalArea, DefaultMaxTotalArea)
                && NearlyEqual(PixelSizeMm, DefaultPixelSizeMm)
                && SortMode == DefaultSortMode;
        }

        private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= DefaultTolerance;

        /// <summary>
        /// 按当前图像换算阈值与面积规格。成功返回 true，note 为给用户看的说明；
        /// 失败返回 false，note 为原因。只改 MinGray/MaxGray/MinArea/MaxSingleArea，
        /// 不动二值化方式、检测目标、形态学参数（那些与图像量级无关，改了反而违背用户意图）。
        /// </summary>
        private bool TryAdaptCore(HImage src, out string note)
        {
            note = string.Empty;
            var temp = new List<HObject>();
            try
            {
                if (!TryToGrayImage(src, temp, out HObject gray, out string grayError))
                {
                    note = grayError;
                    return false;
                }

                HOperatorSet.GetImageType(gray, out HTuple typeTuple);
                string imageType = typeTuple.Length > 0 ? typeTuple.S : "?";

                // 用实际灰度范围，而不是按像素类型硬编码：
                // 12 位相机常只用 0~4095，而 uint2 的存储范围是 0~65535，两者差 16 倍。
                HOperatorSet.GetDomain(gray, out HObject domain);
                temp.Add(domain);
                HOperatorSet.MinMaxGray(domain, gray, 0, out HTuple minTuple, out HTuple maxTuple, out HTuple _);
                double grayMin = minTuple.Length > 0 ? minTuple[0].D : 0;
                double grayMax = maxTuple.Length > 0 ? maxTuple[0].D : 0;
                if (double.IsNaN(grayMin) || double.IsNaN(grayMax))
                {
                    note = "灰度范围为无效值（图可能为空）";
                    return false;
                }
                if (grayMax < grayMin) (grayMin, grayMax) = (grayMax, grayMin);

                // ── 阈值换算 ──
                double range = Math.Max(grayMax - grayMin, 1);   // 均匀图防"零段"
                double newMinGray, newMaxGray;
                if (DetectTarget == DetectTarget.Dark)
                {
                    // 暗缺陷：取靠暗侧的一段（最暗的 25%）
                    newMinGray = grayMin;
                    newMaxGray = grayMin + DarkSideFraction * range;
                }
                else
                {
                    // 亮缺陷：对称地取靠亮侧的一段（最亮的 25%）
                    newMinGray = grayMax - BrightSideFraction * range;
                    newMaxGray = grayMax;
                }
                newMinGray = ClampFinite(newMinGray, 0, GrayUpperBound);
                newMaxGray = ClampFinite(newMaxGray, 0, GrayUpperBound);

                // ── 面积换算：以 640×480 为基准，按面积比例缩放，保持"滤噪点/判定规格"语义不变 ──
                HOperatorSet.GetImageSize(gray, out HTuple wTuple, out HTuple hTuple);
                int width = wTuple.I, height = hTuple.I;
                double scale = width * (double)height / (BaselineWidth * BaselineHeight);
                double newMinArea = ClampFinite(DefaultMinArea * scale, 0, AreaClampMax);
                double newMaxArea = ClampFinite(DefaultMaxSingleArea * scale, 0, AreaClampMax);

                // 灰度取整（byte/uint2 都是整数灰度），面积保留 1 位小数便于阅读。
                // 期间屏蔽预览排队：4 个 setter 会各触发一次 SchedulePreview，
                // 不屏蔽就会在 200ms 后白跑一遍全图算法（改完的值和这次预览用的是同一组参数，结果完全一样）。
                _suppressPreviewSchedule = true;
                try
                {
                    MinGray = Math.Round(newMinGray);
                    MaxGray = Math.Round(newMaxGray);
                    MinArea = Math.Round(newMinArea, 1);
                    MaxSingleArea = Math.Round(newMaxArea, 1);
                }
                finally
                {
                    _suppressPreviewSchedule = false;
                }

                // 记下这次适配出的阈值：将来切"检测目标"时靠它判断"用户有没有手调过阈值"
                _lastAdaptedMinGray = MinGray;
                _lastAdaptedMaxGray = MaxGray;
                _hasAdapted = true;

                note = $"已按当前图像（{imageType} / {width}×{height}）适配默认参数："
                     + $"灰度 {MinGray:0.#}~{MaxGray:0.#}，面积下限 {MinArea:0.#}、单缺陷上限 {MaxSingleArea:0.#}";
                return true;
            }
            catch (Exception ex)
            {
                note = ex.Message;
                return false;
            }
            finally
            {
                foreach (var o in temp)
                {
                    try { o.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
                }
            }
        }

        #endregion

        #endregion

        #region 插件生命周期

        public object GetConfigView(IStepConfigData stepData)
        {
            // 走到这里说明宿主是为"打开配置界面"而创建的实例——先盖章再灌值，
            // 这样 ApplyConfigValues 触发的 setter 也能正常排队预览
            MarkAsConfigInstance();
            Initialize(stepData);
            return new BlobDetectView { DataContext = this };
        }

        public override void Dispose()
        {
            if (_previewDebounce != null)
            {
                _previewDebounce.Stop();
                _previewDebounce.Tick -= OnPreviewTick;
                _previewDebounce = null;
            }
            PreviewImage = null;   // setter 释放预览图
            ClearHistogram();      // 直方图是纯托管数据，清引用即可
            _adaptNote = null;
            _renderer.Dispose();   // 关闭离屏渲染窗口
            base.Dispose();        // 输出端口里的 HImage / HRegion 交给基类统一回收
        }

        #endregion

        #region 流程执行

        /// <summary>
        /// 跑算法并给端口赋值。
        /// 契约提醒：进入本方法时基类已把 Success 预置为 true，只有"执行失败"才写 Fail；
        /// "判定为 NG"不算执行失败——它是一个正常结果，只写 ErrorMessage 说明原因。
        /// </summary>
        public override void RunAlgorithm(IExecutionContext context)
        {
            // 开轮重置所有"基类不会回收"的端口：基类的 AutoDisposeRoundOutputs 只处理 IDisposable
            // （HImage/HRegion），int/bool/double/HTuple 端口的上一轮值会原样留下——
            // 本轮若失败，下游会读到上一轮的脏数据（这是极易漏的镜像 bug）
            DefectCount.Value = 0;
            MaxArea.Value = 0;
            TotalArea.Value = 0;
            MaxAreaMm2.Value = 0;
            TotalAreaMm2.Value = 0;
            IsOk.Value = false;
            CenterRows.Value = new HTuple();
            CenterCols.Value = new HTuple();
            DefectAreas.Value = new HTuple();
            DefectWidths.Value = new HTuple();
            DefectHeights.Value = new HTuple();

            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                return;
            }

            BlobResult result;
            try
            {
                result = ExecuteBlobCore(src, context.Logger);
            }
            catch (Exception ex)
            {
                // 带上"哪一步在干嘛"的上下文前缀，比基类兜底的"异常类型: 消息"更好排查
                Fail($"Blob 缺陷检测失败：{ex.Message}");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (result.Failed)
            {
                Fail(result.FailMessage);
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            // 端口赋值：HImage / HRegion 的所有权移交端口，由基类在下一轮开头或 Dispose 时回收。
            // 渲染失败时标注图为 null，这里跳过赋值，端口保持轮首的"空"状态
            if (result.AnnotatedImage != null) DefectImage.TypedValue = result.AnnotatedImage;
            if (result.Defects != null) Defects.TypedValue = result.Defects;
            DefectCount.Value = result.DefectCount;
            MaxArea.Value = result.MaxArea;
            TotalArea.Value = result.TotalArea;
            MaxAreaMm2.Value = result.MaxAreaMm2;
            TotalAreaMm2.Value = result.TotalAreaMm2;
            IsOk.Value = result.IsOk;
            CenterRows.Value = result.CenterRows;
            CenterCols.Value = result.CenterCols;
            DefectAreas.Value = result.Areas;
            DefectWidths.Value = result.Widths;
            DefectHeights.Value = result.Heights;

            // NG 是正常结果（Success 保持 true），但原因必须留痕，便于日志/上报/现场排查
            if (!result.IsOk)
                ErrorMessage.Value = result.NgReason;

            context.Logger?.Info(
                $"{InstanceName} 缺陷数={result.DefectCount} 最大面积={result.MaxArea:0.#}px 总面积={result.TotalArea:0.#}px"
                + $" 判定={(result.IsOk ? "OK" : "NG")}"
                + (result.IsOk ? string.Empty : $"（{result.NgReason}）"));
        }

        #endregion

        #region 算法核心（配置预览与流程运行共用，保证"所见即所得"）

        /// <summary>
        /// 面积类参数的夹取上限。
        ///
        /// 为什么不是早期那个 1e7：1e7 当初被当成 select_shape 的面积上限写死在算法里，
        /// 结果在 >1000 万像素的相机上，整块背景（面积超过 1e7）会被静默滤掉——既不计数也不 NG，
        /// 漏检还报 OK。现在 select_shape 的上限改为"图像像素总数"，常量只用于参数夹取，故放宽。
        /// </summary>
        private const double AreaClampMax = 1e12;

        /// <summary>灰度上限常量：兼容 byte(255) 与 16 位(65535) 图像</summary>
        private const double GrayUpperBound = 65535;

        /// <summary>执行结果（算法核心的输出载体；AnnotatedImage / Defects 的所有权交给调用方）</summary>
        private sealed class BlobResult
        {
            /// <summary>是否为"输入不合法"这类前置失败（区别于异常）</summary>
            public bool Failed;
            public string FailMessage = string.Empty;

            public HImage? AnnotatedImage;
            public HRegion? Defects;
            public int DefectCount;
            public double MaxArea;
            public double TotalArea;
            public double MaxAreaMm2;
            public double TotalAreaMm2;
            public bool IsOk = true;
            public HTuple CenterRows = new();
            public HTuple CenterCols = new();
            public HTuple Areas = new();
            public HTuple Widths = new();
            public HTuple Heights = new();
            public string NgReason = string.Empty;
        }

        /// <summary>归一化后的参数（避免中途被界面改值；也把脏配置夹进算法能安全接受的区间）</summary>
        private sealed class BlobParams
        {
            public ThresholdMode Mode;
            public string Polarity = "light";
            public double MinGray;
            public double MaxGray;
            public int VarMaskWidth;
            public int VarMaskHeight;
            public double VarStdDevScale;
            public double VarAbsThreshold;
            public double MinArea;
            public double MaxBlobArea;
            public double MinCircularity;
            public double MaxAspectRatio;
            public int ExcludeBorderPx;
            public bool FillHoles;
            public double OpenRadius;
            public double CloseRadius;
            public double MergeRadius;
            public int MaxDefectCount;
            public int MinDefectCount;
            public double MaxSingleArea;
            public double MaxTotalArea;
            public double PixelSizeMm;
            public DefectSortMode SortMode;
        }

        /// <summary>
        /// 把输入图转成单通道灰度图，登记进 temp（调用方 finally 统一释放）。
        ///
        /// 为什么必须单独一步：rgb1_to_gray 对单通道图会报错，所以要先 count_channels 判断。
        /// 抽成方法供"算法核心 / 直方图 / 自适应"三处共用——三处必须对"什么算可处理的灰度图"给出同一答案，
        /// 否则会出现"检测能跑、直方图不能算"这类不一致。
        /// </summary>
        private static bool TryToGrayImage(HObject src, List<HObject> temp, out HObject gray, out string error)
        {
            gray = null!;
            error = string.Empty;

            HOperatorSet.CountChannels(src, out HTuple channels);
            if (channels.I == 1)
            {
                HOperatorSet.CopyImage(src, out gray);      // 灰度图复制一份，后续可自由释放
            }
            else if (channels.I == 3)
            {
                HOperatorSet.Rgb1ToGray(src, out gray);
            }
            else
            {
                error = $"不支持的图像通道数：{channels.I}（仅支持 1 通道灰度或 3 通道彩色）";
                return false;
            }

            temp.Add(gray);
            return true;
        }

        /// <summary>
        /// 算法核心：参数进 → 结果出，不含端口 / 界面逻辑。
        /// </summary>
        /// <param name="src">输入图像（不拥有，绝不释放）</param>
        /// <param name="logger">日志通道，可为 null（配置预览路径）</param>
        private BlobResult ExecuteBlobCore(HImage src, ILogService? logger)
        {
            var p = NormalizedParameters();
            var result = new BlobResult();

            // 本方法自建的中间 HALCON 对象统一登记，finally 一次性释放。
            // 只登记"临时对象"；要交出去的 AnnotatedImage / Defects 是另外 new 出来的独立句柄，不受影响
            var temp = new List<HObject>();
            try
            {
                // ── 1. 拿一张可安全处理的灰度图 ──
                if (!TryToGrayImage(src, temp, out HObject gray, out string grayError))
                {
                    result.Failed = true;
                    result.FailMessage = grayError;
                    return result;
                }

                // 图像尺寸：既用于"面积上限取像素总数"，也用于触边判定
                HOperatorSet.GetImageSize(gray, out HTuple sizeW, out HTuple sizeH);
                int imgW = sizeW.Length > 0 ? sizeW[0].I : 0;
                int imgH = sizeH.Length > 0 ? sizeH[0].I : 0;

                // ── 2. 二值化（三种方式各自的极性参数在此统一由 DetectTarget 映射） ──
                HObject region;
                switch (p.Mode)
                {
                    case ThresholdMode.Fixed:
                        // 固定阈值：区间本身表达极性，MinGray/MaxGray 已在归一化时排好序
                        HOperatorSet.Threshold(gray, out region, p.MinGray, p.MaxGray);
                        break;

                    case ThresholdMode.Auto:
                        // 自动阈值：最大类间方差法，自动求分割点；极性决定取亮侧还是暗侧
                        HOperatorSet.BinaryThreshold(gray, out region, "max_separability", p.Polarity, out _);
                        break;

                    default: // Dynamic
                        // 动态阈值：局部窗口统计背景，能吃掉光照不均
                        HOperatorSet.VarThreshold(gray, out region, p.VarMaskWidth, p.VarMaskHeight,
                            p.VarStdDevScale, p.VarAbsThreshold, p.Polarity);
                        break;
                }
                temp.Add(region);

                // ── 3. 形态学（填 0 表示不做该步） ──
                HObject shaped = region;
                if (p.OpenRadius > 0)
                {
                    HOperatorSet.OpeningCircle(shaped, out HObject opened, p.OpenRadius);
                    temp.Add(opened);
                    shaped = opened;
                }
                if (p.CloseRadius > 0)
                {
                    HOperatorSet.ClosingCircle(shaped, out HObject closed, p.CloseRadius);
                    temp.Add(closed);
                    shaped = closed;
                }

                // ── 4. 拆分连通域（连成一片的候选区分成一个个独立缺陷） ──
                HOperatorSet.Connection(shaped, out HObject connected);
                temp.Add(connected);

                // ── 5. 合并相邻缺陷（一条划痕常被噪声断成几段，个数虚高必然 NG） ──
                // 必须先 union1 再闭运算：直接对区域数组做形态学，HALCON 是逐对象处理，合不到一起
                HObject grouped = connected;
                if (p.MergeRadius > 0)
                {
                    HOperatorSet.Union1(connected, out HObject unioned);
                    temp.Add(unioned);
                    HOperatorSet.ClosingCircle(unioned, out HObject merged, p.MergeRadius);
                    temp.Add(merged);
                    HOperatorSet.Connection(merged, out HObject regrouped);
                    temp.Add(regrouped);
                    grouped = regrouped;
                }

                // ── 6. 填孔（缺陷内部反光会把一个缺陷挖成环形：面积偏小、甚至被拆成多个） ──
                HObject solid = grouped;
                if (p.FillHoles)
                {
                    HOperatorSet.FillUp(grouped, out HObject filledUp);
                    temp.Add(filledUp);
                    solid = filledUp;
                }

                // ── 7. 特征筛选（面积 + 可选圆度，一次 select_shape 做完） ──
                // 面积上限取"图像像素总数"而不是常量：写死 1e7 时，大画幅上整块背景会被静默滤掉
                // （既不计数也不 NG = 漏检还报 OK）。用户填了 MaxBlobArea 就以他的为准。
                double areaUpper = p.MaxBlobArea > 0 ? p.MaxBlobArea : Math.Max(imgW * (double)imgH, 1);

                var features = new List<string> { "area" };
                // 下限必须夹到不超过上限：用户把 MinArea 填得比上限还大时，
                // select_shape 会直接抛 #1304（Wrong value of control parameter 4）把整条流程带崩——
                // 按本插件"配置坏了最多结果不对，绝不炸流程"的原则，这里静默夹取
                var featureMins = new List<double> { Math.Min(p.MinArea, areaUpper) };
                var featureMaxs = new List<double> { areaUpper };
                if (p.MinCircularity > 0)
                {
                    // 圆度 0~1：1 = 正圆，细长划痕约 0.02~0.1。用来分开"圆形斑点"与"细长划痕"
                    features.Add("circularity");
                    featureMins.Add(p.MinCircularity);
                    featureMaxs.Add(1.0);
                }
                HOperatorSet.SelectShape(
                    solid, out HObject shapeSelected,
                    new HTuple(features.ToArray()), "and",
                    new HTuple(featureMins.ToArray()), new HTuple(featureMaxs.ToArray()));
                temp.Add(shapeSelected);

                // ── 8. 长宽比 / 触边筛选 + 排序（select_shape 表达不了或本版本特征名不支持，自实现） ──
                HObject defectsObj;
                if (p.MaxAspectRatio > 0 || p.ExcludeBorderPx > 0 || p.SortMode != DefectSortMode.None)
                {
                    var order = BuildKeepOrder(shapeSelected, p, imgW, imgH);
                    defectsObj = RebuildRegion(shapeSelected, order);
                    temp.Add(defectsObj);
                }
                else
                {
                    defectsObj = shapeSelected;
                }

                // ── 9. 计数 ──
                HOperatorSet.CountObj(defectsObj, out HTuple number);
                result.DefectCount = number.Length > 0 ? number[0].I : 0;

                // ── 10. 逐项特征（面积 / 中心 / 外接矩形宽高） ──
                // 坑：23.05 没有 area 算子，取面积统一用 area_center，行/列用 _ 丢弃
                HOperatorSet.AreaCenter(defectsObj, out HTuple area, out HTuple rows, out HTuple cols);
                HOperatorSet.SmallestRectangle1(defectsObj, out HTuple rect1, out HTuple col1, out HTuple rect2, out HTuple col2);
                // 空元组不能直接 tuple_max / tuple_sum，先判个数
                result.MaxArea = (result.DefectCount > 0 && area.Length > 0) ? area.TupleMax().D : 0;
                result.TotalArea = (result.DefectCount > 0 && area.Length > 0) ? area.TupleSum().D : 0;
                result.CenterRows = rows;
                result.CenterCols = cols;
                result.Areas = area;
                result.Widths = BuildSizeTuple(col1, col2);
                result.Heights = BuildSizeTuple(rect1, rect2);

                // 像素当量只影响 mm² 输出，不参与判定——避免"改了当量就改判定结果"这种隐式耦合
                double mmPerPx2 = p.PixelSizeMm * p.PixelSizeMm;
                result.MaxAreaMm2 = result.MaxArea * mmPerPx2;
                result.TotalAreaMm2 = result.TotalArea * mmPerPx2;

                // ── 11. 判定 ──
                result.IsOk = !(result.DefectCount > p.MaxDefectCount
                                || result.DefectCount < p.MinDefectCount
                                || result.MaxArea > p.MaxSingleArea
                                || (p.MaxTotalArea > 0 && result.TotalArea > p.MaxTotalArea));
                result.NgReason = BuildNgReason(result.DefectCount, result.MaxArea, result.TotalArea, p);

                // ── 12. 缺陷区域移交（new 一份独立句柄交给端口；temp 里的原对象照常在 finally 释放） ──
                result.Defects = new HRegion(defectsObj);

                // ── 13. 标注图 ──
                // 渲染只是"给人看"，失败最多丢 DefectImage，绝不能把一次成功的检测判成失败
                try
                {
                    // 只在相关参数生效时才补行，避免默认配置下文字刷满整张图
                    var lines = new List<string>
                    {
                        $"判定：{(result.IsOk ? "OK" : "NG")}",
                        $"缺陷数：{result.DefectCount}",
                        $"最大面积：{result.MaxArea:0.#} px",
                    };
                    if (p.MaxTotalArea > 0) lines.Add($"总面积：{result.TotalArea:0.#} px");
                    if (Math.Abs(p.PixelSizeMm - 1.0) > 1e-9) lines.Add($"最大面积：{result.MaxAreaMm2:0.###} mm²");

                    HObject displayBase = BuildDisplayBase(src, temp);
                    result.AnnotatedImage = _renderer.Render(
                        displayBase,
                        defectsObj,
                        lines.ToArray(),
                        result.IsOk ? "green" : "red");
                }
                catch (Exception rex)
                {
                    logger?.Warn($"{InstanceName} 标注图渲染失败，DefectImage 将为空：{rex.Message}");
                    result.AnnotatedImage = null;
                }

                return result;
            }
            finally
            {
                foreach (var o in temp)
                {
                    try { o.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
                }
            }
        }

        /// <summary>
        /// 取用于显示的底图：原图（彩色保持彩色），非 byte 类型转成 byte。
        /// 为什么显示原图而不是灰度图：标注图是给人看的，彩色输入保留彩色信息更利于判断；
        /// 灰度输入时"原图"与"灰度图"本来就是同一张，不产生差异。
        /// 返回的 HObject 所有权：要么是调用方的 src（原样借用），要么是登记进 temp 的新对象，
        /// 渲染方只读不改，本方法绝不制造需要额外回收的中间包装。
        /// </summary>
        private HObject BuildDisplayBase(HImage src, List<HObject> temp)
        {
            HOperatorSet.GetImageType(src, out HTuple type);
            if (type.S == "byte") return src;

            // 多通道（如彩色 uint2）不参与灰度范围度量，直接转
            HOperatorSet.CountChannels(src, out HTuple channels);
            if (channels.I != 1)
            {
                HOperatorSet.ConvertImageType(src, out HObject direct, "byte");
                temp.Add(direct);
                return direct;
            }

            // HALCON 图形显示只稳妥支持 byte。但 convert_image_type(...,'byte') 是"截断"不是"缩放"：
            // 实测 uint2 灰度 1632 转出来就是 255，12/16 位相机的标注底图会整片死白，底图信息全丢。
            // 所以先按"实际灰度范围"线性拉伸到 0~255 再转。
            HOperatorSet.GetDomain(src, out HObject domain);
            temp.Add(domain);
            HOperatorSet.MinMaxGray(domain, src, 0, out HTuple minT, out HTuple maxT, out HTuple _);
            double gMin = minT.Length > 0 ? minT[0].D : 0;
            double gMax = maxT.Length > 0 ? maxT[0].D : 0;

            if (double.IsNaN(gMin) || double.IsNaN(gMax) || gMax - gMin < 1e-9)
            {
                // 退化（均匀图/空图）：拉伸没有意义，直接转（反正没有层次可保留）
                HOperatorSet.ConvertImageType(src, out HObject flat, "byte");
                temp.Add(flat);
                return flat;
            }

            double k = 255.0 / (gMax - gMin);
            HOperatorSet.ScaleImage(src, out HObject scaled, k, -gMin * k);
            temp.Add(scaled);
            HOperatorSet.ConvertImageType(scaled, out HObject converted, "byte");
            temp.Add(converted);
            return converted;
        }

        /// <summary>
        /// 把界面参数夹进算法能安全接受的区间。
        ///
        /// 为什么不能直接信属性值：方案文件可能来自老版本、也可能被手工改过，
        /// 越界值喂给算子会抛 HALCON 异常把整条流程带崩（"非法值不能炸"是硬要求）。
        /// 这里做的是"静默夹取"——预览/运行都用夹取后的值，保证配置坏了最多是结果不对，不会炸流程。
        /// </summary>
        private BlobParams NormalizedParameters()
        {
            var p = new BlobParams
            {
                Mode = ThresholdMode,
                Polarity = DetectTarget == DetectTarget.Bright ? "light" : "dark",
            };

            // 固定阈值：上下限填反了也不抛，按"区间"语义自动排好序
            double lo = ClampFinite(MinGray, 0, GrayUpperBound);
            double hi = ClampFinite(MaxGray, 0, GrayUpperBound);
            p.MinGray = Math.Min(lo, hi);
            p.MaxGray = Math.Max(lo, hi);

            p.VarMaskWidth = (int)Math.Round(ClampFinite(VarMaskWidth, 1, 1000));
            p.VarMaskHeight = (int)Math.Round(ClampFinite(VarMaskHeight, 1, 1000));
            p.VarStdDevScale = ClampFinite(VarStdDevScale, 0, 100);
            p.VarAbsThreshold = ClampFinite(VarAbsThreshold, 0, GrayUpperBound);

            p.MinArea = ClampFinite(MinArea, 0, AreaClampMax);
            p.MaxBlobArea = ClampFinite(MaxBlobArea, 0, AreaClampMax);
            p.MinCircularity = ClampFinite(MinCircularity, 0, 1);
            p.MaxAspectRatio = ClampFinite(MaxAspectRatio, 0, 10000);
            p.ExcludeBorderPx = (int)Math.Round(ClampFinite(ExcludeBorderPx, 0, 10000));
            p.FillHoles = FillHoles;
            p.OpenRadius = ClampFinite(OpenRadius, 0, 1000);
            p.CloseRadius = ClampFinite(CloseRadius, 0, 1000);
            p.MergeRadius = ClampFinite(MergeRadius, 0, 1000);
            p.MaxDefectCount = (int)Math.Round(ClampFinite(MaxDefectCount, 0, 1_000_000));
            p.MinDefectCount = (int)Math.Round(ClampFinite(MinDefectCount, 0, 1_000_000));
            p.MaxSingleArea = ClampFinite(MaxSingleArea, 0, AreaClampMax);
            p.MaxTotalArea = ClampFinite(MaxTotalArea, 0, AreaClampMax);
            p.PixelSizeMm = ClampFinite(PixelSizeMm, 0, 1000);
            p.SortMode = SortMode;
            return p;
        }

        /// <summary>夹取到 [min, max]；NaN/Infinity 一律当 min 处理</summary>
        private static double ClampFinite(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// 组装 NG 原因（OK 时为空串）。
        /// 顺序：个数超标 → 个数不足（存在性）→ 单缺陷面积 → 总面积，先报最直观的那个。
        /// 文案对应用户在界面里看到的字段名，方便直接对照。
        /// </summary>
        private static string BuildNgReason(int count, double maxArea, double totalArea, BlobParams p)
        {
            if (count > p.MaxDefectCount)
                return $"缺陷个数 {count} 超过上限 {p.MaxDefectCount}";
            if (count < p.MinDefectCount)
                return $"缺陷个数 {count} 少于下限 {p.MinDefectCount}（目标特征未检到）";
            if (maxArea > p.MaxSingleArea)
                return $"最大缺陷面积 {maxArea:0.#} 超过上限 {p.MaxSingleArea:0.#}";
            if (p.MaxTotalArea > 0 && totalArea > p.MaxTotalArea)
                return $"缺陷总面积 {totalArea:0.#} 超过上限 {p.MaxTotalArea:0.#}";
            return string.Empty;
        }

        /// <summary>由外接矩形的两端坐标算出尺寸元组（宽 = col2-col1+1，高 = row2-row1+1）</summary>
        private static HTuple BuildSizeTuple(HTuple low, HTuple high)
        {
            int n = Math.Min(low.Length, high.Length);
            if (n <= 0) return new HTuple();

            var values = new double[n];
            for (int i = 0; i < n; i++) values[i] = high[i].D - low[i].D + 1;
            return new HTuple(values);
        }

        /// <summary>
        /// 决定"保留哪些缺陷、按什么顺序输出"。
        ///
        /// 为什么自己算而不用 select_shape：
        ///  · 长宽比 —— 本仓库 HALCON（23.05）不认识 'elongation'/'elongatedness' 特征名
        ///    （实测 #3101 Unknown feature），只能拿 smallest_rectangle1 自己算；
        ///  · 触边 —— select_shape 能筛 row/col，但"是否压到边界带宽"要用外接矩形判断；
        ///  · 排序 —— 区域数组本身没有顺序约定，必须由插件定死语义，否则下游按索引取是不可预期的。
        /// </summary>
        /// <returns>保留下来的缺陷在源区域数组中的索引（0 基），顺序即输出顺序</returns>
        private static List<int> BuildKeepOrder(HObject regions, BlobParams p, int imgW, int imgH)
        {
            HOperatorSet.AreaCenter(regions, out HTuple areas, out HTuple rows, out HTuple cols);
            HOperatorSet.SmallestRectangle1(regions, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);

            int n = areas.Length;
            var areaArr = new double[n];
            var rowArr = new double[n];
            var colArr = new double[n];
            for (int i = 0; i < n; i++)
            {
                areaArr[i] = areas[i].D;
                rowArr[i] = rows[i].D;
                colArr[i] = cols[i].D;
            }

            var keep = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                double w = c2[i].D - c1[i].D + 1;
                double h = r2[i].D - r1[i].D + 1;

                // 长宽比 = 长边/短边，越大越细长（正方形 = 1）
                if (p.MaxAspectRatio > 0)
                {
                    double longSide = Math.Max(w, h);
                    double shortSide = Math.Max(Math.Min(w, h), 1);
                    if (longSide / shortSide > p.MaxAspectRatio) continue;
                }

                // 触边：外接矩形压到最外 N 像素就算（打光/裁切边缘效应的固定误检源）
                if (p.ExcludeBorderPx > 0 && TouchesBorder(r1[i].D, c1[i].D, r2[i].D, c2[i].D, imgW, imgH, p.ExcludeBorderPx))
                    continue;

                keep.Add(i);
            }

            switch (p.SortMode)
            {
                case DefectSortMode.AreaDescending:
                    keep.Sort((a, b) => areaArr[b].CompareTo(areaArr[a]));
                    break;

                case DefectSortMode.RowColumn:
                    // 先行后列：与"读数顺序"一致，便于人工核对编号
                    keep.Sort((a, b) =>
                    {
                        int byRow = rowArr[a].CompareTo(rowArr[b]);
                        return byRow != 0 ? byRow : colArr[a].CompareTo(colArr[b]);
                    });
                    break;

                default:   // None：保持 HALCON 区域顺序
                    break;
            }
            return keep;
        }

        /// <summary>外接矩形是否压到图像最外 band 像素</summary>
        private static bool TouchesBorder(double row1, double col1, double row2, double col2, int imgW, int imgH, int band)
            => row1 < band || col1 < band || row2 >= imgH - band || col2 >= imgW - band;

        /// <summary>
        /// 按给定索引顺序重建区域对象（新对象所有权交调用方，调用方负责登记进 temp）。
        /// 空列表时返回空对象集——这样 count_obj 仍是 0，下游不会拿到 null。
        /// </summary>
        private static HObject RebuildRegion(HObject source, List<int> order)
        {
            if (order.Count == 0)
            {
                HOperatorSet.GenEmptyObj(out HObject empty);
                return empty;
            }

            HObject? acc = null;
            for (int k = 0; k < order.Count; k++)
            {
                HOperatorSet.SelectObj(source, out HObject one, order[k] + 1);   // HALCON 索引从 1 起
                if (acc == null)
                {
                    acc = one;
                    continue;
                }
                HOperatorSet.ConcatObj(acc, one, out HObject combined);
                acc.Dispose();
                one.Dispose();
                acc = combined;
            }
            return acc!;
        }

        #endregion
    }
}
