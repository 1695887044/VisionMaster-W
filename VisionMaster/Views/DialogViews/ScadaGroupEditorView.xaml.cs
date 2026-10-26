using System.Windows.Controls;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    public partial class ScadaGroupEditorView : UserControl
    {
        private bool _syncing;

        public ScadaGroupEditorView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 模板与自定义分成两个 ListBox（视觉上是两段），但选中项只有一个。
        /// <b>SelectedItem 必须 OneWay</b>：另一侧列表清空选中时会写回 null，
        /// 若用 TwoWay 就会把刚选中的组从 VM 里抹掉（症状：点自定义组，右侧表单立刻清空）。
        /// 用户点击这一侧负责写 VM，然后把另一侧的选中项清掉。
        /// </summary>
        private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            if (sender is not ListBox list || list.SelectedItem is not GroupRow row) return;
            if (DataContext is not ScadaGroupEditorViewModel vm) return;

            _syncing = true;
            try
            {
                vm.SelectedGroup = row;

                if (!ReferenceEquals(list, TemplateGroupList))
                    TemplateGroupList.SelectedItem = null;
                if (!ReferenceEquals(list, CustomGroupList))
                    CustomGroupList.SelectedItem = null;
            }
            finally
            {
                _syncing = false;
            }
        }
    }
}
