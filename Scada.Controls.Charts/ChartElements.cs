using System.Collections.Generic;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Scada.Controls.Charts
{
    /// <summary>
    /// 曲线图元库的清单（与内置图元的 <see cref="BuiltInElements"/> 同一形态）。
    ///
    /// 之所以要一份"清单"而不是让每个图元类自己注册：与内置图元同一条理由——
    /// 图元库的"我们有哪些图元"必须是一处可数的清单，断言工程据此对账
    /// （数一数清单条数 = 注册表里来自本库的条数），而不是靠"谁还记得调过注册"。
    ///
    /// <b>属性来源</b>：全部由 <c>[ScadaProperty]</c> 源生成器产出，经
    /// <see cref="ScadaPropertyCatalog"/> 按继承链收集。所以加一条属性的动作只有一处——
    /// 在控件类上写一个 partial 属性加一个特性。
    /// </summary>
    internal static class ChartElements
    {
        internal static IReadOnlyList<ElementDescriptor> All { get; } = new ElementDescriptor[]
        {
            new()
            {
                TypeKey = "Hmi.ChartTrend",
                DisplayName = "趋势曲线",
                ControlType = typeof(ChartTrendElement),
                Category = "数值",
                DefaultWidth = 320,
                DefaultHeight = 160,
                Description = "把一根变量的历史值画成时间曲线（压力、温度、转速的趋势）。"
                            + "把「数值」绑到变量上即可；横轴是每个值真实到达的时刻，"
                            + "所以曲线的疏密本身就是信息——密表示在动、疏表示稳住了",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(ChartTrendElement))),
            },
        };
    }
}
