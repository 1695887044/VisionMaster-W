using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionMaster.Services.Help
{
    /// <summary>
    /// 极简 Markdown → FlowDocument 渲染器：只覆盖手册用得到的子集
    /// （标题 / 段落 / 无序·有序列表（可嵌套）/ 表格 / 代码块 / 引用 / 分隔线 /
    ///  粗体·斜体·行内代码 / 链接 / 图片 / 反斜杠转义）。
    ///
    /// 为什么自研而不是引第三方
    /// ---------
    /// ① 本仓 NuGet 还原受代理限制（见 VisionMaster.csproj 里 Watson 那段注释），
    ///    Markdig / WebView2 这类新包拿不到，"引一个包"实际成本远高于写这几百行；
    /// ② 渲染目标不是 HTML 而是 FlowDocument：WPF 打印与分页直接白送
    ///    （"打印"按钮就是 <c>PrintDialog</c> 打这份文档），内嵌浏览器方案还多一层 airspace 问题；
    /// ③ 手册内容是自己写的 md，不需要 CommonMark 全兼容——按需支持即可，
    ///    遇到不认识的语法当普通段落显示，绝不会渲染成空白。
    ///
    /// 不支持的语法（刻意）：脚注、HTML 块、引用式链接、任务列表。出现时按普通文字渲染。
    /// </summary>
    public static class MarkdownToFlowDocument
    {
        // ==================================================================
        //  配色：与 UI\Controls\Themes\Colors.xaml 的 Dialog* 令牌逐条对齐
        //
        //  为什么不直接 {DynamicResource} 取令牌：FlowDocument 是代码里 new 出来的对象树，
        //  不在 XAML 名称作用域里（拿不到应用级字典）。这里的口径是"值对齐、键名写死"，
        //  换皮肤时这一组与 Colors.xaml 一起改（每支都在注释里写了同名令牌，照着对）。
        // ==================================================================

        /// <summary>正文前景 = DialogTextPrimaryBrush #0F172A</summary>
        private static readonly SolidColorBrush TextBrush = MakeBrush(0x0F, 0x17, 0x2A);

        /// <summary>次要文字 = DialogTextSecondaryBrush #475569（三~六级标题、提示块正文）</summary>
        private static readonly SolidColorBrush SecondaryBrush = MakeBrush(0x47, 0x55, 0x69);

        /// <summary>弱文字 = DialogTextMutedBrush #94A3B8（图片降级、页脚说明）</summary>
        private static readonly SolidColorBrush MutedBrush = MakeBrush(0x94, 0xA3, 0xB8);

        /// <summary>主色 = DialogAccentBrush #0EA5E9（链接、提示块竖条）</summary>
        private static readonly SolidColorBrush AccentBrush = MakeBrush(0x0E, 0xA5, 0xE9);

        /// <summary>主色压深 = DialogAccentTextBrush #0369A1（浅底上的链接文字，比主色可读）</summary>
        private static readonly SolidColorBrush AccentTextBrush = MakeBrush(0x03, 0x69, 0xA1);

        /// <summary>描边 = DialogBorderBrush #E2E8F0</summary>
        private static readonly SolidColorBrush BorderBrush = MakeBrush(0xE2, 0xE8, 0xF0);

        /// <summary>代码块底 = DialogInputBackgroundBrush #F8FAFC</summary>
        private static readonly SolidColorBrush CodeBackgroundBrush = MakeBrush(0xF8, 0xFA, 0xFC);

        /// <summary>表头底 = DialogRowHoverBrush #F1F5F9</summary>
        private static readonly SolidColorBrush HeaderBackgroundBrush = MakeBrush(0xF1, 0xF5, 0xF9);

        /// <summary>表格斑马纹 = DialogRowAlternateBrush #F8FAFC（奇数数据行淡底，长表横向读不串行）</summary>
        private static readonly SolidColorBrush ZebraBrush = MakeBrush(0xF8, 0xFA, 0xFC);

        /// <summary>提示块底 = DialogAccentSoftBrush #F0F9FF（主色淡到几乎发白，靠 4px 竖条定性）</summary>
        private static readonly SolidColorBrush QuoteBackgroundBrush = MakeBrush(0xF0, 0xF9, 0xFF);

        /// <summary>行内代码底 = DialogRowHoverBrush #F1F5F9（比代码块深一档，正文里才认得出是"一整枚胶囊"）</summary>
        private static readonly SolidColorBrush CodeSpanBackgroundBrush = MakeBrush(0xF1, 0xF5, 0xF9);

        private static readonly FontFamily BodyFont =
            new("Microsoft YaHei UI, Microsoft YaHei, Segoe UI, Arial");

        private static readonly FontFamily CodeFont =
            new("Consolas, Cascadia Mono, Courier New");

        /// <summary>
        /// 标题字号倍率（索引 = 级别 1~6）。
        /// 按网页文档的落差来：一级明显压住全页（1.875×），二级只比正文高一档（1.25×）。
        /// 旧的 1.70/1.42/1.24 是"逐级平滑递减"，结果是标题不像标题、正文抬头就是一片黑。
        /// </summary>
        private static readonly double[] HeadingScale = { 0, 1.875, 1.25, 1.125, 1.05, 1.0, 0.95 };

        /// <summary>正文定宽（网页长文的阅读宽度）：一行汉字排到 30 多个，眼睛回头不丢行</summary>
        public const double TextColumnWidth = 780;

        /// <summary>正文左右留白</summary>
        public const double HorizontalPadding = 28;

        /// <summary>
        /// 正文容器（<c>FlowDocumentScrollViewer</c>）的最大宽度 = 定宽 + 两侧留白。
        /// 视图侧用 <c>{x:Static}</c> 绑这一个值，别在 XAML 里另写一个数——
        /// 改了这里忘了改那边，就会出现"留白对了、正文宽度没跟着变"。
        /// </summary>
        public const double ViewerMaxWidth = TextColumnWidth + HorizontalPadding * 2;

        /// <summary>
        /// 渲染一页 Markdown。每次都返回新的 FlowDocument（打印时会另渲染一份，
        /// 因为打印要重设页面尺寸，共用屏幕那份会把版面弄乱）。
        /// </summary>
        /// <param name="markdown">正文</param>
        /// <param name="imageResolver">相对路径 → 位图；返回 null 表示取不到（降级成文字）</param>
        /// <param name="bodyFontSize">正文字号（"选项 → 字号"改的就是它）</param>
        public static FlowDocument Render(
            string? markdown,
            Func<string, BitmapSource?>? imageResolver = null,
            double bodyFontSize = 14)
        {
            if (bodyFontSize < 9) bodyFontSize = 9;
            if (bodyFontSize > 30) bodyFontSize = 30;

            var doc = new FlowDocument
            {
                FontFamily = BodyFont,
                FontSize = bodyFontSize,
                // 网页长文的行距（leading 1.8）：中文密、没有词间空格，行距一窄就糊成一片
                LineHeight = Math.Round(bodyFontSize * 1.8),
                Foreground = TextBrush,
                PagePadding = new Thickness(HorizontalPadding, 26, HorizontalPadding, 44),
                // 帮助窗是"单栏长文"：不限列宽会按窗口宽度分成两栏，读起来像报纸。
                // 正文定宽不靠这里——由视图把容器限到 ViewerMaxWidth 并水平居中（宽度只有一处来源）
                ColumnWidth = double.PositiveInfinity,
            };

            var lines = Normalize(markdown).Split('\n');
            var i = 0;
            while (i < lines.Length)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    i++;
                    continue;
                }

                // ---- 代码块 ----
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    i++;
                    var code = new List<string>();
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        code.Add(lines[i]);
                        i++;
                    }
                    if (i < lines.Length) i++;   // 收尾的 ```
                    doc.Blocks.Add(BuildCodeBlock(code, bodyFontSize));
                    continue;
                }

                // ---- 分隔线 ----
                if (IsHorizontalRule(trimmed))
                {
                    doc.Blocks.Add(BuildRule());
                    i++;
                    continue;
                }

                // ---- 标题 ----
                if (TryParseHeading(trimmed, out var level, out var headingText))
                {
                    doc.Blocks.Add(BuildHeading(headingText, level, bodyFontSize, imageResolver));
                    i++;
                    continue;
                }

                // ---- 表格 ----
                if (trimmed.StartsWith("|", StringComparison.Ordinal)
                    && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
                {
                    var rows = new List<string[]> { SplitTableRow(trimmed) };
                    i += 2;
                    while (i < lines.Length && lines[i].Trim().StartsWith("|", StringComparison.Ordinal))
                    {
                        var rowText = lines[i].Trim();
                        if (!IsTableSeparator(rowText))
                            rows.Add(SplitTableRow(rowText));
                        i++;
                    }
                    doc.Blocks.Add(BuildTable(rows, bodyFontSize, imageResolver));
                    continue;
                }

                // ---- 引用 ----
                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    var quoted = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        var t = lines[i].TrimStart();
                        quoted.Add(t.Length > 1 ? t.Substring(1).TrimStart() : string.Empty);
                        i++;
                    }
                    doc.Blocks.Add(BuildQuote(quoted, bodyFontSize, imageResolver));
                    continue;
                }

                // ---- 列表 ----
                if (TryParseListItem(line, out _, out _, out _))
                {
                    var items = new List<(int Indent, bool Ordered, string Text)>();
                    while (i < lines.Length)
                    {
                        if (TryParseListItem(lines[i], out var ordered, out var indent, out var content))
                        {
                            items.Add((indent, ordered, content));
                            i++;
                            continue;
                        }

                        // 列表项的续行：有缩进、非空、且不是新块的开头
                        var raw = lines[i];
                        var nextTrimmed = raw.Trim();
                        if (nextTrimmed.Length > 0
                            && items.Count > 0
                            && LeadingSpaces(raw) >= 2
                            && !IsBlockStart(nextTrimmed))
                        {
                            var last = items[^1];
                            items[^1] = (last.Indent, last.Ordered, SmartJoin(last.Text, nextTrimmed));
                            i++;
                            continue;
                        }
                        break;
                    }
                    doc.Blocks.Add(BuildList(items, bodyFontSize, imageResolver));
                    continue;
                }

                // ---- 段落（默认分支）----
                {
                    var buffer = new List<string> { trimmed };
                    i++;
                    while (i < lines.Length)
                    {
                        var t = lines[i].Trim();
                        if (t.Length == 0 || IsBlockStart(t))
                            break;
                        buffer.Add(t);
                        i++;
                    }
                    doc.Blocks.Add(BuildParagraph(buffer, bodyFontSize, imageResolver));
                }
            }

            return doc;
        }

        // ==================================================================
        //  块级构件
        // ==================================================================

        private static Paragraph BuildHeading(
            string text, int level, double fontSize, Func<string, BitmapSource?>? imageResolver)
        {
            var scale = HeadingScale[Math.Clamp(level, 1, 6)];
            // 上间距比段间距大一档、下间距压小：FlowDocument 的相邻边距**不合并**（CSS 会合并），
            // 照抄网页的"上下等距"在这里会松一倍，读起来像散页
            var top = level switch { 1 => 4.0, 2 => 26.0, 3 => 22.0, _ => 18.0 };
            var paragraph = new Paragraph
            {
                FontSize = Math.Round(fontSize * scale),
                FontWeight = level <= 2 ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = level <= 2 ? TextBrush : SecondaryBrush,
                Margin = new Thickness(0, top, 0, level <= 2 ? 12 : 8),
            };
            if (level == 1)
            {
                // 一级标题压一条底线：正文很长时，"这一页从哪开始"要一眼看得出来
                paragraph.BorderBrush = BorderBrush;
                paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                paragraph.Padding = new Thickness(0, 0, 0, 14);
                paragraph.Margin = new Thickness(0, top, 0, 16);
            }
            AppendInlines(paragraph.Inlines, text, imageResolver, fontSize);
            return paragraph;
        }

        private static Paragraph BuildParagraph(
            List<string> lines, double fontSize, Func<string, BitmapSource?>? imageResolver)
        {
            var text = lines.Aggregate(string.Empty, (acc, l) => acc.Length == 0 ? l : SmartJoin(acc, l));
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 14) };

            // 独占一行的图片居中显示（截图默认靠左，长文里看着会歪）
            var isImageOnly = text.StartsWith("![", StringComparison.Ordinal)
                              && text.EndsWith(")", StringComparison.Ordinal)
                              && text.IndexOf("](", StringComparison.Ordinal) > 0
                              && text.IndexOf(" ", StringComparison.Ordinal) < 0;
            if (isImageOnly)
                paragraph.TextAlignment = TextAlignment.Center;

            AppendInlines(paragraph.Inlines, text, imageResolver, fontSize);
            return paragraph;
        }

        private static Paragraph BuildCodeBlock(List<string> lines, double fontSize)
        {
            var paragraph = new Paragraph
            {
                FontFamily = CodeFont,
                FontSize = Math.Max(9, fontSize - 1.5),
                Background = CodeBackgroundBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 12, 0, 18),
                LineHeight = double.NaN,
            };
            for (var n = 0; n < lines.Count; n++)
            {
                if (n > 0)
                    paragraph.Inlines.Add(new LineBreak());
                paragraph.Inlines.Add(new Run(lines[n].Replace("\t", "    ")));
            }
            if (lines.Count == 0)
                paragraph.Inlines.Add(new Run(" "));
            return paragraph;
        }

        private static Paragraph BuildQuote(
            List<string> lines, double fontSize, Func<string, BitmapSource?>? imageResolver)
        {
            var text = lines.Aggregate(string.Empty, (acc, l) => acc.Length == 0 ? l : SmartJoin(acc, l));
            // 手册里的 > 块实际用途是"注意/说明"（自动生成页用它声明自己不是插件写的），
            // 所以它是整页最该跳出来的一块：主色竖条定性，底色只淡淡铺一层
            var paragraph = new Paragraph
            {
                Background = QuoteBackgroundBrush,
                BorderBrush = AccentBrush,
                BorderThickness = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(14, 10, 12, 10),
                Margin = new Thickness(0, 14, 0, 18),
                Foreground = SecondaryBrush,
            };
            AppendInlines(paragraph.Inlines, text, imageResolver, fontSize);
            return paragraph;
        }

        private static Paragraph BuildRule()
        {
            return new Paragraph
            {
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 20, 0, 20),
                LineHeight = 1,
                FontSize = 1,
            };
        }

        private static Table BuildTable(
            List<string[]> rows, double fontSize, Func<string, BitmapSource?>? imageResolver)
        {
            var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            var table = new Table
            {
                CellSpacing = 0,
                Margin = new Thickness(0, 12, 0, 18),
                BorderBrush = BorderBrush,
                // 底边由最后一行的单元格下边线出：容器再压一条就是双线
                BorderThickness = new Thickness(1, 1, 1, 0),
            };
            for (var c = 0; c < columnCount; c++)
                table.Columns.Add(new TableColumn());

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            for (var r = 0; r < rows.Count; r++)
            {
                // 斑马纹：表头占第 0 行，数据行从 1 起，隔行铺淡底（长端口表横向扫读不串行）
                var isHeader = r == 0;
                var zebra = !isHeader && r % 2 == 1;
                var row = new TableRow();
                for (var c = 0; c < columnCount; c++)
                {
                    var cellText = c < rows[r].Length ? rows[r][c] : string.Empty;
                    var paragraph = new Paragraph
                    {
                        Margin = new Thickness(0),
                        FontSize = Math.Max(9, fontSize - 1),
                        LineHeight = double.NaN,
                    };
                    if (isHeader)
                        paragraph.FontWeight = FontWeights.SemiBold;
                    AppendInlines(paragraph.Inlines, cellText, imageResolver, fontSize);

                    var cell = new TableCell(paragraph)
                    {
                        Padding = new Thickness(11, 8, 11, 8),
                        BorderBrush = BorderBrush,
                        // 只画横线：端口表的列宽差得很远，竖线会把窄列挤成一列一个字
                        BorderThickness = new Thickness(0, 0, 0, 1),
                    };
                    if (isHeader)
                        cell.Background = HeaderBackgroundBrush;
                    else if (zebra)
                        cell.Background = ZebraBrush;
                    row.Cells.Add(cell);
                }
                group.Rows.Add(row);
            }

            return table;
        }

        private static List BuildList(
            List<(int Indent, bool Ordered, string Text)> flat,
            double fontSize,
            Func<string, BitmapSource?>? imageResolver)
        {
            var tree = BuildItemTree(flat);
            return BuildListLevel(tree, flat.Count > 0 && flat[0].Ordered, fontSize, imageResolver);
        }

        private static List BuildListLevel(
            List<MdListItem> items, bool ordered, double fontSize, Func<string, BitmapSource?>? imageResolver)
        {
            var list = new List
            {
                MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(0, 6, 0, 14),
                Padding = new Thickness(24, 0, 0, 0),
            };

            foreach (var item in items)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 4, 0, 4),
                    LineHeight = double.NaN,
                };
                AppendInlines(paragraph.Inlines, item.Text, imageResolver, fontSize);

                var listItem = new ListItem(paragraph);

                // 子项按"有序/无序"分段：同级里混用两种符号时各起一个子列表（Markdown 允许这样写）
                var run = new List<MdListItem>();
                bool? runOrdered = null;
                foreach (var child in item.Children)
                {
                    if (runOrdered != null && child.Ordered != runOrdered)
                    {
                        listItem.Blocks.Add(BuildListLevel(run, runOrdered.Value, fontSize, imageResolver));
                        run = new List<MdListItem>();
                    }
                    runOrdered = child.Ordered;
                    run.Add(child);
                }
                if (run.Count > 0)
                    listItem.Blocks.Add(BuildListLevel(run, runOrdered!.Value, fontSize, imageResolver));

                list.ListItems.Add(listItem);
            }

            return list;
        }

        /// <summary>扁平列表项 → 树（按缩进判父子；每 2 空格算一级，更深的缩进归到最近的父项）</summary>
        private static List<MdListItem> BuildItemTree(List<(int Indent, bool Ordered, string Text)> flat)
        {
            var roots = new List<MdListItem>();
            var stack = new Stack<(int Indent, MdListItem Node)>();

            foreach (var (indent, ordered, text) in flat)
            {
                var node = new MdListItem { Ordered = ordered, Text = text };
                while (stack.Count > 0 && indent <= stack.Peek().Indent)
                    stack.Pop();

                if (stack.Count == 0)
                    roots.Add(node);
                else
                    stack.Peek().Node.Children.Add(node);

                stack.Push((indent, node));
            }

            return roots;
        }

        // ==================================================================
        //  行内解析
        // ==================================================================

        private static void AppendInlines(
            InlineCollection inlines, string text, Func<string, BitmapSource?>? imageResolver, double fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var buffer = new StringBuilder();
            var pos = 0;

            void Flush()
            {
                if (buffer.Length == 0) return;
                inlines.Add(new Run(buffer.ToString()));
                buffer.Clear();
            }

            while (pos < text.Length)
            {
                var ch = text[pos];

                // HTML 注释：写手册时常用来留"给维护者的话"，不该出现在正文里
                if (ch == '<' && Matches(text, pos, "<!--"))
                {
                    var end = text.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                    pos = end < 0 ? text.Length : end + 3;
                    continue;
                }

                // 反斜杠转义
                if (ch == '\\' && pos + 1 < text.Length && IsEscapable(text[pos + 1]))
                {
                    buffer.Append(text[pos + 1]);
                    pos += 2;
                    continue;
                }

                // 行内代码
                if (ch == '`')
                {
                    var end = text.IndexOf('`', pos + 1);
                    if (end > pos + 1)
                    {
                        Flush();
                        inlines.Add(BuildCodeSpan(text.Substring(pos + 1, end - pos - 1), fontSize));
                        pos = end + 1;
                        continue;
                    }
                }

                // 图片 ![alt](path)
                if (ch == '!' && pos + 1 < text.Length && text[pos + 1] == '['
                    && TryReadLink(text, pos + 1, out var alt, out var src, out var afterImage))
                {
                    Flush();
                    var image = BuildImage(alt, src, imageResolver);
                    if (image != null)
                        inlines.Add(image);
                    else
                        inlines.Add(new Run(string.IsNullOrEmpty(alt) ? src : alt) { Foreground = MutedBrush });
                    pos = afterImage;
                    continue;
                }

                // 链接 [text](url)
                if (ch == '[' && TryReadLink(text, pos, out var label, out var url, out var afterLink))
                {
                    Flush();
                    inlines.Add(BuildLink(label, url));
                    pos = afterLink;
                    continue;
                }

                // 粗体 / 斜体（** / __ 与 * / _）
                if (ch == '*' || ch == '_')
                {
                    var runLength = CountCharRun(text, pos, ch);
                    var marker = new string(ch, runLength >= 2 ? 2 : 1);
                    var end = text.IndexOf(marker, pos + marker.Length, StringComparison.Ordinal);
                    if (end > pos + marker.Length)
                    {
                        Flush();
                        var inner = text.Substring(pos + marker.Length, end - pos - marker.Length);
                        Span span = marker.Length == 2 ? new Bold() : new Italic();
                        AppendInlines(span.Inlines, inner, imageResolver, fontSize);
                        inlines.Add(span);
                        pos = end + marker.Length;
                        continue;
                    }
                }

                buffer.Append(ch);
                pos++;
            }

            Flush();
        }

        private static Run BuildCodeSpan(string code, double fontSize)
        {
            return new Run(code)
            {
                FontFamily = CodeFont,
                FontSize = Math.Max(9, fontSize - 1),
                Background = CodeSpanBackgroundBrush,
                Foreground = MakeBrush(0xC0, 0x39, 0x2B),
            };
        }

        private static Inline BuildLink(string label, string url)
        {
            var text = string.IsNullOrEmpty(label) ? url : label;

            // 站内锚点（#小节）：手册里用来互相指，点了不外跳，也就不做可点态
            // 链接用主色压深那一档（AccentTextBrush）：#0EA5E9 本身在白底上写 14px 字偏淡
            if (url.StartsWith("#", StringComparison.Ordinal))
                return new Run(text) { Foreground = AccentTextBrush };

            var hyperlink = new Hyperlink(new Run(text))
            {
                Foreground = AccentTextBrush,
                ToolTip = url,
            };
            hyperlink.Click += (_, _) => OpenExternal(url);
            return hyperlink;
        }

        private static void OpenExternal(string url)
        {
            try
            {
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch
            {
                // 打开外部链接失败不该影响帮助窗（比如系统没配默认浏览器）
            }
        }

        private static Inline? BuildImage(string alt, string path, Func<string, BitmapSource?>? imageResolver)
        {
            var bitmap = imageResolver?.Invoke(path);
            if (bitmap == null)
                return null;

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = 760,
                MaxHeight = 560,
                Margin = new Thickness(4, 6, 4, 10),
                ToolTip = string.IsNullOrEmpty(alt) ? null : alt,
            };
            return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Bottom };
        }

        // ==================================================================
        //  词法辅助
        // ==================================================================

        private static string Normalize(string? markdown) =>
            (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

        private static bool Matches(string text, int pos, string token) =>
            pos + token.Length <= text.Length
            && string.CompareOrdinal(text, pos, token, 0, token.Length) == 0;

        private static bool TryParseHeading(string trimmed, out int level, out string text)
        {
            level = 0;
            text = string.Empty;
            var n = 0;
            while (n < trimmed.Length && trimmed[n] == '#' && n < 6)
                n++;
            if (n == 0 || n >= trimmed.Length || trimmed[n] != ' ')
                return false;

            level = n;
            text = trimmed.Substring(n + 1).Trim().TrimEnd('#').Trim();
            return text.Length > 0;
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            if (trimmed.Length < 3)
                return false;
            var compact = trimmed.Replace(" ", string.Empty);
            if (compact.Length < 3)
                return false;
            var ch = compact[0];
            if (ch != '-' && ch != '*' && ch != '_')
                return false;
            return compact.All(c => c == ch);
        }

        private static bool IsTableSeparator(string line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal))
                return false;
            var cells = SplitTableRow(trimmed);
            return cells.Length > 0 && cells.All(c =>
            {
                var body = c.Trim().Trim(':').Trim();
                return body.Length > 0 && body.All(ch => ch == '-');
            });
        }

        private static string[] SplitTableRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|", StringComparison.Ordinal) && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            var cells = new List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (ch == '\\' && i + 1 < trimmed.Length && (trimmed[i + 1] == '|' || trimmed[i + 1] == '\\'))
                {
                    sb.Append(trimmed[i + 1]);
                    i++;
                    continue;
                }
                if (ch == '|')
                {
                    cells.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }
                sb.Append(ch);
            }
            cells.Add(sb.ToString().Trim());
            return cells.ToArray();
        }

        private static bool TryParseListItem(string rawLine, out bool ordered, out int indent, out string content)
        {
            ordered = false;
            indent = 0;
            content = string.Empty;

            indent = LeadingSpaces(rawLine);
            var body = rawLine.TrimStart();
            if (body.Length == 0)
                return false;

            // 无序：- / * / +
            if ((body[0] == '-' || body[0] == '*' || body[0] == '+')
                && body.Length > 1 && body[1] == ' ')
            {
                content = body.Substring(2).Trim();
                return content.Length > 0;
            }

            // 有序：数字 + . / )
            var digits = 0;
            while (digits < body.Length && char.IsDigit(body[digits]))
                digits++;
            if (digits > 0 && digits + 1 < body.Length
                && (body[digits] == '.' || body[digits] == ')') && body[digits + 1] == ' ')
            {
                ordered = true;
                content = body.Substring(digits + 2).Trim();
                return content.Length > 0;
            }

            return false;
        }

        private static bool IsBlockStart(string trimmed) =>
            trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("|", StringComparison.Ordinal)
            || trimmed.StartsWith(">", StringComparison.Ordinal)
            || trimmed.StartsWith("```", StringComparison.Ordinal)
            || IsHorizontalRule(trimmed)
            || TryParseListItem(trimmed, out _, out _, out _);

        private static bool TryReadLink(
            string text, int bracketPos, out string label, out string target, out int next)
        {
            label = string.Empty;
            target = string.Empty;
            next = bracketPos;

            if (bracketPos >= text.Length || text[bracketPos] != '[')
                return false;

            var closeBracket = text.IndexOf(']', bracketPos + 1);
            if (closeBracket < 0 || closeBracket + 1 >= text.Length || text[closeBracket + 1] != '(')
                return false;

            var closeParen = text.IndexOf(')', closeBracket + 2);
            if (closeParen < 0)
                return false;

            label = text.Substring(bracketPos + 1, closeBracket - bracketPos - 1);
            target = text.Substring(closeBracket + 2, closeParen - closeBracket - 2).Trim();
            next = closeParen + 1;
            return target.Length > 0;
        }

        private static int LeadingSpaces(string line)
        {
            var n = 0;
            while (n < line.Length && line[n] == ' ')
                n++;
            return n;
        }

        private static int CountCharRun(string text, int pos, char ch)
        {
            var n = 0;
            while (pos + n < text.Length && text[pos + n] == ch)
                n++;
            return n;
        }

        private static bool IsEscapable(char ch) =>
            ch is '\\' or '`' or '*' or '_' or '[' or ']' or '(' or ')' or '#' or '|' or '!' or '>' or '~';

        /// <summary>
        /// 段落内换行怎么接：中文之间不加空格（加了会出现"。"后半角空格，读起来像排版错），
        /// 其余按 Markdown 惯例用空格接。仓库里的 md 有硬换行，两种都要照顾。
        /// </summary>
        private static string SmartJoin(string previous, string next)
        {
            if (previous.Length == 0) return next;
            if (next.Length == 0) return previous;

            var left = previous[^1];
            var right = next[0];
            if (IsCjk(left) && IsCjk(right))
                return previous + next;
            return previous + " " + next;
        }

        private static bool IsCjk(char ch) =>
            (ch >= 0x3000 && ch <= 0x303F)      // CJK 标点
            || (ch >= 0x4E00 && ch <= 0x9FFF)   // 汉字
            || (ch >= 0xFF00 && ch <= 0xFFEF);  // 全角字符

        private static SolidColorBrush MakeBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private sealed class MdListItem
        {
            public bool Ordered { get; init; }
            public string Text { get; init; } = string.Empty;
            public List<MdListItem> Children { get; } = new();
        }
    }
}
