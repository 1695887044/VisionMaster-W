using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada;
using VisionMaster.ViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 文本列表管理器（P1-1）。内容区由 EasyDialog 承载，确认后调用 <see cref="Commit"/>。
    ///
    /// 删除列表的二次确认留在视图层：它要弹 MessageBox，而视图模型必须能在
    /// 无窗口的断言环境里跑；视图模型只提供“引用数”和“删掉”两个纯动作。
    /// </summary>
    public partial class StateListManagerView : UserControl
    {
        private readonly StateListManagerViewModel _viewModel;

        public StateListManagerView(ScadaDocument document)
        {
            InitializeComponent();
            _viewModel = new StateListManagerViewModel(document);
            DataContext = _viewModel;
        }

        /// <summary>确认后写回文档（由调用方在 EasyDialog 返回 true 后调用）</summary>
        public void Commit() => _viewModel.Commit();

        private void OnAddListClick(object sender, RoutedEventArgs e) => _viewModel.AddList();

        private void OnRemoveListClick(object sender, RoutedEventArgs e)
        {
            var list = _viewModel.SelectedList;
            if (list == null)
                return;

            var references = _viewModel.CountReferences(list.StateListId);
            var message = references == 0
                ? $"删除文本列表「{list.Name}」？"
                : $"文本列表「{list.Name}」正被 {references} 个控件引用。\n" +
                  "删除后这些控件会显示「未找到文本列表」。继续？";

            var owner = Window.GetWindow(this);
            var result = owner == null
                ? MessageBox.Show(message, "删除文本列表", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                : MessageBox.Show(owner, message, "删除文本列表", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            if (result == MessageBoxResult.OK)
                _viewModel.RemoveSelectedList();
        }

        private void OnAddEntryClick(object sender, RoutedEventArgs e) => _viewModel.AddEntry();

        private void OnRemoveEntryClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ScadaStateEntry entry)
                _viewModel.RemoveEntry(entry);
        }
    }
}
