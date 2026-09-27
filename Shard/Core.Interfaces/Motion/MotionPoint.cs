using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 点位的运动曲线类型。
    ///
    /// 正运动卡的对应关系（驱动层映射，见 ZMotionCard）：
    ///   梯形   → ZAux_Direct_SetSramp(axis, 0)（关闭 S 平滑，按 Accel/Decel 直角加减）
    ///   S 曲线 → ZAux_Direct_SetSramp(axis, 平滑时间)（两端圆滑、中段保持速度）
    /// 「匀速」不提供：直线轴不存在真正的恒速定位（总有加减速段），
    /// 硬造一个"近似匀速"只会让现场对行为产生错误预期 —— 宁可少一个选项。
    /// </summary>
    public enum MotionCurve
    {
        /// <summary>梯形：恒速 + 末端减速（默认）</summary>
        Trapezoid = 0,

        /// <summary>S 曲线：两端缓、中段快</summary>
        SCurve = 1,
    }

    /// <summary>
    /// 轴点位：某张卡上某个逻辑轴的一行"预设运动参数"。
    ///
    /// 每轴固定 16 行（Index 0..15，界面显示 P0–P15），不可增删 ——
    /// 固定行数是刻意的：现场教点位时"第 3 个点位"是口头沟通的坐标，
    /// 行数可变会让"点位号"失去稳定的指代。
    /// </summary>
    public sealed class MotionPoint
    {
        /// <summary>所属逻辑轴名（如 "X"）。与 <see cref="Index"/> 一起构成唯一键</summary>
        public string AxisLogical { get; set; } = string.Empty;

        /// <summary>点位号 0..15（界面显示 P0–P15）</summary>
        public int Index { get; set; }

        /// <summary>点位名称（可空，如 "取料位"）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>目标位置（mm，绝对坐标）</summary>
        public double PositionMm { get; set; }

        /// <summary>速度（mm/s）。&lt;=0 表示用卡级默认速度</summary>
        public double SpeedMmPerS { get; set; }

        /// <summary>加速度（mm/s²）。&lt;=0 表示用卡级默认加速度</summary>
        public double AccelMmPerS2 { get; set; }

        /// <summary>运动曲线</summary>
        public MotionCurve Curve { get; set; } = MotionCurve.Trapezoid;

        /// <summary>界面显示名（P{Index}，名称非空时附在后面）</summary>
        public string Caption => Name.Length > 0 ? $"P{Index} · {Name}" : $"P{Index}";
    }

    /// <summary>点位相关的工具方法（seed / 查询）</summary>
    public static class MotionPointExtensions
    {
        /// <summary>每轴固定点位数</summary>
        public const int PointsPerAxis = 16;

        /// <summary>
        /// 确保某个轴的 16 行点位存在（懒 seed：首次打开该轴的点位表时补齐）。
        ///
        /// seed 规则（规格 5.2）：Pos = 序号×10（可见的递增值，方便直接演示）；
        /// 速度/加减速 = **卡级默认值**（新表打开就是一套可直接运行的参数，
        /// 而不是满屏 0 —— S3-2 实测反馈；运行层对 ≤0 仍按"回退卡默认"处理，两种口径兼容）；
        /// 名称 =「点位{序号}」（设计文档原本是空名，但空着的话 16 行长得一模一样，
        /// 现场教点位时没法口头指认 —— 有默认名，改不改随他）；
        /// 曲线 = 梯形。
        /// </summary>
        public static void EnsureAxisPoints(
            this MotionDescriptor descriptor,
            string axisLogical,
            int pointsPerAxis = PointsPerAxis)
        {
            if (descriptor?.Axes == null || string.IsNullOrWhiteSpace(axisLogical)) return;

            for (int i = 0; i < pointsPerAxis; i++)
            {
                bool exists = descriptor.Points.Any(p =>
                    string.Equals(p.AxisLogical, axisLogical, StringComparison.OrdinalIgnoreCase) && p.Index == i);

                if (!exists)
                {
                    descriptor.Points.Add(new MotionPoint
                    {
                        AxisLogical = axisLogical,
                        Index = i,
                        PositionMm = i * 10,
                        Name = $"点位{i}",
                        SpeedMmPerS = descriptor.Params?.DefaultVelocityMmPerS ?? 0,
                        AccelMmPerS2 = descriptor.Params?.DefaultAccelMmPerS2 ?? 0,
                    });
                }
            }
        }

        /// <summary>
        /// 给已存在的行补默认名（升级兼容：早期 seed 的行名称是空的，列表里一片空白没法指认）。
        /// 只补空名，用户自己改过的名字不动。
        /// </summary>
        public static void FillDefaultNames(this MotionDescriptor descriptor, string axisLogical)
        {
            foreach (var p in descriptor?.Points ?? new List<MotionPoint>())
            {
                if (string.Equals(p.AxisLogical, axisLogical, StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(p.Name))
                {
                    p.Name = $"点位{p.Index}";
                }
            }
        }

        /// <summary>取某轴的 16 行点位（按序号排序；缺失的行会先被 seed 补齐）</summary>
        public static List<MotionPoint> GetAxisPoints(
            this MotionDescriptor descriptor,
            string axisLogical,
            int pointsPerAxis = PointsPerAxis)
        {
            descriptor.EnsureAxisPoints(axisLogical, pointsPerAxis);

            return descriptor.Points
                .Where(p => string.Equals(p.AxisLogical, axisLogical, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Index)
                .ToList();
        }

        /// <summary>删除某轴（轴映射被删时同步清它的点位，避免残留孤儿数据）</summary>
        public static void RemoveAxisPoints(this MotionDescriptor descriptor, string axisLogical)
        {
            descriptor?.Points.RemoveAll(p =>
                string.Equals(p.AxisLogical, axisLogical, StringComparison.OrdinalIgnoreCase));
        }
    }
}
