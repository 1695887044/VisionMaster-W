namespace VisionMaster.Scada
{
    /// <summary>
    /// 图元动画的类型（"一个变量的值怎么改变这个图元的样子"的取值集合）。
    ///
    /// 与 <see cref="ScadaActionType"/> 同样的落盘纪律：本枚举的<b>数值</b>会被写进 .vms，
    /// 一旦发布就不许改、不许插入中间值，只许在末尾追加。删掉某种动画也不许回收它的数值——
    /// 否则老工程里那条动画会被读成另一种动画，静默地把图元挪到别处（组态现场最怕这种）。
    ///
    /// 手册（InoTouchPad 帮助 7.5.1）一共列了 10 种动画，本版<b>只做其中 4 种</b>：
    /// 外观变化 / 水平移动 / 垂直移动 / 可见性。落选的那几种各有原因，记在这里免得后人重复论证：
    /// <list type="bullet">
    /// <item><b>启用对象</b>——唯一有副作用的动画（改 <c>IsEnabled</c>，会连带影响事件发射），
    /// 与其余三种"变量值 → 一个视觉结果"的纯电平映射不同类，单独排期；</item>
    /// <item><b>对角线移动 / 比例移动 / 自定义移动 / 动态移动 / 直接移动</b>——多轴耦合或需要
    /// 目标位置表，属于"移动"这个大类的进阶档，等水平/垂直移动在现场跑稳了再往上叠。</item>
    /// </list>
    ///
    /// 刻意<b>没有</b> <c>None</c>：一条动画记录必然表达一件要做的事，"没有动画"由
    /// <see cref="ScadaElement.Animations"/> 集合为空来表达，不该占一个枚举值。
    /// 副作用是"新建一条动画"的默认值是 <see cref="Appearance"/>——这正是想要的：属性面板上
    /// 点"添加动画"，先长出一条"外观变化"，用户改档位就能用，不必先做一次无意义的类型选择。
    /// </summary>
    public enum ScadaAnimationType
    {
        /// <summary>外观变化：变量值命中哪一档，就用那一档的前景色/背景色/闪烁（手册 7.5.1.1）</summary>
        Appearance = 1,

        /// <summary>水平移动：变量在范围内线性改变图元的 X（手册 7.5.1.4）</summary>
        HorizontalMove = 2,

        /// <summary>垂直移动：变量在范围内线性改变图元的 Y（手册 7.5.1.5）</summary>
        VerticalMove = 3,

        /// <summary>可见性：变量值命中范围时显示或隐藏（手册 7.5.1.10）</summary>
        Visibility = 4,
    }

    /// <summary>
    /// 动画类型的展示名。
    ///
    /// 为什么展示名放在领域层而不是属性面板里写死：动画类型可扩展（启用对象、比例移动…），
    /// 而"这一行的下拉框该显示什么"和"运行日志里该怎么描述这条动画"是同一份知识。
    /// 分两处写，就会出现"面板里叫『水平移动』、日志里叫『HorizontalMove』"。
    /// 与 <see cref="ScadaActionTypeExtensions"/> 同一口径。
    /// </summary>
    public static class ScadaAnimationTypeExtensions
    {
        /// <summary>给人看的动画名（属性面板下拉、诊断文案共用）</summary>
        public static string DisplayName(this ScadaAnimationType type) => type switch
        {
            ScadaAnimationType.Appearance => "外观变化",
            ScadaAnimationType.HorizontalMove => "水平移动",
            ScadaAnimationType.VerticalMove => "垂直移动",
            ScadaAnimationType.Visibility => "可见性",
            // 高版本软件存下的动画类型读进低版本会落到这里（Newtonsoft 按数值反序列化，不校验取值范围）。
            // 报出数值而不是抛异常：坏在一条动画上，不该让整张画面打不开。
            _ => $"未知动画({(int)type})",
        };
    }
}
