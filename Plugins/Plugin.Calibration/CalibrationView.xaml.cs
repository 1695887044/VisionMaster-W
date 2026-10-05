using Core.Interfaces;
using System.Windows;
using System.Windows.Controls;

namespace Plugin.Calibration
{
    /// <summary>
    /// 标定插件配置视图（DataContext = 插件实例自身：Plugin 与 ViewModel 合一）。
    ///
    /// 画布交互全部走控件既有通道（ActiveRoi / DrawObjectList / HTuples 回写），
    /// 「图上取点」是唯一的例外——<c>HalconBase.IsPickMode</c> 的 opt-in 通道
    /// （默认关、仅取点待命时开启），事件在本文件转交插件（事件无法走绑定）。
    /// </summary>
    public partial class CalibrationView : UserControl
    {
        private CalibrationPlugin? _plugin;

        public CalibrationView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 配置视图构造：灌值（Initialize）在构造里完成，与「图像采集」插件同一模式。
        /// </summary>
        public CalibrationView(IStepConfigData stepData, CalibrationPlugin plugin)
        {
            InitializeComponent();
            _plugin = plugin;
            DataContext = plugin;
            plugin.Initialize(stepData);
            Unloaded += (_, _) => { _plugin?.CancelPick(); _plugin = null; };
        }

        /// <summary>画布取点事件 → 插件（待命态下一次点击回填目标行 / A / B）</summary>
        private void OnImagePicked(object sender, Core.Halcon.Controls.HalconBase.ImagePickEventArgs e)
            => _plugin?.ApplyPickedPoint(e.Row, e.Column);

        /// <summary>Esc 取消取点待命（与"再点一次按钮取消"等效）</summary>
        private void OnViewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                _plugin?.CancelPick();
        }
    }
}
