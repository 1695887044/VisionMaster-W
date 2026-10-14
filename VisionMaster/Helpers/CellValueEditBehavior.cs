using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.Models;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 变量表「当前值」单元格的提交式编辑行为（挂在单元格 TextBox 上，Active="True" 启用）：
    ///
    /// - 获得键盘焦点 → 节点进入编辑态（轮询/流程的值推送暂停，输入中的草稿不被冲掉）；
    /// - 回车或失去键盘焦点 → 提交：VariableValueConverter 转换 + IWritableVariable.TryWrite
    ///   （本地=内存写，网络=下发设备）。成功退出编辑态、单元格回读模型值；
    ///   失败红框并承载中文原因，保持编辑态等用户修正（模型未被污染）；
    /// - Esc → 放弃草稿，回读模型值。
    ///
    /// 为什么不用 TwoWay 绑定提交：网络变量的写入是设备 I/O，"每个键击都下发"不可接受；
    /// 而轮询推送又要求"编辑中暂停回读"——两条都指向显式提交，绑定的 LostFocus 触发器
    /// 在焦点让渡顺序上还与"暂停推送"的复位存在竞态。
    /// 提交互契约（进入/提交/放弃）在 <see cref="VariableNode"/> 上，本行为只负责把 WPF
    /// 输入事件翻译过去并处理校验红框。
    /// </summary>
    public static class CellValueEditBehavior
    {
        public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
            "Active", typeof(bool), typeof(CellValueEditBehavior), new PropertyMetadata(false, OnActiveChanged));

        public static bool GetActive(DependencyObject obj) => (bool)obj.GetValue(ActiveProperty);
        public static void SetActive(DependencyObject obj, bool value) => obj.SetValue(ActiveProperty, value);

        private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox box) return;

            if ((bool)e.NewValue)
            {
                box.GotKeyboardFocus += OnGotKeyboardFocus;
                box.LostKeyboardFocus += OnLostKeyboardFocus;
                box.PreviewKeyDown += OnPreviewKeyDown;
            }
            else
            {
                box.GotKeyboardFocus -= OnGotKeyboardFocus;
                box.LostKeyboardFocus -= OnLostKeyboardFocus;
                box.PreviewKeyDown -= OnPreviewKeyDown;
            }
        }

        private static VariableNode? NodeOf(TextBox box) => box.DataContext as VariableNode;

        private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var box = (TextBox)sender;
            ClearInvalid(box);
            NodeOf(box)?.BeginEditValue();
        }

        private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Commit((TextBox)sender);
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;
            if (e.Key == Key.Enter)
            {
                Commit(box);
                e.Handled = true; // 单行框吞掉回车：提交但不让焦点跑掉，方便连续微调
            }
            else if (e.Key == Key.Escape)
            {
                ClearInvalid(box);
                NodeOf(box)?.CancelEditValue();
                e.Handled = true;
            }
        }

        private static void Commit(TextBox box)
        {
            var node = NodeOf(box);
            if (node == null || !node.IsEditingValue) return;

            var error = node.CommitEditValue(box.Text);
            if (error == null)
            {
                ClearInvalid(box);

                // 提交成功立刻交回焦点（回车路径焦点还在框上）：LostKeyboardFocus 会再进
                // Commit，此时已退出编辑态、直接返回；视觉上随 DialogCellInputQuiet 的
                // 透明背景回到"读取模式"，操作员一眼能看出"这次写入已经结束"
                if (box.IsKeyboardFocused)
                    Keyboard.ClearFocus();
                return;
            }

            // 失败如实留痕：红框 + 错误内容（模型未被污染），保持编辑态等用户修正或 Esc 放弃
            MarkInvalid(box, error);
        }

        private static void MarkInvalid(TextBox box, string error)
        {
            var binding = box.GetBindingExpression(TextBox.TextProperty);
            if (binding == null) return;

            var validationError = new ValidationError(new ExceptionValidationRule(), binding)
            {
                ErrorContent = error,
            };
            Validation.MarkInvalid(binding, validationError);
        }

        private static void ClearInvalid(TextBox box)
        {
            var binding = box.GetBindingExpression(TextBox.TextProperty);
            if (binding != null && binding.HasError)
                Validation.ClearInvalid(binding);
        }
    }
}
