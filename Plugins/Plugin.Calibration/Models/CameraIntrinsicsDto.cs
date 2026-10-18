using Core.Interfaces;
using System;

namespace Plugin.Calibration.Models
{
    /// <summary>
    /// 相机内参的**插件本地 DTO**（落盘形态）。
    ///
    /// 为什么必须有一层 DTO，不能直接把 <see cref="CameraIntrinsics"/> 当 <c>[StepConfig]</c> 属性
    /// ---------
    /// `.vms` 落盘走 `TypeNameHandling.Auto` + **命名空间白名单**
    /// （`VisionMaster.Communications.ConnectionConfigSerializationBinder.IsAllowedNamespace`），
    /// 白名单只放行 `VisionMaster.Models` / `VisionMaster.Communications` / `Plugin.*` / `VisionMaster.Scada`——
    /// **`Core.Interfaces` 不在内**。直接把契约类型做成配置属性，会写出"**能存、再打开就抛
    /// `JsonSerializationException`**"的方案文件（2026-10-16 实测复现）。本 DTO 在 `Plugin.Calibration.Models`
    /// 命名空间下，命中 `Plugin.*` 白名单。
    /// 范式同 <see cref="CalibPointRow"/> / <see cref="CalibrationFile"/>。
    ///
    /// 字段与契约一一对应（**全基元/数组**，JSON 往返安全）：
    /// 契约 <see cref="CameraIntrinsics"/> 的每个字段在这里都有一个同义字段，双向转换见
    /// <see cref="ToContract"/> / <see cref="FromContract"/>——**转换只在两处**，别在别处手抄字段
    /// （抄漏一个字段就是"内参看起来在、其实丢了"这类静默故障）。
    ///
    /// 数组元素口径：`double[]` 是白名单放行的基元数组（`AllowedArrayElements` 含 `System.Double`），
    /// 所以 polynomial 的 5 个畸变系数可以直接存。
    /// </summary>
    public sealed class CameraIntrinsicsDto
    {
        /// <summary>标定时的图像宽（失配自证用）</summary>
        public int ImageWidth { get; set; }

        /// <summary>标定时的图像高（失配自证用）</summary>
        public int ImageHeight { get; set; }

        /// <summary>相机模型（HALCON 原生字符串：<c>area_scan_division</c> / <c>area_scan_polynomial</c>）</summary>
        public string CameraType { get; set; } = "";

        /// <summary>焦距（米）</summary>
        public double Focus { get; set; }

        /// <summary>division 模型的径向畸变系数（仅 division 有效）</summary>
        public double Kappa { get; set; }

        /// <summary>polynomial 模型的畸变系数 [k1 k2 k3 p1 p2]（仅 polynomial 有效；null = 本模型不用它）</summary>
        public double[]? Distortion { get; set; }

        /// <summary>像元宽（米/像素）</summary>
        public double Sx { get; set; }

        /// <summary>像元高（米/像素）</summary>
        public double Sy { get; set; }

        /// <summary>主点列坐标（像素）</summary>
        public double Cx { get; set; }

        /// <summary>主点行坐标（像素）</summary>
        public double Cy { get; set; }

        /// <summary>重投影 RMS（像素）</summary>
        public double ReprojectionRmsPx { get; set; }

        /// <summary>参与标定的图像张数</summary>
        public int ImageCount { get; set; }

        /// <summary>标定板描述文件名（口径自证）</summary>
        public string CaltabDescr { get; set; } = "";

        /// <summary>相机序列号</summary>
        public string CameraSerial { get; set; } = "";

        /// <summary>标定创建时间（UTC）</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>契约 → DTO（**深拷贝数组**：DTO 可能被界面/快照长期持有，不能与契约共享引用）</summary>
        public static CameraIntrinsicsDto FromContract(CameraIntrinsics c) => new()
        {
            ImageWidth = c.ImageWidth,
            ImageHeight = c.ImageHeight,
            CameraType = c.CameraType ?? "",
            Focus = c.Focus,
            Kappa = c.Kappa,
            Distortion = c.Distortion == null ? null : (double[])c.Distortion.Clone(),
            Sx = c.Sx,
            Sy = c.Sy,
            Cx = c.Cx,
            Cy = c.Cy,
            ReprojectionRmsPx = c.ReprojectionRmsPx,
            ImageCount = c.ImageCount,
            CaltabDescr = c.CaltabDescr ?? "",
            CameraSerial = c.CameraSerial ?? "",
            CreatedAtUtc = c.CreatedAtUtc,
        };

        /// <summary>DTO → 契约（同样深拷贝数组）</summary>
        public CameraIntrinsics ToContract() => new()
        {
            ImageWidth = ImageWidth,
            ImageHeight = ImageHeight,
            CameraType = CameraType ?? "",
            Focus = Focus,
            Kappa = Kappa,
            Distortion = Distortion == null ? null : (double[])Distortion.Clone(),
            Sx = Sx,
            Sy = Sy,
            Cx = Cx,
            Cy = Cy,
            ReprojectionRmsPx = ReprojectionRmsPx,
            ImageCount = ImageCount,
            CaltabDescr = CaltabDescr ?? "",
            CameraSerial = CameraSerial ?? "",
            CreatedAtUtc = CreatedAtUtc,
        };

        /// <summary>
        /// 本 DTO 是否良构（走契约的**同一份**判据——两处各写一遍迟早分叉）。
        /// 失败时给出中文原因（调用方直接上屏）。
        /// </summary>
        public bool IsUsable(out string? error) => CameraIntrinsics.IsUsable(ToContract(), out error);
    }
}
