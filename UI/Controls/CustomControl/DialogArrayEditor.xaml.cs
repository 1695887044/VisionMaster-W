using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace UI.CustomControl
{
    /// <summary>
    /// 数组元素编辑器：一串可增删改、可上下移的文本值。
    ///
    /// 用法（宿主侧只做"字符串 ↔ 真实类型"的转换，编辑本身交给本控件）：
    /// <code>
    /// var editor = new DialogArrayEditor { ElementTypeName = "Int32", Hint = "..." };
    /// editor.Load(new[] { "1", "2" });
    /// if (EasyDialog.ShowSync("编辑数组", editor))
    ///     foreach (var text in editor.Values) { /* Convert.ChangeType ... */ }
    /// </code>
    ///
    /// 为什么这里只处理字符串：控件在 UI 库，不认识任何业务类型（数组元素可能是 int/double/string/bool...），
    /// 把"字符串解析成目标类型"留在宿主，控件就不会因为多一种元素类型而改动。
    /// </summary>
    public partial class DialogArrayEditor : UserControl
    {
        /// <summary>元素列表（就地编辑：界面上改一个值等于改这里一项）</summary>
        public ObservableCollection<ArrayEditorItem> Items { get; } = new();

        public DialogArrayEditor()
        {
            InitializeComponent();
            DataContext = this;
            Items.CollectionChanged += (_, _) => RefreshState();
            RefreshState();
        }

        /// <summary>元素类型名（仅用于界面提示，如 "Int32" / "String"）</summary>
        public static readonly DependencyProperty ElementTypeNameProperty =
            DependencyProperty.Register(nameof(ElementTypeName), typeof(string), typeof(DialogArrayEditor),
                new PropertyMetadata(string.Empty));

        public string ElementTypeName
        {
            get => (string)GetValue(ElementTypeNameProperty);
            set => SetValue(ElementTypeNameProperty, value);
        }

        /// <summary>顶部说明文案（例如"超出部分会被截断"这类业务提醒）</summary>
        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint), typeof(string), typeof(DialogArrayEditor),
                new PropertyMetadata(string.Empty));

        public string Hint
        {
            get => (string)GetValue(HintProperty);
            set => SetValue(HintProperty, value);
        }

        /// <summary>用现有元素填充（null 视为空串，编辑框不接受 null）</summary>
        public void Load(IEnumerable<string?> values)
        {
            Items.Clear();
            if (values != null)
            {
                foreach (var v in values)
                    Items.Add(CreateItem(v ?? string.Empty));
            }
            RefreshState();
        }

        /// <summary>取回当前元素（顺序即列表顺序）</summary>
        public IReadOnlyList<string> Values => Items.Select(i => i.Value ?? string.Empty).ToList();

        private ArrayEditorItem CreateItem(string value) => new() { Value = value };

        private void OnAddClick(object sender, RoutedEventArgs e)
        {
            Items.Add(CreateItem(string.Empty));
            RefreshState();
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (TryGetItem(sender, out var item)) Items.Remove(item);
        }

        private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(sender, -1);

        private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(sender, 1);

        private void Move(object sender, int delta)
        {
            if (!TryGetItem(sender, out var item)) return;

            int from = Items.IndexOf(item);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= Items.Count) return;

            Items.Move(from, to);
            RefreshState();
        }

        private static bool TryGetItem(object sender, out ArrayEditorItem item)
        {
            item = (sender as FrameworkElement)?.DataContext as ArrayEditorItem;
            return item != null;
        }

        /// <summary>
        /// 重编下标 + 刷新空状态与计数。
        /// 下标必须每次变更后重算：删掉 [0] 之后原来的 [1] 必须变成 [0]，
        /// 否则界面上显示的编号与变量表里 Children 的编号会对不上（那两处都靠位置索引）。
        /// </summary>
        private void RefreshState()
        {
            for (int i = 0; i < Items.Count; i++)
                Items[i].DisplayIndex = $"[{i}]";

            EmptyHint.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CountText.Text = $"共 {Items.Count} 个元素";
        }
    }

    /// <summary>
    /// 编辑器里的一行。
    /// 自己实现 INotifyPropertyChanged 而不是用 Prism 的 BindableBase：UI 库不依赖 Prism，
    /// 而这里要通知的只有一个下标（Value 由界面单向写回，不需要反向通知）。
    /// </summary>
    public class ArrayEditorItem : INotifyPropertyChanged
    {
        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set => _value = value ?? string.Empty;
        }

        private string _displayIndex = string.Empty;
        public string DisplayIndex
        {
            get => _displayIndex;
            set
            {
                if (_displayIndex == value) return;
                _displayIndex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayIndex)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
