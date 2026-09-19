using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;

namespace Core.Editing
{
    /// <summary>
    /// 脚本编辑器行为集合（视图层职责，不涉及 VM）：
    /// 撤销/重做、自动缩进、括号配对、Ctrl+/ 注释切换、Ctrl+滚轮缩放、
    /// 当前行高亮、错误行高亮、查找(Ctrl+F)/替换(Ctrl+H)。
    /// 与脚本语言无关：注释前缀由调用方指定（C# 用 "//"，Halcon 用 "*"）。
    /// </summary>
    public static class ScriptEditorBehavior
    {
        /// <param name="editor">目标编辑器</param>
        /// <param name="commentPrefix">行注释前缀，如 "//"（C#）或 "*"（Halcon）</param>
        public static void Attach(TextEditor editor, string commentPrefix = "//")
        {
            var textArea = editor.TextArea;

            AttachUndoRedo(textArea);
            AttachAutoIndentAndBrackets(textArea);
            AttachCommentToggle(textArea, commentPrefix);
            AttachZoom(editor, textArea);
            AttachLineHighlight(textArea);
            AttachFindReplace(editor, textArea);
        }

        // ---------- 1. 撤销/重做 ----------

        private static void AttachUndoRedo(TextArea textArea)
        {
            textArea.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Undo,
                (s, e) => { if (textArea.Document.UndoStack.CanUndo) textArea.Document.UndoStack.Undo(); e.Handled = true; }));
            textArea.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Redo,
                (s, e) => { if (textArea.Document.UndoStack.CanRedo) textArea.Document.UndoStack.Redo(); e.Handled = true; }));
            textArea.InputBindings.Add(new KeyBinding(ApplicationCommands.Undo, Key.Z, ModifierKeys.Control));
            textArea.InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Y, ModifierKeys.Control));
        }

        // ---------- 3. 自动缩进 + 括号配对 ----------

        private static void AttachAutoIndentAndBrackets(TextArea textArea)
        {
            // 回车：继承上一行缩进
            textArea.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
                if (textArea.Selection.Length > 0) return; // 有选区时保留默认行为

                e.Handled = true;
                var doc = textArea.Document;
                int caret = textArea.Caret.Offset;
                var line = doc.GetLineByOffset(caret);
                string before = doc.GetText(line.Offset, caret - line.Offset);
                string indent = new string(before.TakeWhile(char.IsWhiteSpace).ToArray());

                doc.Replace(caret, 0, "\n" + indent);
                textArea.Caret.Offset = caret + 1 + indent.Length;
            };

            // 括号/引号配对：输入 ( 自动补 )；' 自动闭合或越过
            textArea.TextEntered += (s, e) =>
            {
                var doc = textArea.Document;
                int caret = textArea.Caret.Offset;

                switch (e.Text)
                {
                    case "(":
                        doc.Insert(caret, ")");
                        break; // 光标留在括号内
                    case "[":
                        doc.Insert(caret, "]");
                        break;
                    case "'":
                        // 右侧已有引号 → 越过（type-over）；否则补一个闭合引号并把光标留在中间
                        if (caret < doc.TextLength && doc.GetCharAt(caret) == '\'')
                            textArea.Caret.Offset = caret + 1;
                        else
                            doc.Insert(caret, "'");
                        break;
                }
            };
        }

        // ---------- 7. Ctrl+/ 注释切换 ----------

        private static void AttachCommentToggle(TextArea textArea, string commentPrefix)
        {
            textArea.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.OemQuestion || Keyboard.Modifiers != ModifierKeys.Control) return;
                e.Handled = true;
                ToggleComments(textArea, null, commentPrefix);
            };
        }

        /// <summary>强制注释（add=true）或取消注释（add=false）当前行/选区——右键菜单用。</summary>
        public static void SetComment(TextArea textArea, bool add, string commentPrefix = "//")
            => ToggleComments(textArea, add, commentPrefix);

        // Selection 端点是 TextViewPosition(Line/Column)，转文档偏移
        private static int SelOffset(TextDocument doc, TextViewPosition p) =>
            doc.GetOffset(p.Line, p.Column);

        /// <summary>force=null 自动判断方向；true/false 强制注释/取消。</summary>
        private static void ToggleComments(TextArea textArea, bool? force, string prefix)
        {
            var doc = textArea.Document;
            int startLine, endLine;
            if (textArea.Selection.Length > 0)
            {
                startLine = doc.GetLineByOffset(SelOffset(doc, textArea.Selection.StartPosition)).LineNumber;
                endLine = doc.GetLineByOffset(SelOffset(doc, textArea.Selection.EndPosition)).LineNumber;
            }
            else
            {
                startLine = endLine = textArea.Caret.Line;
            }

            var lines = new List<IDocumentLine>();
            for (int i = startLine; i <= endLine; i++)
                lines.Add(doc.GetLineByNumber(i));
            if (lines.Count == 0) return;

            // 全部已注释 → 取消；否则 → 注释（force 可强制指定方向）
            bool allCommented = lines.All(l =>
            {
                string t = doc.GetText(l.Offset, l.Length);
                return t.Trim().Length == 0
                    || t.TrimStart().StartsWith(prefix, StringComparison.Ordinal);
            });
            bool doComment = force ?? !allCommented;

            doc.BeginUpdate(); // 多次改动合并为一个撤销单元
            try
            {
                foreach (var l in lines)
                {
                    string t = doc.GetText(l.Offset, l.Length);
                    if (t.Trim().Length == 0) continue;

                    if (!doComment)
                    {
                        int idx = t.IndexOf(prefix, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            int removeLen = (idx + prefix.Length < t.Length && t[idx + prefix.Length] == ' ')
                                ? prefix.Length + 1 : prefix.Length;
                            doc.Replace(l.Offset + idx, removeLen, "");
                        }
                    }
                    else
                    {
                        int indent = t.Length - t.TrimStart().Length;
                        doc.Insert(l.Offset + indent, prefix + " ");
                    }
                }
            }
            finally
            {
                doc.EndUpdate();
            }
        }

        // ---------- 9. Ctrl+滚轮缩放 ----------

        private static void AttachZoom(TextEditor editor, TextArea textArea)
        {
            editor.PreviewMouseWheel += (s, e) =>
            {
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                e.Handled = true;
                double delta = e.Delta > 0 ? 1 : -1;
                editor.FontSize = Math.Clamp(editor.FontSize + delta, 9, 36);
            };
        }

        // ---------- 5. 查找 Ctrl+F / 替换 Ctrl+H ----------

        private static void AttachFindReplace(TextEditor editor, TextArea textArea)
        {
            // 官方搜索面板：Ctrl+F 打开、F3 下一个（AvalonEdit.Search 自带快捷键绑定）
            SearchPanel.Install(textArea);

            textArea.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    ShowReplaceDialog(editor, textArea);
                }
            };
        }

        private static Window? _replaceWin;

        private static void ShowReplaceDialog(TextEditor editor, TextArea textArea)
        {
            if (_replaceWin != null)
            {
                _replaceWin.Activate();
                return;
            }

            var tbFind = new System.Windows.Controls.TextBox { Margin = new Thickness(4), FontSize = 13 };
            var tbRepl = new System.Windows.Controls.TextBox { Margin = new Thickness(4), FontSize = 13 };

            var btnNext = new System.Windows.Controls.Button { Content = "查找下一个", MinWidth = 80, Margin = new Thickness(4) };
            var btnOne = new System.Windows.Controls.Button { Content = "替换", MinWidth = 60, Margin = new Thickness(4) };
            var btnAll = new System.Windows.Controls.Button { Content = "全部替换", MinWidth = 80, Margin = new Thickness(4) };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(8) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Add(grid, new System.Windows.Controls.TextBlock { Text = "查找:", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center }, 0, 0);
            Add(grid, tbFind, 0, 1);
            Add(grid, new System.Windows.Controls.TextBlock { Text = "替换:", Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center }, 1, 0);
            Add(grid, tbRepl, 1, 1);
            var sp = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            sp.Children.Add(btnNext);
            sp.Children.Add(btnOne);
            sp.Children.Add(btnAll);
            Add(grid, sp, 2, 0, 3);

            var doc = editor.Document;
            int lastFind = -1;

            bool FindNext()
            {
                if (tbFind.Text.Length == 0) return false;
                int from = (lastFind >= 0 && lastFind < doc.TextLength) ? lastFind + 1 : 0;
                int idx = doc.IndexOf(tbFind.Text, from, doc.TextLength - from,
                    System.StringComparison.Ordinal);
                if (idx < 0) idx = doc.IndexOf(tbFind.Text, 0, doc.TextLength,
                    System.StringComparison.Ordinal); // 从头循环
                if (idx < 0) return false;
                lastFind = idx;
                textArea.Selection = Selection.Create(textArea, idx, idx + tbFind.Text.Length);
                textArea.Caret.Offset = idx + tbFind.Text.Length;
                textArea.Caret.BringCaretToView();
                return true;
            }

            btnNext.Click += (s, e) => { if (!FindNext()) FlashNoMatch(editor); };
            btnOne.Click += (s, e) =>
            {
                if (textArea.Selection.Length > 0 &&
                    doc.GetText(SelOffset(doc, textArea.Selection.StartPosition), textArea.Selection.Length) == tbFind.Text)
                {
                    doc.Replace(SelOffset(doc, textArea.Selection.StartPosition), textArea.Selection.Length, tbRepl.Text);
                    lastFind = -1;
                }
                if (!FindNext()) FlashNoMatch(editor);
            };
            btnAll.Click += (s, e) =>
            {
                if (tbFind.Text.Length == 0) return;
                string text = doc.Text;
                int count = (System.Text.RegularExpressions.Regex.Matches(
                    text, System.Text.RegularExpressions.Regex.Escape(tbFind.Text)).Count);
                doc.BeginUpdate();
                doc.Text = text.Replace(tbFind.Text, tbRepl.Text);
                doc.EndUpdate();
                lastFind = -1;

                btnAll.Content = count > 0 ? $"已替换 {count} 处" : "无匹配";
                var revert = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                revert.Tick += (s2, e2) =>
                {
                    revert.Stop();
                    btnAll.Content = "全部替换";
                };
                revert.Start();
            };

            _replaceWin = new Window
            {
                Title = "替换",
                Content = grid,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = Window.GetWindow(editor),
                ShowInTaskbar = false,
                Topmost = true,
                MinWidth = 340,
            };
            _replaceWin.Closed += (s, e) => _replaceWin = null;
            _replaceWin.Show();
            tbFind.Focus();
        }

        private static void FlashNoMatch(TextEditor editor)
        {
            // 无匹配时轻提示（不改标题，短暂闪烁边框）
            editor.BorderBrush = Brushes.IndianRed;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                editor.ClearValue(TextEditor.BorderBrushProperty);
            };
            timer.Start();
        }

        private static void Add(System.Windows.Controls.Grid g, UIElement el, int row, int col, int colSpan = 1)
        {
            System.Windows.Controls.Grid.SetRow(el, row);
            System.Windows.Controls.Grid.SetColumn(el, col);
            if (colSpan > 1) System.Windows.Controls.Grid.SetColumnSpan(el, colSpan);
            g.Children.Add(el);
        }

        // ---------- 8. 当前行高亮 + 2. 错误行高亮（IBackgroundRenderer 官方扩展点） ----------

        private sealed class LineBackgroundRenderer : IBackgroundRenderer
        {
            private static readonly Brush CaretBrush = Make(Color.FromArgb(0x18, 0x3F, 0x5B, 0x8F));
            private static readonly Brush ErrorBrush = Make(Color.FromArgb(0x3C, 0xE0, 0x40, 0x40));

            private static Brush Make(Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }

            public KnownLayer Layer => KnownLayer.Background;

            public int CaretLine = -1;
            public readonly HashSet<int> ErrorLines = new HashSet<int>();

            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                var doc = textView.Document;
                if (doc == null) return;

                // 当前行（淡蓝灰）
                if (CaretLine >= 1 && CaretLine <= doc.LineCount)
                    Fill(doc.GetLineByNumber(CaretLine), CaretBrush);

                // 错误行（淡红，覆盖当前行色）
                foreach (int ln in ErrorLines)
                {
                    if (ln >= 1 && ln <= doc.LineCount)
                        Fill(doc.GetLineByNumber(ln), ErrorBrush);
                }

                void Fill(DocumentLine line, Brush brush)
                {
                    var builder = new BackgroundGeometryBuilder
                    {
                        AlignToWholePixels = true,
                        ExtendToFullWidthAtLineEnd = true, // 行尾空白区也铺满
                    };
                    builder.AddSegment(textView, line); // DocumentLine 即 ISegment
                    var geo = builder.CreateGeometry();
                    if (geo != null)
                        drawingContext.DrawGeometry(brush, null, geo);
                }
            }
        }

        private static LineBackgroundRenderer? _renderer;

        private static void AttachLineHighlight(TextArea textArea)
        {
            var renderer = new LineBackgroundRenderer();
            _renderer = renderer;
            textArea.TextView.BackgroundRenderers.Add(renderer);

            void Refresh()
            {
                renderer.CaretLine = textArea.Caret.Line;
                textArea.TextView.InvalidateLayer(KnownLayer.Background);
            }

            textArea.Caret.PositionChanged += (s, e) => Refresh();

            // 编辑后行号可能错位：清除错误高亮（重新校验会再标）
            textArea.Document.Changed += (s, e) =>
            {
                if (renderer.ErrorLines.Count > 0)
                    renderer.ErrorLines.Clear();
                textArea.TextView.InvalidateLayer(KnownLayer.Background);
            };
            Refresh();
        }

        /// <summary>标记校验失败的行（红色背景）；传 null/空清除。跳转由调用方负责。</summary>
        public static void SetErrorLines(TextEditor editor, IEnumerable<int>? lines)
        {
            if (_renderer == null) return;
            _renderer.ErrorLines.Clear();
            if (lines != null)
                foreach (var l in lines)
                    _renderer.ErrorLines.Add(l);
            editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }
}
