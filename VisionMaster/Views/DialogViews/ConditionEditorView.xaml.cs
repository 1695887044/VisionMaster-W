using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// ConditionEditorView.xaml 的交互逻辑
    /// </summary>
    public partial class ConditionEditorView : UserControl
    {
        public ConditionEditorView()
        {
            InitializeComponent();
        }

        // 1. 处理双击字典变量
        private void VarTreeView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 获取当前点击的 UI 元素
            var dependencyObject = e.OriginalSource as DependencyObject;
            if (dependencyObject == null) return;

            // 1. 向上寻找被点击的 TreeViewItem
            TreeViewItem item = GetVisualParent<TreeViewItem>(dependencyObject);
            if (item == null) return;

            // 2. 检查点击的是不是第二层（输出端口 PortSchema）
            // 如果点击的是第一层（NodeLinkViewModel本身），我们不响应
            if (item.DataContext is PortSchema portData)
            {
                // 3. 继续向上寻找它的父级 TreeViewItem
                TreeViewItem parentItem = GetVisualParent<TreeViewItem>(item);
                if (parentItem != null && parentItem.DataContext is NodeLinkViewModel nodeData)
                {
                    // 🌟 完美拼接出：StepID.PortName (例如 Delay_0.Time 或 Global.Score)
                    string insertText = $"{nodeData.StepID}.{portData.Name}";

                    InsertTextAtCursor(insertText);
                    e.Handled = true; // 阻止事件冒泡
                }
            }
        }

        // 这是一个非常经典的 WPF 视觉树往上找父级的通用方法，直接贴进去用
        private static T GetVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;

            if (parentObject is T parent)
                return parent;
            else
                return GetVisualParent<T>(parentObject);
        }

        // 2. 处理点击符号按钮
        private void OperatorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string symbol = btn.Content.ToString();
                InsertTextAtCursor(symbol);
            }
        }

        // 3. 核心工具方法：在光标处安全插入文本
        private void InsertTextAtCursor(string text)
        {
            // 如果是括号，特殊处理：让光标跳到括号中间，体验拉满！
            if (text.Trim() == "()")
            {
                ExpressionTextBox.SelectedText = "()";
                ExpressionTextBox.CaretIndex -= 1; // 光标后退一格
            }
            else
            {
                // 替换当前选中的文本，或者在光标位置插入
                ExpressionTextBox.SelectedText = text;
                // 将光标移到插入内容的末尾
                ExpressionTextBox.CaretIndex += text.Length;
            }

            // 🌟 必须手动触发绑定更新，因为 SelectedText 的修改不会立刻触发 Text 的双向绑定
            var bindingExpression = ExpressionTextBox.GetBindingExpression(TextBox.TextProperty);
            bindingExpression?.UpdateSource();

            // 保持焦点在输入框，方便用户继续手敲数字
            ExpressionTextBox.Focus();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
          
            
        }
    }
}
