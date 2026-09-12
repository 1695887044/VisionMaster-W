using System;
using System.Text;
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

namespace Plugin.ImageScript
{
    /// <summary>
    /// 给 TextEditor 挂上 Halcon 算子智能提示：
    /// ① 输入字母自动弹出补全列表（实时过滤，Ctrl+Space 强制全量）
    /// ② 选中算子插入完整参数模板并选中首个占位参数
    /// ③ 鼠标悬停算子名 → 签名/含义/参数说明/常见用途 悬浮卡
    /// 编辑器交互属视图层职责，不经过 VM（MVVM 允许 View 层行为挂在此处）。
    /// </summary>
    public static class ScriptIntelliSense
    {
        private static CompletionWindow _current;

        /// <summary>接口变量信息（名称/方向/类型），由视图按当前编辑过程提供。</summary>
        public sealed class VarInfo
        {
            public string Name { get; set; }
            public string Kind { get; set; }   // "输入" / "输出"
            public string Type { get; set; }   // HImage / Double / ...
        }

        private static Func<IList<VarInfo>> _varsProvider;

        public static void Attach(TextEditor editor, Func<IList<VarInfo>> varsProvider = null)
        {
            _varsProvider = varsProvider;
            var textArea = editor.TextArea;

            // ① 输入字母/数字/下划线：按前缀弹补全；( 触发参数提示
            textArea.TextEntered += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Text)) return;
                char c = e.Text[0];

                if (c == '(')
                    ShowParamHint(textArea, textArea.Caret.Offset);
                else if (c == ')' || c == '\n' || c == '\r')
                    CloseParamTip();

                if (!IsIdChar(c))
                {
                    _current?.Close();
                    _current = null;
                    return;
                }
                ShowCompletionForWordAtCaret(textArea);
            };

            // ② Ctrl+Space：强制弹出（空前缀=全量列表）；Tab：参数占位符步进；Esc：关参数提示
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

            // ③ 模板编辑期间跟踪占位符偏移（在参数上打字后，后续占位符自动移位）
            textArea.Document.Changed += OnDocChangedTrackTabStops;

            // ③ 悬浮提示（AvalonEdit 6.3 移除了 ToolTipManager，自建悬停计时器）
            AttachHoverTooltip(editor, textArea);
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
                // 统一用编辑器相对坐标（GetPositionFromPoint 的输入约定）
                var p = e.GetPosition(editor);

                // 提示已显示且鼠标仍在同一单词上 → 保持显示（容忍微抖动）
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

            // 优先算子文档，其次接口变量
            object panel = null;
            var op = string.IsNullOrEmpty(word) ? null : OperatorDoc.Find(word);
            if (op != null)
            {
                panel = BuildTipPanel(op);
            }
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
                // 官方换算：编辑器相对点 → 行列 → 文档 offset（内部处理滚动/行号槽/换行）
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

            // 点在标识符内/紧邻其右都算命中（鼠标常停在字母上）
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

        /// <summary>模板插入后注册占位符（OpCompletionData 调用），startIdx 为当前选中项。</summary>
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

            // 改动发生在当前占位符范围内 → 视为"打字替换参数"，后续占位符整体移位
            bool insideCurrent = m0.Offset >= cur[0] && m0.Offset <= cur[1] + Math.Max(0, delta);
            if (!insideCurrent)
            {
                _tabStops = null; // 在别处编辑：模板流程作废
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

            // "(" 左侧回溯算子名
            int p = caretAfterParen - 2;
            while (p >= 0 && IsIdChar(doc.GetCharAt(p)))
                p--;
            int nameStart = p + 1;
            if (nameStart >= caretAfterParen - 1) return;

            string name = doc.GetText(nameStart, caretAfterParen - 1 - nameStart);
            var op = OperatorDoc.Find(name);
            if (op == null) return;

            CloseParamTip();
            _paramTip = new ToolTip
            {
                Content = BuildTipPanel(op),
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

        // ---------- 常用代码片段（数据在 ScriptTemplates.cs，经编辑器右键菜单插入） ----------

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

        private static void ShowCompletion(TextArea textArea, int startOffset, string prefix)
        {
            _current?.Close();

            var window = new CompletionWindow(textArea)
            {
                CloseWhenCaretAtBeginning = true,
            };
            window.StartOffset = startOffset;
            window.EndOffset = textArea.Caret.Offset;

            int added = 0;

            // 接口变量优先（当前过程最常用的名字，排在算子前）
            foreach (var v in SafeVars())
            {
                if (v == null || string.IsNullOrEmpty(v.Name)) continue;
                if (prefix.Length > 0 &&
                    !v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                window.CompletionList.CompletionData.Add(new VarCompletionData(v));
                added++;
            }

            foreach (var item in OperatorDoc.Match(prefix, max: prefix.Length == 0 ? 200 : 60))
            {
                window.CompletionList.CompletionData.Add(item);
                added++;
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
                    ? "运行时由连线（上游输出）或手填值传入本过程"
                    : "过程执行后作为输出端口供下游连线",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            return panel;
        }

        /// <summary>构建悬浮文档卡片：名称/分类/签名/含义/用途/参数说明。</summary>
        private static object BuildTipPanel(OpDoc op)
        {
            var panel = new StackPanel { MaxWidth = 460, Margin = new Thickness(8) };

                panel.Children.Add(new TextBlock
                {
                    Text = op.Name,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = Brushes.DarkBlue,
                });

                panel.Children.Add(new TextBlock
                {
                    Text = $"分类：{op.Category}",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 2, 0, 2),
                });

                panel.Children.Add(new TextBlock
                {
                    Text = op.Signature,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    Padding = new Thickness(4),
                });

                panel.Children.Add(new TextBlock
                {
                    Text = op.Summary,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 2),
                });

                panel.Children.Add(new TextBlock
                {
                    Text = "常见用途：" + op.Usage,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });

                AddParamSection(panel, "输入参数", op.Io, op.Ic);
                AddParamSection(panel, "输出参数", op.Oo, op.Oc);
                return panel;
            }

            private static void AddParamSection(StackPanel panel, string title, params OpParam[][] groups)
            {
                OpParam[] all = Array.Empty<OpParam>();
                foreach (var g in groups)
                {
                    if (g == null) continue;
                    var merged = new OpParam[all.Length + g.Length];
                    Array.Copy(all, merged, all.Length);
                    Array.Copy(g, 0, merged, all.Length, g.Length);
                    all = merged;
                }
                if (all.Length == 0) return;

                panel.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                });

                foreach (var p in all)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"· {p.Name} ({p.Type})：{p.Desc}",
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(8, 1, 0, 1),
                    });
                }
            }
    }

    /// <summary>接口变量补全项：插入变量名（不带括号），优先级高于算子。</summary>
    public sealed class VarCompletionData : ICompletionData
    {
        private readonly ScriptIntelliSense.VarInfo _v;

        public VarCompletionData(ScriptIntelliSense.VarInfo v) => _v = v;

        public ImageSource Image => null;
        public string Text => _v.Name;
        public object Content => _v.Name;
        public object Description => $"接口{_v.Kind}变量 · {_v.Type}";
        public double Priority => 10;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, _v.Name);
        }
    }
}
