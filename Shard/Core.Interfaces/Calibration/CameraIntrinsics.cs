using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 相机内参（「标定」插件**内参标定**的产物）：显式建模镜头畸变的相机几何。
    ///
    /// 与 <see cref="CalibrationTransform"/> 的分工
    /// ---------
    /// · <see cref="CalibrationTransform"/> 管"像素 ↔ 机械坐标"的映射（仿射/投影/分段仿射）；
    /// · 本类管"**畸变像素 ↔ 去畸变像素**"的映射（镜头畸变的物理模型）。
    /// 两者**串联**使用：畸变像素 →（本类去畸变）→ 理想针孔像素 →（CalibrationTransform）→ 机械坐标。
    /// 详见「标定」插件主文档 §1.2 的畸变三条升级路线（透视 / 网格 / 内参）。
    ///
    /// 为什么放在 Core.Interfaces
    /// ---------
    /// ① 跨插件传递必须走契约程序集（插件之间不互相引用）；
    /// ② 契约层**零 HALCON 依赖**（与 CalibrationTransform / CameraFrame 同一纪律）：本类全是基元类型，
    ///    JSON 往返安全。HALCON 的 CamPar tuple 由**消费方**（标定/坐标变换插件）自己拼——
    ///    拼法只有一处范式，见 <see cref="CameraType"/> 注释。
    ///
    /// ⚠ 持久化纪律（2026-10-16 实测，务必先读）
    /// ---------
    /// **本类不得直接用作插件 `[StepConfig]` 属性的类型**：`.vms` 落盘走 `TypeNameHandling.Auto` +
    /// 命名空间白名单（`ConnectionConfigJsonConverter.IsAllowedNamespace`），而白名单**只放行
    /// `VisionMaster.Models` / `VisionMaster.Communications` / `Plugin.*` / `VisionMaster.Scada`**——
    /// `Core.Interfaces` 不在内。直接落盘会写出"能存、**再打开就抛 JsonSerializationException**"的方案文件。
    /// 插件侧要持久化内参，必须用**插件自己命名空间下的 DTO**（范式：`Plugin.Calibration.Models.CameraIntrinsicsDto`），
    /// 与本类互转。
    ///
    /// 字段口径（HALCON 原生 CamPar 形状，**不是** OpenCV 的 3×3 内参矩阵）
    /// ---------
    /// HALCON 的 CamPar 是"类型 + 焦距 + 畸变系数 + 像元尺寸 + 主点 + 图尺寸"的扁平 tuple，
    /// 没有 OpenCV 那种 3×3 矩阵（焦距与像元尺寸是**分开**的两个量，3×3 矩阵是把它们乘起来的结果）。
    /// 本类按 HALCON 口径逐字段存，使 `CamParam` 的构造与解析都是**无损直译**：
    ///   · `area_scan_division`   → [type, focus, kappa, sx, sy, cx, cy, w, h]（9 元）
    ///   · `area_scan_polynomial` → [type, focus, k1, k2, k3, p1, p2, sx, sy, cx, cy, w, h]（13 元）
    /// （设计稿 §3.1 曾按 OpenCV 口径写 `CameraMatrix[9]` + `Distortion[5/8/12]`；
    /// 实现按 HALCON 原生口径落地并记为实现勘误——见 `docs\标定插件\标定插件.md` §4.5。）
    ///
    /// 两种畸变模型（现场按**重投影 RMS 与去畸变后的平面残差**选型，不预设）
    /// ---------
    /// · <see cref="DivisionType"/>：单系数（kappa），广角/低畸变常用。**可解析求逆**——
    ///   实测往返误差 2.7e-13 px（1280×1024、kappa=-1500）。
    /// · <see cref="PolynomialType"/>：多系数（k1 k2 k3 p1 p2），强畸变/需切向项时用。**只能迭代反解**——
    ///   实测往返误差最坏 1.1e-4 px（同条件）。消费方做反向映射时的容差必须按模型分档，见
    ///   `Plugin.PoseTransform.PoseTransformPlugin` 的回显自校验阈值。
    ///
    /// 失配自证（消费方据此报错，绝不静默用旧内参）
    /// ---------
    /// · <see cref="ImageWidth"/> / <see cref="ImageHeight"/>：标定时的图像尺寸——与标定变换、
    ///   与运行期当前图尺寸不一致 = 换了分辨率/换了相机 → 旧内参失效（畸变系数是**像素帧相关**的量，
    ///   跨帧使用会把点挪到"看起来合理"的错误位置，是典型的静默故障）；
    /// · <see cref="CameraSerial"/>：这份内参属于哪台相机（多相机下机器可读的身份）；
    /// · <see cref="CaltabDescr"/>：用了哪块标定板（口径自证——板描述变了等于换了量具）；
    /// · <see cref="ReprojectionRmsPx"/> / <see cref="ImageCount"/>：标定质量的量化指标（人工核对）。
    /// </summary>
    public sealed class CameraIntrinsics
    {
        /// <summary>division 模型名（HALCON 原生字符串，逐字）。参数形状：focus, kappa, sx, sy, cx, cy, w, h</summary>
        public const string DivisionType = "area_scan_division";

        /// <summary>polynomial 模型名（HALCON 原生字符串，逐字）。参数形状：focus, k1, k2, k3, p1, p2, sx, sy, cx, cy, w, h</summary>
        public const string PolynomialType = "area_scan_polynomial";

        /// <summary>polynomial 模型的畸变系数个数（k1 k2 k3 p1 p2）</summary>
        public const int PolynomialCoeffCount = 5;

        /// <summary>标定时的图像宽（失配自证用）</summary>
        public int ImageWidth { get; set; }

        /// <summary>标定时的图像高（失配自证用）</summary>
        public int ImageHeight { get; set; }

        /// <summary>
        /// 相机模型：<see cref="DivisionType"/> 或 <see cref="PolynomialType"/>（HALCON 原生字符串，逐字）。
        /// 空串 = 未标定/数据损坏（<see cref="IsUsable"/> 判失败）。
        /// </summary>
        public string CameraType { get; set; } = "";

        /// <summary>焦距（米）。HALCON 口径：与像元尺寸 <see cref="Sx"/> / <see cref="Sy"/> 分开存储</summary>
        public double Focus { get; set; }

        /// <summary>
        /// division 模型的径向畸变系数（单位 1/m²，量级通常 1e2~1e4）。
        /// **仅 <see cref="DivisionType"/> 有效**；polynomial 模型恒为 0。
        /// </summary>
        public double Kappa { get; set; }

        /// <summary>
        /// polynomial 模型的畸变系数 [k1 k2 k3 p1 p2]（<see cref="PolynomialCoeffCount"/> 元）。
        /// **仅 <see cref="PolynomialType"/> 有效**；division 模型为 null（不是空数组——null 表达"本模型不用它"）。
        /// </summary>
        public double[]? Distortion { get; set; }

        /// <summary>像元宽（米/像素）</summary>
        public double Sx { get; set; }

        /// <summary>像元高（米/像素）</summary>
        public double Sy { get; set; }

        /// <summary>主点列坐标（像素）</summary>
        public double Cx { get; set; }

        /// <summary>主点行坐标（像素）</summary>
        public double Cy { get; set; }

        /// <summary>重投影 RMS（像素）：标定质量的量化指标（越小越好；判读口径见主文档）</summary>
        public double ReprojectionRmsPx { get; set; }

        /// <summary>参与标定的图像张数</summary>
        public int ImageCount { get; set; }

        /// <summary>标定板描述文件名（口径自证：换了板等于换了量具）</summary>
        public string CaltabDescr { get; set; } = "";

        /// <summary>相机序列号（多相机的"这份内参属于哪台"；与标定变换的同名字段交叉核对）</summary>
        public string CameraSerial { get; set; } = "";

        /// <summary>标定创建时间（UTC）</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>
        /// 本内参是否良构（消费方与生产方共用**同一份**判据——两处各写一遍迟早分叉）。
        /// 失败时给出中文原因（消费方直接上屏，不要自己造文案）。
        /// </summary>
        public static bool IsUsable(CameraIntrinsics? c, out string? error)
        {
            error = null;

            if (c == null)
            {
                error = "相机内参缺失：这份标定声明了「去畸变像素」语义，但没有携带内参数据"
                      + "——请重新运行「标定」步骤的内参标定";
                return false;
            }

            if (c.CameraType != DivisionType && c.CameraType != PolynomialType)
            {
                error = $"相机内参的模型类型无法识别（「{c.CameraType}」）：只支持 {DivisionType} / {PolynomialType}"
                      + "——请重新运行「标定」步骤的内参标定";
                return false;
            }

            if (c.CameraType == PolynomialType)
            {
                if (c.Distortion == null || c.Distortion.Length != PolynomialCoeffCount)
                {
                    error = $"相机内参损坏（{PolynomialType} 需要 {PolynomialCoeffCount} 个畸变系数，"
                          + $"实际 {(c.Distortion == null ? "缺失" : c.Distortion.Length.ToString())}）："
                          + "请重新运行「标定」步骤的内参标定";
                    return false;
                }
                for (int i = 0; i < c.Distortion.Length; i++)
                {
                    if (!IsFinite(c.Distortion[i]))
                    {
                        error = $"相机内参含非法数值（畸变系数第 {i + 1} 个为 NaN/Inf）：请重新运行内参标定";
                        return false;
                    }
                }
            }

            if (!IsFinite(c.Focus) || c.Focus <= 0)
            {
                error = $"相机内参非法（焦距 {c.Focus} 必须为正的有限值）：请重新运行内参标定";
                return false;
            }

            if (!IsFinite(c.Sx) || c.Sx <= 0 || !IsFinite(c.Sy) || c.Sy <= 0)
            {
                error = $"相机内参非法（像元尺寸 {c.Sx}×{c.Sy} 必须为正的有限值）：请重新运行内参标定";
                return false;
            }

            if (!IsFinite(c.Cx) || !IsFinite(c.Cy))
            {
                error = "相机内参非法（主点坐标含 NaN/Inf）：请重新运行内参标定";
                return false;
            }

            if (!IsFinite(c.Kappa))
            {
                error = "相机内参非法（畸变系数 kappa 含 NaN/Inf）：请重新运行内参标定";
                return false;
            }

            if (c.ImageWidth <= 0 || c.ImageHeight <= 0)
            {
                error = $"相机内参非法（标定图尺寸 {c.ImageWidth}×{c.ImageHeight}）：请重新运行内参标定";
                return false;
            }

            return true;
        }

        /// <summary>有限值判定（NaN/Inf 都算非法）</summary>
        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
