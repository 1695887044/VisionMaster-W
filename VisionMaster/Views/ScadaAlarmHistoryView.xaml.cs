using System;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace VisionMaster.Views
{
    /// <summary>
    /// 报警历史抽屉的交互逻辑。
    ///
    /// 这个类里只做三件事，其余全归 <see cref="ScadaAlarmHistoryViewModel"/>：
    /// ① 造视图模型（本面板的视图模型要注入"二次确认"这个口，所以不能在 XAML 里自动装配）；
    /// ② 两个够不着视图模型的界面动作——弹保存对话框、收起面板；
    /// ③ 转交宿主交来的运行态上下文（<see cref="Attach"/>）。
    ///
    /// 为什么 DataContext 由构造期手工设，不写 <c>prism:ViewModelLocator.AutoWireViewModel</c>
    /// ---------
    /// 自动装配只能调无参构造，而本视图模型必须拿到"要问用户的那句话由谁弹"——那是视图层的活
    /// （<see cref="MessageBox"/> 在断言宿主里根本不存在）。手工设一次，比给视图模型开一个
    /// 可空的静态回调口子干净。
    ///
    /// 为什么"导出"不绑命令、直接写 Click
    /// ---------
    /// 保存对话框是 WPF 的 <c>Microsoft.Win32.SaveFileDialog</c>，它<b>不该</b>出现在视图模型里
    /// （视图模型要能在无窗口的断言环境里跑，而那里弹不出对话框）。所以按钮走 Click，
    /// 拿到路径之后再把"纯活"交给 <see cref="ScadaAlarmHistoryViewModel.Export"/>——
    /// 于是"选路径"与"写文件"两件事各自可测。
    /// </summary>
    public partial class ScadaAlarmHistoryView : UserControl
    {
        /// <summary>本面板的视图模型（<see cref="DataContext"/> 就是它，见构造函数）</summary>
        private readonly ScadaAlarmHistoryViewModel _viewModel;

        public ScadaAlarmHistoryView()
        {
            InitializeComponent();

            // Dispatcher 传自己的而不是 Application.Current.Dispatcher：断言宿主里没有 Application，
            // 而本面板的重建与节拍都必须落在它所在的那条 UI 线程上。
            _viewModel = new ScadaAlarmHistoryViewModel(Dispatcher, Confirm);
            DataContext = _viewModel;
        }

        /// <summary>面板的视图模型。窗口顶条的未确认徽标按 ElementName 绑它，宿主也可读</summary>
        public ScadaAlarmHistoryViewModel ViewModel => _viewModel;

        /// <summary>面板当前是展开的吗</summary>
        public bool IsOpen => Visibility == Visibility.Visible;

        /// <summary>
        /// 展开面板。
        ///
        /// 这里<b>不</b>重读历史：视图模型的订阅在收起期间照旧挂着（顶条的未确认徽标就靠它亮着），
        /// 所以表一直是新的。收起时也重建一次，等于把同一件事做两遍。
        /// </summary>
        public void Open() => Visibility = Visibility.Visible;

        /// <summary>收起面板。报警照常记录，只是不看它</summary>
        public void Close() => Visibility = Visibility.Collapsed;

        /// <summary>展开 ↔ 收起（顶条按钮用）</summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>
        /// 转交运行态上下文。<paramref name="context"/> 为 null = 退挂。
        ///
        /// 由 <see cref="ScadaRuntimeWindow"/> 调用（窗口 Loaded 时挂上、Closed 时摘掉）：
        /// 面板自己不去 <c>Window.GetWindow(this)</c> 上溯找画布——那是"面板恰好住在一个有画布的窗口里"
        /// 这种偶然耦合，换个宿主就断。由宿主显式转交，契约是"你给我上下文，其余别管"。
        /// </summary>
        public void Attach(ScadaRuntimeContext? context) => _viewModel.Attach(context);

        /// <summary>
        /// "全部确认"的二次确认口。注入给视图模型，由它在真正动手之前调一次。
        ///
        /// 用 <see cref="MessageBoxImage.Warning"/> 而不是 Question：确认是不可撤销的动作，
        /// 图标该提示的是"注意"，不是"随便问问"。默认按钮落在"取消"一侧（OKCancel 的默认即如此），
        /// 手快连敲回车不会把一整批报警确认掉。
        /// </summary>
        private bool Confirm(string message)
        {
            const string title = "确认报警";

            // owner 可能为 null（控件还没进可视树时）——MessageBox 的 owner 重载不收 null，得分开写。
            var owner = Window.GetWindow(this);
            var result = owner == null
                ? MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                : MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            return result == MessageBoxResult.OK;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// 导出当前列表（已筛选的那些）为 CSV。
        ///
        /// 建议文件名复用 <see cref="ScadaAlarmHistoryWriter.SuggestFileName"/>——与按天滚动的流水账
        /// 同名规则，这样把导出件放回 <c>Alarms</c> 目录时不会与历史文件打架，命名规则也只留一处。
        /// </summary>
        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出报警历史",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = ScadaAlarmHistoryWriter.SuggestFileName(DateTime.Now),
                DefaultExt = ".csv",
                AddExtension = true,
            };

            // 拿窗口当 owner：对话框才会跟着运行窗口居中、且不会被运行窗口盖到后面去
            // （运行态窗口是 Topmost 形态时尤其明显）。
            var owner = Window.GetWindow(this);
            if (owner == null ? dialog.ShowDialog() != true : dialog.ShowDialog(owner) != true)
                return;

            _viewModel.Export(dialog.FileName);
        }
    }
}
