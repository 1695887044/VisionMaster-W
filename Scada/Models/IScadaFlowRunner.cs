namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态的<b>流程启停出口</b>：「启动流程 / 停止流程 / 执行一次」三种动作落到哪条流程上、
    /// 落没落成，由它说了算。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 动作分发器（<c>ScadaActionDispatcher</c>）在 WPF 侧，它拿得到日志服务、拿得到变量值源，
    /// 却<b>拿不到流程引擎</b>——引擎、运行管理器与编译器是宿主应用层的单例，领域层（本程序集）
    /// 刻意不引用它们。于是"让某条流程跑起来/停下来"这件事需要一个中介：分发器只认识本接口，
    /// 接口的实现者（宿主侧 <c>ScadaFlowRunner</c>）手里握着引擎与编译器。
    ///
    /// 为什么接口定在领域层、实现放在宿主侧
    /// ---------
    /// 与 <see cref="IScadaNavigator"/> 的理由完全相同：<b>接口描述的是"要什么"，
    /// 实现解决的是"上哪儿拿"</b>。领域层知道"流程要按 Id 优先、名字兜底寻址，只认当前方案
    /// 里的流程"这条规则，却不知道引擎对象存在哪儿、会话怎么建；宿主知道这些，
    /// 但一旦让它去写"找不到就按名字猜"，规则的副本就多了一份。
    ///
    /// 三个方法的语义差别（为什么连续幂等、单次不幂等、停止最宽）
    /// ---------
    /// - <see cref="StartContinuous"/> 的语义是<b>"让目标处于运行状态"</b>：它已经在跑了，
    ///   要的状态就已成立——返回 true 并说一句"未重复启动"。操作员连点十下启动按钮，
    ///   日志里不该有十条吓人的警告（与 <see cref="IScadaNavigator.Navigate"/> 的幂等同一取舍）。
    /// - <see cref="RunOnce"/> 的语义是<b>"把这一遍真的跑了"</b>：目标正在跑的时候这一遍
    ///   无从发生，只能拒绝并说明——把"没跑"包装成"跑了"，操作员会以为检测结果已经有了
    ///   （假成功比失败危险得多，与引擎单次执行的 Try 版本同一理由）。
    /// - <see cref="Stop"/> 的语义是<b>"发出停止"</b>：目标本就没在跑时照样成功——
    ///   操作员要的"它停下来"这个状态已经成立（幂等）。停止键永远不该报错——
    ///   连"流程已停用"也不挡它（见契约④，2026-10-12 架构裁决）。
    ///
    /// 实现方的四条契约
    /// ---------
    /// 1) <b>不许抛异常</b>：返回 false 时用 <paramref name="reason"/> 说清为什么，
    ///    由分发器原样记进日志。抛出来会把"一次点击"变成一次崩溃。
    /// 2) <b>没接引擎 / 找不到流程时安静返回</b>：设计态下面板预览、断言宿主、网关进程
    ///    都会调到空实现或残缺环境上——返回 false 并给 reason（Stop 例外：幂等成功，见上）。
    /// 3) <b>寻址 Id 优先、名字兜底</b>（OrdinalIgnoreCase）：与
    ///    <see cref="IScadaValueSource.TryResolve"/> / <see cref="IScadaNavigator.Navigate"/>
    ///    同一套——流程改名后旧配置仍能靠 Id 找到，手写/导入的、只有名字的配置也不至于完全没用。
    /// 4) <b>只认当前方案里的流程；停用门只守启动、不守停止</b>：启动类方法
    ///    （StartContinuous / RunOnce）只受理<b>启用</b>的流程——停用的流程不给启动
    ///    （那是"配了却不该跑"的配置）；<b>Stop 豁免停用门</b>（2026-10-12 架构裁决：
    ///    工业现场的停止/急停入口永远要能按——启动被挡是对的，停止不能挡，停用流程
    ///    被误启/仍在跑时恰恰最需要这个出口能停它）。流程被删掉/方案没开等情形
    ///    一律安静 false 并给 reason（存在性门对三个方法一视同仁）。
    /// </summary>
    public interface IScadaFlowRunner
    {
        /// <summary>
        /// 让目标流程进入连续运行（到停止为止，循环执行）。
        /// </summary>
        /// <param name="flowId">目标流程稳定身份（权威）；空白 = 旧数据只按名找</param>
        /// <param name="fallbackName">目标流程名（Id 落空时按名找，忽略大小写与首尾空格）</param>
        /// <param name="reason">
        /// 返回 true 时带一句<b>净效果</b>（"已启动" / "已在运行，未重复启动"），
        /// false 时带一句话原因（"找不到流程…" / "流程已停用" / "未启动：…"），
        /// 由分发器原样写进日志与审计的说明列。返回 false 时它就是排查的入口，不许是 null。
        /// </param>
        /// <returns>目标是否已处于连续运行（刚启动的算，本来就在跑的也算）</returns>
        bool StartContinuous(string flowId, string? fallbackName, out string? reason);

        /// <summary>
        /// 把目标流程跑一遍（单次执行，跑完即停）。
        /// </summary>
        /// <param name="flowId">目标流程稳定身份（权威）；空白 = 旧数据只按名找</param>
        /// <param name="fallbackName">目标流程名（Id 落空时按名找，忽略大小写与首尾空格）</param>
        /// <param name="reason">
        /// 返回 true 时带一句净效果（"已下发单次执行"——完成后由实现方在运行日志里补一行回执）；
        /// false 时带一句话原因（"流程正在运行，本次未执行" / "找不到流程…" 等）。
        /// </param>
        /// <returns>
        /// 这一遍是否已经受理执行。<b>不</b>代表"已经跑完"——执行本身是异步的，跑完与否
        /// 由实现方在运行日志里交代。false = 本次什么都没发生。
        /// </returns>
        bool RunOnce(string flowId, string? fallbackName, out string? reason);

        /// <summary>
        /// 向目标流程发出停止。
        /// </summary>
        /// <param name="flowId">目标流程稳定身份（权威）；空白 = 旧数据只按名找</param>
        /// <param name="fallbackName">目标流程名（Id 落空时按名找，忽略大小写与首尾空格）</param>
        /// <param name="reason">一句净效果（"已发出停止" / "本就未在运行"）；false 时为原因（如"找不到流程…"）</param>
        /// <returns>目标是否已处于停止状态（刚停的算，本来就没跑的也算——幂等）</returns>
        bool Stop(string flowId, string? fallbackName, out string? reason);
    }
}
