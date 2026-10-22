using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [SK] 软键盘：数字/字符两套布局、提交失败不关、取消/清除/退格/正负号。
        ///
        /// 只测键盘自己的拼字与目标调用；输入控件的校验/写变量仍走 TextField/IOField 原有路径
        /// （见 CB 段与 Phase2UiChecks），这里用一个假目标把两条分支都走一遍。
        /// </summary>
        private static class ScadaSoftKeyboardChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("软键盘（数字 / 字符）");

                var keyboard = new ScadaSoftKeyboard();
                var target = new FakeKeyboardTarget
                {
                    IsNumeric = true,
                    EditText = "12.5",
                    UnitText = "ms",
                    RangeText = "范围 1–100",
                    OriginalText = "10.0",
                };

                keyboard.Open(target);

                Check("软键盘①数字布局打开：显示当前值/单位/范围/原值，数字键盘可见、字符键盘收起",
                    keyboard.IsOpen
                    && target.EditText == "12.5"
                    && (keyboard.FindName("NumericPad") as Grid)?.Visibility == Visibility.Visible
                    && (keyboard.FindName("TextPad") as Grid)?.Visibility == Visibility.Collapsed
                    && (keyboard.FindName("MetaText") as TextBlock)?.Text.Contains("范围 1–100", StringComparison.Ordinal) == true,
                    (keyboard.FindName("MetaText") as TextBlock)?.Text);

                keyboard.PressKey("7");

                Check("软键盘②数字键追加到末尾，显示文本跟着 DP/绑定更新",
                    target.EditText == "12.57"
                    && (keyboard.FindName("DisplayText") as TextBlock)?.Text == "12.57",
                    $"{target.EditText} / {(keyboard.FindName("DisplayText") as TextBlock)?.Text}");

                keyboard.PressKey("back");
                keyboard.PressKey("clear");
                keyboard.PressKey("1");
                keyboard.PressKey(".");
                keyboard.PressKey("5");
                keyboard.PressKey("sign");

                Check("软键盘③退格/清除/小数点/正负号按数字键盘语义工作",
                    target.EditText == "-1.5",
                    target.EditText);

                keyboard.PressKey("ok");

                Check("软键盘④确定走目标 CommitEdit：成功后目标退出编辑、键盘收起",
                    target.CommitCalls == 1
                    && !target.IsEditing
                    && !keyboard.IsOpen,
                    $"commit={target.CommitCalls} / editing={target.IsEditing} / open={keyboard.IsOpen}");

                var failing = new FakeKeyboardTarget
                {
                    IsNumeric = true,
                    EditText = "150",
                    FailCommit = true,
                };

                keyboard.Open(failing);
                keyboard.PressKey("ok");

                Check("软键盘⑤提交失败：键盘不关、目标仍在编辑态、错误原文上屏",
                    failing.CommitCalls == 1
                    && failing.IsEditing
                    && keyboard.IsOpen
                    && (keyboard.FindName("ErrorText") as TextBlock)?.Text == "超出范围 1–100"
                    && (keyboard.FindName("ErrorText") as TextBlock)?.Visibility == Visibility.Visible,
                    (keyboard.FindName("ErrorText") as TextBlock)?.Text);

                keyboard.PressKey("esc");

                Check("软键盘⑥Esc 走目标 CancelEdit：退出编辑、清错误、键盘收起",
                    failing.CancelCalls == 1
                    && !failing.IsEditing
                    && !keyboard.IsOpen,
                    $"cancel={failing.CancelCalls} / editing={failing.IsEditing}");

                var textTarget = new FakeKeyboardTarget
                {
                    IsNumeric = false,
                    EditText = string.Empty,
                    MaxLength = 3,
                };

                keyboard.Open(textTarget);
                keyboard.PressKey("shift");
                keyboard.PressKey("a");
                keyboard.PressKey("b");
                keyboard.PressKey("c");
                keyboard.PressKey("d");

                Check("软键盘⑦字符布局：Shift 打首字母大写，长度到上限后不再追加",
                    textTarget.EditText == "Abc"
                    && (keyboard.FindName("TextPad") as Grid)?.Visibility == Visibility.Visible
                    && (keyboard.FindName("NumericPad") as Grid)?.Visibility == Visibility.Collapsed,
                    textTarget.EditText);

                keyboard.PressKey("space");
                keyboard.PressKey("clear");
                keyboard.PressKey("x");
                keyboard.PressKey("@");
                keyboard.PressKey("esc");

                Check("软键盘⑧字符布局的空格/清除/常用符号/Esc 走同一套目标接口",
                    textTarget.EditText == "x@"
                    && textTarget.CancelCalls == 1
                    && !keyboard.IsOpen,
                    $"{textTarget.EditText} / cancel={textTarget.CancelCalls}");

                var below = ScadaSoftKeyboard.ComputePlacement(
                    new Rect(100, 100, 200, 50), new Size(300, 200), new Size(800, 600));
                var above = ScadaSoftKeyboard.ComputePlacement(
                    new Rect(100, 450, 200, 50), new Size(300, 200), new Size(800, 600));
                var clamped = ScadaSoftKeyboard.ComputePlacement(
                    new Rect(0, 0, 10, 10), new Size(500, 400), new Size(400, 300));

                Check("软键盘⑨摆放纯函数：下方优先 / 放不下翻上方 / 夹回画面区",
                    below == new Point(50, 158)
                    && above == new Point(50, 242)
                    && clamped == new Point(8, 8),
                    $"below={below} / above={above} / clamped={clamped}");

                var longTarget = new FakeKeyboardTarget
                {
                    IsNumeric = false,
                    EditText = new string('x', 400),
                    MaxLength = 512,
                };

                keyboard.Open(longTarget);
                keyboard.MaxWidth = 300;
                keyboard.Measure(new Size(300, 1000));

                Check("软键盘⑩长文本不撑宽：MaxWidth 约束下键盘宽度不超过可用宽度",
                    keyboard.DesiredSize.Width <= 300.5
                    && keyboard.DesiredSize.Width > 0,
                    keyboard.DesiredSize.ToString());

                keyboard.Close();

                var hexField = new IOFieldElement { FormatType = IOFieldElement.FormatHex };
                var decimalField = new IOFieldElement { FormatType = IOFieldElement.FormatDecimal };

                Check("软键盘11十六进制数值域走字符键盘（a-f 可输入），十进制仍走数字键盘",
                    !((IScadaSoftKeyboardTarget)hexField).IsNumeric
                    && ((IScadaSoftKeyboardTarget)decimalField).IsNumeric,
                    $"hex={((IScadaSoftKeyboardTarget)hexField).IsNumeric} / decimal={((IScadaSoftKeyboardTarget)decimalField).IsNumeric}");
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("软键盘 UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }

            private sealed class FakeKeyboardTarget : IScadaSoftKeyboardTarget, INotifyPropertyChanged
            {
                private string? _editText;
                private string? _editError;
                private bool _isEditing = true;

                public bool IsEditing
                {
                    get => _isEditing;
                    set
                    {
                        if (_isEditing == value) return;
                        _isEditing = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
                    }
                }

                public bool IsEditable => true;

                public string? EditText
                {
                    get => _editText;
                    set
                    {
                        if (_editText == value) return;
                        _editText = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditText)));
                    }
                }

                public string? EditError
                {
                    get => _editError;
                    set
                    {
                        if (_editError == value) return;
                        _editError = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditError)));
                    }
                }

                public bool IsNumeric { get; set; }

                public int MaxLength { get; set; } = 32;

                public string? UnitText { get; set; }

                public string? RangeText { get; set; }

                public string? OriginalText { get; set; }

                public int CommitCalls { get; private set; }

                public int CancelCalls { get; private set; }

                public bool FailCommit { get; set; }

                public event PropertyChangedEventHandler? PropertyChanged;

                public void SetEditText(string text) => EditText = text;

                public void CommitEdit()
                {
                    CommitCalls++;

                    if (FailCommit)
                    {
                        EditError = "超出范围 1–100";
                        return;
                    }

                    IsEditing = false;
                    EditError = null;
                }

                public void CancelEdit()
                {
                    CancelCalls++;
                    IsEditing = false;
                    EditError = null;
                }
            }
        }
    }
}
