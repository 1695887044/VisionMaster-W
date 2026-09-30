using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 轴映射表的编辑校验（**唯一**实现）。
    ///
    /// 为什么必须有它：轴映射行内编辑此前是完全不设防的 ——
    /// 逻辑轴名可以为空、可以重复，物理轴号可以为负、可以重复。
    /// 而逻辑名是流程与点位表引用的唯一键：
    ///   · 重名 → <c>MotionDescriptor.ResolveAxisIndex</c> 的 FirstOrDefault 变成**随机命中**；
    ///   · 重名 → 删轴时按名删点位会**误删**另一根轴教好的点位。
    /// 这两类错误在真机上的表现都是"动的不是我以为的那根轴"。
    ///
    /// 【职责划分】这里只做**本卡内**的格式与占用校验（名字是否为空、本卡轴号是否撞）。
    /// **全局**唯一性归 <see cref="MotionAxisRegistry"/> —— 它才看得到所有卡，
    /// 也只有它能"拒绝注册"而不是"事后报一句冲突"。
    /// 两层的分工：调用方先过这里（快、本地），再过注册表（权威）。
    ///
    /// 抽成静态纯函数是为了能被直接断言（不需要设备、不需要 WPF 宿主）。
    /// </summary>
    public static class MotionAxisValidator
    {
        /// <summary>
        /// 未连卡时也铺出的最少轴行数。
        ///
        /// 没有它，"轴映射"在连接之前是一片空白 —— 用户连"脉冲当量填在哪儿"都不知道。
        /// 取 4 是因为它覆盖了绝大多数小型卡；大卡连上后按能力补齐。
        /// </summary>
        public const int MinimumAxisRows = 4;

        /// <summary>逻辑轴名最大长度（界面列宽与日志可读性的上限，不是硬约束）</summary>
        public const int MaxLogicalNameLength = 32;

        /// <summary>
        /// 凸轮轴标签的分隔符。逻辑轴名里禁止出现它 ——
        /// 凸轮表的轴引用是「卡名 · 轴名」拼出来的字符串，轴名里再带分隔符就无法反解。
        /// </summary>
        public const string AxisLabelSeparator = " · ";

        /// <summary>逻辑轴名的合法性（返回 null = 通过；否则是可直接上 toast 的中文原因）</summary>
        public static string? ValidateLogicalName(
            MotionDescriptor? card, AxisMapping? self, string? rawName)
        {
            var name = (rawName ?? string.Empty).Trim();

            if (name.Length == 0) return "逻辑轴名不能为空（流程与点位表都按它引用）";

            if (name.Length > MaxLogicalNameLength)
                return $"逻辑轴名不能超过 {MaxLogicalNameLength} 个字符";

            if (name.Contains(AxisLabelSeparator.Trim()))
                return $"逻辑轴名不能包含「{AxisLabelSeparator.Trim()}」（凸轮轴标签用它做分隔）";

            if (card?.Axes == null) return null;

            var duplicate = card.Axes.Any(a =>
                !ReferenceEquals(a, self)
                && string.Equals((a.LogicalName ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));

            return duplicate ? $"逻辑轴名「{name}」在本卡已存在（重名会让流程按名寻址变成随机命中）" : null;
        }

        /// <summary>物理轴号的合法性（返回 null = 通过）</summary>
        public static string? ValidatePhysicalIndex(
            MotionDescriptor? card, AxisMapping? self, int physicalIndex)
        {
            if (physicalIndex < 0) return "物理轴号不能为负（卡内轴号是 0 基）";

            if (card?.Axes == null) return null;

            var duplicate = card.Axes.FirstOrDefault(a =>
                !ReferenceEquals(a, self) && a.PhysicalIndex == physicalIndex);

            return duplicate == null
                ? null
                : $"物理轴号 {physicalIndex} 已被轴「{NameOf(duplicate)}」占用（两根轴指向同一个物理轴会互相打架）";
        }

        /// <summary>下一个可用的逻辑轴名（A{序号}，跳过已占用的）</summary>
        public static string NextLogicalName(MotionDescriptor? card, AxisMapping? self = null)
        {
            var axes = card?.Axes ?? new List<AxisMapping>();
            for (var i = 0; i < 1024; i++)
            {
                var candidate = $"A{i}";
                var taken = axes.Any(a =>
                    !ReferenceEquals(a, self)
                    && string.Equals((a.LogicalName ?? string.Empty).Trim(), candidate, StringComparison.OrdinalIgnoreCase));

                if (!taken) return candidate;
            }

            return $"A{DateTime.UtcNow.Ticks % 100000}";
        }

        /// <summary>下一个可用的物理轴号（0 基，跳过已占用的）</summary>
        public static int NextPhysicalIndex(MotionDescriptor? card, AxisMapping? self = null)
        {
            var axes = card?.Axes ?? new List<AxisMapping>();
            for (var i = 0; i < 1024; i++)
            {
                if (!axes.Any(a => !ReferenceEquals(a, self) && a.PhysicalIndex == i)) return i;
            }

            return 0;
        }

        private static string NameOf(AxisMapping mapping)
            => string.IsNullOrWhiteSpace(mapping.LogicalName)
                ? $"轴{mapping.PhysicalIndex}"
                : mapping.LogicalName;
    }
}
