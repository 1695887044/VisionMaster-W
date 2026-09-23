using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// Choice 型属性下拉框里的一项：<b>落盘值</b>配一个<b>给人看的名字</b>。
    ///
    /// 为什么要有这么一对，而不是像原来那样"候选是什么就显示什么"：描述符里的候选是
    /// <b>落盘值</b>（<c>"Circle"</c>、<c>"Output"</c>），它们属于文件格式，不能为了好看改成中文
    /// （改了旧 .vms 就读不出来）；而面板上该显示的是给人看的词（<c>圆形</c>、<c>输出</c>）。
    /// 两者必须分开，否则"改显示文案"和"改文件格式"就被绑成了同一件事。
    ///
    /// 与 <see cref="ScadaActionTypeExtensions.DisplayName"/> 那一套同源：<b>值 → 名字</b>的对应关系
    /// 放领域层，谁要用谁取（属性面板的下拉、将来的操作日志、导入导出报告），不各写一份。
    ///
    /// 用法与 <c>ScadaActionTypeOption</c> 完全同构：ComboBox 用 <c>DisplayMemberPath</c> +
    /// <c>SelectedValuePath</c> 就能直接绑，不必为"落盘值显示成中文"再引进一个值转换器。
    /// </summary>
    public sealed class ScadaChoiceOption
    {
        public ScadaChoiceOption(string value, string displayName)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            DisplayName = displayName ?? value;
        }

        /// <summary>写回模型、落进 .vms 的值（= 描述符候选里的那一份字面量，一个字符都不改）</summary>
        public string Value { get; }

        /// <summary>下拉里显示的名字</summary>
        public string DisplayName { get; }
    }

    /// <summary>
    /// Choice 候选值的<b>全局词汇表</b>：落盘值 → 面板上显示的中文名。
    ///
    /// 为什么是"全局一张表"而不是"每个属性各带一份译名"
    /// ---------
    /// 同一个值在不同属性上必须是同一个词。<c>"Off"</c> 在指示灯、泵、电机上都是"熄灭/停止"那一类意思，
    /// 各属性各译一份，早晚出现"这台设备上叫『关』、那台上叫『熄灭』"。一张表还有第二个好处：
    /// 将来做<b>中英切换</b>就是换这张表（或按语言取不同的一张），描述符与 XAML 一行都不用动
    /// ——这正是本表存在的最大理由。
    ///
    /// 代价（已知并接受）：描述符不再是"面板显示什么"的唯一真相，译名在这张表里。
    /// 多语言本来就是独立于"图元有哪些属性"的另一件事，硬塞进描述符只会让描述符长出一堆
    /// <c>DisplayName</c> 字段，且每个图元都要重抄一遍同样的词。
    ///
    /// <b>表里没有的值原样返回</b>，这不是兜底而是刻意的，有三类值正靠它通过：
    /// ① 候选本来就是中文的（位按钮的模式：置位/复位/取反/按下ON/按下OFF）；
    /// ② 运行期算出来的文案（权限行的"不限制"、坏值"未知角色(9)"）；
    /// ③ 将来新加的候选还没配译名——显示成英文原值，难看但看得懂，绝不会是一片空白。
    ///
    /// 同一张表里出现的"一个值对多个属性"：<c>Center</c> 同时供水平对齐与垂直对齐用
    /// （两个属性上都读作"居中"）；<c>Stopped/Running/Fault</c> 同时供泵、电机、管路的流动状态用。
    /// 行标签已经说清了是哪一维/哪台设备，表里再拆成两份只会得到两条一模一样的译名。
    /// </summary>
    public static class ScadaChoiceNames
    {
        /// <summary>
        /// 落盘值 → 中文名。<b>键必须与描述符里的候选字面量逐字符一致</b>
        /// （大小写敏感，用 <see cref="StringComparer.Ordinal"/>）——写错一个字母不会报错，
        /// 只会安静地显示成英文原值，所以改这里时务必与 <c>BuiltInElements</c> 的候选对一遍。
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> Table =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ---- 字重（所有带文字的图元共用同一组候选） ----
                ["Normal"] = "常规",
                ["SemiBold"] = "半粗",
                ["Bold"] = "粗体",

                // ---- 对齐：水平（Left/Center/Right/Justify）与垂直（Top/Center/Bottom）共用 ----
                ["Left"] = "左对齐",
                ["Center"] = "居中",
                ["Right"] = "右对齐",
                ["Justify"] = "两端对齐",
                ["Top"] = "顶部",
                ["Bottom"] = "底部",

                // ---- 形状（指示灯、图形开关） ----
                ["Circle"] = "圆形",
                ["Square"] = "正方形",

                // ---- 朝向（分隔线、进度条） ----
                ["Horizontal"] = "水平",
                ["Vertical"] = "垂直",

                // ---- IO 域：模式 ----
                ["Output"] = "输出",
                ["Input"] = "输入",
                ["InputOutput"] = "输入输出",

                // ---- IO 域：格式类型 ----
                ["Decimal"] = "十进制",
                ["Hex"] = "十六进制",
                ["Binary"] = "二进制",

                // ---- 状态灯：熄灭 / 点亮 / 警告 / 报警 ----
                ["Off"] = "熄灭",
                ["On"] = "点亮",
                ["Warning"] = "警告",
                ["Alarm"] = "报警",

                // ---- 设备状态（泵、电机）与流动状态（管路） ----
                ["Stopped"] = "停止",
                ["Running"] = "运行",
                ["Fault"] = "故障",

                // ---- 管路流向 ----
                ["LeftToRight"] = "从左到右",
                ["RightToLeft"] = "从右到左",
                ["None"] = "不显示",
            };

        /// <summary>
        /// 落盘值 → 中文显示名。表里没有的值<b>原样返回</b>（理由见类注释），
        /// 因此调用方永远拿到一个可以往界面上放的串，不必自己判空。
        /// </summary>
        public static string DisplayName(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return Table.TryGetValue(value, out string? name) ? name : value;
        }

        /// <summary>
        /// 把一串落盘值配成下拉候选（值 + 显示名）。
        ///
        /// 顺序<b>原样保留</b>：候选顺序是描述符定的（如状态灯按 熄灭→点亮→警告→报警 由轻到重），
        /// 这里只是加一列名字，不该顺手重排。
        /// </summary>
        public static IReadOnlyList<ScadaChoiceOption> ToOptions(IReadOnlyList<string> values)
        {
            if (values.Count == 0) return Array.Empty<ScadaChoiceOption>();

            var options = new ScadaChoiceOption[values.Count];
            for (int i = 0; i < values.Count; i++)
                options[i] = new ScadaChoiceOption(values[i], DisplayName(values[i]));

            return options;
        }
    }
}
