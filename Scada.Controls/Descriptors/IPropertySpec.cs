using System;
using System.Collections.Generic;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 属性面板一行所需的<b>最小元数据</b>：标签叫什么、用什么编辑器、边界在哪、能不能绑变量。
    ///
    /// 为什么要单独抽这个接口（<see cref="ElementPropertyDescriptor"/> 已经全有了）：
    /// 属性面板原先只认图元，而画面自己也有名称/尺寸/底色这些要编辑的东西。
    /// 两边共用一套编辑器模板（文本框、数值框、色块、复选框）是显然的，
    /// 但共用的前提是"行"只依赖一份契约，而不是依赖 <c>ElementPropertyDescriptor</c> 这个具体类——
    /// 那个类还带着 <c>TargetProperty</c>（依赖属性换算落点）和 <c>DefaultValue</c>（属性袋缺键时的兜底），
    /// 这两样对画面属性毫无意义，硬套等于让画面去冒充一个图元。
    ///
    /// 于是分工变成：
    /// <code>
    /// IPropertySpec          —— 面板看得见的部分（本接口）
    ///   ├─ ElementPropertyDescriptor —— 图元属性：多了 TargetProperty / DefaultValue / IsGeometry
    ///   └─ ScadaPagePropertySpec     —— 画面属性：多了读写委托（画面是强类型 CLR 属性，没有属性袋）
    /// </code>
    /// 新增一类要编辑的对象（比如以后给图层加属性），实现这个接口即可，面板与模板一行不改。
    /// </summary>
    public interface IPropertySpec
    {
        /// <summary>属性键（图元侧是属性袋键或 "$" 几何键；画面侧只是标识，不落盘）</summary>
        string Key { get; }

        /// <summary>给人看的名字（面板标签）</summary>
        string DisplayName { get; }

        /// <summary>编辑器类型（面板据此挑模板）</summary>
        ElementPropertyKind Kind { get; }

        /// <summary>分组标题</summary>
        string Group { get; }

        /// <summary>悬停说明（可为空）</summary>
        string? Description { get; }

        /// <summary>能否绑定工程变量（决定"ƒx"按钮显不显示）</summary>
        bool IsBindable { get; }

        /// <summary>数值下界（仅 Number 型有意义；越界只标红不夹值）</summary>
        double Min { get; }

        /// <summary>数值上界（仅 Number 型有意义）</summary>
        double Max { get; }

        /// <summary>下拉候选（仅 Choice 型有意义）</summary>
        IReadOnlyList<string> Choices { get; }
    }
}
