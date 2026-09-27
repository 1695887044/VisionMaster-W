using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 凸轮插补方式（存储与 UI 共用的两个字符串常量）。
    ///
    /// 为什么是字符串而不是枚举：凸轮拐点随方案 JSON 落盘，规格（第 7 章）定义的
    /// points_json 就是 <c>{"m":..,"s":..,"interp":"直线"}</c> —— 存字符串让磁盘格式
    /// 与规格逐字一致，手写/导入的表不必经过一次枚举转换。
    /// </summary>
    public static class MotionCamInterp
    {
        /// <summary>直线：本段按线性插值</summary>
        public const string Line = "直线";

        /// <summary>三次曲线：smoothstep（t²(3−2t)），两端缓中段顺</summary>
        public const string Cubic = "三次曲线";

        /// <summary>下拉候选（顺序即 UI 顺序）</summary>
        public static readonly string[] Options = { Line, Cubic };

        /// <summary>容错读取：非法值一律回退直线</summary>
        public static string Normalize(string? value)
            => value == Cubic ? Cubic : Line;
    }

    /// <summary>
    /// 电子凸轮表的一个拐点：主轴位置 m → 从轴位置 s，以及"到下一点"的插补形状。
    /// 末段回接首点形成周期（周期 = max(m) − min(m)）。
    /// </summary>
    public sealed class MotionCamPoint
    {
        /// <summary>主轴位置（mm）</summary>
        public double M { get; set; }

        /// <summary>从轴位置（mm）</summary>
        public double S { get; set; }

        /// <summary>本段（该点到下一点）的插补：<see cref="MotionCamInterp"/></summary>
        public string Interp { get; set; } = MotionCamInterp.Line;
    }

    /// <summary>
    /// 电子凸轮表（**全局实体，不挂在任何运动卡下**）。
    ///
    /// 主轴/从轴存的是「卡名 · 轴名」标签，从**所有卡的全部启用轴**里任意组合（跨卡合法）；
    /// 删卡不动凸轮表 —— 表的合法性在启动同步时校验（找不到轴会表现为"无法同步"，见界面闸门）。
    /// 与 <see cref="MotionDescriptor"/> 同一落盘体系：随方案 JSON 持久化。
    /// </summary>
    public sealed class MotionCamTable
    {
        /// <summary>内部稳定身份</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>表名（可重命名）</summary>
        public string Name { get; set; } = "凸轮表";

        /// <summary>主轴（发报）标签「卡名 · 轴名」；空 = 未选择（启动闸门拦截）</summary>
        public string MasterAxis { get; set; } = string.Empty;

        /// <summary>从轴（跟随）标签；空 = 未选择（启动闸门拦截）</summary>
        public string SlaveAxis { get; set; } = string.Empty;

        /// <summary>拐点集合（无序存储，插值时按 m 升序使用）</summary>
        public List<MotionCamPoint> Points { get; set; } = new();
    }

    /// <summary>
    /// 凸轮表的纯数学部分（周期 / 插值 / 增点）。
    ///
    /// 抽成静态纯函数只有一个理由：<b>这些公式必须能被无 WPF、无设备的测试直接断言</b>
    /// （规格第 6.4 章"算法必须逐字对齐"）——挂在 VM 或视图上就只能在有消息循环的宿主里测了。
    /// </summary>
    public static class MotionCamMath
    {
        /// <summary>
        /// 一个周期的跨度 = max(m) − min(m)；拐点不足 2 个时为 0（不可同步）。
        /// </summary>
        public static double Cycle(IReadOnlyList<MotionCamPoint> points)
        {
            if (points == null || points.Count < 2) return 0;

            double min = double.MaxValue, max = double.MinValue;
            foreach (var p in points)
            {
                if (p.M < min) min = p.M;
                if (p.M > max) max = p.M;
            }

            return max - min;
        }

        /// <summary>
        /// 按表插值：主轴位置 m → 从轴位置。
        ///
        /// 逐字对齐规格 6.4：
        ///   ① 拐点按 m 升序；cycle = 末点.m − 首点.m，cycle ≤ 0 直接返回首点.s；
        ///   ② m 折回周期：<c>((m − first.m) mod cycle + cycle) mod cycle + first.m</c>；
        ///   ③ 落在相邻段 [a, b] 内：t = (mm − a.m)/(b.m − a.m)，
        ///      三次曲线段用 smoothstep tt = t·t·(3−2t)，直线段 tt = t；
        ///      返回 a.s + tt·(b.s − a.s)；
        ///   ④ 兜底返回末点.s。
        /// </summary>
        public static double InterpAt(IReadOnlyList<MotionCamPoint> points, double m)
        {
            if (points == null || points.Count == 0) return 0;

            var sorted = points.OrderBy(p => p.M).ToList();
            var first = sorted[0];
            var last = sorted[sorted.Count - 1];
            var cycle = last.M - first.M;
            if (cycle <= 0) return first.S;

            var mm = ((m - first.M) % cycle + cycle) % cycle + first.M;

            for (var i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];
                if (mm < a.M || mm > b.M) continue;

                var t = b.M == a.M ? 0 : (mm - a.M) / (b.M - a.M);
                var tt = a.Interp == MotionCamInterp.Cubic ? t * t * (3 - 2 * t) : t;
                return a.S + tt * (b.S - a.S);
            }

            return last.S;
        }

        /// <summary>
        /// 「添加拐点」的默认值（规格 6.4）：m = 排序末点.m + 10，
        /// s = 末点.s + 斜率×10（斜率取最后两点 Δs/Δm，不足两点取 1），插补 = 直线。
        /// </summary>
        public static MotionCamPoint NextPointBySlope(IReadOnlyList<MotionCamPoint> points)
        {
            if (points == null || points.Count == 0)
                return new MotionCamPoint { M = 0, S = 0, Interp = MotionCamInterp.Line };

            var sorted = points.OrderBy(p => p.M).ToList();
            var last = sorted[sorted.Count - 1];
            var prev = sorted.Count >= 2 ? sorted[sorted.Count - 2] : null;
            var slope = prev != null && last.M != prev.M ? (last.S - prev.S) / (last.M - prev.M) : 1;

            return new MotionCamPoint
            {
                M = last.M + 10,
                S = Math.Round(last.S + slope * 10),
                Interp = MotionCamInterp.Line,
            };
        }

        /// <summary>
        /// 空库首启的默认表（规格 6.4）：五拐点，直线与三次曲线交替 ——
        /// 打开轮廓预览就能同时看到"三次段平滑、直线段笔直"两种形状。
        /// </summary>
        public static MotionCamTable CreateSeedTable()
            => new()
            {
                Name = "凸轮表 1",
                Points =
                {
                    new MotionCamPoint { M = 0, S = 0, Interp = MotionCamInterp.Line },
                    new MotionCamPoint { M = 25, S = 90, Interp = MotionCamInterp.Cubic },
                    new MotionCamPoint { M = 50, S = 180, Interp = MotionCamInterp.Line },
                    new MotionCamPoint { M = 75, S = 270, Interp = MotionCamInterp.Cubic },
                    new MotionCamPoint { M = 100, S = 360, Interp = MotionCamInterp.Line },
                },
            };

        /// <summary>「新建」按钮的空白表：两点对角线（规格 6.4）</summary>
        public static MotionCamTable CreateBlankTable(string name)
            => new()
            {
                Name = name,
                Points =
                {
                    new MotionCamPoint { M = 0, S = 0, Interp = MotionCamInterp.Line },
                    new MotionCamPoint { M = 100, S = 100, Interp = MotionCamInterp.Line },
                },
            };
    }
}
