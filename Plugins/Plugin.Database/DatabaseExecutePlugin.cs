using System;
using System.ComponentModel.DataAnnotations;
using Core.Interfaces;
using Core.Interfaces.Database;

namespace Plugin.Database
{
    /// <summary>
    /// 数据库执行：跑一条 UPDATE / DELETE（必须带 WHERE），输出影响行数与耗时。
    ///
    /// 失败语义：默认 <c>FailFast=true</c>——写库失败要停线，不能"红着继续生产"。
    /// 影响行数超过语句上配置的上限时，服务端会在事务内回滚并返回失败（宁可不写，也不误删）。
    /// </summary>
    [Display(
        Name = "数据库执行",
        GroupName = "数据处理",
        Description = "执行一条 UPDATE / DELETE 命名语句（必须带 WHERE）；失败默认中断当轮",
        ShortName = "\uf013")]
    public class DatabaseExecutePlugin : DatabasePluginBase
    {
        public DatabaseExecutePlugin() : base(failFastDefault: true) { }

        public OutputPort<int> AffectedRows { get; } = new("AffectedRows", "实际影响行数");

        public OutputPort<double> ElapsedMs { get; } = new("ElapsedMs", "本次执行耗时（毫秒）");

        protected override DbStatementKind RequiredKind => DbStatementKind.Execute;

        protected override DbOutputRow[] CreateOutputRows() => new[]
        {
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

            AffectedRows.Value = result.AffectedRows;
            ElapsedMs.Value = result.ElapsedMs;
        }
    }
}
