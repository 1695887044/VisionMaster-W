using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 「变量赋值」的配置视图（见 VariableAssignmentView.xaml 顶部的设计说明）。
    ///
    /// 视图本身不写任何业务逻辑：数据来自 DataContext（即 VariableAssignmentPlugin）暴露的三个端口，
    /// 值的读写在 LinkableValueEditor 内部完成，点「确定」时由主程序调插件的 OnConfirm 回写 InputValues。
    /// 这样与其它插件的自定义视图是同一套约定。
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
}
