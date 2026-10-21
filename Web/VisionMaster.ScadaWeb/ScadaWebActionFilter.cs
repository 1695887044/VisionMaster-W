using System;
using System.Collections.Generic;
using VisionMaster.Scada;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// Web 命令层的动作白名单过滤器（M2）：把桌面端配好的一条事件钩子<b>复制</b>成
    /// "只含 Web 允许执行的动作"的一次性副本，交给 <see cref="IScadaActionDispatcher.Dispatch"/>
    /// 按原顺序执行。
    ///
    /// <b>为什么必须是副本</b>
    /// ---------
    /// Web 命令来自网络，而钩子与它的 <see cref="ScadaEventHook.Actions"/> 属于桌面文档：
    /// 直接在原钩子上增删，会写进 <c>.vms</c>、进撤销栈，表现就是"Web 上点了一下，
    /// 桌面的组态配置被改了"。副本脱离文档，改它不影响任何模型，也不需要撤销。
    /// 副本仍然是一个普通 <see cref="ScadaEventHook"/>，所以分发器可以原样消费
    /// （它只读 <see cref="ScadaEventHook.Event"/>、<see cref="ScadaEventHook.Actions"/>
    /// 与确认开关）。
    ///
    /// <b>为什么 Navigate / NavigateBack 永远剔除（镜像模式）</b>
    /// ---------
    /// 运行态 Web 是"所有人看同一页"的镜像：切页是<b>现场</b>的动作，不是某个客户端的视图偏好。
    /// 允许一条 Web 点击把桌面运行窗口切走，会同时改掉所有其它客户端看到的画面。
    /// 所以即使调用方把 <see cref="ScadaActionType.Navigate"/> 或
    /// <see cref="ScadaActionType.NavigateBack"/> 放进 allowed，这里也一律剔除，
    /// 并把它记进 <c>skipped</c>——命令层据此落审计（动作列写 <c>Skipped/mirror-mode</c>），
    /// 而不是让用户看到"点了没反应"却查不到原因。
    ///
    /// <b>为什么副本强制 RequireConfirmation = false</b>
    /// ---------
    /// 那个二次确认是桌面端的模态框（宿主侧的 <c>IScadaConfirmationService</c>，住在控件库，
    /// 本模块不引用它）；Web 命令链上没有人能回答它，分发器会按"取消"处理，整串动作一条都不执行。
    /// Web 侧的确认（如果将来要）属于命令层自己的交互，不能复用桌面那个开关。
    ///
    /// <b>线程</b>
    /// ---------
    /// 调用方必须在宿主 UI 线程上调用：本方法读 <see cref="ScadaEventHook.Actions"/>（文档模型，
    /// 单线程语义），而 Web 的网络 handler 跑在 IO 线程上。命令层应把"过滤 + 分发"整段投到
    /// 宿主 Dispatcher 里执行，不要在网络线程上先过滤再把副本递过去。
    /// </summary>
    public static class ScadaWebActionFilter
    {
        /// <summary>
        /// 镜像模式下无条件剔除的页面类动作：切页与回退都会改掉所有客户端看到的画面，
        /// 属于“现场动作”，不是某个浏览器的视图偏好。
        /// 过滤器、白名单构建、审计原因三处共用这一份判据，避免口径分叉。
        /// </summary>
        public static bool IsMirrorOnlyPageAction(ScadaActionType type)
            => type is ScadaActionType.Navigate or ScadaActionType.NavigateBack;

        /// <summary>
        /// 按白名单复制一条钩子。原钩子与原 <see cref="ScadaEventHook.Actions"/> 集合
        /// <b>只读不写</b>（不增删、不替换、不重排）。
        ///
        /// <b>剔除规则（按判定顺序）</b>
        /// <list type="number">
        /// <item><see cref="ScadaActionType.Navigate"/> / <see cref="ScadaActionType.NavigateBack"/>：无条件剔除（镜像模式，见类注释）；</item>
        /// <item>不在 <paramref name="allowed"/> 里的动作类型：剔除；</item>
        /// <item>集合里的 <c>null</c> 项（手工改坏的 <c>.vms</c>）：静默跳过，执行侧本来也会跳过它。</item>
        /// </list>
        /// 被剔除的动作<b>按原顺序</b>逐个记进 <paramref name="skipped"/>（同类型出现几次就记几次，
        /// 命令层能据此回答"这次到底挡掉了几条"）；<c>null</c> 项没有类型，不进列表。
        /// </summary>
        /// <param name="source">桌面端的事件钩子；<c>null</c> 返回 <c>null</c>（分发器对空钩子本来就是空操作）</param>
        /// <param name="allowed">本次允许执行的动作类型集合；传 <c>null</c> 按"一条都不放行"处理</param>
        /// <param name="skipped">被剔除的动作类型（含 Navigate / NavigateBack）；没有剔除时为空列表</param>
        /// <returns>
        /// 可被 <see cref="IScadaActionDispatcher.Dispatch"/> 直接执行的副本；
        /// <paramref name="source"/> 为 <c>null</c> 时返回 <c>null</c>。
        /// 副本的 <see cref="ScadaEventHook.Actions"/> 可能为空（全部被剔除）——分发器对空集合也是空操作。
        /// </returns>
        public static ScadaEventHook? BuildAllowedCopy(
            ScadaEventHook? source,
            IReadOnlySet<ScadaActionType> allowed,
            out List<ScadaActionType> skipped)
        {
            skipped = new List<ScadaActionType>();

            if (source == null)
                return null;

            var copy = new ScadaEventHook
            {
                Event = source.Event,

                // 桌面模态确认在 Web 链路上没人能回答；保留 true 会让整串动作被"取消"掉。
                RequireConfirmation = false,

                // 提示语仍复制：命令层审计/前端提示可能要用同一句话（它只是文案，不触发行为）。
                ConfirmationMessage = source.ConfirmationMessage,
            };

            foreach (var action in source.Actions)
            {
                // 手工改坏的 .vms 会把 null 原样反序列化进集合（ScadaEventHook.Subscribe 为此专门挡过）。
                // 这里同样跳过：它没有类型可记，也不该让一条坏动作把整次 Web 命令炸掉。
                if (action is null)
                    continue;

                var type = action.Type;

                // 页面类动作必须先判：只有这样才能保证"即使在 allowed 里也一定被剔除"。
                if (IsMirrorOnlyPageAction(type)
                    || allowed is null || !allowed.Contains(type))
                {
                    skipped.Add(type);
                    continue;
                }

                copy.Actions.Add(CloneAction(action));
            }

            return copy;
        }

        /// <summary>
        /// 逐字段复制一条动作。刻意<b>不</b>漏任何一组参数：
        /// 写变量（VariableId/VariableName/Value/OnlyIfPreviousSucceeded）、
        /// 流程五条（FlowId/FlowName）、运动九条（AxisId/AxisName/TargetMm/VelocityMmPerS）。
        ///
        /// 为什么逐字段而不是"整个对象搬过去"：动作是可变模型对象，搬引用等于让 Web 命令与桌面文档
        /// 共享同一份可变状态（将来分发器或命令层补一个字段就会写到文档上）。
        /// 这里多写十几行换来的是"Web 永远碰不到模型"这条硬边界。
        /// </summary>
        private static ScadaAction CloneAction(ScadaAction source) => new()
        {
            Type = source.Type,
            Text = source.Text,
            VariableId = source.VariableId,
            VariableName = source.VariableName,
            // 数组下标必须一起复制：漏了它，副本默认 -1（整个变量），
            // 一次"写数组元素"的 Web 点击会变成"写整个变量"（2026-10-21 终审 P0-2）。
            ArrayIndex = source.ArrayIndex,
            Value = source.Value,
            OnlyIfPreviousSucceeded = source.OnlyIfPreviousSucceeded,
            TargetPageId = source.TargetPageId,
            TargetPageName = source.TargetPageName,
            FlowId = source.FlowId,
            FlowName = source.FlowName,
            AxisId = source.AxisId,
            AxisName = source.AxisName,
            TargetMm = source.TargetMm,
            VelocityMmPerS = source.VelocityMmPerS,
        };
    }
}
