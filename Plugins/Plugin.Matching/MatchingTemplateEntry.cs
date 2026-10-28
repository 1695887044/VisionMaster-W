using System.ComponentModel;
using Core.Halcon.Color;
using HalconDotNet;
using Newtonsoft.Json;

namespace Plugin.Matching
{
    /// <summary>
    /// 模板库条目：一个产品型号 / 一个特征 = 一条，整库随方案 JSON 落盘
    /// （模型/掩膜都是 base64，与单模板时代同一套"随方案走"约定）。
    ///
    /// 提取参数（角度/缩放/步长/梯度阈值/极性/金字塔）跟随条目——每个产品分开学、分开调；
    /// 匹配参数（分数下限/匹配数/重叠/激进度）是运行行为，保持插件级全局。
    ///
    /// 全属性实现 INPC：配置界面直接绑定条目属性（改值即时写回），
    /// 插件订阅 PropertyChanged 驱动"参数已改·需重新学习"徽标。
    /// 运行期句柄（模型/轮廓）不序列化，按条目懒加载缓存。
    /// </summary>
    public class MatchingTemplateEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _name = "模板1";

        /// <summary>配方键：RecipeName 端口按它选择模板。改名后列表即时刷新</summary>
        public string Name
        {
            get => _name;
            set { if (!string.Equals(_name, value)) { _name = value; OnChanged(nameof(Name)); } }
        }

        private string _shape = RoiShapeNames.Rectangle;

        /// <summary>模板区域形状：Rectangle / Circle / Ellipse（画布右键画什么就是什么）</summary>
        public string Shape
        {
            get => _shape;
            set { if (_shape != value) { _shape = value; OnChanged(nameof(Shape)); } }
        }

        private double[] _rect = Array.Empty<double>();

        /// <summary>
        /// 模板区域参数，固定 5 个，与区域颜色检查的 RoiParams 同一约定：
        /// 矩形 [行,列,角度(弧度),半长,半宽]；圆 [行,列,半径,0,0]；椭圆 [行,列,角度,半径1,半径2]。
        /// </summary>
        public double[] Rect
        {
            get => _rect;
            set { if (_rect != value) { _rect = value; OnChanged(nameof(Rect)); } }
        }

        /// <summary>
        /// NCC 模型原点（模板域重心）相对 ROI 中心的偏移（模型坐标系，行/列）。
        ///
        /// 形状模型用 set_shape_model_origin 把原点搬到 ROI 中心；NCC 没有对应算子，
        /// find_ncc_model 返回的是模板域重心。学习时记下这个偏移，匹配时按角度旋转后补回，
        /// 两种算法的 Row/Column 才同口径。旧方案没有该字段 → 0；
        /// 非对称掩膜的旧 NCC 模板需要重新学习一次才能获得修正。
        /// </summary>
        public double NccOffsetRow { get; set; }

        /// <summary>NCC 原点列偏移，语义见 <see cref="NccOffsetRow"/></summary>
        public double NccOffsetCol { get; set; }

        private string _mask = string.Empty;

        /// <summary>涂抹排除域（掩膜图 PNG 的 base64，255=挖掉）；空 = 没有涂抹</summary>
        public string Mask
        {
            get => _mask;
            set { if (_mask != value) { _mask = value; OnChanged(nameof(Mask)); } }
        }

        private string _model = string.Empty;

        /// <summary>模型载荷（write_shape_model 的 base64，含原点锚定）；空 = 未学习</summary>
        public string Model
        {
            get => _model;
            set { if (_model != value) { _model = value; OnChanged(nameof(Model)); } }
        }

        private string _refImagePath = string.Empty;

        /// <summary>该模板自己的参考图路径（学习/重学时载入）</summary>
        public string RefImagePath
        {
            get => _refImagePath;
            set { if (_refImagePath != value) { _refImagePath = value; OnChanged(nameof(RefImagePath)); } }
        }

        private string _refImageAssetId = string.Empty;

        /// <summary>
        /// 参考图来自哪份**图像资产**（留空 = 用 <see cref="RefImagePath"/>）。
        ///
        /// 路径是本机绝对路径：换台机器、或参考图被挪走，"重学"就断了。资产 Id 跨机器稳定，
        /// 路径由宿主按方案目录解析。**只在配置态用**（预览与重学）；运行期匹配读的是
        /// <see cref="Model"/> 里的 base64 载荷，不碰这两个字段。
        /// </summary>
        public string RefImageAssetId
        {
            get => _refImageAssetId;
            set
            {
                var next = value ?? string.Empty;
                if (_refImageAssetId == next) return;
                _refImageAssetId = next;
                OnChanged(nameof(RefImageAssetId));
            }
        }

        private MatchingAlgorithm _algorithm = MatchingAlgorithm.Shape;

        /// <summary>
        /// 提取参数：匹配算法（基于形状 / 基于灰度相关 NCC）。
        ///
        /// 创建期定死：两种算法的模型不是同一种句柄，改它必须重新学习。
        /// 老方案 JSON 里没有这个字段 → 反序列化后保持默认 <see cref="MatchingAlgorithm.Shape"/>，
        /// 与"老方案都是形状匹配"的事实一致，行为不变。
        /// </summary>
        public MatchingAlgorithm Algorithm
        {
            get => _algorithm;
            set { if (_algorithm != value) { _algorithm = value; OnChanged(nameof(Algorithm)); } }
        }

        private int _numLevels = 5;

        /// <summary>提取参数：金字塔层数（1~10）</summary>
        public int NumLevels
        {
            get => _numLevels;
            set { if (_numLevels != value) { _numLevels = value; OnChanged(nameof(NumLevels)); } }
        }

        private int _trackingLevel = 1;

        /// <summary>
        /// 运行期搜索参数：HALCON `find_shape_model` / `find_scaled_shape_model` 的
        /// `NumLevels` 第二个值（最低跟踪层）。
        ///
        /// · 1 = 跟踪到最细层（默认，精度最高，与旧版单值 NumLevels 行为一致）；
        /// · -1 = 不跟踪到最低层（HALCON 例程 `[0,-1]` 的用法；散焦/高噪声场景更稳，
        ///   本仓 `matching_defocused_pcb` 对拍里 121 张散焦图只有 -1 能全部找到）。
        ///
        /// 只影响搜索，不影响模型创建；改它不需要重新学习模板，也不进 LearnedSignature。
        /// </summary>
        public int TrackingLevel
        {
            get => _trackingLevel;
            set { if (_trackingLevel != value) { _trackingLevel = value; OnChanged(nameof(TrackingLevel)); } }
        }

        private double _minAngleDeg = -180;

        /// <summary>提取参数：模板允许的最小旋转角（度）</summary>
        public double MinAngleDeg
        {
            get => _minAngleDeg;
            set { if (_minAngleDeg != value) { _minAngleDeg = value; OnChanged(nameof(MinAngleDeg)); } }
        }

        private double _maxAngleDeg = 180;

        /// <summary>提取参数：模板允许的最大旋转角（度）</summary>
        public double MaxAngleDeg
        {
            get => _maxAngleDeg;
            set { if (_maxAngleDeg != value) { _maxAngleDeg = value; OnChanged(nameof(MaxAngleDeg)); } }
        }

        private double _angleStepDeg;

        /// <summary>提取参数：角度步长（度，0 = 按模板尺寸自动定）</summary>
        public double AngleStepDeg
        {
            get => _angleStepDeg;
            set { if (_angleStepDeg != value) { _angleStepDeg = value; OnChanged(nameof(AngleStepDeg)); } }
        }

        private double _gradientThreshold;

        /// <summary>提取参数：梯度阈值（0 = 自动对比度）</summary>
        public double GradientThreshold
        {
            get => _gradientThreshold;
            set { if (_gradientThreshold != value) { _gradientThreshold = value; OnChanged(nameof(GradientThreshold)); } }
        }

        private bool _usePolarity = true;

        /// <summary>提取参数：是否使用极性（黑白方向一致才算命中，更快）</summary>
        public bool UsePolarity
        {
            get => _usePolarity;
            set { if (_usePolarity != value) { _usePolarity = value; OnChanged(nameof(UsePolarity)); } }
        }

        private bool _scaleEnabled;

        /// <summary>提取参数：是否启用缩放匹配</summary>
        public bool ScaleEnabled
        {
            get => _scaleEnabled;
            set { if (_scaleEnabled != value) { _scaleEnabled = value; OnChanged(nameof(ScaleEnabled)); } }
        }

        private double _scaleMin = 0.8;

        /// <summary>提取参数：最小缩放倍率</summary>
        public double ScaleMin
        {
            get => _scaleMin;
            set { if (_scaleMin != value) { _scaleMin = value; OnChanged(nameof(ScaleMin)); } }
        }

        private double _scaleMax = 1.2;

        /// <summary>提取参数：最大缩放倍率</summary>
        public double ScaleMax
        {
            get => _scaleMax;
            set { if (_scaleMax != value) { _scaleMax = value; OnChanged(nameof(ScaleMax)); } }
        }

        // ── 各向异性缩放（行/列分别缩放）：与上面的等比档互斥 ──

        private bool _anisoScaleEnabled;

        /// <summary>
        /// 提取参数：各向异性缩放（行/列分别缩放，HALCON `create_aniso_shape_model`）。
        ///
        /// 打开后**忽略**等比档的 <see cref="ScaleEnabled"/>/<see cref="ScaleMin"/>/<see cref="ScaleMax"/>
        /// （界面把等比那两个框灰掉），改用下面四个范围。只对形状算法有效：
        /// NCC 没有对应算子，学习时明确告警并忽略（不静默当已启用）。
        /// 老方案 JSON 没有该字段 → 反序列化后 false，行为与升级前完全一致。
        /// </summary>
        public bool AnisoScaleEnabled
        {
            get => _anisoScaleEnabled;
            set { if (_anisoScaleEnabled != value) { _anisoScaleEnabled = value; OnChanged(nameof(AnisoScaleEnabled)); } }
        }

        private double _scaleRowMin = 0.9;

        /// <summary>提取参数：行方向最小缩放（各向异性档；HALCON 限制 &gt; 0，本仓夹 0.1~10）</summary>
        public double ScaleRowMin
        {
            get => _scaleRowMin;
            set { if (_scaleRowMin != value) { _scaleRowMin = value; OnChanged(nameof(ScaleRowMin)); } }
        }

        private double _scaleRowMax = 1.1;

        /// <summary>提取参数：行方向最大缩放（各向异性档；必须 ≥ 行最小）</summary>
        public double ScaleRowMax
        {
            get => _scaleRowMax;
            set { if (_scaleRowMax != value) { _scaleRowMax = value; OnChanged(nameof(ScaleRowMax)); } }
        }

        private double _scaleColMin = 0.9;

        /// <summary>提取参数：列方向最小缩放（各向异性档；HALCON 限制 &gt; 0，本仓夹 0.1~10）</summary>
        public double ScaleColMin
        {
            get => _scaleColMin;
            set { if (_scaleColMin != value) { _scaleColMin = value; OnChanged(nameof(ScaleColMin)); } }
        }

        private double _scaleColMax = 1.1;

        /// <summary>提取参数：列方向最大缩放（各向异性档；必须 ≥ 列最小）</summary>
        public double ScaleColMax
        {
            get => _scaleColMax;
            set { if (_scaleColMax != value) { _scaleColMax = value; OnChanged(nameof(ScaleColMax)); } }
        }

        /// <summary>
        /// 学习那一刻"各向异性是否生效"的快照（运行期口径守卫用，随方案落盘）。
        ///
        /// 为什么需要：HALCON 对"模型类型与查找算子不匹配"**不报错**（实测 23.05：
        /// `find_aniso_shape_model` 查等比模型 = 行/列永远相等；`find_scaled_shape_model` 查各向异性模型
        /// = 一个都找不到），所以"改了开关没重学"在运行期是静默错结果。
        /// 运行期拿它与当前开关比对，不一致就落一行 Warn 提醒重学。
        /// 老方案没有该字段 → false（升级前的模型都是等比/未缩放），语义正确。
        /// </summary>
        public bool LearnedAnisoScaleEnabled { get; set; }

        private string _learnedSignature = string.Empty;

        /// <summary>学习时的参数指纹：与当前参数不一致 = 该条"参数已改·需重新学习"</summary>
        public string LearnedSignature
        {
            get => _learnedSignature;
            set { if (_learnedSignature != value) { _learnedSignature = value; OnChanged(nameof(LearnedSignature)); } }
        }

        // ── 运行期句柄缓存（不序列化；按条目懒加载） ──

        [JsonIgnore] public HTuple? RuntimeModelId;
        [JsonIgnore] public string? RuntimeModelSource;
        [JsonIgnore] public HObject? RuntimeContours;
    }
}
