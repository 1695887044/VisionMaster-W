using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 「变量赋值」的配置视图（见 VariableAssignmentView.xaml 顶部的设计说明）。
    ///
    /// 视图本身不写任何业务逻辑：数据来自 DataContext（即 VariableAssignmentPlugin）暴露的端口，
    /// 值的读写在 LinkableValueEditor 内部完成，点「确定」时由主程序调插件的 OnConfirm 回写 InputValues。
    ///
    /// 唯一的例外是「变量名」这一行：它要上报"用户动了这一行"这一个**界面事实**
    /// （用于解除老方案里残留的旧连线，见 NotifyNameEditedByUser 的注释）。
    /// 这件事只有视图知道——端口值变化既可能来自用户输入、也可能来自程序灌值（试运行桥接），
    /// 所以在插件的端口事件上判不出来。
    /// </summary>
    public partial class VariableAssignmentView : UserControl
    {
        public VariableAssignmentView()
        {
            InitializeComponent();

            // 只接真实的**用户输入**事件：键盘键入 / 退格删字符 / 剪切 / 粘贴 / 从下拉里选。
            // 程序灌值（候选重算后的回填、试运行把上游实际值桥接进端口）不会触发这些。
            NameBox.PreviewTextInput += (_, __) => NotifyNameEdited();
            NameBox.PreviewKeyDown += (_, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                // Ctrl+X 也走键位（PreviewTextInput 只覆盖"敲出来的字符"，剪切/粘贴是命令）
                if (e.Key == Key.Back || e.Key == Key.Delete || (ctrl && e.Key == Key.X))
                    NotifyNameEdited();
            };
            // 粘贴：键盘事件里 Ctrl+V 也是命令，得挂 TextBox 的 Pasting 附着事件（会从内部编辑器冒上来）
            NameBox.AddHandler(
                DataObject.PastingEvent,
                new DataObjectPastingEventHandler((_, __) => NotifyNameEdited()));
            // 右键菜单里的「剪切 / 删除」既没有键盘事件、也不走 Pasting —— 用命令预览执行钩子兜住
            // （只在**用户手势**触发命令时发；程序改文本不发，符合"用户意图只能由真实输入判定"）
            CommandManager.AddPreviewExecutedHandler(NameBox, (_, e) =>
            {
                if (ReferenceEquals(e.Command, ApplicationCommands.Cut)
                    || ReferenceEquals(e.Command, ApplicationCommands.Delete))
                    NotifyNameEdited();
            });
            // 拖拽落文同理（TextBox 内部会把 Drop 标 Handled，所以要 handledEventsToo: true 才收得到）
            NameBox.AddHandler(
                DragDrop.DropEvent,
                new DragEventHandler((_, __) => NotifyNameEdited()),
                handledEventsToo: true);
            NameBox.SelectionChanged += (_, e) =>
            {
                // 只在"选进来一项"时算编辑：候选重算会让 SelectedItem 变 null（那是 RemovedItems）
                if (e.AddedItems.Count > 0)
                    NotifyNameEdited();
            };
        }

        private void NotifyNameEdited()
            => (DataContext as VariableAssignmentPlugin)?.NotifyNameEditedByUser();
    }

    /// <summary>
    /// 作用域卡片（RadioButton.IsChecked，bool?）↔ Scope.Value（string）。
    ///
    /// 只做 UI 转换：选中卡片时写回 Runtime / Global；取消选中时返回 Binding.DoNothing，
    /// 让同组另一张卡片负责写入新值。业务侧仍然只认 Scope.Value 的字符串白名单。
    /// </summary>
    public sealed class ScopeEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? (parameter as string ?? Binding.DoNothing) : Binding.DoNothing;
    }

    /// <summary>
    /// Scope.Value → 中文显示名（运行时变量 / 全局变量）。
    /// 非空且不在白名单里的值显示「未知作用域（值）」——不静默显示成"运行时变量"，
    /// 与插件侧 CurrentScope 不再回退 Runtime 的口径一致。只读展示，不回写。
    /// </summary>
    public sealed class ScopeLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string scope = value as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(scope))
                return "未选择作用域";
            if (string.Equals(scope, VariableAssignmentPlugin.ScopeGlobal, StringComparison.OrdinalIgnoreCase))
                return "全局变量";
            if (string.Equals(scope, VariableAssignmentPlugin.ScopeRuntime, StringComparison.OrdinalIgnoreCase))
                return "运行时变量";
            return $"未知作用域（{scope}）";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 变量名候选行的显示文本：名字 + 声明类型。
    ///
    /// 输入是 [候选名字（string，来自 NameCandidates）, 插件的 NameCandidateItems]，
    /// 按名字找回同源展示项。找不到时原样回退名字 —— 候选行绝不会变成空白。
    /// 选择落下的值仍是名字字符串（ItemsSource 仍是 NameCandidates），不改变任何业务契约。
    /// </summary>
    public sealed class NameCandidateDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string name = values.Length > 0 ? values[0] as string ?? string.Empty : string.Empty;
            if (values.Length < 2
                || values[1] is not System.Collections.Generic.IEnumerable<VariableAssignmentPlugin.NameCandidateItem> items)
                return name;

            foreach (var item in items)
            {
                if (!string.Equals(item.Name, name, StringComparison.Ordinal))
                    continue;

                return string.IsNullOrWhiteSpace(item.TypeName)
                    ? item.Name
                    : $"{item.Name}    {item.TypeName}";
            }

            return name;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>变量名为空时显示占位文案；只读展示，不回写端口。</summary>
    public sealed class NameDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? "未命名变量" : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
