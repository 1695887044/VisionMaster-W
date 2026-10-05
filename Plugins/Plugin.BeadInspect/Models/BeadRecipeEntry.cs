using System.ComponentModel;
using HalconDotNet;
using Newtonsoft.Json;

namespace Plugin.BeadInspect
{
    /// <summary>
    /// 胶路配方条目（方案说明书 §3.3）：一个产品型号 = 一条，整库随方案 JSON 落盘。
    ///
    /// 模型不可序列化（HALCON 硬约束，§2 决策 6）——落盘的是「输入」：
    /// 参考图路径 + 矫正四点 + 参考路径点列 + 胶宽/容差/极性 + 学习指纹；
    /// bead 模型与 planar 模型在运行期按指纹惰性重建（见插件 EnsureBeadModel / EnsurePlanarModel）。
    ///
    /// 全属性实现 INPC（照抄 MatchingTemplateEntry 范式）：下一批配置界面直接绑定条目属性。
    /// </summary>
    public class BeadRecipeEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _name = "配方1";

        /// <summary>配方键：RecipeName 端口按它选择条目</summary>
        public string Name
        {
            get => _name;
            set { if (!string.Equals(_name, value)) { _name = value; OnChanged(nameof(Name)); } }
        }

        private string _refImagePath = string.Empty;

        /// <summary>参考图路径（planar 对齐模板 + 矫正基准；学习/重学时载入）</summary>
        public string RefImagePath
        {
            get => _refImagePath;
            set { if (_refImagePath != value) { _refImagePath = value; OnChanged(nameof(RefImagePath)); } }
        }

        private string _refNoBeadImagePath = string.Empty;

        /// <summary>
        /// 无胶参考图路径（自动提取中心线的「参考图差分」口径用，§6.4 ②）。
        /// 空 = 无无胶图，提取回退 black-hat（P6 实测：差分 σ=0.96px 最紧，black-hat 只覆盖部分）。
        /// 探针用的就是 adhesive_bead_ref.png（本身就是无胶图）。
        /// </summary>
        public string RefNoBeadImagePath
        {
            get => _refNoBeadImagePath;
            set { if (_refNoBeadImagePath != value) { _refNoBeadImagePath = value; OnChanged(nameof(RefNoBeadImagePath)); } }
        }

        private string _refPointsJson = "[]";

        /// <summary>参考路径点列 [[row,col],...]（三条入口的统一中间表示；坐标在矫正/对齐后的参考坐标系）</summary>
        public string RefPointsJson
        {
            get => _refPointsJson;
            set { if (_refPointsJson != value) { _refPointsJson = value; OnChanged(nameof(RefPointsJson)); } }
        }

        private string _rectifyQuadJson = string.Empty;

        /// <summary>
        /// 矫正四点（PlanarDeformable 模式用）。空 = 不矫正，直接在参考图上建平面模型（P2：非必需但更稳）。
        /// 两种格式：
        /// ① [[row,col]×4] —— 只存源四点，目标矩形由外接矩形加 20px 边距自动推导；
        /// ② {"src":[[row,col]×4], "dst":[[row,col]×4]} —— 显式源/目标四点（复刻范例/探针坐标系时用）。
        /// </summary>
        public string RectifyQuadJson
        {
            get => _rectifyQuadJson;
            set { if (_rectifyQuadJson != value) { _rectifyQuadJson = value; OnChanged(nameof(RectifyQuadJson)); } }
        }

        private double _targetWidth = 15;

        /// <summary>目标胶宽（像素）。P3 实测 15.26（范例的 14 是其自身坐标系的值）；0 = 用插件级 TargetWidth</summary>
        public double TargetWidth
        {
            get => _targetWidth;
            set { if (Math.Abs(_targetWidth - value) > 1e-9) { _targetWidth = value; OnChanged(nameof(TargetWidth)); } }
        }

        private double _widthTolerance = 8;

        /// <summary>胶宽容差（像素）。P12：须 ≥ 提取路径偏差上界 8.39px，默认 8（范例的 7 照搬会过报）；0 = 用插件级</summary>
        public double WidthTolerance
        {
            get => _widthTolerance;
            set { if (Math.Abs(_widthTolerance - value) > 1e-9) { _widthTolerance = value; OnChanged(nameof(WidthTolerance)); } }
        }

        private double _positionTolerance = 30;

        /// <summary>位置容差（像素）。P9：待现场良品样本标定，默认保守值 30；0 = 用插件级</summary>
        public double PositionTolerance
        {
            get => _positionTolerance;
            set { if (Math.Abs(_positionTolerance - value) > 1e-9) { _positionTolerance = value; OnChanged(nameof(PositionTolerance)); } }
        }

        private string _polarity = "dark";

        /// <summary>极性：dark / light（HALCON 硬约束只支持二选一）；空 = 用插件级 Polarity</summary>
        public string Polarity
        {
            get => _polarity;
            set { if (_polarity != value) { _polarity = value; OnChanged(nameof(Polarity)); } }
        }

        private string _learnedSignature = string.Empty;

        /// <summary>学习指纹（点列 + 参数 + 参考图），与运行期重建指纹同口径；空 = 未学习</summary>
        public string LearnedSignature
        {
            get => _learnedSignature;
            set { if (_learnedSignature != value) { _learnedSignature = value; OnChanged(nameof(LearnedSignature)); } }
        }

        // ── 运行期句柄缓存（不序列化；按指纹惰性重建，插件负责 Clear/Dispose） ──

        /// <summary>bead 检测模型句柄（create_bead_inspection_model 产物）</summary>
        [JsonIgnore] public HTuple? RuntimeBeadModel;

        /// <summary>当前 bead 句柄对应的指纹</summary>
        [JsonIgnore] public string? RuntimeBeadSource;

        /// <summary>平面可变形模型句柄（create_planar_uncalib_deformable_model 产物，P5：重建 100~360ms 必须缓存）</summary>
        [JsonIgnore] public HTuple? RuntimePlanarModel;

        /// <summary>当前 planar 句柄对应的指纹</summary>
        [JsonIgnore] public string? RuntimePlanarSource;

        /// <summary>平面区锚点 Row（hom_mat2d_translate 用，范例 prepare_alignment 口径 = 平面区质心）</summary>
        [JsonIgnore] public double RuntimeRowT;

        /// <summary>平面区锚点 Col（同上）</summary>
        [JsonIgnore] public double RuntimeColT;

        /// <summary>矫正后的参考图（RectifyQuadJson 非空时持有；自动提取差分用它对齐出无胶基准）</summary>
        [JsonIgnore] public HObject? RuntimeRectifiedRef;

        /// <summary>平面区（fill_up 后的外扩域；提取中心线与显示用）</summary>
        [JsonIgnore] public HObject? RuntimePlane;

        /// <summary>参考路径 XLD（显示用；随 bead 模型重建）</summary>
        [JsonIgnore] public HObject? RuntimeContour;

        // ── 指纹（运行期缓存键与「已学习」标记的唯一出处，P2-2） ──

        /// <summary>
        /// bead 模型指纹：运行期缓存键（插件 EnsureBeadModel）与配置界面「已学习」标记
        /// （LearnedSignature 比对，CurrentBeadSignature）都必须走这里——两处各自手写会漂移，
        /// 漂移会让「已学习」永远对不上。入参 = 已折算的有效参数
        /// （配方值优先、0 回落插件级，折算在调用处完成）。
        /// </summary>
        public string BuildBeadSignature(
            double targetWidth, double widthTolerance, double positionTolerance, string polarity) =>
            $"bead|{RefPointsJson}|{targetWidth:0.###}|{widthTolerance:0.###}|{positionTolerance:0.###}|{polarity}";

        // ── 解析辅助（JSON 往返的唯一入口，插件与断言共用） ──

        /// <summary>解析参考路径点列 [[row,col],...] → (rows, cols)。语法错误给中文原因</summary>
        public static bool TryParsePoints(string? json, out double[] rows, out double[] cols, out string error)
        {
            rows = Array.Empty<double>();
            cols = Array.Empty<double>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "参考路径点列为空：请在配置界面拾取或导入胶路中心线";
                return false;
            }
            try
            {
                var pts = JsonConvert.DeserializeObject<double[][]>(json);
                if (pts == null || pts.Length == 0)
                {
                    error = "参考路径点列为空：请在配置界面拾取或导入胶路中心线";
                    return false;
                }
                rows = new double[pts.Length];
                cols = new double[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    if (pts[i] == null || pts[i].Length < 2)
                    {
                        error = $"参考路径第 {i + 1} 个点格式无效（应为 [row, col]）";
                        return false;
                    }
                    rows[i] = pts[i][0];
                    cols[i] = pts[i][1];
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"参考路径点列 JSON 解析失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 解析矫正四点。空串 = 不矫正（合法，返回 true 且 srcRows 长度为 0）；
        /// 支持两种格式（见 RectifyQuadJson 注释）。dst 为 null 时由 PrepareAlignment 自动推导。
        /// </summary>
        public static bool TryParseRectifyQuad(
            string? json,
            out double[] srcRows,
            out double[] srcCols,
            out double[]? dstRows,
            out double[]? dstCols,
            out string error)
        {
            srcRows = Array.Empty<double>();
            srcCols = Array.Empty<double>();
            dstRows = null;
            dstCols = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
                return true; // 空 = 不矫正

            try
            {
                var dto = JsonConvert.DeserializeObject<RectifyQuadDto>(json);
                if (dto?.Src != null)
                {
                    if (!FromPairs(dto.Src, out srcRows, out srcCols, out error, 4)) return false;
                    if (dto.Dst != null && !FromPairs(dto.Dst, out dstRows!, out dstCols!, out error, 4)) return false;
                    return true;
                }

                // 纯 [[row,col]×4]（或任意点数）格式
                var plain = JsonConvert.DeserializeObject<double[][]>(json);
                if (plain == null)
                {
                    error = "矫正四点 JSON 为空";
                    return false;
                }
                return FromPairs(plain, out srcRows, out srcCols, out error);
            }
            catch (Exception ex)
            {
                error = $"矫正四点 JSON 解析失败：{ex.Message}";
                return false;
            }
        }

        private static bool FromPairs(double[][] pairs, out double[] rows, out double[] cols, out string error, int expect = 0)
        {
            rows = Array.Empty<double>();
            cols = Array.Empty<double>();
            error = string.Empty;
            if (expect > 0 && pairs.Length != expect)
            {
                error = $"矫正四点应为 {expect} 个点，实际 {pairs.Length} 个";
                return false;
            }
            rows = new double[pairs.Length];
            cols = new double[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i] == null || pairs[i].Length < 2)
                {
                    error = $"矫正四点第 {i + 1} 个点格式无效（应为 [row, col]）";
                    return false;
                }
                rows[i] = pairs[i][0];
                cols[i] = pairs[i][1];
            }
            return true;
        }

        private sealed class RectifyQuadDto
        {
            public double[][]? Src { get; set; }
            public double[][]? Dst { get; set; }
        }
    }
}
