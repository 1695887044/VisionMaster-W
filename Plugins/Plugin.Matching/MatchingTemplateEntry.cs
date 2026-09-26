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

        private int _numLevels = 5;

        /// <summary>提取参数：金字塔层数（1~10）</summary>
        public int NumLevels
        {
            get => _numLevels;
            set { if (_numLevels != value) { _numLevels = value; OnChanged(nameof(NumLevels)); } }
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
