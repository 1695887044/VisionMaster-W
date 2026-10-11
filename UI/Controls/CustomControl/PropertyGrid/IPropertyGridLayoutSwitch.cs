namespace UI.CustomControl
{
    /// <summary>
    /// 参数面板布局可切（**表格 ⇄ 卡片**）的**可选契约**。
    ///
    /// 插件配置视图实现它，插件配置壳才会亮出"卡片式"开关；不实现就只有表格式一种布局 ——
    /// 壳不去猜内容类型（与 AutoPortConfigView 的"窗口尺寸由视图自己负责"是同一条原则）。
    ///
    /// 为什么放在 UI 库：它是"视图 ⇄ 壳"之间的界面约定，宿主与插件视图都引用本库；
    /// 放进 Core.Interfaces 会把一个纯界面约定混进插件数据契约里。
    ///
    /// 实现方（视图）自己负责切布局的**全部**后果 —— 至少包括：
    ///   · 把参数网格换成 <see cref="CardPropertyGrid"/>；
    ///   · 卡片式自带左侧分组页签条（约 128px）与卡片留白（约 68px），
    ///     固定开销远大于表格式，所以**参数列要给够宽度**（见 PreProcessingView 的 540）。
    /// </summary>
    public interface IPropertyGridLayoutSwitch
    {
        /// <summary>当前是否为卡片式布局；插件配置壳切换时会写这个属性</summary>
        bool UseCardLayout { get; set; }
    }
}
