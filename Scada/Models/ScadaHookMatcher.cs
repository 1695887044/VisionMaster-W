using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// "一张钩子表里，某个事件命中了几条"——<b>命中口径的唯一实现</b>。
    ///
    /// 为什么要把这十几行抽出来
    /// ---------
    /// 现在有三处要问同一个问题：<see cref="ScadaRuntime.RaiseElementEvent"/>（图元钩子）、
    /// <see cref="ScadaRuntime.RaisePageEvent"/>（画面钩子）、
    /// <see cref="ScadaVariableEventEngine"/>（变量钩子，可能在后台线程上问）。
    /// 三处各写一份的话，将来任何一次口径微调（比如"空动作表算不算配过"）都只改到一两处，
    /// 剩下的表现为"同一个配置在图元上响、在变量上不响"——最难查的那种不一致。
    ///
    /// 命中条件（三处逐字一致）
    /// ---------
    /// ① 钩子非空（手工改坏的 .vms 里可能真有 null 项）；
    /// ② 事件类型对上；
    /// ③ 动作表<b>非空</b>——"配了事件但一条动作都没加"等同于没配，
    ///   它不该在日志里留下一行"触发了但什么都没干"。
    ///
    /// 为什么返回值是"命中条数"而不是 bool：调用方要用它回答"这次操作到底有没有反应"
    /// （<see cref="ScadaRuntime.RaiseElementEvent"/> 的返回值就是这么用的）。
    /// </summary>
    internal static class ScadaHookMatcher
    {
        /// <summary>扫一遍钩子表，把命中的逐条交给 <paramref name="broadcast"/>，返回命中条数</summary>
        public static int Raise(IEnumerable<ScadaEventHook> hooks, ScadaEventType eventType, Action<ScadaEventHook> broadcast)
        {
            int hits = 0;

            foreach (var hook in hooks)
            {
                if (hook == null || hook.Event != eventType || hook.Actions.Count == 0)
                    continue;

                broadcast(hook);
                hits++;
            }

            return hits;
        }

        /// <summary>
        /// 这个事件在这张钩子表里<b>配过没有</b>——条件与 <see cref="Raise"/> 逐字一致。
        ///
        /// 单独留一个方法是为了"被权限拦下的图元要不要出声"这种判断：
        /// 它要回答的是"本来该不该有反应"，条件必须和"点下去有没有反应"一模一样。
        /// </summary>
        public static bool Has(IEnumerable<ScadaEventHook> hooks, ScadaEventType eventType)
        {
            foreach (var hook in hooks)
            {
                if (hook != null && hook.Event == eventType && hook.Actions.Count > 0)
                    return true;
            }

            return false;
        }
    }
}
