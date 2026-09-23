using System.Windows.Controls;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 「事件 + 动作表」编辑器（公共控件）：勾选框 + 动作表。
    /// 视图本身无逻辑——勾选语义、增删改序、选变量/选画面全在
    /// <see cref="ViewModels.ScadaEventEditorViewModel"/> 里。
    /// DataContext 由使用方给（属性面板事件行给 <c>ScadaEventRow.Editor</c>，
    /// 变量事件弹窗给当前选中变量的那一组编辑器）。
    /// </summary>
    public partial class ScadaEventEditor : UserControl
    {
        public ScadaEventEditor()
        {
            InitializeComponent();
        }
    }
}
