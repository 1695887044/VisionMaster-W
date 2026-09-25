using System.Windows.Controls;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 「变量赋值」的配置视图（见 VariableAssignmentView.xaml 顶部的设计说明）。
    ///
    /// 视图本身不写任何业务逻辑：数据来自 DataContext（即 VariableAssignmentPlugin）暴露的三个端口，
    /// 值的读写在 LinkableValueEditor 内部完成，点「确定」时由主程序调插件的 OnConfirm 回写 InputValues。
    /// 这样与其它插件的自定义视图是同一套约定。
    /// </summary>
    public partial class VariableAssignmentView : UserControl
    {
        public VariableAssignmentView()
        {
            InitializeComponent();
        }
    }
}
