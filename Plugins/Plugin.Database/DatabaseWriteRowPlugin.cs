using System;
using System.ComponentModel.DataAnnotations;
using Core.Interfaces;
using Core.Interfaces.Database;

namespace Plugin.Database
{
    /// <summary>
    /// 数据库写行：跑一条 INSERT（含 ON CONFLICT 的 UPSERT），输出是否受理 / 影响行数 / 耗时。
    ///
    /// 失败语义：默认 <c>FailFast=true</c>——每件结果落库失败必须停线，否则追溯数据会静默缺口。
    /// <c>Accepted=true</c> 表示语句执行成功；<c>AffectedRows=0</c> 是合法结果
    /// （例如 ON CONFLICT DO NOTHING 命中已有行），下游据此判断"是否真的新增了一行"。
    /// </summary>
    [Display(
        Name = "数据库写行",
        GroupName = "数据处理",
        Description = "按命名语句写一行（INSERT / UPSERT）；失败默认中断当轮，保证追溯数据不留缺口",
        ShortName = "\uf0c7")]
    public class DatabaseWriteRowPlugin : DatabasePluginBase
    {
        public DatabaseWriteRowPlugin() : base(failFastDefault: true) { }

        public OutputPort<bool> Accepted { get; } = new("Accepted", "语句是否执行成功（AffectedRows=0 也可能是成功）");

        public OutputPort<int> AffectedRows { get; } = new("AffectedRows", "实际影响行数（UPSERT 命中已有行时为 0）");

        public OutputPort<double> ElapsedMs { get; } = new("ElapsedMs", "本次执行耗时（毫秒）");

        protected override DbStatementKind RequiredKind => DbStatementKind.WriteRow;

        protected override DbOutputRow[] CreateOutputRows() => new[]
        {
            new DbOutputRow("Accepted", "布尔", "语句是否执行成功"),
            new DbOutputRow("AffectedRows", "整数", "实际影响行数"),
            new DbOutputRow("ElapsedMs", "数值", "耗时（毫秒）")
        };

        public override void RunAlgorithm(IExecutionContext context)
        {
            var values = BuildParameterValues();
            var options = BuildOptions(context);

            if (!context.Database.TryExecute(StatementId, values, options, out var result, out var error))
            {
                HandleFailure(error);
                return;
            }

            Accepted.Value = true;
            AffectedRows.Value = result.AffectedRows;
            ElapsedMs.Value = result.ElapsedMs;
        }
    }
}
