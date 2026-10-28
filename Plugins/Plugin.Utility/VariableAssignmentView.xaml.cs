using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Plugin.Util.Models;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 「变量赋值」v3 配置视图。
    ///
    /// 视图本身不写业务逻辑：数据来自 DataContext（插件实例）与行视图模型（AssignmentRowVm）。
    /// 唯一的例外是「用户真的动了某一行的名字」这一个界面事实：
    ///   - 端口值变化既可能来自用户输入、也可能来自程序灌值（试运行桥接 / 候选重填），
    ///     所以只能在真实输入事件上判（R35）；
    ///   - 迁移行残留的旧 Name 连线只在用户动手时解除，避免误改活模型。
    /// </summary>
    public partial class VariableAssignmentView : UserControl
    {
        public VariableAssignmentView()
        {
            InitializeComponent();

            // 行列表里没有固定的 NameBox：在视图级挂真实输入事件，再沿可视树上溯到 Tag="NameEditor" 的 ComboBox。
            AddHandler(TextBox.PreviewTextInputEvent, new TextCompositionEventHandler(OnNamePreviewTextInput), true);
            AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnNamePreviewKeyDown), true);
            AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler(OnNamePasting), true);
            CommandManager.AddPreviewExecutedHandler(this, OnCommandPreviewExecuted);
            AddHandler(DragDrop.DropEvent, new DragEventHandler(OnNameDrop), true);
            AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(OnNameSelectionChanged), true);

            SizeChanged += OnViewSizeChanged;
        }

        private VariableAssignmentPlugin? Plugin => DataContext as VariableAssignmentPlugin;

        private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Plugin != null)
                Plugin.IsNarrow = ActualWidth < 700;
        }

        private static ComboBox? FindNameEditor(object? source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is ComboBox combo
                    && string.Equals(combo.Tag as string, "NameEditor", StringComparison.Ordinal))
                    return combo;

                current = current is Visual || current is Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        private void NotifyNameEdited(object? source)
        {
            var combo = FindNameEditor(source);
            if (combo?.DataContext is AssignmentRowVm vm)
                Plugin?.NotifyRowNameEditedByUser(vm.Row.RowId);
        }

        private void OnNamePreviewTextInput(object sender, TextCompositionEventArgs e)
            => NotifyNameEdited(e.OriginalSource);

        private void OnNamePreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Back || e.Key == Key.Delete || (ctrl && e.Key == Key.X))
                NotifyNameEdited(e.OriginalSource);
        }

        private void OnNamePasting(object sender, DataObjectPastingEventArgs e)
            => NotifyNameEdited(e.OriginalSource);

        private void OnCommandPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (ReferenceEquals(e.Command, ApplicationCommands.Cut)
                || ReferenceEquals(e.Command, ApplicationCommands.Delete))
                NotifyNameEdited(e.OriginalSource);
        }

        private void OnNameDrop(object sender, DragEventArgs e)
            => NotifyNameEdited(e.OriginalSource);

        private void OnNameSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;

            var combo = FindNameEditor(e.OriginalSource);
            if (combo == null || !combo.IsDropDownOpen) return;

            if (combo.DataContext is AssignmentRowVm vm)
                Plugin?.NotifyRowNameEditedByUser(vm.Row.RowId);
        }

        private void OnRowsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && sender is ListBox list)
                list.ScrollIntoView(e.AddedItems[0]);
        }
    }

    /// <summary>bool → Visibility 的反相版本（空态/未选中态用）。</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
