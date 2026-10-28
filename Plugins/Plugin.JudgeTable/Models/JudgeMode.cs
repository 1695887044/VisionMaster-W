using System.ComponentModel.DataAnnotations;

namespace Plugin.JudgeTable.Models
{
    /// <summary>
    /// 单条规则的判定模式。
    ///
    /// 【值仍是英文成员名，显示名走 [Display(Name)]】
    /// 流程与 .vms 存的是成员名（枚举写回按名字解析），界面显示中文一律经
    /// <see cref="Core.Interfaces.StepConfigOption.ForEnum"/> 读这里的 Name —— 改中文显示名
    /// 不会打断已配好的流程（R40 同一口径）。
    /// </summary>
    public enum JudgeMode
    {
        /// <summary>数值：支持 a / a ± b / a ~ b / ≥ a / ≤ a</summary>
        [Display(Name = "数值")]
        Numeric = 0,

        /// <summary>文本：精确匹配，可开忽略大小写 / 忽略空白 / 允许编辑距离 / 易混字符映射</summary>
        [Display(Name = "文本")]
        Text = 1,

        /// <summary>集合：逗号分隔的多项比较（全部期望都在 / 完全一致 / 不许多余）</summary>
        [Display(Name = "集合")]
        Set = 2,

        /// <summary>正则：期望值是一条 .NET 正则</summary>
        [Display(Name = "正则")]
        Regex = 3,

        /// <summary>布尔：期望 true / false（大小写不敏感）</summary>
        [Display(Name = "布尔")]
        Boolean = 4,

        /// <summary>计数：实际值取"元素个数"，期望按 min ~ max 解析</summary>
        [Display(Name = "计数")]
        Count = 5,

        /// <summary>非空：非空白字符串 / 非空集合 / 非 null</summary>
        [Display(Name = "非空")]
        NotEmpty = 6,
    }

    /// <summary>
    /// 集合模式的语义（三项互斥）。
    /// </summary>
    public enum SetSemantics
    {
        /// <summary>期望里的每一项都必须出现在实际值里（实际可以更多）</summary>
        [Display(Name = "全部期望都在")]
        AllExpected = 0,

        /// <summary>实际值与期望集合完全一致（既不许缺也不许多）</summary>
        [Display(Name = "完全一致")]
        Exact = 1,

        /// <summary>实际值里不许出现期望之外的元素（期望可以有多余项）</summary>
        [Display(Name = "不许多余")]
        NoExtra = 2,
    }
}
