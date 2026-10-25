using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    public partial class RecipeManagerView : UserControl
    {
        public RecipeManagerView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 候选表的勾选格：整格可点。
        ///
        /// 为什么不让 CheckBox 自己吃掉这次点击：勾选框只有 13px，行高 32px，
        /// 鼠标落在格子里任意位置都该切换（同类表格的通行手感）。
        /// 这里负责把"格子里除 CheckBox 本体以外的点击"补上；
        /// 点在 CheckBox 上时它自己会处理，再切一次就等于没动——所以判断命中的是 CheckBox 就跳过。
        ///
        /// 不可勾选的行（IsDisabled）在 <see cref="RecipeCaptureCandidate.IsChecked"/> 的 setter 里
        /// 已经是"点也不翻"，这里不重复判一次，避免两处规则各说各话。
        /// </summary>
        private void OnCaptureCheckCellClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: RecipeCaptureCandidate row }) return;
            if (e.OriginalSource is DependencyObject origin && IsInsideCheckBox(origin)) return;

            row.IsChecked = !row.IsChecked;
            e.Handled = true;
        }

        private static bool IsInsideCheckBox(DependencyObject? node)
        {
            for (var current = node; current != null; current = LogicalTreeHelper.GetParent(current) ?? System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is CheckBox) return true;
                if (current is Border) return false;   // 走到格子边界就停，避免顺着逻辑树爬到别人的勾选框
            }

            return false;
        }

        /// <summary>
        /// 「配方值」格：单击就把焦点交给里面的输入框，等于"一点就能改"，
        /// 不用先选中行再按 F2（DataGrid 是本只读样式，那套快捷键对模板列不生效）。
        ///
        /// 不可改的行（无权限/运行中/目标已删除）没有输入框可进，这里什么都不做，
        /// 原因已经挂在悬停提示上。
        /// </summary>
        private void OnValueCellClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: RecipeParameterRow { CanEditValue: true } } cell) return;
            if (e.OriginalSource is DependencyObject origin && IsInsideTextBox(origin)) return;   // 已经点进框里了

            if (FindDescendant<TextBox>(cell) is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        }

        /// <summary>回车提交、Esc 放弃本次编辑（Esc 直接把字顶回服务端那份）。</summary>
        private void OnValueBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: RecipeParameterRow row } box) return;

            switch (e.Key)
            {
                case Key.Enter:
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    Commit(row);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                    row.RecipeValue = row.CommittedRecipeValue;
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>焦点离开即提交——点了别处也算"我改好了"，免得改完忘记回车。</summary>
        private void OnValueBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { DataContext: RecipeParameterRow row } box) return;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Commit(row);
        }

        private void Commit(RecipeParameterRow row)
        {
            if (DataContext is RecipeManagerViewModel vm)
                vm.CommitParameterValue(row);
        }

        private static bool IsInsideTextBox(DependencyObject? node)
        {
            for (var current = node; current != null; current = LogicalTreeHelper.GetParent(current) ?? System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is TextBox) return true;
                if (current is Grid) return false;
            }

            return false;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                if (FindDescendant<T>(child) is { } nested) return nested;
            }

            return null;
        }

        /// <summary>
        /// 参数表勾选列：整格可点。
        ///
        /// 为什么挂在 DataGrid 的 MouseLeftButtonUp 而不是 SelectingCells：
        /// DataGrid 没有 SelectingCells 事件（那是 DataGridCell 的），带这种属性名会 MC3072 直接编译失败。
        /// 为什么不是给 CheckBox 再挂 Click：那个 CheckBox 已经绑了 <c>IsChecked</c> TwoWay，
        /// 点它自己就会翻一次，再挂 Click 就是翻两次等于没动——所以只补"格子里除 CheckBox 本体以外的点击"，
        /// 与候选表 <see cref="OnCaptureCheckCellClicked"/> 同一套规则。
        ///
        /// 只认第 0 列且点在勾选格里；配方值列有自己的 <see cref="OnValueCellClicked"/>，
        /// 两个处理器靠"哪一格"互斥，不会互相干扰。
        /// </summary>
        private void OnParameterRowClicked(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject origin) return;
            if (e.ClickCount != 1) return;

            var cell = AncestorOf<DataGridCell>(origin);
            if (cell is null || cell.Column is null || cell.Column.DisplayIndex != 0) return;
            if (cell.DataContext is not RecipeParameterRow row) return;
            if (FindDescendant<CheckBox>(cell) is { } box && IsDescendantOf(origin, box)) return;

            row.IsChecked = !row.IsChecked;
            e.Handled = true;
        }

        private static T? AncestorOf<T>(DependencyObject? node) where T : DependencyObject
        {
            for (var current = node; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is T hit) return hit;
            }

            return null;
        }

        private static ContentPresenter? PresenterFromRow(DataGridRow? row)
            => row is null ? null : FindDescendant<ContentPresenter>(row);

        private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
        {
            for (var current = node; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }

            return false;
        }
    }
}
