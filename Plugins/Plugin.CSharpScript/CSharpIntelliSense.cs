using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// 给 TextEditor 挂上 C# 脚本智能提示（仿 ImageScript.ScriptIntelliSense）：
    /// ① 输入字母自动弹补全（Ctrl+Space 强制全量）；输入 Context. 弹成员列表
    /// ② 选中 API 成员插入调用模板，参数为 Tab 占位符可步进
    /// ③ 鼠标悬停词 → API 文档/接口变量 悬浮卡
    /// ④ 输入 ( 弹参数签名提示
    /// 编辑器交互属视图层职责，不经过 VM。
    /// </summary>
    public static class CSharpIntelliSense
    {
        private static CompletionWindow _current;

        /// <summary>接口变量信息（名称/方向/类型），由视图按变量表提供。</summary>
        public sealed class VarInfo
        {
            public string Name { get; set; }
            public string Kind { get; set; }   // "输入" / "输出"
            public string Type { get; set; }   // Int / Double / HImage / ...
        }

        private static Func<IList<VarInfo>> _varsProvider;

        public static void Attach(TextEditor editor, Func<IList<VarInfo>> varsProvider = null)
        {
            _varsProvider = varsProvider;
            var textArea = editor.TextArea;

            // ① 输入字母/数字/下划线：按前缀弹补全；. 弹 Context 成员；( 触发参数提示
            textArea.TextEntered += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Text)) return;
                char c = e.Text[0];

                if (c == '(')
                    ShowParamHint(textArea, textArea.Caret.Offset);
                else if (c == ')' || c == '\n' || c == '\r')
                    CloseParamTip();

                if (c == '.' && IsContextDot(textArea.Document, textArea.Caret.Offset))
                {
                    ShowCompletion(textArea, textArea.Caret.Offset, "", contextOnly: true);
                    return;
                }

                if (!IsIdChar(c))
                {
                    _current?.Close();
                    _current = null;
                    return;
                }
                ShowCompletionForWordAtCaret(textArea);
            };

            // ② Ctrl+Space 强制全量；Tab 占位符步进；Esc 关参数提示
            textArea.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Space &&
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    ShowCompletion(textArea, textArea.Caret.Offset, "");
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Tab && TryJumpTabStop(textArea))
                {
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                    CloseParamTip();
            };

            // ③ 模板编辑期间跟踪占位符偏移
            textArea.Document.Changed += OnDocChangedTrackTabStops;

            // ④ 悬浮提示（AvalonEdit 6.3 移除了 ToolTipManager，自建悬停计时器）
            AttachHoverTooltip(editor, textArea);
        }

        // ---------- Context. 成员补全判断 ----------

        /// <summary>判断 offset 处的 "." 左侧是否为 Context（仅它后面弹成员列表）。</summary>
        private static bool IsContextDot(IDocument doc, int offsetAfterDot)
        {
            if (doc == null) return false;
            int p = offsetAfterDot - 2; // '.' 左侧第一个字符
            while (p >= 0 && IsIdChar(doc.GetCharAt(p)))
                p--;
            int start = p + 1;
            int len = offsetAfterDot - 1 - start;
            if (len <= 0) return false;
            return doc.GetText(start, len) == "Context";
        }

        // ---------- 悬浮提示 ----------

        private static DispatcherTimer _hoverTimer;
        private static ToolTip _hoverTip;
        private static Point _lastMouse;
        private static string _currentTipWord;

        private static void AttachHoverTooltip(TextEditor editor, TextArea textArea)
        {
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _hoverTimer.Tick += (s, e) =>
            {
                _hoverTimer.Stop();
                ShowHoverTip(editor, textArea, _lastMouse);
            };

            textArea.MouseMove += (s, e) =>
            {
                var p = e.GetPosition(editor);

                // 提示已显示且鼠标仍在同一单词上 → 保持显示
                if (_hoverTip != null && _hoverTip.IsOpen && _currentTipWord != null)
                {
                    int off = GetOffsetAtPoint(editor, p);
                    string word = ExtractWordAt(textArea.Document, off);
                    if (word == _currentTipWord)
                        return;
                }

                if (p == _lastMouse) return;
                _lastMouse = p;
                CloseHoverTip();
                _hoverTimer.Start();
            };
            textArea.MouseLeave += (s, e) => { _hoverTimer.Stop(); CloseHoverTip(); };
            editor.TextChanged += (s, e) => { _hoverTimer.Stop(); CloseHoverTip(); };
        }

        private static void CloseHoverTip()
        {
            _currentTipWord = null;
            if (_hoverTip != null)
            {
                _hoverTip.IsOpen = false;
                _hoverTip = null;
            }
        }

        private static void ShowHoverTip(TextEditor editor, TextArea textArea, Point mouseInEditor)
        {
            int offset = GetOffsetAtPoint(editor, mouseInEditor);
            string word = ExtractWordAt(textArea.Document, offset);

            // 优先 API 文档，其次接口变量
            object panel = null;
            var api = string.IsNullOrEmpty(word) ? null : CSharpApiDoc.Find(word);
            if (api != null)
                panel = BuildApiTipPanel(api);
            else
            {
                var v = FindVar(word);
                if (v != null) panel = BuildVarTipPanel(v);
            }

            if (panel == null) return;

            _currentTipWord = word;
            _hoverTip = new ToolTip
            {
                Content = panel,
                PlacementTarget = editor,
                Placement = PlacementMode.Mouse,
                StaysOpen = true,   // 生命周期手动管理：离开单词/编辑器或文本变化时才关闭
            };
            _hoverTip.IsOpen = true;
        }

        private static int GetOffsetAtPoint(TextEditor editor, Point pointInEditor)
        {
            try
            {
                var tp = editor.GetPositionFromPoint(pointInEditor);
                if (tp == null) return -1;
                return editor.Document.GetOffset(tp.Value.Line, tp.Value.Column);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>取 offset 处的完整标识符（点不在词上返回 null）。</summary>
        private static string ExtractWordAt(IDocument doc, int offset)
        {
            if (doc == null || offset < 0 || offset >= doc.TextLength) return null;

            var line = doc.GetLineByOffset(offset);
            string text = doc.GetText(line);
            int posInLine = offset - line.Offset;

            if (posInLine >= text.Length) posInLine = text.Length - 1;
            if (posInLine < 0 || !IsIdChar(text[posInLine]))
            {
                if (posInLine > 0 && IsIdChar(text[posInLine - 1])) posInLine--;
                else return null;
            }

            int s = posInLine;
            while (s > 0 && IsIdChar(text[s - 1])) s--;
            int e2 = posInLine;
            while (e2 < text.Length && IsIdChar(text[e2])) e2++;

            return e2 > s ? text.Substring(s, e2 - s) : null;
        }

        private static IList<VarInfo> SafeVars()
        {
            try { return _varsProvider?.Invoke() ?? (IList<VarInfo>)Array.Empty<VarInfo>(); }
            catch { return Array.Empty<VarInfo>(); }
        }

        private static VarInfo FindVar(string word)
        {
            if (string.IsNullOrEmpty(word)) return null;
            foreach (var v in SafeVars())
                if (v != null && string.Equals(v.Name, word, StringComparison.Ordinal))
                    return v;
            return null;
        }

        private static bool IsIdChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_';

        // ---------- Tab 参数占位符步进 ----------

        private static List<int[]> _tabStops;   // [start,end] 文档偏移
        private static int _tabStopIdx;

        /// <summary>模板插入后注册占位符（ApiCompletionData 调用），startIdx 为当前选中项。</summary>
        public static void SetTabStops(List<int[]> stops, int startIdx)
        {
            _tabStops = stops;
            _tabStopIdx = startIdx;
        }

        private static bool TryJumpTabStop(TextArea textArea)
        {
            if (_tabStops == null || _tabStops.Count < 2)
                return false;

            if (_tabStopIdx >= _tabStops.Count - 1)
            {
                _tabStops = null; // 最后一个参数后 Tab 退出模板
                return false;
            }

            _tabStopIdx++;
            var st = _tabStops[_tabStopIdx];
            textArea.Caret.Offset = st[1];
            textArea.Selection = Selection.Create(textArea, st[0], st[1]);
            textArea.Caret.BringCaretToView();
            return true;
        }

        private static void OnDocChangedTrackTabStops(object sender, DocumentChangeEventArgs e)
        {
            if (_tabStops == null) return;
            if (_tabStopIdx >= _tabStops.Count)
            {
                _tabStops = null;
                return;
            }

            // 6.3：变更明细在 OffsetChangeMap；多段改动（如撤销）保守作废模板
            var map = e.OffsetChangeMap;
            if (map == null || map.Count != 1)
            {
                _tabStops = null;
                return;
            }
            var m0 = map[0];

            var cur = _tabStops[_tabStopIdx];
            int delta = m0.InsertionLength - m0.RemovalLength;

            bool insideCurrent = m0.Offset >= cur[0] && m0.Offset <= cur[1] + Math.Max(0, delta);
            if (!insideCurrent)
            {
                _tabStops = null;
                return;
            }

            cur[1] += delta;
            for (int i = _tabStopIdx + 1; i < _tabStops.Count; i++)
            {
                _tabStops[i][0] += delta;
                _tabStops[i][1] += delta;
            }
        }

        // ---------- ( 参数提示 ----------

        private static ToolTip _paramTip;

        private static void ShowParamHint(TextArea textArea, int caretAfterParen)
        {
            var doc = textArea.Document;
            if (doc == null || caretAfterParen < 2) return;

            // "(" 左侧回溯 API 名
            int p = caretAfterParen - 2;
            while (p >= 0 && IsIdChar(doc.GetCharAt(p)))
                p--;
            int nameStart = p + 1;
            if (nameStart >= caretAfterParen - 1) return;

            string name = doc.GetText(nameStart, caretAfterParen - 1 - nameStart);
            var api = CSharpApiDoc.Find(name);
            if (api == null) return;

            CloseParamTip();
            _paramTip = new ToolTip
            {
                Content = BuildApiTipPanel(api),
                PlacementTarget = textArea,
                Placement = PlacementMode.MousePoint,
                StaysOpen = true,
            };
            _paramTip.IsOpen = true;
        }

        private static void CloseParamTip()
        {
            if (_paramTip != null)
            {
                _paramTip.IsOpen = false;
                _paramTip = null;
            }
        }

        // ---------- 补全列表 ----------

        private static void ShowCompletionForWordAtCaret(TextArea textArea)
        {
            var doc = textArea.Document;
            if (doc == null) return;

            int caret = textArea.Caret.Offset;
            int start = caret;
            while (start > 0 && IsIdChar(doc.GetCharAt(start - 1)))
                start--;

            string prefix = doc.GetText(start, caret - start);
            if (prefix.Length == 0) return;
            ShowCompletion(textArea, start, prefix);
        }

        private static void ShowCompletion(TextArea textArea, int startOffset, string prefix, bool contextOnly = false)
        {
            _current?.Close();

            var window = new CompletionWindow(textArea)
            {
                CloseWhenCaretAtBeginning = true,
            };
            window.StartOffset = startOffset;
            window.EndOffset = textArea.Caret.Offset;

            int added = 0;

            // 接口变量优先（最常用的名字）
            if (!contextOnly)
            {
                foreach (var v in SafeVars())
                {
                    if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                    if (prefix.Length > 0 &&
                        !v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    window.CompletionList.CompletionData.Add(new VarCompletionData(v));
                    added++;
                }
            }

            // Context API 成员
            foreach (var m in CSharpApiDoc.Match(prefix, max: prefix.Length == 0 ? 200 : 60))
            {
                window.CompletionList.CompletionData.Add(new ApiCompletionData(m));
                added++;
            }

            // 高频关键字（contextOnly 模式不给，避免 Context. 后混入无关词）
            if (!contextOnly)
            {
                foreach (var k in CSharpApiDoc.CommonKeywords)
                {
                    if (prefix.Length > 0 &&
                        !k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    window.CompletionList.CompletionData.Add(new KeywordCompletionData(k));
                    added++;
                }
            }

            if (added == 0)
                return;

            window.Closed += (s, e) =>
            {
                if (ReferenceEquals(_current, window))
                    _current = null;
            };

            _current = window;
            window.Show();
        }

        // ---------- 悬浮文档卡片 ----------

        /// <summary>API 悬浮卡：名称/签名/说明。</summary>
        private static object BuildApiTipPanel(CSharpApiDoc.ApiMember m)
        {
            var panel = new StackPanel { MaxWidth = 460, Margin = new Thickness(8) };

            panel.Children.Add(new TextBlock
            {
                Text = m.Name,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x79, 0x5E, 0x26)),
            });

            panel.Children.Add(new TextBlock
            {
                Text = m.Signature,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 4, 0, 0),
            });

            panel.Children.Add(new TextBlock
            {
                Text = m.Summary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });

            return panel;
        }

        /// <summary>接口变量悬浮卡：名称/方向/类型。</summary>
        private static object BuildVarTipPanel(VarInfo v)
        {
            var panel = new StackPanel { MaxWidth = 360, Margin = new Thickness(8) };
            panel.Children.Add(new TextBlock
            {
                Text = v.Name,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x00, 0x80)),
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"接口{v.Kind}变量 · 类型 {v.Type}",
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            });
            panel.Children.Add(new TextBlock
            {
                Text = v.Kind == "输入"
                    ? "运行时由连线（上游输出）或手填值传入本插件"
                    : "插件执行后作为输出端口供下游连线",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            return panel;
        }
    }
}
