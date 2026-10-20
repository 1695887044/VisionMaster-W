using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「系统设置」弹窗：运行窗口形态 / 落屏、安全与会话、历史留档、画布（图集）。
    ///
    /// 视图本身无逻辑——字段清单在 <see cref="Models.SystemSettingsDraft"/> 上（反射生成），
    /// 读写配置、校验、审计、取消语义全在
    /// <see cref="ViewModels.DialogViewModels.ScadaSystemSettingsViewModel"/> 里。
    /// 这个文件存在的唯一理由是"确定/取消/错误条需要一个宿主"：
    /// <c>EasyDialog.ShowPropertyGridSync</c> 自己建窗口、自己收口成 bool，
    /// 写盘失败时没有地方把原因留在屏幕上。
    /// </summary>
    public partial class ScadaSystemSettingsView : UserControl
    {
        public ScadaSystemSettingsView()
        {
            InitializeComponent();
        }
    }
}
