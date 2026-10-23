using System;
using System.ComponentModel.DataAnnotations;
using Core.Interfaces;
using Core.Interfaces.Database;

namespace Plugin.Database
{
    /// <summary>
    /// 数据库查询：按命名语句查库，输出首值 / 结果 JSON / 行数 / 列名 / 是否截断 / 耗时。
    ///
    /// 失败语义：默认 <c>FailFast=false</c>——查询失败只让本步骤红，流程继续（读配方失败通常走下游 If 判空，
    /// 不该因为一次读库失败把整条线停掉）。需要停线的现场把 FailFast 打开。
    /// <c>RowCount=0</c> 是成功，不是失败。
    /// </summary>
    [Display(
        Name = "数据库查询",
        GroupName = "数据处理",
        Description = "按硬件管理里封装好的命名语句查询数据库；0 行不算失败，结果可写全局变量或参与判断",
        ShortName = "\uf002")]
    public class DatabaseQueryPlugin : DatabasePluginBase
    {
        public DatabaseQueryPlugin() : base(failFastDefault: false) { }

        public OutputPort<object> Scalar { get; } = new("Scalar", "第一行第一列；无行时为 null");

        public OutputPort<string> RowsJson { get; } = new("RowsJson", "结果行 JSON 数组（受最大行数限制）");

        public OutputPort<int> RowCount { get; } = new("RowCount", "返回行数（0 不算失败）");

        public OutputPort<string> Columns { get; } = new("Columns", "列名（逗号分隔，保持查询顺序）");

        public OutputPort<bool> Truncated { get; } = new("Truncated", "是否被最大行数截断（true = 只拿到前 N 行）");

        public OutputPort<double> ElapsedMs { get; } = new("ElapsedMs", "本次查询耗时（毫秒）");

        protected override DbStatementKind RequiredKind => DbStatementKind.Query;

        protected override DbOutputRow[] CreateOutputRows() => new[]
        {
            new DbOutputRow("Scalar", "任意", "第一行第一列；无行时为 null"),
            new DbOutputRow("RowsJson", "文本", "结果行 JSON 数组"),
            new DbOutputRow("RowCount", "整数", "返回行数"),
            new DbOutputRow("Columns", "文本", "列名（逗号分隔）"),
            new DbOutputRow("Truncated", "布尔", "是否被最大行数截断"),
            new DbOutputRow("ElapsedMs", "数值", "耗时（毫秒）")
        };

        public override void RunAlgorithm(IExecutionContext context)
        {
            var values = BuildParameterValues();
            var options = BuildOptions(context);

            if (!context.Database.TryQuery(StatementId, values, options, out var result, out var error))
            {
                HandleFailure(error);
                return;
            }

            Scalar.Value = result.Rows.Count > 0 && result.Columns.Count > 0 ? result.Rows[0][0] : null;
            RowsJson.Value = BuildRowsJson(result);
            RowCount.Value = result.RowCount;
            Columns.Value = string.Join(",", result.Columns);
            Truncated.Value = result.Truncated;
            ElapsedMs.Value = result.ElapsedMs;
        }
    }
}
