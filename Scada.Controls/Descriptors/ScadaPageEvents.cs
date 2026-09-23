using System.Collections.Generic;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 画面自身支持的<b>事件钩子</b>清单（"这一页能配哪些事件"的唯一出处）。
    ///
    /// 为什么单开一个类而不是塞进 <see cref="ScadaPageProperties.All"/>：事件不是属性。
    /// 属性行是"读一个值、写一个值"，事件行是"勾上之后挂一串动作"——两者在面板上长得就不一样
    /// （前者一行，后者一块可增删排序的动作表）。图元侧早就把这件事分开过：
    /// 事件住在 <see cref="ElementDescriptor.Events"/>，而<b>不在</b> <c>descriptor.Properties</c> 里。
    /// 画面侧照同一个药方，否则面板就得给同一个集合里的行做类型判断。
    ///
    /// 只声明<b>有人发得出来</b>的事件（与 <see cref="BuiltInElements"/> 声明图元事件同一口径）：
    /// 清单里出现发不出来的事件，用户配上的就是个永远不响的钩子。
    /// <see cref="ScadaEventType.Loaded"/> 由运行态首帧画完后上报，
    /// <see cref="ScadaEventType.Unloaded"/> 由运行态停止 / 切走这一页时上报。
    /// <see cref="ScadaEventType.ValueChanged"/> 要等 S6 数据泵，所以不在这里——
    /// 它也不是画面级语义（画面自己没有"绑定的值"）。
    ///
    /// 新增一个画面事件时，改的就只有这一处 + 运行态里那条真发事件的路径，面板一行都不动。
    /// </summary>
    public static class ScadaPageEvents
    {
        /// <summary>
        /// 画面事件落在哪一组。
        ///
        /// 与「启动画面」同处「运行」组：这两行的共同点是"这一页<b>跑起来</b>是什么样"，
        /// 而不是画布上看到了什么。图元事件自成一「事件」组（图元面板的最后一组），
        /// 所以分组由宿主的清单给，而不是写死在事件行里。
        /// </summary>
        public const string Group = "运行";

        /// <summary>清单（面板按此顺序长事件行，顺序即面板上的先后）</summary>
        public static IReadOnlyList<ScadaEventType> All { get; } = new[]
        {
            ScadaEventType.Loaded,      // 首帧画完后触发一次
            ScadaEventType.Unloaded,    // 被切走 / 运行停止时触发
        };
    }
}
