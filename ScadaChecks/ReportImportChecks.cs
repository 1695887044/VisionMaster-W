using System.Collections.Generic;
using System.Linq;
using VisionMaster.Scada;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [RP-IMP] 报表「从账本导入列」的纯逻辑：账本表头 → 列配置。
        ///
        /// 这条断言钉的是"导入不会造出无效列"：空列名与重名列必须被丢掉
        /// （账本里同名两列，报表按名字取值只会取到第一列，留着第二列等于骗用户）。
        /// 文件系统那半（找最新账本、读首行）不在这里测——它依赖真实方案目录，
        /// 由界面里的失败提示兜底（没有账本时明确说"先让方案跑一次"）。
        /// </summary>
        private static class ReportImportChecks
        {
            public static void Run()
            {
                Section("[RP-IMP] 报表「从账本导入列」：表头 → 列配置");

                var header = new List<string> { " 时间 ", "产量", "合格数", "", "产量", "合格率" };
                var columns = ScadaReportColumnImport.FromHeader(header);

                Check("[RP40] 表头去空白、跳过空列与重名列；首列（时间）140px，其余列宽 0=占满",
                    columns.Count == 4
                    && columns[0].Source == "时间"
                    && columns[0].Width == ScadaReportColumnImport.TimeColumnWidth
                    && columns.Skip(1).All(c => c.Width == 0)
                    && columns.Select(c => c.Source).SequenceEqual(new[] { "时间", "产量", "合格数", "合格率" })
                    && columns.All(c => c.Aggregate == ScadaReportAggregate.None && c.Title == c.Source),
                    string.Join(" | ", columns.Select(c => $"{c.Source}:{c.Width}:{c.Aggregate}")));

                Check("[RP41] 空表头返回空集合（调用方据此提示「账本没有表头」，不造一堆空列）",
                    ScadaReportColumnImport.FromHeader(null).Count == 0
                    && ScadaReportColumnImport.FromHeader(new List<string>()).Count == 0
                    && ScadaReportColumnImport.FromHeader(new List<string> { "", "   " }).Count == 0,
                    "null / 空 / 全空白 三种输入");
            }
        }
    }
}
