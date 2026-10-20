using System.Windows;
using System.Windows.Controls;

namespace UI.CustomControl
{
    /// <summary>
    /// 属性网格的附加属性：把「该分组有几条属性」透给卡片式左栏导航项的模板。
    ///
    /// 【为什么用附加属性而不是 Tag】<c>TabItem.Tag</c> 已经被
    /// <see cref="UI.CustomControl.PropertyGrid.PropertyGridBase"/> 用来存放
    /// <c>IGrouping&lt;string, PropertyInfo&gt;</c>（懒构建靠它取组），
    /// 再塞一个计数就得改契约；附加属性零侵入，模板用 <c>TemplatedParent</c> 绑即可。
    ///
    /// 【为什么不直接绑 Tag.Count】WPF 绑定不支持对 <c>IGrouping</c> 调 <c>Count()</c> 扩展方法，
    /// 且分组是 LINQ 惰性序列，模板里没有 <c>Count</c> 属性可绑。计数在构建页签时算一次最省。
    /// </summary>
    public static class PropertyGridAttach
    {
        /// <summary>分组内的属性条数。0 = 不显示计数（模板里由 DataTrigger 收起）。</summary>
        public static readonly DependencyProperty GroupCountProperty =
            DependencyProperty.RegisterAttached(
                "GroupCount",
                typeof(int),
                typeof(PropertyGridAttach),
                new FrameworkPropertyMetadata(0));

        public static void SetGroupCount(DependencyObject element, int value) =>
            element.SetValue(GroupCountProperty, value);

        public static int GetGroupCount(DependencyObject element) =>
            (int)element.GetValue(GroupCountProperty);
    }
}
