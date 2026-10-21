using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态报警命令出口（M2：Web 命令层的「确认报警 / 全部确认」）。
    ///
    /// 为什么单独一个接口，而不是把 <see cref="ScadaAlarmEngine"/> 直接交给可选模块
    /// ---------
    /// 可选模块（Web 运行态）只引用 <c>VM.Scada</c>，够得到引擎类型；但引擎是<b>宿主装配</b>出来的
    /// 对象（方案级监控 <c>ScadaMonitoringHost</c> 持有、随方案加载重建），Web 模块自己解析不到、
    /// 也不该解析。本接口只描述"确认报警"这一件事，由宿主把"此刻活着的那台引擎"接在后面——
    /// 与 <see cref="IScadaRunHub"/> 把运行事实交给可选模块是同一条分工：
    /// 领域层定契约，宿主接线，模块只发命令。
    ///
    /// <b>线程</b>：只允许在宿主 UI 线程调用。宿主侧的实现读的是 UI 线程字段（当前监控/运行窗口），
    /// 引擎本身虽然自带上锁，但"当前是不是在运行"这个判断不是线程安全的。
    /// Web 命令层必须把调用投到宿主 Dispatcher 上（与快照/值帧同一条线程纪律）。
    ///
    /// <b>审计</b>：本接口<b>不接收 actor、也不写审计</b>。操作者、Web 会话、来源地址只有调用方
    /// （Web 命令层）知道；确认动作的"成功/失败/拒绝"与那句可读原因由调用方按既有口径
    /// 落到 <see cref="IScadaAuditSink"/>（动作列写"确认报警"）。接口这边不重复记一遍——
    /// 同一件事记两处，早晚变成两条对不上的记录（与宿主不重复记成功动作同一条理由）。
    ///
    /// <b>失败用 out error 而不是抛异常</b>：Web 命令的失败是常态（报警已被别人确认、已经恢复清除、
    /// 桌面端没在运行），它们都要变成一条能上屏的中文原因；抛异常会把"业务结果"与"程序故障"
    /// 混在一起，命令层还得逐条 try/catch。
    /// </summary>
    public interface IScadaAlarmCommands
    {
        /// <summary>
        /// 确认<b>某一条</b>报警。
        /// 返回 <c>true</c> = 这次真的发生了态迁移（激活 → 已确认，或已恢复 → 了结）；
        /// 返回 <c>false</c> = 没有执行，原因在 <paramref name="error"/>。
        ///
        /// <b>0 的两种含义必须分开</b>：引擎的 <see cref="ScadaAlarmEngine.Acknowledge"/> 返回 0，
        /// 既可能是"这条报警不在实时列表上"（已恢复清除 / 不属于本轮运行），也可能是
        /// "它已经确认过"（<see cref="ScadaAlarmState.Acknowledged"/> 再按一次）。
        /// 这两件事对操作员的意义完全不同——前者是"手慢了"，后者是"点错了"——
        /// 所以实现要先查一次 <see cref="ScadaAlarmEngine.ActiveAlarms"/> 再给出可读的 error，
        /// 不许一律回一句"确认失败"。
        /// </summary>
        /// <param name="alarmId">报警记录的稳定身份（<see cref="ScadaAlarmRecord.AlarmId"/>）</param>
        /// <param name="error">失败原因（可直接上屏）；成功时为 <c>null</c></param>
        bool TryAcknowledge(Guid alarmId, out string? error);

        /// <summary>
        /// 确认当前<b>全部</b>待确认报警（激活 + 已恢复未确认）。
        /// 返回真正发生态迁移的条数；<c>0</c> = 没有可确认的（不是失败，error 为 <c>null</c>）。
        ///
        /// 与 <see cref="ScadaAlarmEngine.AcknowledgeAll"/> 同一口径：已确认过的不计入条数，
        /// 所以"全部确认"不会报出一个虚高的数字。
        /// </summary>
        /// <param name="error">失败原因（未运行 / 引擎未挂载 / 引擎抛异常）；正常时为 <c>null</c></param>
        /// <returns>本次真正确认的条数</returns>
        int AcknowledgeAll(out string? error);
    }
}
