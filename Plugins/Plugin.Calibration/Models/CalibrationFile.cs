using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Plugin.Calibration.Models
{
    /// <summary>
    /// 标定文件（导入/导出）的一等数据形态——**只装"标定数据 + 质量策略 + 元信息"**：
    /// · 不含锁定开关 / 预览图 / 画布状态（那些是"本机状态"，跟着人走，不跟着文件走）；
    /// · 快照（Snapshot）只是"导出时质量如何"的窗口，导入端不依赖它（导入后自行重算）。
    ///
    /// 兼容性约定（Version）：
    /// · 1 = 本版（2026-10-05）：模式/网格/表格四值（可空）/A、B（可空）/已知长度/相机/阈值/护栏/时间戳/尺寸；
    /// · 导入端遇到 **更高版本** 必须拒绝（明确报错）；同/低版本按缺省字段宽容处理（缺省 null = 未填）。
    /// </summary>
    public sealed class CalibrationExportFile
    {
        /// <summary>格式版本（见类注释的兼容性约定）</summary>
        public int Version { get; set; }

        /// <summary>导出时刻（UTC，仅记录）</summary>
        public DateTime ExportedAtUtc { get; set; }

        /// <summary>标定模式（0=像素当量 / 1=九点，将来扩展）</summary>
        public int Mode { get; set; }

        /// <summary>网格边长 N（九点模式）</summary>
        public int GridSize { get; set; }

        /// <summary>像素当量模式：已知长度（mm）</summary>
        public double KnownLengthMm { get; set; }

        /// <summary>像素当量模式：A 点 Row（null = 未取点）</summary>
        public double? ScaleARow { get; set; }

        /// <summary>像素当量模式：A 点 Col</summary>
        public double? ScaleACol { get; set; }

        /// <summary>像素当量模式：B 点 Row</summary>
        public double? ScaleBRow { get; set; }

        /// <summary>像素当量模式：B 点 Col</summary>
        public double? ScaleBCol { get; set; }

        /// <summary>相机序列号</summary>
        [JsonProperty]
        public string CameraSerial { get; set; } = "";

        /// <summary>质量阈值（像素）</summary>
        public double ResidualThresholdPx { get; set; } = 1.0;

        /// <summary>当量护栏下限</summary>
        public double MmPerPixelMin { get; set; } = 0.001;

        /// <summary>当量护栏上限</summary>
        public double MmPerPixelMax { get; set; } = 1.0;

        /// <summary>标定数据最后改动时间（UTC）</summary>
        public DateTime CalibrationStampUtc { get; set; }

        /// <summary>标定时的图像宽（0 = 未记录）</summary>
        public int SourceImageWidth { get; set; }

        /// <summary>标定时的图像高（0 = 未记录）</summary>
        public int SourceImageHeight { get; set; }

        /// <summary>来源标签（标定图文件名等）</summary>
        public string SourceTag { get; set; } = "";

        /// <summary>标定表（空行也在内；null = 未填，0 是合法坐标）</summary>
        public List<CalibrationExportPoint> Points { get; set; } = new();

        /// <summary>导出时的质量快照（人工核对用；导入端不依赖）</summary>
        public CalibrationExportSnapshot? Snapshot { get; set; }
    }

    /// <summary>标定文件里的一行（null = 未填；0 是合法坐标）</summary>
    public sealed class CalibrationExportPoint
    {
        /// <summary>行名（导入端会统一重排为 P1..PN）</summary>
        public string Name { get; set; } = "";

        public double? MachineX { get; set; }
        public double? MachineY { get; set; }
        public double? ImageRow { get; set; }
        public double? ImageCol { get; set; }
    }

    /// <summary>质量快照（导出时刻；导入端不依赖）</summary>
    public sealed class CalibrationExportSnapshot
    {
        /// <summary>标定类型（同 CalibrationKind）</summary>
        public int Kind { get; set; }

        /// <summary>像素当量（mm/px）</summary>
        public double MmPerPixel { get; set; }

        /// <summary>残差 RMS（像素）</summary>
        public double ResidualRmsPx { get; set; }

        /// <summary>最大单点残差（像素）</summary>
        public double ResidualMaxPx { get; set; }
    }
}
