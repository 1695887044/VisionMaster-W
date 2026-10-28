using System.ComponentModel;
using System.Runtime.CompilerServices;
using Core.Halcon.Color;
using HalconDotNet;
using Newtonsoft.Json;

namespace Plugin.DeformableMatching
{
    /// <summary>
    /// 单模板条目：参考图 + ROI + 掩膜 + 模型载荷 + 创建期/查找期参数。
    ///
    /// 全属性实现 INPC：配置视图直接绑定条目属性，改值即时写回；
    /// 插件订阅 PropertyChanged 驱动「参数已改·需重新学习」。
    ///
    /// 运行期句柄（模型/平面轮廓）不序列化，按条目懒加载缓存：
    ///   · RuntimeModelId     = read_deformable_model 的句柄；
    ///   · RuntimeModelSource = 句柄对应的 Model 载荷（缓存键）；
    ///   · RuntimeModelContours = 平面模式的模型轮廓（get_deformable_model_contours，level 1 基）。
    /// </summary>
    public sealed class DeformableTemplateEntry : INotifyPropertyChanged
    {
        private string _name = "模板1";
        private string _refImagePath = string.Empty;
        private string _refImageAssetId = string.Empty;
        private string _shape = RoiShapeNames.Rectangle;
        private double[] _templateRect = { 200, 200, 0, 60, 45 };
        private string _templateMask = string.Empty;
        private string _model = string.Empty;
        private DeformableAlgorithm _algorithm = DeformableAlgorithm.LocalDeformable;
        private DeformableAlgorithm _learnedAlgorithm = DeformableAlgorithm.LocalDeformable;
        private string _learnedSignature = string.Empty;
        private int _numLevels;
        private double _angleStartDeg = -30;
        private double _angleExtentDeg = 60;
        private double _scaleRMin = 1;
        private double _scaleRMax = 1;
        private double _scaleCMin = 1;
        private double _scaleCMax = 1;
        private double _smoothness = 11;
        private bool _usePolarity = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get => _name; set => Set(ref _name, value); }

        /// <summary>学习用参考图（绝对路径；只有重学才需要）。</summary>
        public string RefImagePath { get => _refImagePath; set => Set(ref _refImagePath, value ?? string.Empty); }

        /// <summary>
        /// 参考图来自哪份**图像资产**（留空 = 用 <see cref="RefImagePath"/>）。
        /// 路径是本机绝对路径，换台机器就断；资产 Id 跨机器稳定，路径由宿主按方案目录解析。
        /// 只在配置态用（载入参考图 / 重学）；运行期匹配读的是 <see cref="Model"/> 的 base64。
        /// </summary>
        public string RefImageAssetId
        {
            get => _refImageAssetId;
            set => Set(ref _refImageAssetId, value ?? string.Empty);
        }

        /// <summary>Rectangle / Circle / Ellipse（RoiShapeNames）。</summary>
        public string Shape { get => _shape; set => Set(ref _shape, value ?? RoiShapeNames.Rectangle); }

        /// <summary>[row, col, phiRad, half1, half2]；圆时 half1 = 半径，half2 忽略。</summary>
        public double[] TemplateRect { get => _templateRect; set => Set(ref _templateRect, value ?? Array.Empty<double>()); }

        /// <summary>掩膜 PNG base64，可空；学习时从 ROI 域里差集掉。</summary>
        public string TemplateMask { get => _templateMask; set => Set(ref _templateMask, value ?? string.Empty); }

        /// <summary>形变模型 base64（write_deformable_model 的字节）。</summary>
        public string Model { get => _model; set => Set(ref _model, value ?? string.Empty); }

        /// <summary>当前算法（用户选择）。</summary>
        public DeformableAlgorithm Algorithm { get => _algorithm; set => Set(ref _algorithm, value); }

        /// <summary>学习时写死：模型是用哪种算法学的（校验 Model 类型，不能只存 Algorithm）。</summary>
        public DeformableAlgorithm LearnedAlgorithm { get => _learnedAlgorithm; set => Set(ref _learnedAlgorithm, value); }

        /// <summary>学习时的创建期参数指纹；查找期参数不进指纹。</summary>
        public string LearnedSignature { get => _learnedSignature; set => Set(ref _learnedSignature, value ?? string.Empty); }

        /// <summary>创建期 0 → 'auto'；查找期 0 合法（默认）。</summary>
        public int NumLevels { get => _numLevels; set => Set(ref _numLevels, value); }

        /// <summary>查找期参数（创建期 HALCON 不读）。</summary>
        public double AngleStartDeg { get => _angleStartDeg; set => Set(ref _angleStartDeg, value); }

        /// <summary>查找期参数（创建期 HALCON 不读）。</summary>
        public double AngleExtentDeg { get => _angleExtentDeg; set => Set(ref _angleExtentDeg, value); }

        /// <summary>创建期参数（最小尺度）。</summary>
        public double ScaleRMin { get => _scaleRMin; set => Set(ref _scaleRMin, value); }

        /// <summary>查找期参数（创建期不读）。</summary>
        public double ScaleRMax { get => _scaleRMax; set => Set(ref _scaleRMax, value); }

        /// <summary>创建期参数。</summary>
        public double ScaleCMin { get => _scaleCMin; set => Set(ref _scaleCMin, value); }

        /// <summary>查找期参数（创建期不读）。</summary>
        public double ScaleCMax { get => _scaleCMax; set => Set(ref _scaleCMax, value); }

        /// <summary>查找期参数；HALCON 典型 11，最小 3。</summary>
        public double Smoothness { get => _smoothness; set => Set(ref _smoothness, value); }

        /// <summary>创建期参数（metric = use_polarity / ignore_local_polarity）。</summary>
        public bool UsePolarity { get => _usePolarity; set => Set(ref _usePolarity, value); }

        [JsonIgnore]
        public HTuple? RuntimeModelId;

        [JsonIgnore]
        public string? RuntimeModelSource;

        [JsonIgnore]
        public HObject? RuntimeModelContours;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
