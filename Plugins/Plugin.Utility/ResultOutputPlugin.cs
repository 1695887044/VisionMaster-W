using System.ComponentModel.DataAnnotations;
using Core.Interfaces;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 结果输出步骤：把本轮业务判定（OK/NG）显式声明给宿主。
    ///
    /// 【为什么需要它】底栏 KPI / SCADA / 报表都需要"这一轮到底 OK 还是 NG"，
    /// 但插件对 NG 的步骤状态语义并不一致：判定表 NG 会 <c>Fail()</c>，
    /// 卡尺/斑点 NG 时 <c>Success</c> 仍为 true。用"有步骤 Failed"当 NG 必然把
    /// 产品不良和技术故障混在一起。所以判定必须由流程显式声明，宿主轮末读取。
    ///
    /// 【它不做什么】
    /// - 不计算节拍、不统计计数、不输出当前节点——那些由宿主统计；
    /// - 不写全局变量/系统变量——系统变量是只读派生量，写全局变量会退回用户变量
    ///   改名/删除/复制/迁移那一套生命周期问题。
    ///
    /// 【关键语义】NG 时本步骤仍然 <c>Success=true</c>。判定是正常结果；
    /// 步骤失败只表示"没拿到判定"，否则父流程/调用流程会把产品 NG 当技术故障。
    /// </summary>
    [Display(
        Name = "结果输出",
        GroupName = "流程控制",
        Description = "声明本轮业务判定（OK/NG），供运行统计与底栏 KPI 使用；每条生产流程应恰好一个，放在主路径末尾。",
        ShortName = "\uf00c")]
    [ParallelSafe]
    public class ResultOutputPlugin : VisionPluginBase, IFlowResultSource
    {
        /// <summary>
        /// 本轮业务判定。必填：编译器会按 <see cref="InputPort{T}.IsRequired"/> 检查未接线
        /// （没有上游连线也没有手填常量时直接报 [参数缺失]，不会等到运行期才静默按 false 跑）。
        /// </summary>
        public InputPort<bool> IsOkPort { get; } =
            new("IsOk", false, "本轮判定：true = OK，false = NG") { IsRequired = true };

        /// <summary>NG 原因/摘要（可选）。宿主会把它写进本轮记录与系统变量 LastReason。</summary>
        public InputPort<string> ReasonPort { get; } =
            new("Reason", "", "NG 原因/说明（可选）") { IsRequired = false };

        bool IFlowResultSource.IsOk => IsOkPort.GetTypedValue();

        string IFlowResultSource.Reason => ReasonPort.GetTypedValue() ?? string.Empty;

        public override void RunAlgorithm(IExecutionContext context)
        {
            bool isOk = IsOkPort.GetTypedValue();
            string reason = ReasonPort.GetTypedValue() ?? string.Empty;

            // 手填常量也能编译通过（编译器的必填检查允许常量）；生产流程应接上游判定，
            // 这里只提示，不失败——否则"固定值做测试"这种合法用法会被堵死。
            if (IsOkPort.LinkedSource == null)
                context.Logger.Warn($"{InstanceName} 的 IsOk 使用固定值 {(isOk ? "true" : "false")}（未接上游判定）");

            context.Logger.Info(
                $"{InstanceName} 判定={(isOk ? "OK" : "NG")}{(string.IsNullOrWhiteSpace(reason) ? string.Empty : "：" + reason)}");

            // 判定是正常结果：NG 也不让步骤失败。步骤失败只表示"没拿到判定"。
            Success.Value = true;
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
