using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 给 <see cref="TextBlock"/> 加"命中搜索词的那一段换色"的附加属性。
    ///
    /// 为什么源文本走附加属性 <c>HighlightTextBlock.Text</c>，而不是直接用内置的 <c>TextBlock.Text</c>：
    /// 高亮的实现只能是把文本拆成若干 <see cref="Run"/> 塞进 <c>TextBlock.Inlines</c>，
    /// 而 <c>Inlines</c> 与内置 <c>Text</c> 是同一个存储的两副面孔——一旦往里写 Run，
    /// 内置 <c>Text</c> 上的绑定就被顶掉（下一次源属性变化会把 Inlines 整片清空，
    /// 表现为"搜索一次以后名字就再也不变了"）。所以源文本也由本类接住：
    /// XAML 里写 <c>local:HighlightTextBlock.Text="{Binding Name}"</c>，
    /// 内置 <c>Text</c> 不绑任何东西。**别把它改回 Text 绑定**。
    ///
    /// 匹配口径：<see cref="StringComparison.OrdinalIgnoreCase"/>（与 ToolViewModel.ApplyFilter
    /// 的过滤口径一致——两边不一致会出现"名字里明明有、却没高亮"的错觉）。
    /// 空搜索词 = 原样一段纯文本，不进高亮分支。
    /// </summary>
    public static class HighlightTextBlock
    {
        /// <summary>要显示的源文本（替代内置 Text 绑定，原因见类型注释）</summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(HighlightTextBlock), new PropertyMetadata(null, OnInputChanged));

        /// <summary>搜索词；为空则整段原样显示</summary>
        public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
            "Highlight", typeof(string), typeof(HighlightTextBlock), new PropertyMetadata(null, OnInputChanged));

        /// <summary>命中片段的底色（不设则只换前景色）</summary>
        public static readonly DependencyProperty HighlightBackgroundProperty = DependencyProperty.RegisterAttached(
            "HighlightBackground", typeof(Brush), typeof(HighlightTextBlock), new PropertyMetadata(null, OnInputChanged));

        /// <summary>命中片段的前景色（不设则沿用 TextBlock 自己的前景色）</summary>
        public static readonly DependencyProperty HighlightForegroundProperty = DependencyProperty.RegisterAttached(
            "HighlightForeground", typeof(Brush), typeof(HighlightTextBlock), new PropertyMetadata(null, OnInputChanged));

        public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
        public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

        public static string? GetHighlight(DependencyObject d) => (string?)d.GetValue(HighlightProperty);
        public static void SetHighlight(DependencyObject d, string? value) => d.SetValue(HighlightProperty, value);

        public static Brush? GetHighlightBackground(DependencyObject d) => (Brush?)d.GetValue(HighlightBackgroundProperty);
        public static void SetHighlightBackground(DependencyObject d, Brush? value) => d.SetValue(HighlightBackgroundProperty, value);

        public static Brush? GetHighlightForeground(DependencyObject d) => (Brush?)d.GetValue(HighlightForegroundProperty);
        public static void SetHighlightForeground(DependencyObject d, Brush? value) => d.SetValue(HighlightForegroundProperty, value);

        private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock) Rebuild(textBlock);
        }

        private static void Rebuild(TextBlock textBlock)
        {
            var text = GetText(textBlock) ?? string.Empty;
            var term = GetHighlight(textBlock);

            textBlock.Inlines.Clear();

            if (string.IsNullOrEmpty(text))
            {
                // 空文本也要留一个空 Run：不留的话 TextBlock 会退回内置 Text（空串），
                // 结果一样，但留一个 Run 能让"已经接管过"这件事在调试时看得出来。
                textBlock.Inlines.Add(new Run(string.Empty));
                return;
            }

            if (string.IsNullOrEmpty(term))
            {
                textBlock.Inlines.Add(new Run(text));
                return;
            }

            var background = GetHighlightBackground(textBlock);
            var foreground = GetHighlightForeground(textBlock);

            int cursor = 0;
            while (cursor < text.Length)
            {
                int hit = text.IndexOf(term, cursor, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(cursor)));
                    break;
                }

                if (hit > cursor) textBlock.Inlines.Add(new Run(text.Substring(cursor, hit - cursor)));

                var run = new Run(text.Substring(hit, term.Length));
                if (background != null) run.Background = background;
                if (foreground != null) run.Foreground = foreground;
                textBlock.Inlines.Add(run);

                cursor = hit + term.Length;
            }
        }
    }
}
