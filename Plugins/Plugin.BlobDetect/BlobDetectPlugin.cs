using Core.Commands;
using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using System;
using System.ComponentModel;
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
        Description = "阈值分割 + 连通域分析，检出划痕/暗斑等缺陷并按个数与面积判定 OK/NG；支持固定/自动/动态阈值与亮暗同检，支持检测/排除区域，输出逐缺陷面积、圆度、长宽比、方向等特征",
        ShortName = "\uf002"
    )]
    public partial class BlobDetectPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 输入 / 输出端口（名字即连线名，编译期就存在，供按名连线）

        /// <summary>待检测的输入图像（可链接上游；未连线时按既有插件的惯例给中文提示后判失败）</summary>
        public InputPort<HImage> SrcImage { get; } = new("SrcImage", description: "待检测的输入图像");

        /// <summary>
        /// 检测区域（可选，IsRequired=false）：只在该区域内找缺陷，接 Plugin.CreateRoi 的 MaskRegion 即可；
        /// 未连线时整图检测（老方案行为不变）。
        /// 语义与 reduce_domain 一致：候选区先与它取交集再做后续处理，压在区域边界上的缺陷按裁剪后的面积计。
        /// </summary>
        public InputPort<HRegion> MaskRegion { get; } = new("MaskRegion", description: "检测区域（可选）：只在该区域内检测；未连线=整图")
        { IsRequired = false };

        /// <summary>
        /// 排除区域（可选，IsRequired=false）：该区域内的候选一律丢弃。
        /// 用在哪：螺丝孔、二维码、标记载体这类"位置固定、永远不该报"的误检源，
        /// 触边排除只能处理图像边缘，处理不了画面中间的固定干扰。
        /// </summary>
        public InputPort<HRegion> ExcludeRegion { get; } = new("ExcludeRegion", description: "排除区域（可选）：区域内的候选一律丢弃")
        { IsRequired = false };

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

        // ── 逐缺陷形状特征补全（只增不改：上面所有端口的名字/类型/顺序一律不动）──
        // 分拣场景要在下游把"圆斑=气泡 / 长条=划痕"分流，光靠筛选参数把特征"筛掉"不够，
        // 得把每个缺陷的特征值作为数组输出，与 CenterRows 等端口同一顺序逐项对齐。

        /// <summary>各缺陷圆度数组（0~1，1=正圆，细长划痕接近 0；顺序同上）。与「圆度下限」筛选用的同一特征</summary>
        public OutputPort<HTuple> DefectCircularities { get; } = new("DefectCircularities", "各缺陷圆度数组（0~1，1=正圆）");

        /// <summary>各缺陷长宽比数组（外接矩形 长边/短边，正方形=1；顺序同上）。与「长宽比上限」筛选同一口径</summary>
        public OutputPort<HTuple> DefectAspectRatios { get; } = new("DefectAspectRatios", "各缺陷长宽比数组（长边/短边）");

        /// <summary>各缺陷方向角数组（度，-90~90，主轴与水平方向夹角；顺序同上）。圆形缺陷方向无意义，仅对细长缺陷有参考价值</summary>
        public OutputPort<HTuple> DefectPhis { get; } = new("DefectPhis", "各缺陷方向角数组（度）");

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
        private const bool DefaultDetectBrightAndDark = false; // 默认单极性（保持既有语义）
        private const int DefaultMinDefectCount = 0;      // 0 = 不做存在性判定
        private const double DefaultMaxTotalArea = 0;     // 0 = 不限总面积
        private const double DefaultPixelSizeMm = 1.0;    // 1.0 = 输出即像素值，不假设标定（与卡尺插件同口径）
        private const DefectSortMode DefaultSortMode = DefectSortMode.None;
        private const int DefaultDisplayViewIndex = 1;    // 默认发布到 1 号视图窗口（与图像采集插件同口径）

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

        /// <summary>二值化方式（切换时界面只显示该方式自己的参数）</summary>
        [StepConfig, DefaultValue(DefaultThresholdMode)]
        public partial ThresholdMode ThresholdMode { get; set; }

        partial void OnThresholdModeChanged(ThresholdMode value)
        {
            // 三个分区 + "检测目标"的显隐都跟着方式走，一次性把相关通知补全
            OnPropertyChanged(nameof(IsFixedThreshold));
            OnPropertyChanged(nameof(IsAutoThreshold));
            OnPropertyChanged(nameof(IsDynamicThreshold));
            OnPropertyChanged(nameof(ShowDetectTarget));
            OnPropertyChanged(nameof(ShowDualPolarity));
            SchedulePreview();
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

        /// <summary>
        /// 亮暗同检：同一张图上同时检出比背景亮与比背景暗的缺陷（仅自动/动态阈值下生效）。
        ///
        /// 用在哪：一块板面上"暗划痕 + 亮亮点"要一次检完——单极性只能各摆一个节点跑两遍。
        /// 做法：两种极性各做一次二值化，两张候选区取并集再走后续流程。
        /// 固定阈值不提供这一项：它的灰度区间 [MinGray, MaxGray] 本身就是"区间内全要"，
        /// 想双向就切成自动/动态阈值再勾选，参数区不摆一个不生效的开关。
        /// </summary>
        [StepConfig, DefaultValue(DefaultDetectBrightAndDark)]
        public partial bool DetectBrightAndDark { get; set; }

        partial void OnDetectBrightAndDarkChanged(bool value)
        {
            // 同检一开，"检测目标"就失效了（两种极性都要）——跟着隐藏，别留一个不生效的开关
            OnPropertyChanged(nameof(ShowDetectTarget));
            SchedulePreview();
        }

        /// <summary>阈值是否仍停留在上次适配出来的那组值（用于判断"用户有没有手调过"）</summary>
        private bool IsThresholdAtLastAdaptedValue()
            => NearlyEqual(MinGray, _lastAdaptedMinGray) && NearlyEqual(MaxGray, _lastAdaptedMaxGray);

        /// <summary>固定阈值：下限灰度</summary>
        [StepConfig, DefaultValue(DefaultMinGray)]
        public partial double MinGray { get; set; }

        partial void OnMinGrayChanged(double value) => SchedulePreview();

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
        [StepConfig, DefaultValue(DefaultMaxGray)]
        public partial double MaxGray { get; set; }

        partial void OnMaxGrayChanged(double value) => SchedulePreview();

        /// <summary>动态阈值：局部窗口宽（像素）</summary>
        [StepConfig, DefaultValue(DefaultVarMaskWidth)]
        public partial int VarMaskWidth { get; set; }

        partial void OnVarMaskWidthChanged(int value) => SchedulePreview();

        /// <summary>动态阈值：局部窗口高（像素）</summary>
        [StepConfig, DefaultValue(DefaultVarMaskHeight)]
        public partial int VarMaskHeight { get; set; }

        partial void OnVarMaskHeightChanged(int value) => SchedulePreview();

        /// <summary>动态阈值：标准差权重</summary>
        [StepConfig, DefaultValue(DefaultVarStdDevScale)]
        public partial double VarStdDevScale { get; set; }

        partial void OnVarStdDevScaleChanged(double value) => SchedulePreview();

        /// <summary>动态阈值：绝对灰度偏移量</summary>
        [StepConfig, DefaultValue(DefaultVarAbsThreshold)]
        public partial double VarAbsThreshold { get; set; }

        partial void OnVarAbsThresholdChanged(double value) => SchedulePreview();

        #endregion

        #region ② 特征筛选（噪声清理 + 形状 + 触边）

        /// <summary>面积下限：小于它的连通域当噪点丢弃</summary>
        [StepConfig, DefaultValue(DefaultMinArea)]
        public partial double MinArea { get; set; }

        partial void OnMinAreaChanged(double value) => SchedulePreview();

        /// <summary>
        /// 单缺陷面积上限（筛选用，0 = 不限，此时上限自动取"图像像素总数"）。
        ///
        /// 为什么要把它与判定用的 MaxSingleArea 分开：一个是"这个东西算不算缺陷"（筛选，超了直接不计数），
        /// 一个是"这个缺陷合不合格"（判定，超了判 NG）。早期版本只有后者、且把 select_shape 的筛选上限写死 1e7，
        /// 结果是大画幅（>1000 万像素）上整块背景被 1e7 静默滤掉——既不计数也不 NG，等于漏检还报 OK。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMaxBlobArea)]
        public partial double MaxBlobArea { get; set; }

        partial void OnMaxBlobAreaChanged(double value) => SchedulePreview();

        /// <summary>
        /// 圆度下限（0 = 不筛）。取值 0~1：1 = 正圆，细长划痕约 0.02~0.1，方块约 0.67。
        /// 用来把"圆形斑点"与"细长划痕"分开——这是现场最常见的两类缺陷区分需求。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMinCircularity)]
        public partial double MinCircularity { get; set; }

        partial void OnMinCircularityChanged(double value) => SchedulePreview();

        /// <summary>
        /// 长宽比上限（0 = 不筛）。长宽比 = 外接矩形 长边/短边，越大越细长（正方形 = 1）。
        ///
        /// 为什么不直接用 HALCON 的 'elongation' 特征：实测本仓库 HALCON 版本（23.05）不认识该特征名
        /// （select_shape / region_features 均报 #3101 Unknown feature；仓库脚本模板里教用户写 'elongation' 是错的）。
        /// 这里改用 smallest_rectangle1 自己算，语义与 elongation 一致且在任何版本都成立。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMaxAspectRatio)]
        public partial double MaxAspectRatio { get; set; }

        partial void OnMaxAspectRatioChanged(double value) => SchedulePreview();

        /// <summary>
        /// 排除触边缺陷的边界带宽（像素，0 = 不排除）。
        /// 缺陷外接矩形只要碰到图像（或 ROI 外接矩形）最外 N 像素，就当"打光/裁切边缘效应"丢弃。
        ///
        /// 为什么必须有这一项：打光边缘效应是固定的误检源（自带样图上就有不少缺陷落在右缘/底缘），
        /// 而上游 ROI 只能整体裁形状、没法"往里缩一圈"，所以只能在本插件里按"是否触边"过滤。
        /// </summary>
        [StepConfig, DefaultValue(DefaultExcludeBorderPx)]
        public partial int ExcludeBorderPx { get; set; }

        partial void OnExcludeBorderPxChanged(int value) => SchedulePreview();

        #endregion

        #region ③ 形态学

        /// <summary>开运算半径（0 = 不做开运算）</summary>
        [StepConfig, DefaultValue(DefaultOpenRadius)]
        public partial double OpenRadius { get; set; }

        partial void OnOpenRadiusChanged(double value) => SchedulePreview();

        /// <summary>闭运算半径（0 = 不做闭运算）</summary>
        [StepConfig, DefaultValue(DefaultCloseRadius)]
        public partial double CloseRadius { get; set; }

        partial void OnCloseRadiusChanged(double value) => SchedulePreview();

        /// <summary>
        /// 是否填孔（fill_up）。缺陷内部若有亮斑/反光会把一个缺陷"挖成环形"：
        /// 面积偏小、甚至被拆成多个区域。勾选后按外轮廓计算面积。
        /// </summary>
        [StepConfig, DefaultValue(DefaultFillHoles)]
        public partial bool FillHoles { get; set; }

        partial void OnFillHolesChanged(bool value) => SchedulePreview();

        /// <summary>
        /// 相邻缺陷的合并半径（像素，0 = 不合并）。把间距小于 2×半径 的几段连成一个缺陷。
        ///
        /// 用在哪：一条划痕常常被打光/噪声断成三四段，个数虚高好几倍，判定必然 NG。
        /// 做法：union1（先合成一个区域集）→ closing_circle → connection。
        /// 注意必须先 union1：直接对区域数组做形态学，HALCON 是逐对象处理、合不到一起。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMergeRadius)]
        public partial double MergeRadius { get; set; }

        partial void OnMergeRadiusChanged(double value) => SchedulePreview();

        #endregion

        #region ④ 判定规格

        /// <summary>缺陷个数上限（超过即 NG）</summary>
        [StepConfig, DefaultValue(DefaultMaxDefectCount)]
        public partial int MaxDefectCount { get; set; }

        partial void OnMaxDefectCountChanged(int value) => SchedulePreview();

        /// <summary>
        /// 缺陷个数下限（少于即 NG，0 = 不做这项判定）。
        ///
        /// 用在哪：Blob 在产线上大量用于"存在性检测"——这个特征/这个标记有没有。
        /// 此时"一个都没检到"恰恰是坏消息，而只靠上限的话 0 个会判 OK。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMinDefectCount)]
        public partial int MinDefectCount { get; set; }

        partial void OnMinDefectCountChanged(int value) => SchedulePreview();

        /// <summary>
        /// 单个缺陷面积上限（超过即 NG）。填 0 = 不做这项判定（与总面积上限同一口径）。
        /// 历史版本里 0 的语义是"任何缺陷都 NG"，与本插件其余规格参数"0 = 不启用"相反而易踩坑，已对齐；
        /// 依赖旧行为的方案请把「缺陷个数上限」设为 0（零容忍）来表达同样的判定。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMaxSingleArea)]
        public partial double MaxSingleArea { get; set; }

        partial void OnMaxSingleAreaChanged(double value) => SchedulePreview();

        /// <summary>
        /// 缺陷总面积上限（超过即 NG，0 = 不做这项判定）。
        /// 用在哪：每个缺陷单独看都合格、但整片脏污/麻点密布——只有总面积能兜住这种情况。
        /// </summary>
        [StepConfig, DefaultValue(DefaultMaxTotalArea)]
        public partial double MaxTotalArea { get; set; }

        partial void OnMaxTotalAreaChanged(double value) => SchedulePreview();

        #endregion

        #region ⑤ 输出

        /// <summary>
        /// 像素当量（mm/px）。默认 1.0 = 输出的 mm² 数值等于像素值，不假设任何标定；
        /// 现场做完标定后填真实值，MaxAreaMm2 / TotalAreaMm2 即为物理量。
        /// 与仓库 Plugin.CaliperMeasure 的 PixelSizeMm 同一口径（那边注释写明"项目暂无标定模块"）。
        /// 注意：判定仍按像素做，像素当量只影响 mm² 输出，避免"改了当量就改判定结果"这种隐式耦合。
        /// </summary>
        [StepConfig, DefaultValue(DefaultPixelSizeMm)]
        public partial double PixelSizeMm { get; set; }

        partial void OnPixelSizeMmChanged(double value) => SchedulePreview();

        /// <summary>
        /// 输出排序。CenterRows / CenterCols / DefectAreas / DefectWidths / DefectHeights 与 Defects 端口
        /// 一律按此顺序逐项对齐，下游按同一个索引取即可对上号。
        /// </summary>
        [StepConfig, DefaultValue(DefaultSortMode)]
        public partial DefectSortMode SortMode { get; set; }

        partial void OnSortModeChanged(DefectSortMode value) => SchedulePreview();

        /// <summary>
        /// 运行显示窗口索引：正式运行时把标注图发布到主界面几号视图窗口（1~9），0 = 不发布。
        ///
        /// 为什么必须有这一项：检测完成后操作员要能在产线屏幕上直接看到"缺陷在哪、判了什么"，
        /// 而配置对话框只在调参时打开。与图像采集等出图插件的 DisplayViewIndex 同一口径；
        /// 0 档留给"标注图由下游专门步骤处理"的方案。
        /// 注意它只影响正式运行，配置态预览（右侧标注图）始终显示。
        /// </summary>
        [StepConfig, DefaultValue(DefaultDisplayViewIndex)]
        public partial int DisplayViewIndex { get; set; }

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
        /// 再摆一个不生效的开关只会让人困惑，所以这一项固定阈值时隐藏；
        /// 勾了"亮暗同检"后两种极性都要、这一项同样失效，一并隐藏。
        /// </summary>
        public bool ShowDetectTarget => ThresholdMode != ThresholdMode.Fixed && !DetectBrightAndDark;

        /// <summary>
        /// 是否显示「亮暗同检」开关。
        /// 与 ShowDetectTarget 同一逻辑：固定阈值下灰度区间自己表达"要什么"，
        /// 摆一个不生效的开关只会让人困惑，所以只在自动/动态阈值下出现。
        /// </summary>
        public bool ShowDualPolarity => !IsFixedThreshold;

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

        #region 缺陷清单（配置态展示：逐缺陷数值与标注图编号对照，调筛选参数不用再对着图数）

        /// <summary>缺陷清单的最大行数：预览是给人调参的，噪声图上几百个缺陷全列出来只会把 UI 拖垮；
        /// 超出部分截断，行数说明写在表头（端口输出不受影响，永远全量）</summary>
        private const int MaxDefectRows = 200;

        /// <summary>缺陷清单行集合（只读快照，顺序/语义与输出端口逐项一致；仅配置态刷新）</summary>
        public System.Collections.ObjectModel.ObservableCollection<DefectRow> DefectRows { get; } = new();

        private string _defectCountText = string.Empty;
        /// <summary>表头右侧的行数说明（"共 N 个" / "共 N 个（仅列前 M 行）"）</summary>
        public string DefectCountText
        {
            get => _defectCountText;
            private set => SetProperty(ref _defectCountText, value);
        }

        /// <summary>用一次检测结果填充缺陷清单（任何失败/空结果都清成空表）</summary>
        private void FillDefectRows(BlobResult result)
        {
            DefectRows.Clear();
            int n = Math.Min(result.DefectCount, MaxDefectRows);
            for (int i = 0; i < n; i++)
            {
                DefectRows.Add(new DefectRow
                {
                    Index = i + 1,   // 与标注图上写的编号一致（1 基）
                    Area = result.Areas.Length > i ? result.Areas[i].D : 0,
                    Circularity = result.Circularities.Length > i ? result.Circularities[i].D : 0,
                    AspectRatio = result.AspectRatios.Length > i ? result.AspectRatios[i].D : 0,
                    Width = result.Widths.Length > i ? result.Widths[i].D : 0,
                    Height = result.Heights.Length > i ? result.Heights[i].D : 0,
                    Row = result.CenterRows.Length > i ? result.CenterRows[i].D : 0,
                    Col = result.CenterCols.Length > i ? result.CenterCols[i].D : 0,
                });
            }
            DefectCountText = result.DefectCount > MaxDefectRows
                ? $"共 {result.DefectCount} 个（仅列前 {MaxDefectRows} 行）"
                : $"共 {result.DefectCount} 个";
        }

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

        // ---- 预览后台化与检测结果缓存（大图调参的界面冻结根治 + 判定参数即时反馈）----
        private int _previewRound;              // 预览轮次号：后台结果回来时对不上号 = 过期，只释放不回填
        private bool _previewInFlight;          // 一轮预览正在后台跑（跑的时候新请求记入待刷新）
        private bool _previewRefreshPending;    // 在跑时又来过刷新请求（跑完拿最新图与参数再来一遍）
        private DetectPack? _detectPack;        // 阶段一检测结果缓存（检测组参数没变就复用，跳过全图算法）
        private string? _detectPackKey;         // 阶段一缓存对应的检测组参数指纹
        private HImage? _detectSource;          // 阶段一缓存对应的源图私有副本（重渲染底图）
        private int _highlightIndex;            // 缺陷清单联动高亮的缺陷编号（0 = 无）

        // ---- 预览区页签导航（右列：页签一"图像视图" / 页签二"数据输出"）----
        private int _selectedPreviewTab;
        /// <summary>右侧预览区选中的页签（0 = 图像视图，1 = 数据输出）。
        /// 缺陷清单点选行时自动切回 0，让黄框高亮看得见</summary>
        public int SelectedPreviewTab
        {
            get => _selectedPreviewTab;
            set => SetProperty(ref _selectedPreviewTab, value);
        }

        /// <summary>页签导航的文案（与 SelectedPreviewTab 的下标一一对应）</summary>
        public IReadOnlyList<string> PreviewTabLabels { get; } = new[] { "图像视图", "数据输出" };

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
            // 检测/排除区域同理：链接动作本身不改任何参数，不订阅的话接上区域后预览纹丝不动，
            // 用户会以为区域没生效
            MaskRegion.ValueChanged += OnSrcImageValueChanged;
            ExcludeRegion.ValueChanged += OnSrcImageValueChanged;
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
        /// 把动作切回 UI 线程执行（预览后台化的回填通道）。
        /// 配置实例由对话框持有，Application.Current.Dispatcher 就是 UI 线程；
        /// 没有 Application（设计态/单元测试）或本来就在 UI 线程时直接执行，避免无谓调度与死锁。
        /// </summary>
        private static void PostToUI(Action action)
        {
            var dispatcher = UiDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        /// <summary>配置实例是否已释放：关闭对话框后在途的后台预览回来时，靠它拒绝往已释放的绑定上写</summary>
        private volatile bool _disposed;

        /// <summary>
        /// 重算预览（入口在 UI 线程）。
        ///
        /// 分工（这一版把全图算法挪出了 UI 线程，根治大图调参的界面冻结）：
        ///   UI 线程只做三件便宜的事——取源图引用、CopyImage 出一份预览私有副本（毫秒级）、
        ///   自动适配（只读写参数，读的是私有副本）；全图检测、直方图、标注渲染都在后台线程跑，
        ///   完成后回 UI 线程回填绑定。
        ///
        /// 竞态防护
        /// ---------
        ///   · 轮次号：后台结果回来时对不上号 = 过期，只释放不回填（连点刷新不会旧图盖新图）；
        ///   · 在跑标志：跑的时候又来刷新请求，记一笔"待刷新"，跑完拿**最新**的图与参数再来一遍
        ///     （中间态直接跳过，最后一把总是最新）；
        ///   · 已释放守卫：配置窗口关闭后回来的一切只释放。
        ///
        /// 无 Dispatcher 的环境（离线跑流程 / 单元测试）保持旧的同步行为。
        /// </summary>
        public void RefreshPreview()
        {
            // 运行实例不跑预览（配置态能力的隔离，理由见 _isConfigInstance 注释）
            if (!_isConfigInstance) return;

            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                _previewRound++;
                DisposeDetectCache();
                PreviewImage = null;
                ClearHistogram();
                DefectRows.Clear();
                DefectCountText = "共 0 个";
                SetStatusWithAdaptNote("输入图像为空：请为上方“输入图像”指定图片，或连接上游图像端口", StatusLevel.Warning);
                return;
            }

            // 在跑就别并发（并发会把缓存替换与渲染缠在一起）：记一笔待刷新，跑完拿最新状态再来
            if (_previewInFlight)
            {
                _previewRefreshPending = true;
                return;
            }

            var round = ++_previewRound;
            HImage snapshot;
            try
            {
                snapshot = src.CopyImage();   // UI 线程上的最后一笔像素访问：拷一份预览私有副本
            }
            catch (Exception ex)
            {
                SetStatusWithAdaptNote($"输入图像取副本失败：{ex.Message}", StatusLevel.Error);
                return;
            }

            // 自动适配：仅当参数仍等于出厂默认值时才执行（工业软件硬要求，理由同前）。
            // 读的是私有副本（无竞态）；写参数期间屏蔽 setter 触发的排队，改完统一走后台刷新。
            if (AreAllParametersAtFactoryDefault() && TryAdaptCore(snapshot, out string adaptNote))
                _adaptNote = adaptNote;

            // 切换"检测目标"后的补适配：仅在阈值仍停留在上次适配值时才做，绝不覆盖用户手调的阈值
            if (_pendingAdaptOnTargetChange)
            {
                _pendingAdaptOnTargetChange = false;
                if (IsThresholdAtLastAdaptedValue() && TryAdaptCore(snapshot, out string reAdaptNote))
                    _adaptNote = reAdaptNote;
            }

            if (UiDispatcher == null)
            {
                // 离线环境（无消息泵）：保持旧的同步行为。
                // 注意 snapshot 的所有权已按缓存命中与否移交（命中=已回收 / 未命中=归检测缓存），这里不再释放
                PreviewRunCore(snapshot, round);
                return;
            }

            _previewInFlight = true;
            SetStatus("正在分析…", StatusLevel.Info);

            Task.Run(() =>
            {
                try
                {
                    PreviewRunCore(snapshot, round);
                }
                catch (Exception ex)
                {
                    // snapshot 的所有权在 PreviewRunCore 内部已交接（命中=已回收 / 未命中=归缓存），
                    // 异常路径同样不能在这里补刀 —— 缓存底图被提前释放会让下一轮预览直接崩
                    PostToUI(() =>
                    {
                        if (round != _previewRound || _disposed) return;
                        PreviewImage = null;
                        DefectRows.Clear();
                        DefectCountText = "共 0 个";
                        SetStatusWithAdaptNote($"预览失败：{ex.Message}", StatusLevel.Error);
                    });
                }
                finally
                {
                    _previewInFlight = false;
                }

                // 跑的时候来过刷新请求 → 按最新的图与参数再来一遍（自适应/快照重新在 UI 线程走）
                PostToUI(() =>
                {
                    if (_previewRefreshPending && !_disposed)
                    {
                        _previewRefreshPending = false;
                        RefreshPreview();
                    }
                });
            });
        }

        /// <summary>
        /// 预览执行核心（后台线程）：阶段一检测（缓存复用）→ 排序判定渲染 → 回 UI 回填。
        /// snapshot 的所有权归本方法：命中缓存时立即回收，未命中时移交检测缓存当渲染底图。
        /// </summary>
        private void PreviewRunCore(HImage snapshot, int round)
        {
            var p = NormalizedParameters();

            // ── 阶段一：检测（检测组参数与源图都没变时直接复用缓存，毫秒级跳过全图算法）──
            var detKey = BuildDetectKey(snapshot);
            if (_detectPack == null || _detectPackKey != detKey)
            {
                DisposeDetectCache();
                _detectPack = DetectBlob(snapshot, p, null);
                _detectPackKey = detKey;
                _detectSource = snapshot;          // 所有权移交：缓存的渲染底图就是它
                snapshot = _detectSource;
            }
            else
            {
                snapshot.Dispose();                // 命中缓存：本轮副本立即回收（缓存里另有底图）
                snapshot = _detectSource!;
            }

            // ── 直方图（后台算，INPC 标量属性 WPF 会自动调度回 UI 线程）──
            ComputeHistogram(snapshot);

            // ── 阶段二：排序 + 逐项特征 + 判定 + 标注渲染 ──
            var result = ArrangeJudgeRender(_detectPack, snapshot, p, null);

            var message = result.IsOk
                ? $"OK：缺陷数 {result.DefectCount}，最大缺陷面积 {result.MaxArea:0.#} px（在规格内）"
                : $"NG：{result.NgReason}";
            if (HistogramErrorVisible)
                message += $"（{HistogramError}）";

            // ── 回 UI 线程回填（过期/已释放的结果在这里只释放不回填）──
            PostToUI(() =>
            {
                if (round != _previewRound || _disposed)
                {
                    result.AnnotatedImage?.Dispose();
                    result.Defects?.Dispose();
                    return;
                }

                result.Defects?.Dispose();
                result.Defects = null;                 // 预览用不上区域对象

                PreviewImage = result.AnnotatedImage;   // setter 负责释放上一张预览图
                FillDefectRows(result);                 // 缺陷清单与标注图同源，逐项对得上编号

                SetStatusWithAdaptNote(message, result.IsOk ? StatusLevel.Info : StatusLevel.Error);
            });
        }

        /// <summary>阶段一缓存的检测组参数指纹：这些参数与源图/掩膜都没变时，检测结果直接复用。
        /// 长宽比/触边/排序不进来 —— 它们由阶段二承担，变化时只重排不重检</summary>
        private string BuildDetectKey(HImage source)
        {
            return string.Join("|",
                ThresholdMode, DetectTarget, DetectBrightAndDark,
                MinGray, MaxGray, VarMaskWidth, VarMaskHeight,
                VarStdDevScale, VarAbsThreshold,
                MinArea, MaxBlobArea, MinCircularity,
                FillHoles, OpenRadius, CloseRadius, MergeRadius,
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(MaskRegion.ActualValue),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ExcludeRegion.ActualValue));
        }

        /// <summary>释放阶段一缓存（候选区域 + 源图副本）</summary>
        private void DisposeDetectCache()
        {
            try { _detectPack?.Dispose(); } catch { /* 释放失败不阻断 */ }
            _detectPack = null;
            _detectPackKey = null;
            try { _detectSource?.Dispose(); } catch { }
            _detectSource = null;
        }

        /// <summary>
        /// 缺陷清单行选中 → 在预览图上给那根缺陷套黄色高亮框（1 基编号；0 = 清除高亮）。
        /// 复用阶段一缓存的检测产物重渲染（不重跑检测）；缓存不在或正在跑预览时静默跳过。
        /// </summary>
        public void HighlightDefect(int index)
        {
            if (!_isConfigInstance || _disposed || _previewInFlight) return;

            // 清单的用途就是"在图上找到这根缺陷"：点行自动切到图像视图，黄框才看得见
            SelectedPreviewTab = 0;

            var pack = _detectPack;
            var source = _detectSource;
            if (pack == null || source == null || pack.Areas.Length == 0 || index > pack.Areas.Length) return;

            try
            {
                var p = NormalizedParameters();
                var result = ArrangeJudgeRender(pack, source, p, null, highlightIndex: index);
                if (result.AnnotatedImage == null) return;

                var annotated = result.AnnotatedImage;
                PostToUI(() =>
                {
                    if (_disposed || _highlightIndex != index) { annotated.Dispose(); return; }
                    PreviewImage = annotated;   // setter 负责释放上一张预览图
                });
            }
            catch (Exception ex)
            {
                SetStatus($"高亮失败：{ex.Message}", StatusLevel.Warning);
            }
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
                && DetectBrightAndDark == DefaultDetectBrightAndDark
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
                && SortMode == DefaultSortMode
                && DisplayViewIndex == DefaultDisplayViewIndex;
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
            // 预览后台链全部作废：轮次号推进 + 在跑标志清掉 + 阶段一缓存释放
            _previewRound++;
            _previewRefreshPending = false;
            DisposeDetectCache();
            _highlightIndex = 0;

            if (_previewDebounce != null)
            {
                _previewDebounce.Stop();
                _previewDebounce.Tick -= OnPreviewTick;
                _previewDebounce = null;
            }
            PreviewImage = null;   // setter 释放预览图
            ClearHistogram();      // 直方图是纯托管数据，清引用即可
            DefectRows.Clear();    // 缺陷清单是纯托管快照，清引用即可
            DefectCountText = string.Empty;
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
            DefectCircularities.Value = new HTuple();
            DefectAspectRatios.Value = new HTuple();
            DefectPhis.Value = new HTuple();

            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                return;
            }

            var p = NormalizedParameters();
            BlobResult result;
            try
            {
                using var pack = DetectBlob(src, p, context.Logger);
                result = ArrangeJudgeRender(pack, src, p, context.Logger);
            }
            catch (Exception ex)
            {
                // 带上"哪一步在干嘛"的上下文前缀，比基类兜底的"异常类型: 消息"更好排查
                Fail($"Blob 缺陷检测失败：{ex.Message}");
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
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
            DefectCircularities.Value = result.Circularities;
            DefectAspectRatios.Value = result.AspectRatios;
            DefectPhis.Value = result.Phis;

            // 发布到主程序视图窗口（DisplayViewIndex 已是真实窗口号 1~9；0 = 不发布则跳过）。
            // NG 也要发——恰恰是判 NG 时操作员最需要立刻看到缺陷在哪。
            // 总线无订阅者/无 WPF 宿主时是 no-op，离线跑流程不受影响
            if (DisplayViewIndex > 0 && result.AnnotatedImage != null)
                this.PublishPreview(result.AnnotatedImage, DisplayViewIndex);

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

        /// <summary>执行结果（判定与渲染的输出载体；AnnotatedImage / Defects 的所有权交给调用方）</summary>
        private sealed class BlobResult
        {
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
            public HTuple Circularities = new();
            public HTuple AspectRatios = new();
            public HTuple Phis = new();
            public string NgReason = string.Empty;
        }

        /// <summary>归一化后的参数（避免中途被界面改值；也把脏配置夹进算法能安全接受的区间）</summary>
        private sealed class BlobParams
        {
            public ThresholdMode Mode;
            public bool DetectBrightAndDark;
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

            // 检测/排除区域：借用的端口值（所有权在端口，算法里绝不 Dispose）；
            // null = 未连线或上游没给值，区域逻辑整段跳过
            public HRegion? Mask;
            public HRegion? Exclude;
            public bool HasMask => Mask != null && Mask.IsInitialized();
            public bool HasExclude => Exclude != null && Exclude.IsInitialized();
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
            else if (channels.I == 4)
            {
                // 4 通道（RGBA/BGRA，常见于相机 SDK 与带透明通道的解码）：alpha 对亮度没有贡献，
                // 取前 3 通道按彩色转灰度即可，不该让整次检测直接失败。
                // 注意 HALCON 只认"通道序"不认颜色语义：BGRA 输入时加权里 R/B 对调，
                // 得到的仍是对比度保留的有效灰度图，对 Blob 检测无实质影响。
                HOperatorSet.AccessChannel(src, out HObject c1, 1);
                HOperatorSet.AccessChannel(src, out HObject c2, 2);
                HOperatorSet.AccessChannel(src, out HObject c3, 3);
                temp.Add(c1);
                temp.Add(c2);
                temp.Add(c3);
                HOperatorSet.Rgb3ToGray(c1, c2, c3, out gray);
            }
            else
            {
                error = $"不支持的图像通道数：{channels.I}（仅支持 1 通道灰度、3 通道彩色或 4 通道 RGBA/BGRA）";
                return false;
            }

            temp.Add(gray);
            return true;
        }

        /// <summary>
        /// 检测阶段：阈值 → 排除 → 形态学 → 连通域 → 合并 → 填孔 → 特征筛选 → 候选特征一次算完。
        /// 硬失败（通道不支持 / 图像非法）抛异常，由调用方按失败契约收口；
        /// 正常路径返回候选包（候选区域与特征的所有权随包转移）。
        /// 产物与判定 / 渲染解耦：检测组参数没变时，预览直接复用这份结果（见预览缓存）。
        /// </summary>
        /// <param name="src">输入图像（不拥有，绝不释放）</param>
        private DetectPack DetectBlob(HImage src, BlobParams p, ILogService? logger)
        {
            var pack = new DetectPack();

            // 本方法自建的中间 HALCON 对象统一登记，finally 一次性释放。
            // 例外：Candidates 是要交出去的产物，返回前会从 temp 摘除（所有权随包转移）
            var temp = new List<HObject>();
            try
            {
                // ── 1. 拿一张可安全处理的灰度图 ──
                if (!TryToGrayImage(src, temp, out HObject gray, out string grayError))
                    throw new InvalidOperationException(grayError);

                // 图像尺寸：既用于"面积上限取像素总数"，也用于触边判定
                HOperatorSet.GetImageSize(gray, out HTuple sizeW, out HTuple sizeH);
                int imgW = sizeW.Length > 0 ? sizeW[0].I : 0;
                int imgH = sizeH.Length > 0 ? sizeH[0].I : 0;
                pack.ImgW = imgW;
                pack.ImgH = imgH;

                // ── 1.5 检测区域推域：有掩膜时把后续算子的计算范围缩到 ROI 内，
                // 小 ROI 大图可省掉绝大部分落在掩膜外的无效计算。语义与"先整图再交集"
                // 只差 ROI 边缘 1~2 像素的形态学行为，而边缘本来就是缝/背景。
                HObject workingGray = gray;
                if (p.HasMask)
                {
                    HOperatorSet.ReduceDomain(gray, p.Mask!, out HObject reduced);
                    temp.Add(reduced);
                    workingGray = reduced;
                }

                // ── 2. 二值化（三种方式各自的极性参数在此统一由 DetectTarget 映射） ──
                HObject region;
                switch (p.Mode)
                {
                    case ThresholdMode.Fixed:
                        // 固定阈值：区间本身表达极性，MinGray/MaxGray 已在归一化时排好序
                        HOperatorSet.Threshold(workingGray, out region, p.MinGray, p.MaxGray);
                        break;

                    case ThresholdMode.Auto:
                        if (p.DetectBrightAndDark)
                        {
                            // 亮暗同检：两种极性各分一次，候选区取并集（Otsu 的分割点对亮/暗各算各的，
                            // 直接拿一个分割点反向取区间是错的，所以这里必须跑两次）
                            HOperatorSet.BinaryThreshold(workingGray, out HObject lightPart, "max_separability", "light", out _);
                            temp.Add(lightPart);
                            HOperatorSet.BinaryThreshold(workingGray, out HObject darkPart, "max_separability", "dark", out _);
                            temp.Add(darkPart);
                            HOperatorSet.Union2(lightPart, darkPart, out region);
                        }
                        else
                        {
                            // 自动阈值：最大类间方差法，自动求分割点；极性决定取亮侧还是暗侧
                            HOperatorSet.BinaryThreshold(workingGray, out region, "max_separability", p.Polarity, out _);
                        }
                        break;

                    default: // Dynamic
                        if (p.DetectBrightAndDark)
                        {
                            // 亮暗同检：同上，两种极性各跑一次动态阈值再取并集
                            HOperatorSet.VarThreshold(workingGray, out HObject lightPart, p.VarMaskWidth, p.VarMaskHeight,
                                p.VarStdDevScale, p.VarAbsThreshold, "light");
                            temp.Add(lightPart);
                            HOperatorSet.VarThreshold(workingGray, out HObject darkPart, p.VarMaskWidth, p.VarMaskHeight,
                                p.VarStdDevScale, p.VarAbsThreshold, "dark");
                            temp.Add(darkPart);
                            HOperatorSet.Union2(lightPart, darkPart, out region);
                        }
                        else
                        {
                            // 动态阈值：局部窗口统计背景，能吃掉光照不均
                            HOperatorSet.VarThreshold(workingGray, out region, p.VarMaskWidth, p.VarMaskHeight,
                                p.VarStdDevScale, p.VarAbsThreshold, p.Polarity);
                        }
                        break;
                }
                temp.Add(region);

                // ── 2.5 排除区域（可选，未连线整段跳过）：从候选区挖掉固定误检源。
                // 检测区域已由 1.5 的推域消化，这里不再重复交集。
                HObject scoped = region;
                if (p.HasExclude)
                {
                    HOperatorSet.Difference(scoped, p.Exclude!, out HObject excluded);
                    temp.Add(excluded);
                    scoped = excluded;
                }

                // ── 3. 形态学（填 0 表示不做该步） ──
                HObject shaped = scoped;
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

                // ── 8. 候选区特征一次算完：筛选 / 排序 / 输出三处共用同一份，不再重复调算子 ──
                HOperatorSet.AreaCenter(shapeSelected, out HTuple candArea, out HTuple candRow, out HTuple candCol);
                HOperatorSet.SmallestRectangle1(shapeSelected,
                    out HTuple candR1, out HTuple candC1, out HTuple candR2, out HTuple candC2);

                // ── 9. 长宽比 / 触边筛选 + 排序：决定"保留哪些、按什么顺序" ──
                var keep = BuildKeepOrder(candArea, candRow, candCol, candR1, candC1, candR2, candC2, p, imgW, imgH);
                bool identityOrder = keep.Count == candArea.Length
                                     && keep.SequenceEqual(Enumerable.Range(0, keep.Count));
                HObject defectsObj = identityOrder ? shapeSelected : RebuildRegion(shapeSelected, keep);
                if (!identityOrder) temp.Add(defectsObj);

                // 产物从登记表摘除：所有权随包转移（temp 收尾不再释放它）
                pack.Candidates = defectsObj;
                temp.Remove(defectsObj);

                // ── 10. 逐项特征：按 keep 顺序从候选特征里挑（不再对缺陷集重复调算子）──
                pack.Areas = PickTuple(candArea, keep);
                pack.Rows = PickTuple(candRow, keep);
                pack.Cols = PickTuple(candCol, keep);
                pack.C1 = PickTuple(candC1, keep);
                pack.C2 = PickTuple(candC2, keep);
                pack.R1 = PickTuple(candR1, keep);
                pack.R2 = PickTuple(candR2, keep);

                return pack;
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
        /// 排序 + 逐项特征 + 判定 + 标注渲染：基于检测产物跑。
        /// 检测参数没变时（预览缓存命中）本方法只有排序与渲染，毫秒级。
        /// src 用作标注底图（预览缓存传"预览私有副本"，运行期传当轮源图）。
        /// highlightIndex 大于 0 时（1 基，对应缺陷清单/端口顺序）在标注图上额外套黄色高亮框。
        /// </summary>
        private BlobResult ArrangeJudgeRender(DetectPack pack, HImage src, BlobParams p, ILogService? logger, int highlightIndex = 0)
        {
            var result = new BlobResult();
            var temp = new List<HObject>();
            try
            {
                // ── 1. 长宽比 / 触边筛选 + 排序 + 重建有序缺陷集 ──
                // 全保留且顺序没变时直接用候选集（省一次重建）；
                // 需要重排时 RebuildRegion 的中间对象登记进 temp，随 finally 释放
                var keep = BuildKeepOrder(pack.Areas, pack.Rows, pack.Cols, pack.R1, pack.C1, pack.R2, pack.C2, p, pack.ImgW, pack.ImgH);
                bool identityOrder = keep.Count == pack.Areas.Length
                                     && keep.SequenceEqual(Enumerable.Range(0, keep.Count));
                HObject defectsObj = identityOrder ? pack.Candidates : RebuildRegion(pack.Candidates, keep);
                if (!identityOrder) temp.Add(defectsObj);

                // ── 2. 计数与逐项特征（按输出顺序从候选特征里挑）──
                HOperatorSet.CountObj(defectsObj, out HTuple number);
                result.DefectCount = number.Length > 0 ? number[0].I : 0;

                result.Areas = PickTuple(pack.Areas, keep);
                result.CenterRows = PickTuple(pack.Rows, keep);
                result.CenterCols = PickTuple(pack.Cols, keep);
                result.Widths = BuildSizeTuple(PickTuple(pack.C1, keep), PickTuple(pack.C2, keep));
                result.Heights = BuildSizeTuple(PickTuple(pack.R1, keep), PickTuple(pack.R2, keep));

                // ── 3. 逐缺陷形状特征（圆度/方向角对最终缺陷集取，长宽比由宽高自算）──
                // phi 是等效椭圆主轴角（弧度 -π/2~π/2，逆时针为正），转成度输出；
                // 圆形缺陷的 phi 无意义（任意值）。整段 try/catch：特征是"锦上添花"的输出，
                // 算不出来只丢这三列，绝不能把一次成功的检测判成失败。
                try
                {
                    if (result.DefectCount > 0)
                    {
                        HOperatorSet.RegionFeatures(defectsObj, "circularity", out HTuple circularities);
                        HOperatorSet.RegionFeatures(defectsObj, "phi", out HTuple phisRad);
                        result.Circularities = circularities;

                        var phisDeg = new double[phisRad.Length];
                        for (int i = 0; i < phisRad.Length; i++)
                            phisDeg[i] = phisRad[i].D * 180.0 / Math.PI;
                        result.Phis = new HTuple(phisDeg);
                    }
                    result.AspectRatios = BuildAspectTuple(result.Widths, result.Heights);
                }
                catch (Exception fex)
                {
                    logger?.Warn($"{InstanceName} 逐缺陷特征计算失败（圆度/长宽比/方向角输出为空）：{fex.Message}");
                    result.Circularities = new HTuple();
                    result.AspectRatios = new HTuple();
                    result.Phis = new HTuple();
                }

                result.MaxArea = (result.DefectCount > 0 && result.Areas.Length > 0) ? result.Areas.TupleMax().D : 0;
                result.TotalArea = (result.DefectCount > 0 && result.Areas.Length > 0) ? result.Areas.TupleSum().D : 0;

                // 像素当量只影响 mm² 输出，不参与判定——避免"改了当量就改判定结果"这种隐式耦合
                double mmPerPx2 = p.PixelSizeMm * p.PixelSizeMm;
                result.MaxAreaMm2 = result.MaxArea * mmPerPx2;
                result.TotalAreaMm2 = result.TotalArea * mmPerPx2;

                // 回填给渲染文案用（标注图左上角那几行）
                pack.MaxArea = result.MaxArea;
                pack.TotalArea = result.TotalArea;

                // ── 4. 判定（纯函数；MaxSingleArea 与其余规格对齐为"0 = 不判定"）──
                (result.IsOk, result.NgReason) = JudgeCore(result.DefectCount, result.MaxArea, result.TotalArea, p);

                // ── 5. 缺陷区域移交（new 一份独立句柄交给端口；检测包里的候选集照常归包管）──
                result.Defects = new HRegion(defectsObj);

                // ── 6. 标注图（渲染只是"给人看"，失败最多丢 DefectImage，绝不能把成功判成失败）──
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

                    // 渲染防护：噪声图上几千个 blob 时逐个画圈+编号会把渲染拖垮。
                    // 按**输出顺序**取前 N 个 —— 编号 1..N 与端口/缺陷清单仍然一一对应；
                    // 输出端口不受影响，永远全量
                    HObject renderTargets = defectsObj;
                    if (result.DefectCount > MaxRenderMarkers)
                    {
                        renderTargets = RebuildRegion(defectsObj, Enumerable.Range(0, MaxRenderMarkers).ToList());
                        temp.Add(renderTargets);
                        lines.Add($"（仅绘制前 {MaxRenderMarkers} 个缺陷）");
                    }

                    result.AnnotatedImage = _renderer.Render(
                        displayBase,
                        renderTargets,
                        lines.ToArray(),
                        result.IsOk ? "green" : "red",
                        highlightIndex > 0 ? BuildHighlightBox(pack, highlightIndex, temp) : null);
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

        /// <summary>渲染标注的最大缺陷数：噪声图上几千个 blob 时逐个画圈+编号会把渲染拖垮。
        /// 只限"画面"；输出端口与缺陷清单仍全量</summary>
        private const int MaxRenderMarkers = 300;

        /// <summary>判定核心（纯函数）：个数/面积与规格比对。MaxSingleArea 与其余规格对齐为"0 = 不判定"。</summary>
        private static (bool IsOk, string NgReason) JudgeCore(int count, double maxArea, double totalArea, BlobParams p)
        {
            bool isOk = !(count > p.MaxDefectCount
                          || count < p.MinDefectCount
                          || (p.MaxSingleArea > 0 && maxArea > p.MaxSingleArea)
                          || (p.MaxTotalArea > 0 && totalArea > p.MaxTotalArea));
            return (isOk, BuildNgReason(count, maxArea, totalArea, p));
        }

        /// <summary>按保留顺序从候选特征里挑出输出特征（keep 为空 = 空元组）</summary>
        private static HTuple PickTuple(HTuple all, List<int> keep)
        {
            if (keep.Count == 0) return new HTuple();

            var values = new double[keep.Count];
            for (int i = 0; i < keep.Count; i++) values[i] = all[keep[i]].D;
            return new HTuple(values);
        }

        /// <summary>清单联动高亮：给第 index 个缺陷（1 基）生成外扩 5px 的包围盒（黄框）</summary>
        private HObject? BuildHighlightBox(DetectPack pack, int index, List<HObject> temp)
        {
            if (index <= 0 || index > pack.Areas.Length) return null;

            HOperatorSet.GenRectangle1(out HObject box,
                Math.Max(0, pack.R1[index - 1].D - 5), Math.Max(0, pack.C1[index - 1].D - 5),
                Math.Min(pack.ImgH - 1.0, pack.R2[index - 1].D + 5), Math.Min(pack.ImgW - 1.0, pack.C2[index - 1].D + 5));
            temp.Add(box);
            return box;
        }

        /// <summary>检测结果包：select_shape 之后的候选区域 + 候选特征。
        /// 所有权随包（Dispose 释放候选区域）；配置预览会把它跨轮缓存复用</summary>
        private sealed class DetectPack : IDisposable
        {
            public HObject Candidates = null!;
            public int ImgW;
            public int ImgH;
            public HTuple Areas = new();
            public HTuple Rows = new();
            public HTuple Cols = new();
            public HTuple R1 = new();
            public HTuple C1 = new();
            public HTuple R2 = new();
            public HTuple C2 = new();

            /// <summary>排序后的最大/总面积（由排序+判定阶段回填，供渲染文案使用）</summary>
            public double MaxArea;
            public double TotalArea;

            public void Dispose() => Candidates?.Dispose();
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
            HOperatorSet.CountChannels(src, out HTuple channels);

            // 4 通道（RGBA/BGRA）：disp_obj 只稳妥支持 1/3 通道，4 通道直接画会报"通道数不对"，
            // 整张标注图就没了。先丢 alpha 转成 3 通道再走显示流程（检测走的是 TryToGrayImage，不受影响）
            HObject displaySource = src;
            if (channels.I == 4)
            {
                displaySource = DropAlphaChannel(src, temp);
                HOperatorSet.CountChannels(displaySource, out channels);
            }

            HOperatorSet.GetImageType(displaySource, out HTuple type);
            if (type.S == "byte") return displaySource;

            // 多通道（如彩色 uint2）不参与灰度范围度量，直接转
            if (channels.I != 1)
            {
                HOperatorSet.ConvertImageType(displaySource, out HObject direct, "byte");
                temp.Add(direct);
                return direct;
            }

            // HALCON 图形显示只稳妥支持 byte。但 convert_image_type(...,'byte') 是"截断"不是"缩放"：
            // 实测 uint2 灰度 1632 转出来就是 255，12/16 位相机的标注底图会整片死白，底图信息全丢。
            // 所以先按"实际灰度范围"线性拉伸到 0~255 再转。
            HOperatorSet.GetDomain(displaySource, out HObject domain);
            temp.Add(domain);
            HOperatorSet.MinMaxGray(domain, displaySource, 0, out HTuple minT, out HTuple maxT, out HTuple _);
            double gMin = minT.Length > 0 ? minT[0].D : 0;
            double gMax = maxT.Length > 0 ? maxT[0].D : 0;

            if (double.IsNaN(gMin) || double.IsNaN(gMax) || gMax - gMin < 1e-9)
            {
                // 退化（均匀图/空图）：拉伸没有意义，直接转（反正没有层次可保留）
                HOperatorSet.ConvertImageType(displaySource, out HObject flat, "byte");
                temp.Add(flat);
                return flat;
            }

            double k = 255.0 / (gMax - gMin);
            HOperatorSet.ScaleImage(displaySource, out HObject scaled, k, -gMin * k);
            temp.Add(scaled);
            HOperatorSet.ConvertImageType(scaled, out HObject converted, "byte");
            temp.Add(converted);
            return converted;
        }

        /// <summary>
        /// 取前 3 通道组成一张 3 通道图（丢掉第 4 通道 alpha）。
        /// HALCON 没有一步到位的"去通道"算子，用 append_channel 两步拼出来；
        /// 全部中间对象登记进 temp，由调用方 finally 统一释放。
        /// </summary>
        private static HObject DropAlphaChannel(HObject src, List<HObject> temp)
        {
            HOperatorSet.AccessChannel(src, out HObject c1, 1);
            HOperatorSet.AccessChannel(src, out HObject c2, 2);
            HOperatorSet.AccessChannel(src, out HObject c3, 3);
            temp.Add(c1);
            temp.Add(c2);
            temp.Add(c3);

            HOperatorSet.AppendChannel(c1, c2, out HObject c12);
            temp.Add(c12);
            HOperatorSet.AppendChannel(c12, c3, out HObject rgb);
            temp.Add(rgb);
            return rgb;
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
                DetectBrightAndDark = DetectBrightAndDark,
                Polarity = DetectTarget == DetectTarget.Bright ? "light" : "dark",
            };

            // 区域端口只在归一化时取一次：本轮运行内固定不变（避免流程跑到一半上游换图造成半新半旧）
            var mask = MaskRegion.ActualValue;
            p.Mask = mask != null && mask.IsInitialized() ? mask : null;
            var exclude = ExcludeRegion.ActualValue;
            p.Exclude = exclude != null && exclude.IsInitialized() ? exclude : null;

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
            if (p.MaxSingleArea > 0 && maxArea > p.MaxSingleArea)
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

        /// <summary>由宽/高元组算长宽比（长边/短边；短边夹到 ≥1 防除零）</summary>
        private static HTuple BuildAspectTuple(HTuple widths, HTuple heights)
        {
            int n = Math.Min(widths.Length, heights.Length);
            if (n <= 0) return new HTuple();

            var values = new double[n];
            for (int i = 0; i < n; i++)
            {
                double w = widths[i].D, h = heights[i].D;
                values[i] = Math.Max(w, h) / Math.Max(Math.Min(w, h), 1);
            }
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
        ///
        /// 特征由调用方一次算好传入（候选区的 area/中心/外接矩形），
        /// 这里不再对同一批区域重复调算子。
        /// </summary>
        /// <returns>保留下来的缺陷在源区域数组中的索引（0 基），顺序即输出顺序</returns>
        private static List<int> BuildKeepOrder(
            HTuple areas, HTuple rows, HTuple cols,
            HTuple r1, HTuple c1, HTuple r2, HTuple c2,
            BlobParams p, int imgW, int imgH)
        {
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

    /// <summary>
    /// 配置界面「缺陷清单」的一行（只读快照）。
    /// 字段语义与同名输出端口一致，顺序即输出端口顺序（Index 与标注图上写的编号一致，1 基）。
    /// 属性全部 get; init; —— 行是刷新时的不可变快照，不存在"表格里的值被改了"这种事。
    /// </summary>
    public sealed class DefectRow
    {
        /// <summary>1 基编号（与标注图编号一致）</summary>
        public int Index { get; init; }
        /// <summary>面积（像素）</summary>
        public double Area { get; init; }
        /// <summary>圆度（0~1，1=正圆）</summary>
        public double Circularity { get; init; }
        /// <summary>长宽比（外接矩形 长边/短边）</summary>
        public double AspectRatio { get; init; }
        /// <summary>外接矩形宽（像素）</summary>
        public double Width { get; init; }
        /// <summary>外接矩形高（像素）</summary>
        public double Height { get; init; }
        /// <summary>中心行坐标（像素）</summary>
        public double Row { get; init; }
        /// <summary>中心列坐标（像素）</summary>
        public double Col { get; init; }
    }
}
