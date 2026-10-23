using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 文字图标附加属性：给任意 <see cref="TextBlock"/> 写一个图标名字，就把它变成图标。
    ///
    /// <code>
    /// &lt;TextBlock scada:ScadaIconHelper.Glyph="Gear" FontSize="14" /&gt;
    /// </code>
    ///
    /// 它做两件事：把 <see cref="TextBlock.Text"/> 换成字形、把 <see cref="TextBlock.FontFamily"/>
    /// 换成 <c>ScadaIconFont</c>。名字表见 <see cref="ScadaIconGlyphs"/>；
    /// 也接受原始码点（<c>"f013"</c> / <c>"&amp;#xf013;"</c>）。
    ///
    /// 字体解析顺序：元素自身资源 → Application 资源 → 本程序集内置的 pack URI。
    /// 最后那条兜底保证断言宿主（没有 Application）里也能出图标。
    /// </summary>
    public static class ScadaIconHelper
    {
        /// <summary>
        /// 本程序集内置的 Solid 字体（与 Themes/ScadaIcons.xaml 的 ScadaIconFont 同一个文件）。
        ///
        /// 用「基 URI + 相对文件 + #家族名」两参构造，而不是单字符串 pack URI：
        /// 实测单字符串形式在无 Application 的断言宿主里 TryGetGlyphTypeface 解析失败（字体在、
        /// 但家族名查不到）；两参形式在纯宿主里也能拿到字形。XAML 的 ScadaIconFont 键保留给
        /// 有 Application 的界面用，代码路径（附加属性）一律走这一条。
        /// </summary>
        public static readonly FontFamily FallbackIconFont = new(
            new Uri("pack://application:,,,/VM.Scada.Controls;component/Assets/", UriKind.Absolute),
            "./FontAwesomeSolid.otf#Font Awesome 6 Pro Solid");

        public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
            "Glyph",
            typeof(string),
            typeof(ScadaIconHelper),
            new PropertyMetadata(null, OnGlyphChanged));

        public static string? GetGlyph(DependencyObject obj)
            => (string?)obj.GetValue(GlyphProperty);

        public static void SetGlyph(DependencyObject obj, string? value)
            => obj.SetValue(GlyphProperty, value);

        /// <summary>按“元素资源 → 应用资源 → 内置字体”的顺序取图标字体</summary>
        public static FontFamily ResolveIconFont(FrameworkElement element)
        {
            if (element.TryFindResource("ScadaIconFont") is FontFamily local)
                return local;

            if (Application.Current?.TryFindResource("ScadaIconFont") is FontFamily app)
                return app;

            return FallbackIconFont;
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock textBlock)
                return;

            if (e.NewValue is not string text || string.IsNullOrWhiteSpace(text))
            {
                textBlock.Text = string.Empty;
                return;
            }

            textBlock.FontFamily = ResolveIconFont(textBlock);
            textBlock.Text = ScadaIconGlyphs.Resolve(text);
        }
    }
}
