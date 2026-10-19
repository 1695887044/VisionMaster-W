using System.Windows.Controls;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// IO 健康面板（UserControl）。
    ///
    /// <para>它不再是弹窗：宿主是 <see cref="SystemHealthView"/> 的「IO」页签，
    /// DataContext 由外壳显式给出（<c>{Binding Io}</c>）。所以这里**不能**写
    /// <c>prism:ViewModelLocator.AutoWireViewModel</c>——那会让 Prism 另建一份 VM 覆盖外壳的。</para>
    /// </summary>
    public partial class IoHealthPanelView : UserControl
    {
        public IoHealthPanelView()
        {
            InitializeComponent();
        }
    }
}
