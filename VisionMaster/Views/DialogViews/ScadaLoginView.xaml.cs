using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 「用户登录」弹窗：登录 / 登出 / 改密。
    /// 视图本身没有逻辑——验身份、开会话、改密码全在
    /// <see cref="ViewModels.DialogViewModels.ScadaLoginViewModel"/> 里。
    /// 这里只有一件事属于视图：<b>打开时焦点落在哪</b>。
    /// </summary>
    public partial class ScadaLoginView : UserControl
    {
        public ScadaLoginView()
        {
            InitializeComponent();

            // 走 Dispatcher 排队而不是在 Loaded 里直接 Focus()：Loaded 触发时弹窗往往还没真正激活，
            // 那一刻设的焦点会被随后激活的窗口拿走。排到 Input 优先级就落在这之后。
            Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusFirstInput));
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
