using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行窗口的共享软键盘（数字 / 字符两种布局）。
    ///
    /// <b>它不认识业务规则</b>：不解析数值、不判上下限、不写变量。键盘只做三件事——
    /// 把键拼成文本、调用目标的 <see cref="IScadaSoftKeyboardTarget.SetEditText"/>、
    /// 在确定/取消时调用目标原有的 <c>CommitEdit</c>/<c>CancelEdit</c>。
    /// 校验、类型转换、权限、写通道全部复用输入控件已有的路径。
    ///
    /// <b>按键一律 Focusable=False</b>：输入控件内部的 TextBox 必须一直持有焦点，
    /// 否则点一下键盘就触发 LostFocus → CommitEdit，键盘会"点一个键就关"。
    ///
    /// <b>显示文本直接绑目标的 EditText</b>：物理键盘打字与软键盘点击走同一个 DP，
    /// 不需要键盘自己维护第二份输入状态。
    /// </summary>
    public partial class ScadaSoftKeyboard : UserControl
    {
        private IScadaSoftKeyboardTarget? _target;
        private bool _shift;

        public ScadaSoftKeyboard()
        {
            InitializeComponent();
        }

        /// <summary>键盘此刻是否打开（运行窗口的 Esc/Enter 分流读它）。</summary>
        public bool IsOpen => Visibility == Visibility.Visible;

        /// <summary>当前编辑目标（没有则为 null）。</summary>
        public IScadaSoftKeyboardTarget? Target => _target;

        /// <summary>绑定到一个编辑目标并显示；数字/字符布局按 <see cref="IScadaSoftKeyboardTarget.IsNumeric"/> 选。</summary>
        public void Open(IScadaSoftKeyboardTarget target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _shift = false;

            NumericPad.Visibility = target.IsNumeric ? Visibility.Visible : Visibility.Collapsed;
            TextPad.Visibility = target.IsNumeric ? Visibility.Collapsed : Visibility.Visible;

            // 固定设计宽（数字 344 / 字符 560）：长文本在显示区截断，不让键盘本体被撑宽；
            // 窗口再小由外层 Viewbox 等比缩小。
            KeyboardPanel.Width = target.IsNumeric ? 344 : 560;
            var textWidth = Math.Max(120, KeyboardPanel.Width - 24);
            DisplayText.MaxWidth = textWidth;
            MetaText.MaxWidth = textWidth;
            ErrorText.MaxWidth = textWidth;

            DisplayText.SetBinding(
                TextBlock.TextProperty,
                new Binding(nameof(IScadaSoftKeyboardTarget.EditText))
                {
                    Source = target,
                    Mode = BindingMode.OneWay,
                    TargetNullValue = string.Empty,
                });

            UpdateShiftVisual();
            Refresh();
            Visibility = Visibility.Visible;
        }

        /// <summary>收起键盘并清掉目标；不替调用方提交/取消。</summary>
        public void Close()
        {
            DisplayText.ClearValue(TextBlock.TextProperty);
            _target = null;
            Visibility = Visibility.Collapsed;
        }

        /// <summary>回读目标的错误与元信息（显示文本由绑定负责）。</summary>
        public void Refresh()
        {
            if (_target is not { } target)
                return;

            MetaText.Text = BuildMeta(target);

            var error = target.EditError;
            ErrorText.Text = error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(error)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static string BuildMeta(IScadaSoftKeyboardTarget target)
        {
            var parts = new List<string>(3);

            if (!string.IsNullOrWhiteSpace(target.RangeText))
                parts.Add(target.RangeText!);

            if (!string.IsNullOrWhiteSpace(target.UnitText))
                parts.Add(target.UnitText!);

            if (!string.IsNullOrWhiteSpace(target.OriginalText))
                parts.Add($"原值 {target.OriginalText}");

            return string.Join(" · ", parts);
        }

        /// <summary>
        /// 九宫格摆放的纯函数：默认放在字段下方；下方放不下翻到上方；左右按字段中心对齐，
        /// 并夹回画面区（至少留 <paramref name="gap"/>）。
        ///
        /// 单独抽出来是为了能被断言直接钉住——窗口里那一段还牵扯 TranslatePoint/缩放，
        /// 混在一起测就只能开真窗口；这里输入是三个矩形，输出是左上角。
        /// </summary>
        public static Point ComputePlacement(Rect field, Size keyboard, Size area, double gap = 8)
        {
            var left = field.Left + (field.Width - keyboard.Width) / 2;
            left = Math.Clamp(left, gap, Math.Max(gap, area.Width - keyboard.Width - gap));

            var top = field.Bottom + gap;

            if (top + keyboard.Height > area.Height - gap)
                top = field.Top - keyboard.Height - gap;

            top = Math.Clamp(top, gap, Math.Max(gap, area.Height - keyboard.Height - gap));

            return new Point(left, top);
        }

        private void OnKeyClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string key })
                PressKey(key);
        }

        /// <summary>
        /// 按下某个键（XAML 按钮与断言共用同一入口）。<paramref name="key"/> 取 Tag 值：
        /// 数字/字母/符号本身、<c>back</c> / <c>clear</c> / <c>sign</c> / <c>shift</c> /
        /// <c>space</c> / <c>esc</c> / <c>ok</c>。
        /// </summary>
        public void PressKey(string key)
        {
            if (_target is not { } target)
                return;

            switch (key)
            {
                case "esc":
                    target.CancelEdit();
                    Close();
                    return;

                case "ok":
                    target.CommitEdit();
                    Refresh();

                    // 提交失败（越限/类型不对/写被拒）时目标仍在编辑态：键盘不关，错误留在显示区。
                    if (!target.IsEditing)
                        Close();
                    return;

                case "back":
                    SetText(RemoveLast(target.EditText ?? string.Empty));
                    return;

                case "clear":
                    SetText(string.Empty);
                    return;

                case "sign":
                    SetText(ToggleSign(target.EditText ?? string.Empty));
                    return;

                case "shift":
                    _shift = !_shift;
                    UpdateShiftVisual();
                    return;

                case "space":
                    Append(" ");
                    return;

                default:
                    Append(key);
                    return;
            }
        }

        private void Append(string text)
        {
            if (_target is not { } target)
                return;

            // Shift 只对字母生效，打一个字母就自动回落（与手机键盘一致）；
            // 数字/符号不受影响，避免"按了 Shift 后数字也变"的困惑。
            if (_shift && text.Length == 1 && char.IsLetter(text[0]))
            {
                text = char.ToUpperInvariant(text[0]).ToString();
                _shift = false;
                UpdateShiftVisual();
            }

            var current = target.EditText ?? string.Empty;
            var next = current + text;
            var max = target.MaxLength;

            if (max > 0 && next.Length > max)
                next = next[..max];

            SetText(next);
        }

        private void SetText(string text)
        {
            if (_target is not { } target)
                return;

            target.SetEditText(text);
            Refresh();
        }

        private static string RemoveLast(string text)
            => text.Length == 0 ? text : text[..^1];

        private static string ToggleSign(string text)
            => text.StartsWith("-", StringComparison.Ordinal) ? text[1..] : "-" + text;

        private void UpdateShiftVisual()
        {
            ShiftButton.FontWeight = _shift ? FontWeights.Bold : FontWeights.Normal;
            ShiftButton.Foreground = (Brush)FindResource(
                _shift ? "HmiAccentBrush" : "HmiTextSecondaryBrush");

            foreach (var button in TextPad.Children.OfType<Button>())
            {
                if (button.Tag is not string tag || tag.Length != 1 || !char.IsLetter(tag[0]))
                    continue;

                button.Content = _shift ? tag.ToUpperInvariant() : tag;
            }
        }
    }
}
