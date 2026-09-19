using System;
using Core.Interfaces;
using VisionMaster.Helpers;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaActionDispatcher"/> 的默认实现：把一条钩子里的动作按顺序落到运行日志上。
    ///
    /// 现在接通了哪些动作
    /// ---------
    /// 「记录日志」（写一行运行日志）与「写变量」（<see cref="ScadaActionType.WriteVariable"/>，
    /// S6 接入）都已真执行；「切换画面」要等 S8 画面导航（运行态换 <c>CurrentPage</c> 并重建可视内容），
    /// 配置与落盘已经支持，只是执行不了。执行不了怎么办，这里选的不是"空 switch 什么都不做"，
    /// 而是<b>照实说一句警告</b>：现场配了一条"切换画面"却一声不响，用户的结论一定是"软件有 bug"，
    /// 然后整件事都得重查一遍；而日志里明写"切换画面要等画面导航（S8）接入，本条未执行"，
    /// 他立刻知道该等版本、而不是该改配置。
    /// 这句话的出处是 <see cref="ScadaActionTypeExtensions.PendingReason"/>（不是本类的私有方法）：
    /// 属性面板上那一行提示用的是同一句，两处口径必须一致。
    ///
    /// 写变量为什么按"没选变量 / 找不到 / 转不过 / 写不进"分四档报
    /// ---------
    /// 这四种失败对现场是四件事：没选变量是<b>配置漏了</b>（去面板上补）；找不到变量是<b>改名或删了</b>
    /// （去变量管理里对一下）；转不过是<b>值写错了</b>（"abc" 写不进 int）；写不进是<b>设备侧的事</b>
    /// （离线 / 没配地址 / 驱动报错，原因由 <c>IWritableVariable.TryWrite</c> 原样带上来）。
    /// 合成一句"写变量失败"，用户就得自己把这四条路各试一遍。
    /// 分档落在日志级别上：前两档是配置问题记 <c>Warn</c>（软件没坏，配置没对），
    /// 后两档是这次操作真没成记 <c>Error</c>（要人去查）。四档都带动作原文，一条都不哑掉。
    ///
    /// 为什么动作执行只往日志里写，不弹对话框
    /// ---------
    /// 操作员点一下按钮就弹窗打断，是组态现场最招人烦的设计（他可能连点二十次）。
    /// 配置类问题（动作没接上、变量名写错）是给集成人员看的，运行日志正好是他们的入口。
    ///
    /// 为什么一次触发里每条动作各写一行，而不是攒成一行
    /// ---------
    /// 三条动作攒成一行，中间那条失败了就说不清是哪条成的、哪条没成；
    /// 一行一条，日志本身就是执行流水，排查"点了没反应"时按时间从上往下读就行。
    ///
    /// 线程：只在 UI 线程上被调用（事件的来源是鼠标与渲染，都在这条线上），故不加锁。
    /// <see cref="IScadaValueSource"/> 也是线程安全的只读查询（写值最终落到变量模型，
    /// 网络变量自己管通讯线程），所以这里不需要额外的同步。
    /// </summary>
    public sealed class ScadaActionDispatcher : IScadaActionDispatcher
    {
        private readonly ILogService _log;
        private readonly IScadaValueSource _values;

        public ScadaActionDispatcher(ILogService log, IScadaValueSource values)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        /// <inheritdoc/>
        public void Dispatch(ScadaEventHook? hook, string? sourceName)
        {
            // 空钩子/空动作表直接收手：没配动作不是错误，日志里也不该留一行"什么都没干"
            if (hook == null || hook.Actions.Count == 0)
                return;

            var subject = string.IsNullOrWhiteSpace(sourceName) ? "未命名对象" : sourceName;
            var head = $"[组态事件] {subject} · {hook.Event.DisplayName()}";

            foreach (var action in hook.Actions)
            {
                // 集合里的 null 只可能来自手工改坏的 .vms（反序列化会把 null 原样塞进 List），
                // 跳过它，不要因为一条坏数据把整串动作和这次点击一起废掉
                if (action == null)
                    continue;

                try
                {
                    Execute(action, head);
                }
                catch (Exception ex)
                {
                    // 接口契约②：一条失败不中断后面的动作。吞掉异常是这里的选择，
                    // 但必须留下带动作原文的一行，否则就成了查不到的哑失败。
                    _log.Error($"{head} → {action.Describe()} 执行失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 执行单条动作。<paramref name="head"/> 是这次触发的公共前缀（谁 + 发生了什么事件），
        /// 传进来而不是在这儿重新拼，是为了让"面板上那一行的措辞"和"日志里那一行的措辞"
        /// 由同一个出处生成（<see cref="ScadaEventTypeExtensions.DisplayName"/> 与
        /// <see cref="ScadaAction.Describe"/>）。
        /// </summary>
        private void Execute(ScadaAction action, string head)
        {
            switch (action.Type)
            {
                case ScadaActionType.Log:
                    // 内容留空时回落成"对象 + 事件"——新建一条动作就立刻能用，不必先逼用户填一句话
                    _log.Info(string.IsNullOrWhiteSpace(action.Text) ? head : $"{head} → {action.Text}");
                    return;

                case ScadaActionType.WriteVariable:
                    ExecuteWriteVariable(action, head);
                    return;

                default:
                    // 切换画面，以及"高版本存下的、本版本不认识的类型"（枚举按数值反序列化，
                    // 不校验取值范围）：两者对现场的处境是同一个——配置存得下、执行还没接上，
                    // 所以共用同一句说明。那句话由领域层出（ScadaActionType.PendingReason），
                    // 与属性面板上那一行提示<b>逐字相同</b>，用户才能把"我配的这条"和"日志里这条"对上号。
                    _log.Warn($"{head} → {action.Describe()}：{action.Type.PendingReason()}，本条未执行");
                    return;
            }
        }

        /// <summary>
        /// 执行一条"写变量"动作：解析变量 → 把文本转成变量的类型 → 走显式写契约。
        ///
        /// 为什么值要从字符串现转、而不是配置时转好存下来：变量类型是可以改的
        /// （变量管理里把 int 改成 string），配置时转好的值会跟着僵死；而且 .vms 是给人看、
        /// 手工可改的，转换必须发生在"真要写的那一刻"，规则只认 <see cref="VariableValueConverter"/> 一份。
        ///
        /// 为什么解析走 <see cref="IScadaValueSource"/> 而不是直接查变量注册表：
        /// 与数据泵（<c>ScadaRuntimeBinder</c>）走同一条寻址通道，Id 优先、名字兜底的口径才不会分叉；
        /// 而且写能力被收在 <c>IWritableVariable</c> 后面，这里只认句柄，
        /// 将来换成"OPC 组写 / 历史回放"只需换值源实现，本方法一行不动。
        /// </summary>
        private void ExecuteWriteVariable(ScadaAction action, string head)
        {
            // ① 没选变量：配置漏了。Describe() 此刻是"写变量（未选变量）"，再补一句就重复了，直接自己组织
            if (action.VariableId == Guid.Empty && string.IsNullOrWhiteSpace(action.VariableName))
            {
                _log.Warn($"{head} → 写变量：还没选变量，本条未执行");
                return;
            }

            // ② 找不到变量：改名或删了（Id 对不上、名字也对不上）
            if (!_values.TryResolve(action.VariableId, action.VariableName, out var handle) || handle == null)
            {
                _log.Warn($"{head} → {action.Describe()}：找不到变量「{action.VariableName}」，本条未执行");
                return;
            }

            // ③ 值转不过去：写错了类型（"abc" 写不进 int）。原因由转换器出，带目标类型名，可直接照做
            if (!VariableValueConverter.TryConvert(action.Value, handle.DataType, out var value, out var convertError))
            {
                _log.Error($"{head} → {action.Describe()}：{convertError}");
                return;
            }

            // ④ 写不进：设备侧的事（离线 / 没配地址 / 驱动异常）。原因由变量自己给，原样带上来不加工——
            //    这里是唯一拿得到真实结果的地方，包一层"写入失败"就把可行动的信息盖掉了。
            if (!handle.TryWrite(value, out var writeError))
            {
                _log.Error($"{head} → {action.Describe()}：写入失败：{writeError}");
                return;
            }

            // 成功照实记一行。这里用 Describe() 而不是自己拼："写变量 启动 := 1" 与属性面板上那条
            // 动作的摘要同一个出处，日志和配置界面对得上号。
            _log.Info($"{head} → {action.Describe()}");
        }
    }
}
