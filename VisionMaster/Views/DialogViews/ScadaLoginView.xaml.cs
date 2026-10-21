using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「用户登录」弹窗：登录 / 登出 / 改密。
    /// 视图本身没有逻辑——验身份、开会话、改密码全在
    /// <see cref="ViewModels.DialogViewModels.ScadaLoginViewModel"/> 里。
    /// 这里只有两件事属于视图：打开时焦点落在哪、密码框与 VM 的双向同步。
    /// </summary>
    public partial class ScadaLoginView : UserControl
    {
        private ScadaLoginViewModel? _viewModel;
        private bool _syncingPassword;

        public ScadaLoginView()
        {
            InitializeComponent();

            // 密码框的同步不依赖主题里的 PasswordBoxHelper（模板/样式被换掉时会静默失效），
            // 视图自己兜一层：界面有圆点、VM.Password 却为空，登录就会永远报「请输入密码」。
            PasswordInput.PasswordChanged += PasswordInput_PasswordChanged;
            DataContextChanged += OnDataContextChanged;

            // 走 Dispatcher 排队而不是在 Loaded 里直接 Focus()：Loaded 触发时弹窗往往还没真正激活，
            // 那一刻设的焦点会被随后激活的窗口拿走。排到 Input 优先级就落在这之后。
            Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusFirstInput));
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel != null) _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel = DataContext as ScadaLoginViewModel;
            if (_viewModel != null) _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            SyncPasswordToUi();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScadaLoginViewModel.Password)) SyncPasswordToUi();
        }

        private void SyncPasswordToUi()
        {
            var value = _viewModel?.Password ?? string.Empty;
            if (string.Equals(PasswordInput.Password, value, StringComparison.Ordinal)) return;

            _syncingPassword = true;
            PasswordInput.Password = value;
            _syncingPassword = false;
        }

        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_syncingPassword || _viewModel == null) return;
            _viewModel.Password = PasswordInput.Password;
        }

        /// <summary>
        /// 焦点放到"接下来该打字的那一格"：登录是"打字 → 回车"的流程，
        /// 先让用户用鼠标点一下输入框是白费一步。
        /// 已登录时用户名是预填好的（见 VM 的 <c>OnDialogOpened</c>），那种情况下该打的是密码。
        /// </summary>
        private void FocusFirstInput()
        {
            if (string.IsNullOrEmpty(UserNameBox.Text)) UserNameBox.Focus();
            else PasswordInput.Focus();
        }
    }
}
