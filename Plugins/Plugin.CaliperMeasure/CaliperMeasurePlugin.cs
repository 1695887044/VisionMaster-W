using Core.Commands;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CaliperMeasure.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 卡尺测量插件（流程节点 + 配置界面 ViewModel 合一，与仓库既有插件同一套写法）。
    ///
    /// 它做的事：在用户拖出的"搜索区矩形"内自动均布 N 把卡尺 → 每把卡尺沿 L1(Phi) 轴扫描灰度跳变
    /// （亚像素边缘）→ 宽度/间隙用 measure_pairs 取成对边缘间距、两点距用 measure_pos 各取一个边点后
    /// distance_pp → 乘像素当量得物理值 → 与标准值/上下公差比对给 OK/NG → 叠一张"原图 + 卡尺 + 边缘点 +
    /// 测量连线 + 判定文字"的标注图。
    ///
    /// 角色说明（务必看清）：
    ///  · 作为流程节点：只干 RunAlgorithm 一件事——跑算法、给 8 个输出端口赋值；
    ///  · 作为配置界面 ViewModel：承载参数、画布搜索区、标注图预览、状态栏，负责"改参数即时看到结果"。
    /// 主程序会给"配置"和"编译执行"各造一个实例，两者共用一份 ExecuteMeasureCore 代码。
    ///
    /// 本批（第二批）在第一批骨架上补齐三种拟合类测量：
    ///  · 点到线距：区1 出代表点（同两点距口径），区2 的 N 个边点 fit_line_contour_xld 拟合直线 → distance_pl 垂距；
    ///  · 圆直径：圆形搜索区沿圆周径向均布 N 把卡尺（扫描方向=径向）→ fit_circle_contour_xld → 直径=2×半径；
    ///  · 角度：两个搜索区各拟合一条直线 → angle_ll → 归一化到 0~180°（角度是量纲无关量，不受像素当量影响）。
    /// 拟合参数区（拟合方式/最大残差/最少点数/环宽）随类型显隐；FittedGeometry 端口对拟合类
    /// 类型承载真正的拟合几何（直线/圆 XLD，角度=两条拟合线），宽度/两点距仍承载测量连线（第一批语义）。
    /// </summary>
    [Display(
        Name = "卡尺测量",
        GroupName = "测量",
        Description = "阵列式卡尺测量：自动均布 N 把卡尺做亚像素找边，支持宽度/间隙、两点距、点到线距、圆直径与角度；输出物理值、偏差与 OK/NG",
        ShortName = "\uf545"
    )]
    public class CaliperMeasurePlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 出厂默认值（唯一真相）

        // 为什么把默认值抽成常量、字段初值只引用它们：
        // 这些值是"通用工业视觉平台的起点"，不针对任何样图；集中一处便于复核与后续调整，
        // 也避免"字段初值与校验基准各写一份"造成静默失配。
        // 逐项依据见各参数注释（每条都写"为什么是这个值"）。

        private const MeasureKind DefaultMeasureKind = MeasureKind.Width;

        /// <summary>卡尺数 N 默认 5：工业常用起步值，兼顾抗噪（多点平均）与速度（每把一次测量）</summary>
        private const int DefaultCaliperCount = 5;

        /// <summary>Sigma 默认 1.0：HALCON 测量例程常用的轻度平滑，压高频噪声又不糊掉边缘</summary>
        private const double DefaultSigma = 1.0;

        /// <summary>边缘阈值默认 30：HALCON 测量例程（含仓库脚本模板库）的常用对比度门限</summary>
        private const double DefaultEdgeThreshold = 30;

        /// <summary>极性默认"全部"：不预设明暗方向，最宽容的起点（现场再按预览收紧）</summary>
        private const EdgePolarity DefaultPolarity = EdgePolarity.All;

        /// <summary>选择默认"全部"：不预设取哪条边；本插件对"全部"的取值口径见 ExecuteMeasureCore 注释</summary>
        private const EdgeSelect DefaultSelect = EdgeSelect.All;

        /// <summary>插值默认 bilinear：亚像素精度与速度的通用折中（nearest 只有整像素，bicubic 最慢）</summary>
        private const EdgeInterpolation DefaultInterpolation = EdgeInterpolation.Bilinear;

        // 判定规格默认 0/0/0 是"有意的零容忍"：本插件是通用平台算子，不该假装知道产品的规格值。
        // 用户看到默认即 NG，会立刻意识到"要去填标准值与上下公差"——与 BlobDetect 的 MaxDefectCount=0
        // （一个缺陷都不允许）保持同一风格：安全侧默认，宁严勿松。
        private const double DefaultStandardValue = 0;
        private const double DefaultUpperTolerance = 0;
        private const double DefaultLowerTolerance = 0;

        /// <summary>像素当量默认 1.0 mm/pixel：等于直接输出像素值，不假设任何标定（项目暂无标定模块）</summary>
        private const double DefaultPixelSizeMm = 1.0;

        // ── 第二批：拟合参数默认值（通用起点，逐条注明依据；绝不针对任何样图调） ──

        /// <summary>
        /// 拟合方式默认 Tukey：卡尺偶尔扫到毛刺会产生离群点，最小二乘（regression）会被一个坏点
        /// 拽歪整条线/圆；Tukey 对残差超限的点大幅降权，是工业拟合的通用默认（抗离群能力最强）。
        /// </summary>
        private const FitAlgorithmKind DefaultFitAlgorithm = FitAlgorithmKind.Tukey;

        /// <summary>
        /// 最大残差默认 1.0 px：边缘点来自亚像素 measure_pos，正常应贴着真实特征边，
        /// 主体点残差超过 1px 说明混入了明显杂边或特征本身弯曲，拟合结果不可信。
        /// 注意口径：本插件用它约束"边缘点到拟合几何距离的中位数"（主体点贴合度），
        /// 而不是单点最大值——若按最大值检查，一个毛刺离群点就会把 Tukey 已正确拟合的结果判死，
        /// 鲁棒拟合就失去意义；中位数与 Tukey 的容错哲学一致（少数离群点拉不动中位数）。
        /// </summary>
        private const double DefaultFitMaxError = 1.0;

        /// <summary>
        /// 最少点数默认 3：直线拟合数学下限 2 点（2 点定线）、圆拟合下限 3 点（3 点定圆）；
        /// 取 3 起步 = 直线有 1 点冗余可抗噪、圆正好达数学下限，是兼顾两者的通用起点。
        /// </summary>
        private const int DefaultFitMinPoints = 3;

        /// <summary>
        /// 环宽（圆直径的径向搜索范围）默认 30 px：用户手画的圆其半径/圆心误差通常在几个到十几个像素，
        /// 30px（±15px）提供宽容差；再大会增加扫入相邻特征的风险，再小则容不下手摆误差。
        /// </summary>
        private const double DefaultAnnulusWidth = 30.0;

        /// <summary>
        /// 圆周卡尺的默认半宽 3.0 px：卡尺宽度的作用是 measure_pos 扫描时垂直于扫描方向的
        /// 灰度平均窗口——太窄噪声大、太宽会混入相邻特征，3~5px 是工业常规窗口；
        /// 当卡尺间距更小时自动夹到间距一半，避免相邻卡尺窗口互相污染。
        /// </summary>
        private const double CircleCaliperHalfWidth = 3.0;

        /// <summary>角度归一化的弧度→度换算依据（180/π）；angle_ll 返回弧度</summary>
        private const double RadToDeg = 180.0 / Math.PI;

        /// <summary>
        /// Sigma 下限 0.4：HALCON 测量算子对高斯滤波核有最小尺寸要求——Sigma 取 0 会直接抛
        /// #1302（Wrong value of control parameter），所以这里按算子要求设下限，
        /// 用户把界面值填成 0/负数时静默夹到 0.4，而不是让整个流程炸掉。
        /// </summary>
        private const double SigmaClampMin = 0.4, SigmaClampMax = 100;
        private const double ThresholdClampMin = 1, ThresholdClampMax = 255;
        private const int CaliperCountClampMin = 1, CaliperCountClampMax = 1000;
        private const double PixelSizeClampMin = 1e-9, PixelSizeClampMax = 1e6;
        private const double ToleranceAbsMax = 1e9;

        // 拟合参数的合法区间：越界值喂给算子/检查逻辑会产生无意义结果，静默夹取（"非法值不能炸"）
        private const double FitMaxErrorClampMin = 0.05, FitMaxErrorClampMax = 1e6;
        private const int FitMinPointsClampMin = 2, FitMinPointsClampMax = 1000;
        private const double AnnulusWidthClampMin = 2.0, AnnulusWidthClampMax = 1e5;

        /// <summary>两拟合线判"接近平行"的阈值：夹角小于约 0.1° 时交点数值上不可信，报错而不是硬算</summary>
        private const double ParallelSinThreshold = 0.0017;

        /// <summary>搜索区半长/半宽下限：小于它算退化矩形（喂给算子会得到无意义结果或抛异常）</summary>
        private const double MinRectHalfSize = 1.0;

        /// <summary>单把卡尺的最小半宽：N 很大时防止半宽被均分到 0 造成退化测量对象</summary>
        private const double MinCaliperHalfWidth = 0.5;

        /// <summary>判定用的浮点容差：公差为 0 时避免浮点噪声把"恰好等于标准"判成 NG</summary>
        private const double JudgeEpsilon = 1e-9;

        #endregion

        #region 输入 / 输出端口（名字即连线名，编译期就存在，供按名连线）

        /// <summary>待测量的输入图像（可链接上游；未连线时按既有插件惯例给中文提示后判失败）</summary>
        public InputPort<HImage> SrcImage { get; } = new("SrcImage", description: "待测量的输入图像");

        /// <summary>测量物理值（像素测量值 × 像素当量）</summary>
        public OutputPort<double> MeasureValue { get; } = new("MeasureValue", "测量值（已乘像素当量）");

        /// <summary>与标准值的偏差（带符号：测大为正、测小为负）</summary>
        public OutputPort<double> Deviation { get; } = new("Deviation", "与标准值的偏差（带符号）");

        /// <summary>判定结果（true = OK 合格）</summary>
        public OutputPort<bool> IsOk { get; } = new("IsOk", "判定结果（true = OK 合格）");

        /// <summary>标注图：原图 + 卡尺小矩形 + 边缘点十字 + 测量连线/拟合几何 + 左上角文字</summary>
        public OutputPort<HImage> MeasureImage { get; } = new("MeasureImage", "标注图（卡尺 + 边缘点 + 测量连线/拟合几何 + 判定文字）");

        /// <summary>
        /// 测量几何（XLD）。第二批起语义按类型区分：
        ///  · 拟合类（点到线距/圆直径/角度）：承载真正的拟合几何——拟合直线 / 拟合圆（gen_circle_contour_xld
        ///    生成的整圆轮廓）/ 角度=两条拟合线（2 个轮廓对象）；
        ///  · 非拟合类（宽度/两点距，第一批语义保持不变）：承载测量连线（各卡尺两边缘连线 / 两代表点连线）。
        /// 端口名与类型不变，下游按自身需要取用。
        /// </summary>
        public OutputPort<HXLD> FittedGeometry { get; } = new("FittedGeometry", "拟合几何（直线/圆/角度两线；宽度与两点距=测量连线）");

        /// <summary>检出的原始边缘点（十字 XLD），供下游或叠加显示</summary>
        public OutputPort<HXLD> EdgePoints { get; } = new("EdgePoints", "检出的原始边缘点（十字 XLD）");

        #endregion

        #region 配置参数（[StepConfig] 由基类随 InputValues 一并存盘/灌值）

        #region ① 测量类型

        private MeasureKind _measureKind = DefaultMeasureKind;
        /// <summary>测量类型（5 种；切换时界面按类型提示所需搜索区数量/形状，拟合参数区随类型显隐）</summary>
        [StepConfig]
        public MeasureKind MeasureKind
        {
            get => _measureKind;
            set
            {
                if (!SetProperty(ref _measureKind, value)) return;
                // 类型变了：所需搜索区数量/形状、界面分区显隐都得跟着走
                OnPropertyChanged(nameof(RequiredRegionCount));
                OnPropertyChanged(nameof(RegionCountHint));
                OnPropertyChanged(nameof(IsWidthMeasure));
                OnPropertyChanged(nameof(IsPointToPointMeasure));
                OnPropertyChanged(nameof(IsFitMeasure));
                OnPropertyChanged(nameof(IsCircleMeasure));
                // 缺搜索区就补足（只增不减，绝不删用户已经摆好的区域）
                EnsureRequiredRegions();
                SchedulePreview();
            }
        }

        #endregion

        #region ② 搜索区与卡尺

        private ObservableCollection<CaliperRegion> _caliperRegions = new();
        /// <summary>
        /// 搜索区列表（随方案持久化）。宽度/圆直径只用第 1 个（圆直径要求是圆形），
        /// 两点距/点到线距/角度用前 2 个（矩形）。每个矩形搜索区内部再按 CaliperCount 均布 N 把卡尺；
        /// 圆形搜索区沿圆周径向均布 N 把卡尺。
        /// </summary>
        [StepConfig]
        public ObservableCollection<CaliperRegion> CaliperRegions
        {
            get => _caliperRegions;
            set
            {
                _caliperRegions = value ?? new ObservableCollection<CaliperRegion>();
                SelectedRegion = null;   // 集合被整体替换（反序列化）后，旧选中项已不在新集合里
                OnPropertyChanged();
                OnPropertyChanged(nameof(RegionCountHint));
            }
        }

        private int _caliperCount = DefaultCaliperCount;
        /// <summary>卡尺数 N：每个搜索区内自动均布的卡尺条数（测量值取各卡尺的平均，多点抗噪）</summary>
        [StepConfig]
        public int CaliperCount
        {
            get => _caliperCount;
            set { if (SetProperty(ref _caliperCount, value)) SchedulePreview(); }
        }

        #endregion

        #region ③ 找边参数

        private double _sigma = DefaultSigma;
        /// <summary>平滑系数 Sigma：越大越平滑、抗噪越强，但边缘定位越"糊"</summary>
        [StepConfig]
        public double Sigma
        {
            get => _sigma;
            set { if (SetProperty(ref _sigma, value)) SchedulePreview(); }
        }

        private double _edgeThreshold = DefaultEdgeThreshold;
        /// <summary>边缘阈值：相邻像素最小灰度差，低于它的跳变不算边（太大→找不到边，太小→噪声当成边）</summary>
        [StepConfig]
        public double EdgeThreshold
        {
            get => _edgeThreshold;
            set { if (SetProperty(ref _edgeThreshold, value)) SchedulePreview(); }
        }

        private EdgePolarity _polarity = DefaultPolarity;
        /// <summary>边缘极性：沿扫描方向允许哪种明暗跳变</summary>
        [StepConfig]
        public EdgePolarity Polarity
        {
            get => _polarity;
            set { if (SetProperty(ref _polarity, value)) SchedulePreview(); }
        }

        private EdgeSelect _select = DefaultSelect;
        /// <summary>选择：每条卡尺上多条边缘时取第一条/最后一条/全部</summary>
        [StepConfig]
        public EdgeSelect Select
        {
            get => _select;
            set { if (SetProperty(ref _select, value)) SchedulePreview(); }
        }

        private EdgeInterpolation _interpolation = DefaultInterpolation;
        /// <summary>插值方式：直接对应 gen_measure_rectangle2 的 Interpolation 参数</summary>
        [StepConfig]
        public EdgeInterpolation Interpolation
        {
            get => _interpolation;
            set { if (SetProperty(ref _interpolation, value)) SchedulePreview(); }
        }

        #endregion

        #region ④ 拟合参数（仅拟合类测量显示：点到线距 / 圆直径 / 角度）

        // 第一批不涉及拟合，此区为空壳注释；第二批启用。仅当 MeasureKind ∈ {点到线距, 圆直径, 角度} 时
        // 界面才显示本分区（IsFitMeasure 控制显隐），避免"改了不起作用"的死控件。

        private FitAlgorithmKind _fitAlgorithm = DefaultFitAlgorithm;
        /// <summary>拟合方式（鲁棒算法）：直接对应 fit_line/circle_contour_xld 的 Algorithm 参数。默认 Tukey 抗离群点</summary>
        [StepConfig]
        public FitAlgorithmKind FitAlgorithm
        {
            get => _fitAlgorithm;
            set { if (SetProperty(ref _fitAlgorithm, value)) SchedulePreview(); }
        }

        private double _fitMaxError = DefaultFitMaxError;
        /// <summary>
        /// 最大残差（px）：边缘点到拟合几何距离的中位数上限，超限判拟合失败（中文说明）。
        /// 用中位数口径容忍少量毛刺离群点，与 Tukey 配套——依据见 DefaultFitMaxError 注释。
        /// </summary>
        [StepConfig]
        public double FitMaxError
        {
            get => _fitMaxError;
            set { if (SetProperty(ref _fitMaxError, value)) SchedulePreview(); }
        }

        private int _fitMinPoints = DefaultFitMinPoints;
        /// <summary>最少点数：参与拟合的最少边缘点数，不足判失败（直线 2 点定线、圆 3 点定圆）</summary>
        [StepConfig]
        public int FitMinPoints
        {
            get => _fitMinPoints;
            set { if (SetProperty(ref _fitMinPoints, value)) SchedulePreview(); }
        }

        private double _annulusWidth = DefaultAnnulusWidth;
        /// <summary>环宽（仅圆直径）：每把卡尺沿半径方向扫描的总宽度(px)，覆盖圆心/半径的摆放误差</summary>
        [StepConfig]
        public double AnnulusWidth
        {
            get => _annulusWidth;
            set { if (SetProperty(ref _annulusWidth, value)) SchedulePreview(); }
        }

        #endregion

        #region ⑤ 判定与标定

        private double _standardValue = DefaultStandardValue;
        /// <summary>标准值（名义尺寸）。默认 0 = 有意的零容忍起点，提示用户去填规格</summary>
        [StepConfig]
        public double StandardValue
        {
            get => _standardValue;
            set { if (SetProperty(ref _standardValue, value)) SchedulePreview(); }
        }

        private double _upperTolerance = DefaultUpperTolerance;
        /// <summary>上公差（允许偏差的正向上限，偏差 ≤ 上公差才算 OK）</summary>
        [StepConfig]
        public double UpperTolerance
        {
            get => _upperTolerance;
            set { if (SetProperty(ref _upperTolerance, value)) SchedulePreview(); }
        }

        private double _lowerTolerance = DefaultLowerTolerance;
        /// <summary>下公差（允许偏差的负向下限，偏差 ≥ 下公差才算 OK；可填负数实现非对称公差）</summary>
        [StepConfig]
        public double LowerTolerance
        {
            get => _lowerTolerance;
            set { if (SetProperty(ref _lowerTolerance, value)) SchedulePreview(); }
        }

        private double _pixelSizeMm = DefaultPixelSizeMm;
        /// <summary>像素当量（mm/pixel）。默认 1.0 = 输出像素值，不假设任何标定；将来有标定模块可改为连线取值</summary>
        [StepConfig]
        public double PixelSizeMm
        {
            get => _pixelSizeMm;
            set { if (SetProperty(ref _pixelSizeMm, value)) SchedulePreview(); }
        }

        #endregion

        #endregion

        #region 视图辅助属性（下拉数据源 / 分区显隐 / 数量提示）

        public MeasureKind[] MeasureKinds { get; } = (MeasureKind[])Enum.GetValues(typeof(MeasureKind));
        public EdgePolarity[] Polarities { get; } = (EdgePolarity[])Enum.GetValues(typeof(EdgePolarity));
        public EdgeSelect[] EdgeSelects { get; } = (EdgeSelect[])Enum.GetValues(typeof(EdgeSelect));
        public EdgeInterpolation[] Interpolations { get; } = (EdgeInterpolation[])Enum.GetValues(typeof(EdgeInterpolation));
        public FitAlgorithmKind[] FitAlgorithms { get; } = (FitAlgorithmKind[])Enum.GetValues(typeof(FitAlgorithmKind));

        public bool IsWidthMeasure => MeasureKind == MeasureKind.Width;
        public bool IsPointToPointMeasure => MeasureKind == MeasureKind.PointToPoint;

        /// <summary>是否为拟合类测量（决定「拟合参数」分区显隐）：点到线距 / 圆直径 / 角度</summary>
        public bool IsFitMeasure => MeasureKind is MeasureKind.PointToLineDistance
                                              or MeasureKind.CircleDiameter
                                              or MeasureKind.Angle;

        /// <summary>是否为圆直径测量（决定「环宽」输入框显隐）</summary>
        public bool IsCircleMeasure => MeasureKind == MeasureKind.CircleDiameter;

        /// <summary>当前测量类型需要的搜索区个数（宽度/圆直径=1 个、其余=2 个）</summary>
        public int RequiredRegionCount => RequiredRegionCountOf(MeasureKind);

        /// <summary>搜索区数量提示（界面灰字，让用户随时知道够不够）</summary>
        public string RegionCountHint =>
            $"当前测量类型需要 {RequiredRegionCount} 个{(MeasureKind == MeasureKind.CircleDiameter ? "圆形" : "矩形")}搜索区，已配置 {CaliperRegions.Count} 个";

        private static int RequiredRegionCountOf(MeasureKind kind) =>
            kind == MeasureKind.Width || kind == MeasureKind.CircleDiameter ? 1 : 2;

        #endregion

        #region 画布交互（与 Plugin.CreateRoi 同一套：画布拖拽 ↔ 参数面板数值框 双向同步）

        /// <summary>
        /// 画布集合（控件 DrawObjectList 的绑定源，VM 是唯一所有者）：
        /// 控件右键新建/删除直接改动此集合 → CollectionChanged 回写 CaliperRegions
        /// </summary>
        public ObservableCollection<DrawingObjectInfo> CanvasRegions { get; } = new();

        private DrawingObjectInfo? _canvasActiveRegion;
        /// <summary>画布编辑中的搜索区（控件 ActiveRoi 双向绑定）</summary>
        public DrawingObjectInfo? CanvasActiveRegion
        {
            get => _canvasActiveRegion;
            set
            {
                if (SetProperty(ref _canvasActiveRegion, value) && value != null)
                    SelectedRegion = CaliperRegions.FirstOrDefault(x => x.Name == value.RoiName);
            }
        }

        private CaliperRegion? _selectedRegion;
        /// <summary>当前选中搜索区（列表选中态；与 CanvasActiveRegion 双向联动，驱动参数微调面板）</summary>
        public CaliperRegion? SelectedRegion
        {
            get => _selectedRegion;
            set
            {
                if (_selectedRegion != null) _selectedRegion.ParamEdited -= OnSelectedRegionParamEdited;
                if (SetProperty(ref _selectedRegion, value) && _selectedRegion != null)
                {
                    _selectedRegion.ParamEdited += OnSelectedRegionParamEdited;
                    CanvasActiveRegion = CanvasRegions.FirstOrDefault(x => x.RoiName == _selectedRegion.Name);
                }
            }
        }

        public CaliperMeasurePlugin()
        {
            CanvasRegions.CollectionChanged += OnCanvasRegionsChanged;

            // 输入图像变了（换图/接上上游变量）也刷新预览，否则选了图还得再动一下参数才看得到
            SrcImage.ValueChanged += (_, _) => OnSourceImageChanged();

            _previewDebounce.Tick += (_, _) => { _previewDebounce.Stop(); RefreshPreview(); };

            DeleteSelectedRegionCommand = new RelayCommand(
                _ => DeleteSelectedRegion(),
                _ => System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox);
            ResetRegionsCommand = new RelayCommand(_ => ResetRegions());
        }

        /// <summary>画布集合变更（控件新建/删除/清空驱动）→ 回写 CaliperRegions 并刷新预览</summary>
        private void OnCanvasRegionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_seedingCanvas) return;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                    foreach (DrawingObjectInfo info in e.NewItems)
                    {
                        info.PropertyChanged += OnCanvasRegionTuplesChanged;
                        CaliperRegions.Add(new CaliperRegion
                        {
                            Name = info.RoiName,
                            ShapeType = info.ShapeType,
                            Params = info.HTuples.Select(t => t.D).ToArray()
                        });
                    }
                    OnRegionsChanged();
                    break;

                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                    foreach (DrawingObjectInfo info in e.OldItems)
                    {
                        info.PropertyChanged -= OnCanvasRegionTuplesChanged;
                        var region = CaliperRegions.FirstOrDefault(x => x.Name == info.RoiName);
                        if (region == null) continue;
                        CaliperRegions.Remove(region);
                        if (SelectedRegion == region) SelectedRegion = null;
                    }
                    OnRegionsChanged();
                    break;

                case NotifyCollectionChangedAction.Reset:
                    CaliperRegions.Clear();
                    SelectedRegion = null;
                    OnRegionsChanged();
                    break;
            }
        }

        /// <summary>
        /// 拖拽/松手回传：控件把句柄参数写进 info.HTuples（INPC）→ 同步 CaliperRegion.Params。
        /// 被删除的搜索区已退订，删除后的回写自动跳过
        /// </summary>
        private void OnCanvasRegionTuplesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples)) return;
            var info = (DrawingObjectInfo)sender!;
            var region = CaliperRegions.FirstOrDefault(x => x.Name == info.RoiName);
            if (region == null || info.HTuples == null) return;
            region.Params = info.HTuples.Select(t => t.D).ToArray();
            SchedulePreview();
        }

        /// <summary>选中搜索区的参数经数值框编辑 → 写入画布 info.HTuples（INPC → 控件应用句柄并重绘）</summary>
        private void OnSelectedRegionParamEdited(CaliperRegion region)
        {
            var info = CanvasRegions.FirstOrDefault(x => x.RoiName == region.Name);
            if (info != null)
                info.HTuples = region.Params.Select(p => new HTuple(p)).ToArray();
            SchedulePreview();
        }

        private void OnRegionsChanged()
        {
            OnPropertyChanged(nameof(RegionCountHint));
            SchedulePreview();
        }

        /// <summary>
        /// 删除当前选中搜索区（Del 键/删除按钮）。CanExecute 守卫焦点在文本框时不触发（Del 是文本编辑键）
        /// </summary>
        public void DeleteSelectedRegion()
        {
            if (SelectedRegion is not CaliperRegion region) return;
            var info = CanvasRegions.FirstOrDefault(x => x.RoiName == region.Name);
            if (info != null) CanvasRegions.Remove(info);
        }

        public ICommand DeleteSelectedRegionCommand { get; }
        public ICommand ResetRegionsCommand { get; }

        private bool _seedingCanvas;

        /// <summary>
        /// 重置搜索区：按当前测量类型重建默认搜索区（宽度=1 个、两点距=2 个）。
        /// 用户把区域拖乱/删光后一键回到可用起点。
        /// </summary>
        public void ResetRegions()
        {
            CanvasRegions.Clear();       // 触发 Reset → 清空 CaliperRegions
            AddDefaultRegions();         // 再按类型补足
        }

        /// <summary>
        /// 补足当前类型所需的搜索区（只增不减）。
        /// 只在"缺"的时候加，绝不删用户已经摆好的区域——删区域这种破坏性操作永远由用户显式触发。
        /// </summary>
        private void EnsureRequiredRegions()
        {
            if (_seedingCanvas) return;
            if (CaliperRegions.Count >= RequiredRegionCount) return;
            AddDefaultRegions();
        }

        /// <summary>
        /// 按当前类型补足默认搜索区。默认值依据（不针对任何样图）：
        ///  · 矩形类型（宽度/两点距/点到线距/角度）：把图像按所需数量切成等宽竖条，每个搜索区取本条的中间、
        ///    覆盖半宽 35% / 半高 25%，Phi=0（此时 L1 沿水平=扫描方向），正好"横跨"竖直走向的边——最常见的起手姿态；
        ///  · 圆直径：播种一个圆形搜索区，圆心取图像中心、半径取短边的 25%——一个"大概在画面中间"的通用起点。
        /// 图像尺寸未知时回退到 640×480（常见小画幅），用户拖一下即可。
        /// 注意：只补"数量"，不纠正"形状"——数量够但形状不对（如圆直径类型下是矩形）由运行期校验给中文指引，
        /// 这里绝不删/改用户已摆好的区域。
        /// </summary>
        private void AddDefaultRegions()
        {
            int need = RequiredRegionCount;
            var (imgW, imgH) = CurrentImageSizeOrFallback();
            bool circle = MeasureKind == MeasureKind.CircleDiameter;
            while (CanvasRegions.Count < need)
            {
                if (circle)
                {
                    var disc = new DrawingObjectInfo(
                        DrawShapeType.Circle,
                        new[] { new HTuple(imgH / 2.0), new HTuple(imgW / 2.0), new HTuple(Math.Min(imgW, imgH) * 0.25) },
                        NextRegionName());
                    CanvasRegions.Add(disc);   // 触发 CollectionChanged → 同步进 CaliperRegions
                    continue;
                }

                int index = CanvasRegions.Count;
                double bandW = (double)imgW / need;
                double cCol = bandW * (index + 0.5);
                double cRow = imgH / 2.0;
                double l1 = bandW * 0.35;
                double l2 = imgH * 0.25;
                var info = new DrawingObjectInfo(
                    DrawShapeType.Rectangle,
                    new[] { new HTuple(cRow), new HTuple(cCol), new HTuple(0.0), new HTuple(l1), new HTuple(l2) },
                    NextRegionName());
                CanvasRegions.Add(info);   // 触发 CollectionChanged → 同步进 CaliperRegions
            }
        }

        /// <summary>生成不重名的搜索区名（搜索区1、搜索区2…）</summary>
        private string NextRegionName()
        {
            int i = CanvasRegions.Count + 1;
            while (CanvasRegions.Any(x => x.RoiName == $"搜索区{i}")) i++;
            return $"搜索区{i}";
        }

        /// <summary>取当前输入图像尺寸；无图时回退 640×480</summary>
        private (int W, int H) CurrentImageSizeOrFallback()
        {
            var src = SrcImage.ActualValue;
            if (src != null && src.IsInitialized())
            {
                try
                {
                    src.GetImageSize(out int w, out int h);
                    if (w > 0 && h > 0) return (w, h);
                }
                catch { /* 取尺寸失败按回退处理 */ }
            }
            return (640, 480);
        }

        #endregion

        #region 预览（配置态：调参即时刷新标注图，做法照搬既有 BlobDetect / PreProcessing 插件）

        private readonly AnnotationRenderer _renderer = new();

        private HImage? _previewImage;
        /// <summary>
        /// 预览图（标注图 + 可拖拽搜索区叠加显示）。换图即弃旧：SetProperty 成功后释放旧实例，
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

        /// <summary>参数逐字符刷新，整幅图+多把卡尺算一遍不便宜，200ms 防抖（与既有插件同参数）</summary>
        private readonly DispatcherTimer _previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

        /// <summary>视图就绪信号（视图 Loaded 时调用）：补足搜索区并取输入图做预览底图</summary>
        public void OnViewLoaded()
        {
            EnsureRequiredRegions();
            RefreshPreview();
        }

        /// <summary>源图变化（换图/接上游）：补足搜索区 + 刷新预览；可能发生在非 UI 线程，先投递回去</summary>
        private void OnSourceImageChanged()
        {
            var dispatcher = UiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => { EnsureRequiredRegions(); SchedulePreview(); }));
                return;
            }
            EnsureRequiredRegions();
            SchedulePreview();
        }

        private void SchedulePreview()
        {
            // 流程线程上跑算法时参数纠偏也会发通知，一路调到这儿。
            // DispatcherTimer 只能在创建它的线程上启停，所以非 UI 线程先投递回去。
            var dispatcher = UiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(SchedulePreview));
                return;
            }
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        /// <summary>UI 线程调度器；没有 Application（单元测试/离线跑流程）时为 null，此时按"就在 UI 线程"处理</summary>
        private static Dispatcher? UiDispatcher => System.Windows.Application.Current?.Dispatcher;

        /// <summary>
        /// 重算预览：UI 线程同步执行，因此不存在"图被流程线程释放掉"的竞态。
        /// 与 RunAlgorithm 共用 ExecuteMeasureCore，保证"所见即所得"。
        /// </summary>
        public void RefreshPreview()
        {
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                PreviewImage = null;
                SetStatus("输入图像为空：请为上方「输入图像」指定图片，或连接上游图像端口", StatusLevel.Warning);
                return;
            }

            MeasureResult result;
            try
            {
                result = ExecuteMeasureCore(src, null);
            }
            catch (Exception ex)
            {
                // 预览不弹框、不抛：把原因写在信息栏，用户改参数重试即可
                SetStatus($"预览失败：{ex.Message}", StatusLevel.Error);
                return;
            }

            if (result.Failed)
            {
                // 失败时预览图/几何都不需要，释放掉（避免非托管内存堆积）
                result.DisposeOutputs();
                PreviewImage = null;
                SetStatus(result.FailMessage, StatusLevel.Error);
                return;
            }

            PreviewImage = result.MeasureImage;   // setter 负责释放上一张预览图
            // 预览只关心"看着对不对"，几何输出没处放，用完即弃
            result.DisposeGeometry();

            SetStatus(result.Summary, result.IsOk ? StatusLevel.Info : StatusLevel.Error);
        }

        #endregion

        #region 插件生命周期

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CaliperMeasureView { DataContext = this };
        }

        /// <summary>配置初始化：灌入配置后按 CaliperRegions 播种画布集合并补足搜索区</summary>
        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);
            _seedingCanvas = true;
            try
            {
                CanvasRegions.Clear();
                foreach (var region in CaliperRegions)
                    CanvasRegions.Add(new DrawingObjectInfo(
                        region.ShapeType, region.Params.Select(p => new HTuple(p)).ToArray(), region.Name));
            }
            finally { _seedingCanvas = false; }
        }

        /// <summary>配置实例释放：预览图、防抖计时器、离屏渲染窗口（直接清字段，避免析构期触发 INPC）</summary>
        public override void Dispose()
        {
            _previewDebounce.Stop();
            PreviewImage = null;                 // setter 释放预览图
            // 画布搜索区兜底释放：正常路径控件 Unloaded 已处置，此处覆盖控件未挂接/异常路径（Dispose 幂等）
            foreach (var info in CanvasRegions)
                info.Dispose();
            _renderer.Dispose();
            base.Dispose();                      // 输出端口里的 HImage / HXLD 交给基类统一回收
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
            //（HImage/HXLD），double/bool 端口的上一轮值会原样留下——
            // 本轮若失败，下游会读到上一轮的脏数据（这是极易漏的镜像 bug）
            MeasureValue.Value = 0;
            Deviation.Value = 0;
            IsOk.Value = false;

            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                return;
            }

            MeasureResult result;
            try
            {
                result = ExecuteMeasureCore(src, context.Logger);
            }
            catch (Exception ex)
            {
                // 带上"哪一步在干嘛"的上下文前缀，比基类兜底的"异常类型: 消息"更好排查
                Fail($"卡尺测量失败：{ex.Message}");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            if (result.Failed)
            {
                result.DisposeOutputs();
                Fail(result.FailMessage);
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            // 端口赋值：HImage / HXLD 的所有权移交端口，由基类在下一轮开头或 Dispose 时回收。
            // 渲染失败时标注图为 null，这里跳过赋值，端口保持轮首的"空"状态
            if (result.MeasureImage != null) MeasureImage.TypedValue = result.MeasureImage;
            if (result.FittedGeometry != null) FittedGeometry.TypedValue = result.FittedGeometry;
            if (result.EdgePoints != null) EdgePoints.TypedValue = result.EdgePoints;

            MeasureValue.Value = result.MeasureValueMm;
            Deviation.Value = result.Deviation;
            IsOk.Value = result.IsOk;

            // NG 是正常结果（Success 保持 true），但原因必须留痕，便于日志/上报/现场排查
            if (!result.IsOk)
                ErrorMessage.Value = result.NgReason;

            context.Logger?.Info($"{InstanceName} {result.Summary}");
        }

        #endregion

        #region 算法核心（配置预览与流程运行共用，保证"所见即所得"）

        /// <summary>一次测量的结果载体；图像/几何的所有权交给调用方（端口赋值或释放）</summary>
        private sealed class MeasureResult
        {
            /// <summary>是否为"输入/配置不合法"这类前置失败（区别于异常）</summary>
            public bool Failed;
            public string FailMessage = string.Empty;

            /// <summary>像素测量值（未乘当量）</summary>
            public double MeasureValuePx;
            /// <summary>物理测量值（已乘当量）</summary>
            public double MeasureValueMm;
            /// <summary>偏差 = 物理值 - 标准值（带符号）</summary>
            public double Deviation;
            /// <summary>判定结果</summary>
            public bool IsOk = true;
            /// <summary>NG 原因（OK 时为空串）</summary>
            public string NgReason = string.Empty;

            /// <summary>标注图（所有权：调用方）</summary>
            public HImage? MeasureImage;
            /// <summary>测量几何 XLD（所有权：调用方）</summary>
            public HXLD? FittedGeometry;
            /// <summary>边缘点 XLD（所有权：调用方）</summary>
            public HXLD? EdgePoints;

            /// <summary>日志/信息栏摘要</summary>
            public string Summary = string.Empty;

            /// <summary>释放所有输出（失败路径或预览用完即弃时调用；Dispose 幂等）</summary>
            public void DisposeOutputs()
            {
                MeasureImage?.Dispose(); MeasureImage = null;
                DisposeGeometry();
            }

            /// <summary>只释放几何输出（预览只保留标注图时用）</summary>
            public void DisposeGeometry()
            {
                FittedGeometry?.Dispose(); FittedGeometry = null;
                EdgePoints?.Dispose(); EdgePoints = null;
            }
        }

        /// <summary>归一化后的参数（避免中途被界面改值；也把脏配置夹进算法能安全接受的区间）</summary>
        private sealed class MeasureParams
        {
            public MeasureKind Kind;
            public string Transition = "all";
            public string Select = "all";
            public string Interpolation = "bilinear";
            public int CaliperCount;
            public double Sigma;
            public double EdgeThreshold;
            public double StandardValue;
            public double UpperTolerance;
            public double LowerTolerance;
            public double PixelSizeMm;

            // ── 第二批：拟合参数（归一化后） ──
            /// <summary>拟合方式（鲁棒算法）；line/circle 算子的值集不同，各自映射（见 *AlgorithmToHalcon）</summary>
            public FitAlgorithmKind FitAlgorithm;
            /// <summary>残差阈值（边缘点到拟合几何距离的中位数上限，px）</summary>
            public double FitMaxError;
            /// <summary>参与拟合的最少边缘点数</summary>
            public int FitMinPoints;
            /// <summary>环宽（圆直径的径向搜索总宽，px）</summary>
            public double AnnulusWidth;

            /// <summary>参与运算的搜索区矩形（每个 [R,C,Phi,L1,L2]）；圆直径类型为空</summary>
            public List<double[]> Rectangles = new();

            /// <summary>圆形搜索区（圆直径类型专用，[R,C,Radius]）；其它类型为 null</summary>
            public double[]? Circle;

            /// <summary>与 Rectangles/Circle 一一对应的搜索区名称（越界报错时要指名道姓）</summary>
            public List<string> RectangleNames = new();

            /// <summary>搜索区校验失败原因（非空即前置失败）</summary>
            public string ValidationError = string.Empty;
        }

        /// <summary>
        /// 算法核心：参数进 → 结果出，不含端口 / 界面逻辑。
        /// </summary>
        /// <param name="src">输入图像（不拥有，绝不释放）</param>
        /// <param name="logger">日志通道，可为 null（配置预览路径）</param>
        private MeasureResult ExecuteMeasureCore(HImage src, ILogService? logger)
        {
            var p = NormalizedParameters();
            var result = new MeasureResult();

            // 本方法自建的中间 HALCON 对象统一登记，finally 一次性释放。
            // 只登记"临时对象"；要交出去的 MeasureImage/几何是另外 new 出来的独立句柄（见各自注释），不受影响
            var temp = new List<HObject>();
            // gen_measure_rectangle2 创建的测量句柄必须 close_measure 释放，否则每轮泄漏一个测量对象
            var measureHandles = new List<HTuple>();

            try
            {
                if (!string.IsNullOrEmpty(p.ValidationError))
                {
                    result.Failed = true;
                    result.FailMessage = p.ValidationError;
                    return result;
                }

                // ── 1. 拿一张可安全处理的灰度图（测量只在单通道灰度上进行） ──
                if (!TryToGrayImage(src, temp, out HObject gray, out string grayError))
                {
                    result.Failed = true;
                    result.FailMessage = grayError;
                    return result;
                }

                HOperatorSet.GetImageSize(gray, out HTuple wTuple, out HTuple hTuple);
                int imgW = wTuple.I, imgH = hTuple.I;

                // 搜索区中心必须在图像范围内。
                // 为什么不靠算子自己报错：中心在图外时 measure_pairs 会抛 #3022（滤波核尺寸非法），
                // 那条英文错误对操作员毫无指导意义；这里提前用中文说清"哪个搜索区、中心在哪、图多大"。
                for (int i = 0; i < p.Rectangles.Count; i++)
                {
                    double rr = p.Rectangles[i][0], cc = p.Rectangles[i][1];
                    if (rr < 0 || rr >= imgH || cc < 0 || cc >= imgW)
                    {
                        result.Failed = true;
                        result.FailMessage = $"搜索区「{p.RectangleNames[i]}」中心 ({rr:0.#}, {cc:0.#}) 超出图像范围（{imgW}×{imgH}）：" +
                                             "请把搜索区拖到图像内";
                        return result;
                    }
                }

                // 逐搜索区、逐卡尺收集中间结果
                var caliperRects = new List<double[]>();   // 画卡尺小矩形用
                var edgeRows = new List<double>();
                var edgeCols = new List<double>();
                // 青色"测量连线"层（源对象，进 temp）：宽度/两点距=量的是哪两点；点线距=垂线段；
                // 角度=夹角弧；圆直径=直径线。交给端口的 FittedGeometry/EdgePoints 是 new HXLDCont(...) 的独立句柄
                HObject measureXld;
                // 品红"拟合几何"层（源对象，进 temp）：拟合类=拟合出的直线/圆/两线；null → FittedGeometry 端口复用 measureXld
                HObject? fittedXld;

                switch (p.Kind)
                {
                    case MeasureKind.Width:
                    {
                        // ── 宽度/间隙：measure_pairs 一次给出成对边缘，IntraDistance 就是间距 ──
                        var calipers = DistributeCalipers(p.Rectangles[0], p.CaliperCount);
                        var widths = new List<double>(calipers.Count);
                        var lineRows = new List<double>();
                        var lineCols = new List<double>();
                        for (int i = 0; i < calipers.Count; i++)
                        {
                            var c = calipers[i];
                            caliperRects.Add(c);

                            var (r1, c1, r2, c2, intra) = MeasurePairsWithRetry(gray, c, p, imgW, imgH, measureHandles);

                            if (intra.Length == 0)
                            {
                                // 只要有一把卡尺找不到边就判失败：把检到的几把平均出一个"看似合理"的数
                                // 是计量最危险的失败模式（错误但不出错）。宁可报错让用户调参数。
                                result.Failed = true;
                                result.FailMessage = $"第 {i + 1} 把卡尺未检出成对边缘（共 {calipers.Count} 把）：" +
                                                     "请降低边缘阈值、放宽极性，或调整/放大搜索区";
                                return result;
                            }

                            widths.Add(intra[0].D);
                            edgeRows.Add(r1[0].D); edgeCols.Add(c1[0].D);
                            edgeRows.Add(r2[0].D); edgeCols.Add(c2[0].D);
                            lineRows.Add(r1[0].D); lineCols.Add(c1[0].D);
                            lineRows.Add(r2[0].D); lineCols.Add(c2[0].D);
                        }
                        // N 把卡尺取平均：多点平均是本插件"阵列式建模"抗噪的主要手段
                        result.MeasureValuePx = widths.Average();

                        measureXld = BuildSegmentXld(lineRows, lineCols, temp);
                        fittedXld = null;   // 不涉及拟合：FittedGeometry 端口沿用"测量连线"（第一批语义不变）
                        break;
                    }

                    case MeasureKind.PointToPoint:
                    {
                        // ── 两点距：两个搜索区各自 measure_pos 取边点 → 每区把各卡尺的点取平均成"一个代表点" → distance_pp ──
                        // 第二批重构：点收集提成 CollectRegionEdgePoints（与拟合类共用同一口径），取值行为与第一批一致
                        var pts1 = CollectRegionEdgePoints(gray, p.Rectangles[0], p, 0, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (pts1 == null) return result;
                        var pts2 = CollectRegionEdgePoints(gray, p.Rectangles[1], p, 1, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (pts2 == null) return result;

                        double p1R = pts1.Average(t => t.R), p1C = pts1.Average(t => t.C);
                        double p2R = pts2.Average(t => t.R), p2C = pts2.Average(t => t.C);

                        HOperatorSet.DistancePp(p1R, p1C, p2R, p2C, out HTuple dist);
                        result.MeasureValuePx = dist.D;

                        measureXld = BuildSegmentXld(new List<double> { p1R, p2R }, new List<double> { p1C, p2C }, temp);
                        fittedXld = null;
                        break;
                    }

                    case MeasureKind.PointToLineDistance:
                    {
                        // ── 点到线距：区1 出"点"（各卡尺边点平均，与两点距同口径），区2 拟合直线，distance_pl 取垂距 ──
                        var ptPts = CollectRegionEdgePoints(gray, p.Rectangles[0], p, 0, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (ptPts == null) return result;
                        var lnPts = CollectRegionEdgePoints(gray, p.Rectangles[1], p, 1, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (lnPts == null) return result;

                        double pR = ptPts.Average(t => t.R), pC = ptPts.Average(t => t.C);

                        if (!TryFitLine(lnPts, p, p.RectangleNames[1], temp,
                                out double lr1, out double lc1, out double lr2, out double lc2, out string fitErr))
                        {
                            result.Failed = true;
                            result.FailMessage = fitErr;
                            return result;
                        }

                        // 垂距 = 点到拟合直线的距离。distance_pl 对"无限直线"取垂距，正是规格要的口径
                        HOperatorSet.DistancePl(pR, pC, lr1, lc1, lr2, lc2, out HTuple plDist);
                        result.MeasureValuePx = plDist.D;

                        // 垂足 = 点在直线上的投影（直线参数式解 t），标注图把"点→垂足"垂线段画出来
                        double dr = lr2 - lr1, dc = lc2 - lc1;
                        double tproj = ((pR - lr1) * dr + (pC - lc1) * dc) / (dr * dr + dc * dc);
                        double fR = lr1 + tproj * dr, fC = lc1 + tproj * dc;

                        measureXld = BuildSegmentXld(new List<double> { pR, fR }, new List<double> { pC, fC }, temp);
                        fittedXld = BuildSegmentXld(new List<double> { lr1, lr2 }, new List<double> { lc1, lc2 }, temp);
                        break;
                    }

                    case MeasureKind.CircleDiameter:
                    {
                        // ── 圆直径：圆周径向均布 N 把卡尺 → fit_circle_contour_xld → 直径 = 2 × 半径 ──
                        var circle = p.Circle!;
                        double cr = circle[0], cc = circle[1];
                        if (cr < 0 || cr >= imgH || cc < 0 || cc >= imgW)
                        {
                            result.Failed = true;
                            result.FailMessage = $"搜索区「{p.RectangleNames[0]}」圆心 ({cr:0.#}, {cc:0.#}) 超出图像范围（{imgW}×{imgH}）：" +
                                                 "请把圆搜索区拖到图像内";
                            return result;
                        }

                        var pts = CollectCircleEdgePoints(gray, circle, p, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (pts == null) return result;

                        if (!TryFitCircle(pts, p, imgW, imgH, temp, out double fcR, out double fcC, out double fRad, out string fitErr))
                        {
                            result.Failed = true;
                            result.FailMessage = fitErr;
                            return result;
                        }

                        result.MeasureValuePx = 2.0 * fRad;

                        // 拟合几何 = 拟合圆轮廓（gen_circle_contour_xld 生成整圆，独立于离散边缘点）。
                        // resolution = 轮廓点间距（px）：取半径的 1% 左右，整圆约 600 点，渲染平滑且无性能负担
                        double circleRes = Math.Clamp(fRad / 100.0, 0.05, 5.0);
                        HOperatorSet.GenCircleContourXld(out HObject circleXld, fcR, fcC, fRad, 0, 2 * Math.PI, "positive", circleRes);
                        temp.Add(circleXld);
                        fittedXld = circleXld;

                        // 测量连线 = 过拟合圆心的水平直径线段（把"量的是直径"画出来）
                        measureXld = BuildSegmentXld(new List<double> { fcR, fcR }, new List<double> { fcC - fRad, fcC + fRad }, temp);
                        break;
                    }

                    case MeasureKind.Angle:
                    {
                        // ── 角度：两搜索区各拟合一条直线 → 夹角归一化到 0~180°（工业习惯） ──
                        var ptsA = CollectRegionEdgePoints(gray, p.Rectangles[0], p, 0, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (ptsA == null) return result;
                        var ptsB = CollectRegionEdgePoints(gray, p.Rectangles[1], p, 1, imgW, imgH, caliperRects, edgeRows, edgeCols, measureHandles, result);
                        if (ptsB == null) return result;

                        if (!TryFitLine(ptsA, p, p.RectangleNames[0], temp, out double a1r, out double a1c, out double a2r, out double a2c, out string errA))
                        {
                            result.Failed = true;
                            result.FailMessage = errA;
                            return result;
                        }
                        if (!TryFitLine(ptsB, p, p.RectangleNames[1], temp, out double b1r, out double b1c, out double b2r, out double b2c, out string errB))
                        {
                            result.Failed = true;
                            result.FailMessage = errB;
                            return result;
                        }

                        // 求两无限直线的交点（行列式解）。接近平行时交点数值上不可信 → 中文报错而不是硬算
                        double uaR = a2r - a1r, uaC = a2c - a1c;
                        double ubR = b2r - b1r, ubC = b2c - b1c;
                        double cross = uaR * ubC - uaC * ubR;
                        double norm = Math.Sqrt(uaR * uaR + uaC * uaC) * Math.Sqrt(ubR * ubR + ubC * ubC);
                        if (norm < 1e-12 || Math.Abs(cross) < ParallelSinThreshold * norm)
                        {
                            result.Failed = true;
                            result.FailMessage = "两条拟合直线接近平行，无法确定夹角：请让两个搜索区分别压在两条相交的特征边上";
                            return result;
                        }
                        double tHit = ((b1r - a1r) * ubC - (b1c - a1c) * ubR) / cross;
                        double iR = a1r + tHit * uaR, iC = a1c + tHit * uaC;

                        // 关键归一化（规格点名的 0~180° 口径）：angle_ll 的返回值依赖两条线的方向向量取向
                        //（fit 输出端点顺序决定），直接取值可能把 150° 的楔角报成 -30°。
                        // 做法：把两条线都归一化为"从交点指向点分布远端"的射线，再喂给 angle_ll——
                        // 射线取向固定后 |angle| 就是两射线间 0~180° 的真实夹角，无补角歧义。
                        double faR, faC, fbR, fbC;
                        if (Dist(a1r, a1c, iR, iC) >= Dist(a2r, a2c, iR, iC)) { faR = a1r; faC = a1c; }
                        else { faR = a2r; faC = a2c; }
                        if (Dist(b1r, b1c, iR, iC) >= Dist(b2r, b2c, iR, iC)) { fbR = b1r; fbC = b1c; }
                        else { fbR = b2r; fbC = b2c; }

                        HOperatorSet.AngleLl(iR, iC, faR, faC, iR, iC, fbR, fbC, out HTuple angle);
                        // |angle_ll| ∈ [0°,180°]：负值只是旋转方向（图像坐标系），绝对值即夹角大小。
                        // 注意：结果是"度"——角度是量纲无关量，像素当量对它无意义（后面赋值时单独处理）
                        result.MeasureValuePx = Math.Abs(angle.D) * RadToDeg;

                        // 夹角弧（青色测量层）：以交点为圆心、画一段 ≤180° 的弧连接两条射线
                        double faLen = Dist(faR, faC, iR, iC);
                        double fbLen = Dist(fbR, fbC, iR, iC);
                        double arcRadius = Math.Clamp(Math.Min(faLen, fbLen) / 3.0, 5.0, 40.0);
                        // HALCON 角度体系：φ = atan2(Δrow, Δcol)（φ=0 沿 +列，与第一批实证一致）
                        double phiA = Math.Atan2(faR - iR, faC - iC);
                        double phiB = Math.Atan2(fbR - iR, fbC - iC);
                        double extent = phiB - phiA;
                        while (extent < 0) extent += 2 * Math.PI;
                        while (extent >= 2 * Math.PI) extent -= 2 * Math.PI;
                        if (extent > Math.PI) { phiA = phiB; extent = 2 * Math.PI - extent; }
                        HOperatorSet.GenCircleContourXld(out HObject arcXld, iR, iC, arcRadius, phiA, phiA + extent, "positive",
                            Math.Clamp(arcRadius / 100.0, 0.05, 5.0));   // resolution = 轮廓点间距(px)
                        temp.Add(arcXld);
                        measureXld = arcXld;

                        // 拟合几何 = 两条拟合线（2 个轮廓对象 concat）
                        var lineAXld = BuildSegmentXld(new List<double> { a1r, a2r }, new List<double> { a1c, a2c }, temp);
                        var lineBXld = BuildSegmentXld(new List<double> { b1r, b2r }, new List<double> { b1c, b2c }, temp);
                        HOperatorSet.ConcatObj(lineAXld, lineBXld, out HObject twoLines);
                        temp.Add(twoLines);
                        fittedXld = twoLines;
                        break;
                    }

                    default:   // 枚举 5 个成员已全覆盖；兜底保证编译器对确定赋值满意
                        measureXld = GenEmptyMeasureXld(temp);
                        fittedXld = null;
                        break;
                }

                // ── 物理值 + 偏差 + 判定 ──
                // 角度是量纲无关量（单位：度），不受像素当量影响——像素当量只作用于长度类测量；
                // 其余类型 = 像素测量值 × 当量。这条区分容易写错，务必保持
                result.MeasureValueMm = p.Kind == MeasureKind.Angle
                    ? result.MeasureValuePx
                    : result.MeasureValuePx * p.PixelSizeMm;
                result.Deviation = result.MeasureValueMm - p.StandardValue;
                result.IsOk = result.Deviation >= p.LowerTolerance - JudgeEpsilon
                           && result.Deviation <= p.UpperTolerance + JudgeEpsilon;
                result.NgReason = result.IsOk ? string.Empty : BuildNgReason(result, p);

                // ── 组装 XLD 叠加层 ──
                // 注意：这些都是"临时对象"（进 temp 释放）；交给端口的 FittedGeometry/EdgePoints 是
                // new HXLDCont(...) 出来的独立句柄（已实测：包装后释放源对象不影响包装结果）。
                HOperatorSet.GenRectangle2ContourXld(out HObject caliperXld,
                    ToTuple(caliperRects, 0), ToTuple(caliperRects, 1), ToTuple(caliperRects, 2),
                    ToTuple(caliperRects, 3), ToTuple(caliperRects, 4));
                temp.Add(caliperXld);

                HOperatorSet.GenCrossContourXld(out HObject crossXld,
                    new HTuple(edgeRows.ToArray()), new HTuple(edgeCols.ToArray()), 12, 0.785398);
                temp.Add(crossXld);

                // FittedGeometry 端口语义（第二批起按类型区分）：
                //  拟合类 = fittedXld（拟合直线/圆/角度两线，与标注图的品红层同源）；
                //  宽度/两点距（第一批语义不变）= 测量连线 measureXld
                result.EdgePoints = new HXLDCont(crossXld);
                result.FittedGeometry = new HXLDCont(fittedXld ?? measureXld);

                // ── 标注图 ──
                // 渲染只是"给人看"，失败最多丢 MeasureImage，绝不能把一次成功的测量判成失败
                try
                {
                    HObject displayBase = BuildDisplayBase(src, temp);
                    // 单位按类型：角度=度（不受像素当量影响），其余=mm
                    string unit = p.Kind == MeasureKind.Angle ? "°" : "mm";
                    result.MeasureImage = _renderer.Render(
                        displayBase,
                        caliperXld,
                        measureXld,
                        fittedXld,
                        crossXld,
                        new[]
                        {
                            $"判定：{(result.IsOk ? "OK" : "NG")}",
                            $"测量值：{result.MeasureValueMm:0.###} {unit}",
                            $"偏差：{result.Deviation:+0.###;-0.###;0} {unit}",
                        },
                        result.IsOk ? "green" : "red");
                }
                catch (Exception rex)
                {
                    logger?.Warn($"{InstanceName} 标注图渲染失败，MeasureImage 将为空：{rex.Message}");
                    result.MeasureImage = null;
                }

                result.Summary = BuildSummary(result, p);
                return result;
            }
            finally
            {
                // 测量句柄：算子名是 close_measure（不存在 clear_measure）
                foreach (var h in measureHandles)
                {
                    try { HOperatorSet.CloseMeasure(h); } catch { /* 关闭失败不阻断整体回收 */ }
                }
                foreach (var o in temp)
                {
                    try { o.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
                }
            }
        }

        /// <summary>
        /// 把搜索区均布成 N 把卡尺。
        ///
        /// 关键几何（已用真实 HALCON 实证，务必看清）：
        /// gen_measure_rectangle2 沿 L1(Phi) 轴扫描、检出的边缘垂直于 L1 轴；因此 N 把卡尺必须
        /// 沿"垂直于 Phi"的方向（L2 轴）均布，每把保留完整 L1 扫描长度——这样 N 把都在同一个
        /// 特征上取边，取平均才有"多点抗噪"的意义。若沿 L1 轴均布，卡尺会顺着扫描方向排开、
        /// 各自扫到不同位置，完全测不对。
        /// 均布规则：把搜索区的整段 L2 宽度等分成 N 份，每把卡尺的半宽 = L2/N（正好铺满不重叠）。
        /// </summary>
        /// <param name="rect">搜索区 [R,C,Phi,L1,L2]</param>
        /// <param name="n">卡尺数（≥1）</param>
        private static List<double[]> DistributeCalipers(double[] rect, int n)
        {
            double r = rect[0], c = rect[1], phi = rect[2], l1 = rect[3], l2 = rect[4];
            var list = new List<double[]>(n);
            if (n <= 1)
            {
                list.Add(new[] { r, c, phi, l1, l2 });
                return list;
            }

            double spacing = 2.0 * l2 / n;
            double halfWidth = Math.Max(spacing / 2.0, MinCaliperHalfWidth);
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
            for (int i = 0; i < n; i++)
            {
                double t = -l2 + spacing * (i + 0.5);   // 沿 L2 轴的偏移量
                double ri = r + t * cosPhi;              // L2 轴单位向量 = (cosPhi, sinPhi)——垂直于实际长轴 (−sinPhi, cosPhi)
                double ci = c + t * sinPhi;              // （第二批实证修正：原 (cosPhi,−sinPhi) 只在 φ=0 时垂直）
                list.Add(new[] { ri, ci, phi, l1, halfWidth });
            }
            return list;
        }

        /// <summary>把 [R,C,Phi,L1,L2] 列表抽出第 index 列，拼成 HTuple（供 gen_rectangle2_contour_xld 批量生成）</summary>
        private static HTuple ToTuple(List<double[]> rects, int index)
            => new(rects.Select(r => r[index]).ToArray());

        /// <summary>生成一个空的 XLD 对象集（default 分支兜底用，正常流程到不了这里）</summary>
        private static HObject GenEmptyMeasureXld(List<HObject> temp)
        {
            HOperatorSet.GenEmptyObj(out HObject empty);
            temp.Add(empty);
            return empty;
        }

        /// <summary>
        /// 建测量句柄（画布参数直通）。
        /// phi 约定实证（第二批探针 0i）：本环境 GenRectangle2（画布/显示/paint）与
        /// gen_measure_rectangle2 的 phi 约定一致——长轴单位向量都是 (−sinφ, cosφ)，
        /// 因此画布矩形参数可直接喂给测量算子，无需换算（第一批"参数直通"基石成立）。
        /// </summary>
        private HTuple CreateMeasureHandle(double[] caliper, MeasureParams p, int imgW, int imgH, List<HTuple> measureHandles)
        {
            HOperatorSet.GenMeasureRectangle2(caliper[0], caliper[1], caliper[2], caliper[3], caliper[4], imgW, imgH, p.Interpolation, out HTuple handle);
            measureHandles.Add(handle);
            return handle;
        }

        /// <summary>
        /// measure_pos 带一次性重试：实测进程内"同一调用首跑可能返回空、第二跑正常"
        /// （HALCON measure 的首次初始化行为，探针 0d 复现：同参连跑 3 遍为 0/1/1 条边）。
        /// 真正没有边缘时第二次仍为空、仍判失败，语义不变；它只兜底"偶发首跑空结果"的误报。
        /// </summary>
        private (HTuple Rows, HTuple Cols) MeasurePosWithRetry(
            HObject gray, double[] caliper, MeasureParams p, int imgW, int imgH, List<HTuple> measureHandles)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var handle = CreateMeasureHandle(caliper, p, imgW, imgH, measureHandles);
                HOperatorSet.MeasurePos(gray, handle, p.Sigma, p.EdgeThreshold, p.Transition, p.Select,
                    out HTuple rowEdge, out HTuple colEdge, out HTuple _, out HTuple _);
                if (rowEdge.Length > 0) return (rowEdge, colEdge);
            }
            return (new HTuple(), new HTuple());
        }

        /// <summary>measure_pairs 带一次性重试（同 MeasurePosWithRetry 的依据），返回首条配对边缘与间距</summary>
        private (HTuple R1, HTuple C1, HTuple R2, HTuple C2, HTuple Intra) MeasurePairsWithRetry(
            HObject gray, double[] caliper, MeasureParams p, int imgW, int imgH, List<HTuple> measureHandles)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var handle = CreateMeasureHandle(caliper, p, imgW, imgH, measureHandles);
                HOperatorSet.MeasurePairs(gray, handle, p.Sigma, p.EdgeThreshold, p.Transition, p.Select,
                    out HTuple r1, out HTuple c1, out HTuple _,
                    out HTuple r2, out HTuple c2, out HTuple _,
                    out HTuple intra, out HTuple _);
                if (intra.Length > 0) return (r1, c1, r2, c2, intra);
            }
            return (new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple());
        }

        /// <summary>
        /// 矩形搜索区的"边缘点收集"：均布 N 把卡尺（沿 L2 轴），每把 measure_pos 取第一条边缘点。
        /// 两点距/点到线距/角度共用同一份实现（同一口径：Select 生效，'全部'时取第一条）。
        /// 任一把卡尺找不到边 → result.Failed 置位并返回 null（延续第一批"必须全部检出"铁律，
        /// 绝不用"部分平均"糊弄出一个看似合理的数）。
        /// </summary>
        private List<(double R, double C)>? CollectRegionEdgePoints(
            HObject gray, double[] rect, MeasureParams p, int regionIndex, int imgW, int imgH,
            List<double[]> caliperRects, List<double> edgeRows, List<double> edgeCols,
            List<HTuple> measureHandles, MeasureResult result)
        {
            var calipers = DistributeCalipers(rect, p.CaliperCount);
            var pts = new List<(double R, double C)>(calipers.Count);
            for (int i = 0; i < calipers.Count; i++)
            {
                var c = calipers[i];
                caliperRects.Add(c);

                var (rowEdge, colEdge) = MeasurePosWithRetry(gray, c, p, imgW, imgH, measureHandles);

                if (rowEdge.Length == 0)
                {
                    result.Failed = true;
                    result.FailMessage = $"第 {regionIndex + 1} 个搜索区的第 {i + 1} 把卡尺未检出边缘（共 {calipers.Count} 把）：" +
                                         "请降低边缘阈值、放宽极性，或调整/放大搜索区";
                    return null;
                }

                // Select='全部' 时 measure_pos 可能给多条边，这里统一取第一条（select='last' 时它本就是最后一条）
                pts.Add((rowEdge[0].D, colEdge[0].D));
                edgeRows.Add(rowEdge[0].D);
                edgeCols.Add(colEdge[0].D);
            }
            return pts;
        }

        /// <summary>
        /// 圆形搜索区的"边缘点收集"：沿圆周径向均布 N 把卡尺。
        ///
        /// 几何（与第一批实证语义自洽，务必看清）：圆的边缘（圆周）垂直于半径方向，所以每把卡尺的
        /// 扫描方向（L1 轴）取该点的"径向角"。HALCON 图像坐标里 L1 轴单位向量 = (sinφ, cosφ)（行,列），
        /// 圆周上角 a 处的点 = (R + r·sin a, C + r·cos a)，其径向单位向量恰为 (sin a, cos a) → φ = a。
        /// 每把卡尺中心放在用户画的圆周上，沿径向 ±环宽/2 扫描：圆心/半径摆不准时真实圆边落在环内也能扫到
        ///（这正是"环宽"参数存在的意义）。
        /// </summary>
        private List<(double R, double C)>? CollectCircleEdgePoints(
            HObject gray, double[] circle, MeasureParams p, int imgW, int imgH,
            List<double[]> caliperRects, List<double> edgeRows, List<double> edgeCols,
            List<HTuple> measureHandles, MeasureResult result)
        {
            double cr = circle[0], cc = circle[1], radius = circle[2];
            int n = p.CaliperCount;
            double halfLen = Math.Max(p.AnnulusWidth / 2.0, MinRectHalfSize);   // 卡尺半长 = 环宽/2（径向扫描范围）
            double spacing = 2.0 * Math.PI * radius / n;                        // 相邻卡尺的弧间距
            // 卡尺半宽：默认 3px（灰度平均窗口的通用起点，见 CircleCaliperHalfWidth 注释）；
            // 间距更小时夹到间距一半，避免相邻卡尺的窗口互相污染
            double halfWidth = Math.Min(CircleCaliperHalfWidth, Math.Max(spacing / 2.0, MinCaliperHalfWidth));

            var pts = new List<(double R, double C)>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                double row = cr + radius * Math.Sin(a);
                double col = cc + radius * Math.Cos(a);
                // 圆贴近图像边缘时卡尺中心可能落到图外——gen_measure_rectangle2 对此抛 #3022 英文错误，提前拦下
                if (row < 0 || row >= imgH || col < 0 || col >= imgW)
                {
                    result.Failed = true;
                    result.FailMessage = $"第 {i + 1} 把圆周卡尺中心 ({row:0.#}, {col:0.#}) 超出图像范围（{imgW}×{imgH}）：" +
                                         "请把圆搜索区拖离图像边缘、缩小半径或减小环宽";
                    return null;
                }

                // 卡尺矩形（显示与测量同参）：phi 取 −a——实测约定下长轴 (−sin(−a),cos(−a)) = (sin a,cos a) = 径向
                caliperRects.Add(new[] { row, col, -a, halfLen, halfWidth });

                var (rowEdge, colEdge) = MeasurePosWithRetry(gray, new[] { row, col, -a, halfLen, halfWidth }, p, imgW, imgH, measureHandles);

                if (rowEdge.Length == 0)
                {
                    result.Failed = true;
                    result.FailMessage = $"第 {i + 1} 把圆周卡尺未检出边缘（共 {n} 把）：请降低边缘阈值、放宽极性，" +
                                         "或增大环宽（真实圆边可能在环外）";
                    return null;
                }

                pts.Add((rowEdge[0].D, colEdge[0].D));
                edgeRows.Add(rowEdge[0].D);
                edgeCols.Add(colEdge[0].D);
            }
            return pts;
        }

        /// <summary>
        /// 把一组边缘点拟合成直线（fit_line_contour_xld）。
        ///
        /// 失败路径（全部中文、不崩）：
        ///  ① 点数不足：少于 FitMinPoints（拟合前自查——HALCON 自己的英文报错对操作员无指导意义）；
        ///  ② 算子异常：点全部重合等退化分布，翻译成"为什么拟合不出来 + 怎么调"；
        ///  ③ 残差过大：主体点（中位数口径）离拟合线太远——特征弯曲或混入杂边。
        ///
        /// fit 算子的固定参数取 HALCON 文档默认：maxNumPoints=-1（全部点参与）、clipEndPoints=0、
        /// iterations=5、clippingFactor=2.0——规格只开放"拟合方式/最大残差/最少点数"，不额外加参数。
        /// </summary>
        private bool TryFitLine(List<(double R, double C)> pts, MeasureParams p, string regionName, List<HObject> temp,
            out double r1, out double c1, out double r2, out double c2, out string error)
        {
            r1 = c1 = r2 = c2 = 0;
            error = string.Empty;

            if (pts.Count < p.FitMinPoints)
            {
                error = $"搜索区「{regionName}」只收集到 {pts.Count} 个边缘点，少于最少点数 {p.FitMinPoints}：" +
                        "请增大卡尺数，或降低「最少点数」";
                return false;
            }

            // 按卡尺排列顺序连成一条开放轮廓（拟合结果与顺序无关，但轮廓方向决定拟合线段的端点取向）
            HOperatorSet.GenContourPolygonXld(out HObject poly,
                new HTuple(pts.Select(t => t.R).ToArray()),
                new HTuple(pts.Select(t => t.C).ToArray()));
            temp.Add(poly);

            try
            {
                HOperatorSet.FitLineContourXld(poly, LineAlgorithmToHalcon(p.FitAlgorithm), -1, 0, 5, 2.0,
                    out HTuple rowBegin, out HTuple colBegin, out HTuple rowEnd, out HTuple colEnd,
                    out HTuple _, out HTuple _, out HTuple _);
                r1 = rowBegin.D; c1 = colBegin.D;
                r2 = rowEnd.D; c2 = colEnd.D;
            }
            catch (HOperatorException ex)
            {
                error = $"拟合直线失败（搜索区「{regionName}」）：边缘点可能全部重合或分布退化——" +
                        $"请检查搜索区是否压在一条干净、连续的特征边上（原始算子信息：{ex.Message}）";
                return false;
            }

            double dr = r2 - r1, dc = c2 - c1;
            double len = Math.Sqrt(dr * dr + dc * dc);
            if (len < 1e-9)
            {
                error = $"拟合直线失败（搜索区「{regionName}」）：拟合结果是一条零长度线段（边缘点全部重合）——请检查搜索区位置";
                return false;
            }

            // 残差检查（中位数口径，依据见 DefaultFitMaxError 注释）：点到直线的标准距离公式
            var residuals = new List<double>(pts.Count);
            foreach (var t in pts)
                residuals.Add(Math.Abs((t.R - r1) * dc - (t.C - c1) * dr) / len);
            double median = Median(residuals);
            if (median > p.FitMaxError)
            {
                error = $"搜索区「{regionName}」拟合残差过大（主体点残差 {median:0.##} px > 允许 {p.FitMaxError:0.##} px）：" +
                        "边缘点可能混入毛刺/杂边，或特征本身弯曲——请增大卡尺数、增大平滑 Sigma，或把搜索区挪到更直的边缘上";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 把一组边缘点拟合成圆（fit_circle_contour_xld），返回圆心与半径。
        ///
        /// 失败路径（全部中文、不崩）：
        ///  ① 点数不足：少于 3（3 点才定圆）或少于 FitMinPoints；
        ///  ② 圆退化：边缘点接近共线或全部重合——HALCON 会给出天文数字半径甚至直接抛异常，
        ///     这里用"半径有限、为正、且不超过图像对角线 10 倍"作退化判据（真实工件圆不会离谱到这个量级）；
        ///  ③ 残差过大：主体点（中位数口径）离拟合圆弧太远。
        /// 轮廓按点序连成闭合多边形（首点复制到末尾），这样 maxClosureDeviation=0 才有意义。
        /// </summary>
        private bool TryFitCircle(List<(double R, double C)> pts, MeasureParams p, int imgW, int imgH, List<HObject> temp,
            out double centerRow, out double centerCol, out double radius, out string error)
        {
            centerRow = centerCol = radius = 0;
            error = string.Empty;

            if (pts.Count < 3)
            {
                error = $"拟合圆至少需要 3 个边缘点（当前 {pts.Count} 个）：请增大卡尺数";
                return false;
            }
            if (pts.Count < p.FitMinPoints)
            {
                error = $"只收集到 {pts.Count} 个边缘点，少于最少点数 {p.FitMinPoints}：请增大卡尺数，或降低「最少点数」";
                return false;
            }

            // 闭合轮廓：点序 + 首点复制到末尾
            var rows = pts.Select(t => t.R).ToList();
            var cols = pts.Select(t => t.C).ToList();
            rows.Add(rows[0]);
            cols.Add(cols[0]);
            HOperatorSet.GenContourPolygonXld(out HObject poly, new HTuple(rows.ToArray()), new HTuple(cols.ToArray()));
            temp.Add(poly);

            try
            {
                // 实测签名（第二批探针 0g 反射）：(contours, algorithm, maxNumPoints, maxClosureDist,
                // clippingEndPoints, iterations, clippingFactor)——没有 PointOrder 输入（那是输出）；
                // algorithm 值集是 geo 系列（见 CircleAlgorithmToHalcon）。maxClosureDist=0 要求闭合轮廓。
                HOperatorSet.FitCircleContourXld(poly, CircleAlgorithmToHalcon(p.FitAlgorithm), -1, 0, 0, 5, 2.0,
                    out HTuple rowC, out HTuple colC, out HTuple rad,
                    out HTuple _, out HTuple _, out HTuple _);
                centerRow = rowC.D; centerCol = colC.D;
                radius = rad.D;
            }
            catch (HOperatorException ex)
            {
                error = $"拟合圆失败：边缘点可能接近共线或全部重合（圆退化）——请检查圆搜索区是否套住圆周特征、" +
                        $"并增大卡尺数（原始算子信息：{ex.Message}）";
                return false;
            }

            double diag = Math.Sqrt((double)imgW * imgW + (double)imgH * imgH);
            if (!double.IsFinite(radius) || radius <= 0 || radius > diag * 10)
            {
                error = $"拟合圆退化：边缘点接近共线或分布过扁，无法确定合理的圆（估算半径 {radius:0.##}）——" +
                        "请让圆搜索区套住整个圆周特征、增大卡尺数，并确认被测特征确实是圆";
                return false;
            }

            // 圆退化几何判据（第二批实测补充）：共线/过扁的点集配 geo 系列鲁棒算法时不会报错，
            // 而是收敛到一个"贴住少数点的小圆"（实测 3 个共线点拟出 r≈31px 的圆）——半径有限、
            // 残差中位数也可能因降权而达标，以上检查都拦不住。几何事实：真实圆周上均布点的
            // 最大跨度 ≈ 直径（N=3 的 120° 均布也有 √3·R ≈ 1.73R）；若拟合圆直径 < 点集跨度
            // 的 60%，说明这个圆根本包不住点集，必是退化解。
            double maxSpan = 0;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    maxSpan = Math.Max(maxSpan, Dist(pts[i].R, pts[i].C, pts[j].R, pts[j].C));
            if (maxSpan < 2.0)
            {
                error = $"拟合圆退化：{pts.Count} 个边缘点几乎重合（跨度仅 {maxSpan:0.##} px，特征可能是直线边缘而非圆周）——" +
                        "请确认被测特征是圆、并让圆搜索区套住整个圆周";
                return false;
            }
            if (2 * radius < maxSpan * 0.6)
            {
                error = $"拟合圆退化：边缘点接近共线或分布过扁，拟合出的圆（直径 {2 * radius:0.##} px）" +
                        $"远小于点集跨度（{maxSpan:0.##} px）——请让圆搜索区套住整个圆周特征、增大卡尺数，" +
                        "并确认被测特征确实是圆";
                return false;
            }

            var residuals = new List<double>(pts.Count);
            foreach (var t in pts)
                residuals.Add(Math.Abs(Math.Sqrt((t.R - centerRow) * (t.R - centerRow) + (t.C - centerCol) * (t.C - centerCol)) - radius));
            double median = Median(residuals);
            if (median > p.FitMaxError)
            {
                error = $"拟合残差过大（主体点残差 {median:0.##} px > 允许 {p.FitMaxError:0.##} px）：" +
                        "边缘点可能混入毛刺/杂边，或被测特征不是圆——请增大卡尺数、增大平滑 Sigma，或检查特征";
                return false;
            }
            return true;
        }

        /// <summary>中位数（残差检查用）：奇数取中间、偶数取两中项平均；空集返回 0</summary>
        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(x => x).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        /// <summary>两点欧氏距离（行-列平面）</summary>
        private static double Dist(double r1, double c1, double r2, double c2)
            => Math.Sqrt((r1 - r2) * (r1 - r2) + (c1 - c2) * (c1 - c2));

        /// <summary>
        /// 把"每两点一段"的坐标串拼成一组折线轮廓。
        /// 为什么不用一次 gen_contour_polygon_xld：它会把整个点序列连成一条折线（N 把卡尺就会连成
        /// 来回穿的一条线），所以必须每段单独生成再 concat_obj 合并。
        /// </summary>
        private static HObject BuildSegmentXld(List<double> rows, List<double> cols, List<HObject> temp)
        {
            HObject? acc = null;
            for (int i = 0; i + 1 < rows.Count; i += 2)
            {
                HOperatorSet.GenContourPolygonXld(out HObject seg,
                    new HTuple(new[] { rows[i], rows[i + 1] }),
                    new HTuple(new[] { cols[i], cols[i + 1] }));
                temp.Add(seg);
                if (acc == null)
                {
                    acc = seg;
                }
                else
                {
                    HOperatorSet.ConcatObj(acc, seg, out HObject merged);
                    temp.Add(merged);
                    acc = merged;
                }
            }
            if (acc == null)
            {
                HOperatorSet.GenEmptyObj(out HObject empty);
                temp.Add(empty);
                acc = empty;
            }
            return acc;
        }

        /// <summary>
        /// 把输入图转成单通道灰度图，登记进 temp（调用方 finally 统一释放）。
        /// rgb1_to_gray 对单通道图会报错，所以必须先 count_channels 判断。
        /// </summary>
        private static bool TryToGrayImage(HObject src, List<HObject> temp, out HObject gray, out string error)
        {
            gray = null!;
            error = string.Empty;

            HOperatorSet.CountChannels(src, out HTuple channels);
            if (channels.I == 1)
            {
                HOperatorSet.CopyImage(src, out gray);
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
        /// 取用于显示的底图：原图（彩色保持彩色），非 byte 类型转成 byte。
        /// HALCON 图形显示只稳妥支持 byte，16 位/float 图直接 disp_obj 可能报错或显示成一片黑。
        /// 返回的 HObject 所有权：要么是调用方的 src（只读借用），要么是登记进 temp 的新对象。
        /// </summary>
        private static HObject BuildDisplayBase(HImage src, List<HObject> temp)
        {
            HOperatorSet.GetImageType(src, out HTuple type);
            if (type.S == "byte") return src;

            HOperatorSet.ConvertImageType(src, out HObject converted, "byte");
            temp.Add(converted);
            return converted;
        }

        /// <summary>
        /// 把界面参数夹进算法能安全接受的区间。
        /// 方案文件可能来自老版本、也可能被手工改过，越界值喂给算子会抛 HALCON 异常把整条流程带崩
        /// （"非法值不能炸"是硬要求）。这里做"静默夹取"——配置坏了最多结果不对，不会炸流程。
        /// 同时校验参与运算的搜索区（类型/参数长度/尺寸），把问题提前变成一句中文说明。
        /// </summary>
        private MeasureParams NormalizedParameters()
        {
            var p = new MeasureParams
            {
                Kind = MeasureKind,
                Transition = PolarityToTransition(Polarity),
                Select = SelectToHalcon(Select),
                Interpolation = InterpolationToHalcon(Interpolation),
                CaliperCount = (int)Math.Round(ClampFinite(CaliperCount, CaliperCountClampMin, CaliperCountClampMax)),
                Sigma = ClampFinite(Sigma, SigmaClampMin, SigmaClampMax),
                EdgeThreshold = ClampFinite(EdgeThreshold, ThresholdClampMin, ThresholdClampMax),
                StandardValue = ClampFinite(StandardValue, -ToleranceAbsMax, ToleranceAbsMax),
                UpperTolerance = ClampFinite(UpperTolerance, -ToleranceAbsMax, ToleranceAbsMax),
                LowerTolerance = ClampFinite(LowerTolerance, -ToleranceAbsMax, ToleranceAbsMax),
                PixelSizeMm = ClampFinite(PixelSizeMm, PixelSizeClampMin, PixelSizeClampMax),
                FitAlgorithm = FitAlgorithm,
                FitMaxError = ClampFinite(FitMaxError, FitMaxErrorClampMin, FitMaxErrorClampMax),
                FitMinPoints = (int)Math.Round(ClampFinite(FitMinPoints, FitMinPointsClampMin, FitMinPointsClampMax)),
                AnnulusWidth = ClampFinite(AnnulusWidth, AnnulusWidthClampMin, AnnulusWidthClampMax),
            };

            int required = RequiredRegionCountOf(p.Kind);
            var used = CaliperRegions.Take(required).ToList();
            if (used.Count < required)
            {
                p.ValidationError = $"当前测量类型需要 {required} 个{(p.Kind == MeasureKind.CircleDiameter ? "圆形" : "矩形")}搜索区，实际只有 {used.Count} 个：" +
                                    "请在画布上右键新建，或点「重置搜索区」";
                return p;
            }

            if (p.Kind == MeasureKind.CircleDiameter)
            {
                // ── 圆直径：唯一搜索区必须是圆形（[圆心行, 圆心列, 半径]） ──
                var region = used[0];
                if (region == null || region.ShapeType != DrawShapeType.Circle)
                {
                    p.ValidationError = $"搜索区「{region?.Name}」不是圆形：圆直径测量需要圆形搜索区" +
                                        "（请删除该搜索区后，在画布右键 → 区域 → 新建圆形）";
                    return p;
                }
                if (region.Params == null || region.Params.Length < 3)
                {
                    p.ValidationError = $"搜索区「{region.Name}」参数不完整（需要 圆心行/圆心列/半径 三项）";
                    return p;
                }
                double radius = Math.Abs(region.Params[2]);
                if (radius < MinRectHalfSize)
                {
                    p.ValidationError = $"搜索区「{region.Name}」半径过小（{radius:0.##}，需 ≥ {MinRectHalfSize}）：请拖大圆形搜索区或在参数微调里改大";
                    return p;
                }
                p.Circle = new[] { region.Params[0], region.Params[1], radius };
                p.RectangleNames.Add(region.Name);
                return p;
            }

            // ── 其余类型：全部要求矩形搜索区 ──
            foreach (var region in used)
            {
                if (region == null || region.ShapeType != DrawShapeType.Rectangle)
                {
                    p.ValidationError = $"搜索区「{region?.Name}」不是矩形：{(p.Kind == MeasureKind.PointToLineDistance ? "点到线距" : p.Kind == MeasureKind.Angle ? "角度" : "卡尺测量")}只支持矩形搜索区（请删除该形状后新建矩形）";
                    return p;
                }
                if (region.Params == null || region.Params.Length < 5)
                {
                    p.ValidationError = $"搜索区「{region.Name}」参数不完整（需要 中心行/中心列/角度/半长/半宽 五项）";
                    return p;
                }

                double l1 = Math.Abs(region.Params[3]);
                double l2 = Math.Abs(region.Params[4]);
                if (l1 < MinRectHalfSize || l2 < MinRectHalfSize)
                {
                    p.ValidationError = $"搜索区「{region.Name}」尺寸过小（半长 L1={l1:0.##}、半宽 L2={l2:0.##}，" +
                                        $"两者都需 ≥ {MinRectHalfSize}）：请拖大搜索区或在参数微调里改大";
                    return p;
                }

                p.Rectangles.Add(new[] { region.Params[0], region.Params[1], region.Params[2], l1, l2 });
                p.RectangleNames.Add(region.Name);
            }
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

        private static string PolarityToTransition(EdgePolarity polarity) => polarity switch
        {
            EdgePolarity.DarkToLight => "positive",
            EdgePolarity.LightToDark => "negative",
            _ => "all",
        };

        private static string SelectToHalcon(EdgeSelect select) => select switch
        {
            EdgeSelect.First => "first",
            EdgeSelect.Last => "last",
            _ => "all",
        };

        private static string InterpolationToHalcon(EdgeInterpolation interpolation) => interpolation switch
        {
            EdgeInterpolation.Nearest => "nearest_neighbor",
            EdgeInterpolation.Bicubic => "bicubic",
            _ => "bilinear",
        };

        /// <summary>拟合方式 → fit_line_contour_xld 的 Algorithm 参数字符串（值集：regression/huber/tukey）</summary>
        private static string LineAlgorithmToHalcon(FitAlgorithmKind algorithm) => algorithm switch
        {
            FitAlgorithmKind.Regression => "regression",
            FitAlgorithmKind.Huber => "huber",
            _ => "tukey",
        };

        /// <summary>
        /// 拟合方式 → fit_circle_contour_xld 的 Algorithm 参数字符串。
        /// 实证（第二批探针 0e）：fit_circle 的值集是 geo 系列（geometric/geohuber/geotukey），
        /// 不接受 fit_line 的 regression/huber/tukey——传错直接 #1301。两者按语义一一对应。
        /// </summary>
        private static string CircleAlgorithmToHalcon(FitAlgorithmKind algorithm) => algorithm switch
        {
            FitAlgorithmKind.Regression => "geometric",
            FitAlgorithmKind.Huber => "geohuber",
            _ => "geotukey",
        };

        /// <summary>组装 NG 原因（OK 时为空串）；文案对应用户在界面里看到的字段名，方便直接对照</summary>
        private static string BuildNgReason(MeasureResult result, MeasureParams p)
        {
            if (result.Deviation > p.UpperTolerance)
                return $"偏差 {result.Deviation:+0.###;-0.###;0} 超出上公差（上公差 {p.UpperTolerance:0.###}）";
            if (result.Deviation < p.LowerTolerance)
                return $"偏差 {result.Deviation:+0.###;-0.###;0} 超出下公差（下公差 {p.LowerTolerance:0.###}）";
            return string.Empty;
        }

        /// <summary>拼一行摘要（写日志 / 信息栏用）</summary>
        private static string BuildSummary(MeasureResult result, MeasureParams p)
        {
            // 类型中文名 + 数值单位：角度类型单位是"度"且不受像素当量影响（量纲无关量），
            // 其余类型单位 mm（像素值 × 当量）
            string kindText = p.Kind switch
            {
                MeasureKind.Width => "宽度/间隙",
                MeasureKind.PointToPoint => "两点距",
                MeasureKind.PointToLineDistance => "点到线距",
                MeasureKind.CircleDiameter => "圆直径",
                _ => "角度",
            };
            string judge = result.IsOk ? "OK" : "NG";
            string tail = result.IsOk ? string.Empty : $"（{result.NgReason}）";

            if (p.Kind == MeasureKind.Angle)
            {
                return $"{kindText}：测量值={result.MeasureValueMm:0.###}°（角度不受像素当量影响）" +
                       $" 标准={p.StandardValue:0.###}° 偏差={result.Deviation:+0.###;-0.###;0}° 判定={judge}{tail} 卡尺={p.CaliperCount}";
            }

            return $"{kindText}：测量值={result.MeasureValueMm:0.###}（像素={result.MeasureValuePx:0.###}×{p.PixelSizeMm:0.####}）" +
                   $" 标准={p.StandardValue:0.###} 偏差={result.Deviation:+0.###;-0.###;0} 判定={judge}{tail} 卡尺={p.CaliperCount}";
        }

        #endregion
    }
}


