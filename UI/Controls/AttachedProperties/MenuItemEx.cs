using System.Windows;

namespace UI.AttachedProperties
{
    /// <summary>步骤右键菜单的图标语气：决定图标底片与文字配色。</summary>
    public enum MenuItemTone
    {
        /// <summary>中性（默认灰底）。</summary>
        Default,

        /// <summary>结构/分支类动作（靛蓝）。</summary>
        Indigo,

        /// <summary>编辑类动作（石板灰）。</summary>
        Slate,

        /// <summary>运行态动作：断点 / 禁用（琥珀）。</summary>
        Amber,

        /// <summary>保存 / 成功类动作（绿）。</summary>
        Green,

        /// <summary>视图类动作（浅灰）。</summary>
        Gray,

        /// <summary>破坏性动作（红）。</summary>
        Danger
    }

    /// <summary>
    /// 步骤右键菜单的展示信息：副标题说明、图标语气、菜单页脚。
    /// 只承载"显示"，命令语义仍由 MenuItem.Command / CommandParameter 承担——
    /// 不在这里塞任何业务判断，避免出现第二份"某项能不能点"的口径。
    /// </summary>
    public static class MenuItemEx
    {
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.RegisterAttached(
                "Description",
                typeof(string),
                typeof(MenuItemEx),
                new PropertyMetadata(null));

        public static string? GetDescription(DependencyObject obj) =>
            (string?)obj.GetValue(DescriptionProperty);

        public static void SetDescription(DependencyObject obj, string? value) =>
            obj.SetValue(DescriptionProperty, value);

        public static readonly DependencyProperty ToneProperty =
            DependencyProperty.RegisterAttached(
                "Tone",
                typeof(MenuItemTone),
                typeof(MenuItemEx),
                new PropertyMetadata(MenuItemTone.Default));

        public static MenuItemTone GetTone(DependencyObject obj) =>
            (MenuItemTone)obj.GetValue(ToneProperty);

        public static void SetTone(DependencyObject obj, MenuItemTone value) =>
            obj.SetValue(ToneProperty, value);

        public static readonly DependencyProperty FooterProperty =
            DependencyProperty.RegisterAttached(
                "Footer",
                typeof(string),
                typeof(MenuItemEx),
                new PropertyMetadata(null));

        public static string? GetFooter(DependencyObject obj) =>
            (string?)obj.GetValue(FooterProperty);

        public static void SetFooter(DependencyObject obj, string? value) =>
            obj.SetValue(FooterProperty, value);

        public static readonly DependencyProperty FlyoutTitleProperty =
            DependencyProperty.RegisterAttached(
                "FlyoutTitle",
                typeof(string),
                typeof(MenuItemEx),
                new PropertyMetadata(null));

        public static string? GetFlyoutTitle(DependencyObject obj) =>
            (string?)obj.GetValue(FlyoutTitleProperty);

        public static void SetFlyoutTitle(DependencyObject obj, string? value) =>
            obj.SetValue(FlyoutTitleProperty, value);

        public static readonly DependencyProperty FlyoutDescriptionProperty =
            DependencyProperty.RegisterAttached(
                "FlyoutDescription",
                typeof(string),
                typeof(MenuItemEx),
                new PropertyMetadata(null));

        public static string? GetFlyoutDescription(DependencyObject obj) =>
            (string?)obj.GetValue(FlyoutDescriptionProperty);

        public static void SetFlyoutDescription(DependencyObject obj, string? value) =>
            obj.SetValue(FlyoutDescriptionProperty, value);

        public static readonly DependencyProperty FlyoutIconProperty =
            DependencyProperty.RegisterAttached(
                "FlyoutIcon",
                typeof(string),
                typeof(MenuItemEx),
                new PropertyMetadata(null));

        public static string? GetFlyoutIcon(DependencyObject obj) =>
            (string?)obj.GetValue(FlyoutIconProperty);

        public static void SetFlyoutIcon(DependencyObject obj, string? value) =>
            obj.SetValue(FlyoutIconProperty, value);
    }
}
