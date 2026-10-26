using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 「从账本导入列」的纯逻辑：账本表头 → 一组报表列配置。
    ///
    /// 为什么单独成一个纯函数
    /// ---------
    /// 报表列填的是<b>账本 CSV 的列名</b>，而列名集合只有打开过账本才知道。让用户对着空白表格
    /// 手打五六个列名，是这类配置最容易出错的地方（少一个空格、大小写不一致、漏一列）。
    /// 把"表头 → 列配置"抽出来，既能一键生成，也能在断言里钉住口径（不碰文件系统）。
    ///
    /// 口径
    /// ---------
    /// · 表头去空白；空列名与<b>重复列名</b>（忽略大小写）跳过——账本里同名两列，报表按名字取值只会取到第一列，
    ///   留着第二列等于让用户以为它在生效；
    /// · 标题默认等于列名（之后用户可改显示名）；
    /// · 第一列按"时间列"给 140px 固定宽（账本首列就是采样时刻），其余列宽填 0 = 占满剩余；
    /// · 聚合方式一律 <see cref="ScadaReportAggregate.None"/>（明细照抄）；要汇总由用户逐列改，
    ///   不替用户猜"这列该求和还是该平均"。
    /// </summary>
    public static class ScadaReportColumnImport
    {
        /// <summary>首列（时间列）的默认宽度</summary>
        public const int TimeColumnWidth = 140;

        /// <summary>账本表头 → 报表列。表头为空时返回空集合（调用方负责给出"没有表头"的提示）</summary>
        public static IReadOnlyList<ScadaReportColumn> FromHeader(IReadOnlyList<string>? header)
        {
            if (header == null || header.Count == 0)
                return Array.Empty<ScadaReportColumn>();

            var result = new List<ScadaReportColumn>(header.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in header)
            {
                var name = (raw ?? string.Empty).Trim();

                if (name.Length == 0 || !seen.Add(name))
                    continue;

                result.Add(new ScadaReportColumn(
                    name,
                    name,
                    format: string.Empty,
                    aggregate: ScadaReportAggregate.None,
                    width: result.Count == 0 ? TimeColumnWidth : 0));
            }

            return result;
        }
    }
}
