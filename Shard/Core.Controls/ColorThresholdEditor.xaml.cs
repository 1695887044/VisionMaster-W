using System.Windows.Controls;

namespace Core.Controls
{
    /// <summary>
    /// 颜色判据阈值编辑器（共用控件）。
    ///
    /// 视图里不写任何逻辑：参数值靠 DataContext 继承 + 按属性名绑定，
    /// 所以任何暴露了同名属性的颜色算子都能直接嵌它，不需要接口也不需要泛型。
    /// 属性含义见 Core.Halcon\Color\ColorThresholds.cs。
    /// </summary>
    public partial class ColorThresholdEditor : UserControl
    {
        public ColorThresholdEditor()
        {
            InitializeComponent();
        }
    }
}
