using System;
using System.ComponentModel;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「用户登录」弹窗：<b>把"用户名 + 密码"换成"此刻是谁登录着"</b>。
    ///
    /// 它在权限体系里的位置
    /// ---------
    /// 本类<b>一条规则都不自己定</b>，只做搬运：
    /// <list type="number">
    /// <item><see cref="ScadaUserStore.TryValidate"/>：用户名 + 密码 → 一个已经通过验证的角色（验身份）。</item>
    /// <item><see cref="ScadaAccessPolicy.Login"/>：把那个角色装进全局会话（记会话）。</item>
    /// </list>
    /// 两条都不做的话本类就什么也没做——所以这里<b>不会</b>出现"密码对不对""角色够不够"之类的判断。
    /// 将来权限口径要改（比如加一档角色），改的是上面那两处，不是这里。
    ///
    /// 三个刻意的决定
    /// ---------
    /// ① <b>不订阅 <see cref="ScadaAccessPolicy.AutoLoggedOut"/></b>。
    ///    空闲超时那一刻，这个弹窗多半是<b>关着的</b>（没人会开着登录框去干活），
    ///    在这里订阅等于为了一个几乎不会命中的场景，多背一份"关窗时必须记得解订阅"
    ///    的生命周期债。超时提示归主窗口（那里一直开着，正是该说话的地方）。
    ///    本弹窗改为订阅 <see cref="ScadaAccessPolicy"/> 的<b>属性变更</b>——
    ///    这样"开着登录框时正好超时"也能看到状态行跟着变成"未登录"，而订阅在
    ///    <see cref="OnDialogOpened"/> 挂、<see cref="OnDialogClosed"/> 摘，生命周期与弹窗一致。
    ///
    /// ② <b>登录成功即关窗</b>。按下「登录」的人要的是"进去"，不是"看到一句成功提示"。
    ///    关窗之前把明文密码清掉（见 <see cref="Password"/> 的注释）。
    ///
    /// ③ <b>登录按钮不禁用</b>。用户名/密码为空时，禁用按钮只会让人对着一个灰按钮猜原因；
    ///    放行点击、由账号存储报出「请输入用户名」/「请输入密码」，界面上有字可读。
    ///    这也正是 <see cref="ScadaUserStore.TryValidate"/> 那几句文案存在的意义。
    /// </summary>
    public class ScadaLoginViewModel : BindableBase, IDialogAware
    {
        private readonly ScadaUserStore _userStore;
        private readonly ScadaAccessPolicy _accessPolicy;
        private readonly IDialogService _dialogService;

        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string? _loginMessage;

        private string _changeUserName = string.Empty;
        private string _oldPassword = string.Empty;
        private string _newPassword = string.Empty;
        private string _confirmPassword = string.Empty;
        private string? _changeMessage;
        private bool _changeSucceeded;

        private bool _showDefaultPasswordHint;
        private int _selectedTabIndex;

        public ScadaLoginViewModel(ScadaUserStore userStore, ScadaAccessPolicy accessPolicy,
                                   IDialogService dialogService)
        {
            _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            LoginCommand = new DelegateCommand(OnLogin);
            LogoutCommand = new DelegateCommand(OnLogout);
            ChangePasswordCommand = new DelegateCommand(OnChangePassword);
            OpenUserManagerCommand = new DelegateCommand(OnOpenUserManager);
            SubmitCommand = new DelegateCommand(OnSubmit);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>窗口标题。视图的标题栏文字也绑在这里，免得同一个字符串写两遍</summary>
        public string Title => "用户登录";

        #region 登录页

        /// <summary>登录名。打开弹窗时若已登录会预填当前用户——换用户时省一次打字</summary>
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        /// <summary>
        /// 密码明文。<b>用完即清</b>：验证通过的那一刻它就变成"会话里的一个角色"，
        /// 没有任何理由继续留在内存里（更不该跨弹窗存活）。打开弹窗时也会清一次。
        /// </summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        /// <summary>登录失败原因（红字）。成功时为 <c>null</c></summary>
        public string? LoginMessage
        {
            get => _loginMessage;
            private set => SetProperty(ref _loginMessage, value);
        }

        /// <summary>
        /// 出厂密码提示条：只在 <see cref="ScadaUserStore.DefaultAdminName"/> 仍在使用出厂密码时亮起。
        ///
        /// 为什么要提前说：出厂密码是公开的（写在说明书里），机器一旦入网它就不再是一道门。
        /// 等登录成功再说就晚了——那时候弹窗已经关了。所以它出现在"输入之前"，
        /// 而且只在真用得上的时候出现，不占常态界面的位置。
        /// </summary>
        public bool ShowDefaultPasswordHint
        {
            get => _showDefaultPasswordHint;
            private set => SetProperty(ref _showDefaultPasswordHint, value);
        }

        #endregion

        #region 状态行（读的都是全局会话，不是本弹窗自己的字段）

        /// <summary>此刻的身份，如"admin（管理员）"/"未登录（操作员）"</summary>
        public string LoginStateText => _accessPolicy.LoginStateText;

        public bool IsLoggedIn => _accessPolicy.IsLoggedIn;

        /// <summary>
        /// 「用户管理」入口是否可见。用 <c>Satisfies</c>（"至少管理员"）而不是 <c>==</c>：
        /// 管理员是最高档，两者当前等价，但将来若在管理员之上再加一档，这里的语义仍然对。
        ///
        /// <b>它只是一道界面门</b>。真正的把关在账号存储里（删/改最后一个管理员会被拒），
        /// 界面隐藏挡不住直接调接口——这是 S12 一开始就定下的口径。
        /// </summary>
        public bool CanManageUsers => _accessPolicy.CurrentRole.Satisfies(ScadaRole.Administrator);

        #endregion

        #region 改密页

        /// <summary>要改密码的账号名（与登录页分开：可以登录 A、顺手改 B 的密码，只要知道 B 的旧密码）</summary>
        public string ChangeUserName
        {
            get => _changeUserName;
            set => SetProperty(ref _changeUserName, value);
        }

        public string OldPassword
        {
            get => _oldPassword;
            set => SetProperty(ref _oldPassword, value);
        }

        public string NewPassword
        {
            get => _newPassword;
            set => SetProperty(ref _newPassword, value);
        }

        /// <summary>
        /// 再输一遍新密码。这一层校验<b>必须在界面侧</b>：账号存储只认"旧密码 + 新密码"，
        /// 它没有第二次输入可比（那是输入框的事，不是数据的事）。
        /// </summary>
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
        }

        /// <summary>改密结果（成功绿 / 失败红）</summary>
        public string? ChangeMessage
        {
            get => _changeMessage;
            private set => SetProperty(ref _changeMessage, value);
        }

        /// <summary>改密是否成功。只用来决定 <see cref="ChangeMessage"/> 的颜色</summary>
        public bool ChangeSucceeded
        {
            get => _changeSucceeded;
            private set => SetProperty(ref _changeSucceeded, value);
        }

        #endregion

        /// <summary>当前页签（0=登录，1=修改密码）。回车提交时靠它决定提交哪一页</summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public DelegateCommand LoginCommand { get; }

        public DelegateCommand LogoutCommand { get; }

        public DelegateCommand ChangePasswordCommand { get; }

        public DelegateCommand OpenUserManagerCommand { get; }

        /// <summary>
        /// 回车键的落点：<b>提交当前页</b>。绑在整窗上（子控件里按回车都会冒泡到这里），
        /// 所以登录页回车 = 登录、改密页回车 = 改密，不必给每个输入框各挂一遍。
        /// </summary>
        public DelegateCommand SubmitCommand { get; }

        public DelegateCommand CloseCommand { get; }

        private void OnLogin()
        {
            if (!_userStore.TryValidate(UserName, Password, out var role, out var error))
            {
                LoginMessage = error;
                return;
            }

            _accessPolicy.Login(UserName, role);

            // 明文用完即弃：上面这一行之后，身份已经以"角色"的形式存在于会话里了。
            Password = string.Empty;
            LoginMessage = null;

            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
        }

        /// <summary>
        /// 退出登录。这里<b>只调 <see cref="ScadaAccessPolicy.Logout"/></b>——
        /// 界面上的状态行、按钮可见性全部靠订阅它的属性变更自动跟着变，
        /// 不在这里手动挨个通知（手动通知的写法在"别处也改了会话"时会漏）。
        /// </summary>
        private void OnLogout()
        {
            _accessPolicy.Logout();

            UserName = string.Empty;
            ChangeUserName = string.Empty;
            LoginMessage = null;
            ChangeMessage = null;
        }

        private void OnChangePassword()
        {
            ChangeSucceeded = false;

            if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
            {
                ChangeMessage = "两次输入的新密码不一致";
                return;
            }

            if (!_userStore.TryChangePassword(ChangeUserName, OldPassword, NewPassword, out var error))
            {
                ChangeMessage = error;
                return;
            }

            var name = ChangeUserName.Trim();

            // 三个密码框一起清：改完就没有一个还有用的了。
            OldPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;

            ChangeSucceeded = true;
            ChangeMessage = $"账号「{name}」的密码已修改。";

            // 顺手把登录页的用户名填好（人已经在这儿了，别再让他打第二遍）。
            // 刻意<b>不</b>自动跳到登录页：跳过去会把刚出现的绿色提示一起藏掉，
            // 用户看到的是"页面一闪，什么也没说"。
            UserName = name;
        }

        /// <summary>打开「用户管理」弹窗。注册名与 <c>App.xaml.cs</c> 里的注册串一致</summary>
        private void OnOpenUserManager() => _dialogService.ShowDialog("ScadaUserManagerView");

        private void OnSubmit()
        {
            if (SelectedTabIndex == 1) ChangePasswordCommand.Execute();
            else LoginCommand.Execute();
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() => _accessPolicy.PropertyChanged -= OnAccessPolicyPropertyChanged;

        /// <summary>
        /// 打开时现读会话与账号文件，<b>不依赖"容器每次都新建一个 VM"</b>
        /// （与 <see cref="ScadaRunWindowSettingsViewModel"/> 同一条口径：
        /// 万一哪天注册方式变成复用实例，字段里留着的就是上一次的输入）。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            // ① 清掉上一次的输入。密码明文尤其不能跨弹窗存活。
            Password = string.Empty;
            OldPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            LoginMessage = null;
            ChangeMessage = null;
            ChangeSucceeded = false;

            // ② 已登录就预填当前用户：改自己的密码、或者换个人登录，都不必再打一遍名字。
            //    未登录时刻意<b>不</b>预填任何名字（包括 admin）：一个看起来"已经填好"的
            //    用户名框，会让人直接按回车然后对着报错发愣。
            if (_accessPolicy.IsLoggedIn)
            {
                UserName = _accessPolicy.CurrentUserName;
                ChangeUserName = _accessPolicy.CurrentUserName;
            }

            // ③ 出厂密码检测放在这里算一次。它是十万次 PBKDF2（几十毫秒），
            //    绝不能塞进属性的 getter —— 那样每次界面刷新都会重算一遍。
            ShowDefaultPasswordHint = _userStore.IsUsingDefaultPassword(ScadaUserStore.DefaultAdminName);

            // ④ 订阅会话变更：弹窗开着的时候超时登出，状态行也要跟着变。
            //    先摘再挂，防止"同一个 VM 被打开两次"时挂上两份。
            _accessPolicy.PropertyChanged -= OnAccessPolicyPropertyChanged;
            _accessPolicy.PropertyChanged += OnAccessPolicyPropertyChanged;
        }

        #endregion

        /// <summary>
        /// 会话变了 → 把本弹窗里那几个<b>派生</b>属性重算一遍。
        /// 只通知这三个，不传 <c>null</c> 让 WPF 全量刷新（理由见
        /// <see cref="ScadaAccessPolicy.RaiseLoginStateChanged"/>）。
        /// </summary>
        private void OnAccessPolicyPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(LoginStateText));
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(CanManageUsers));
        }
    }
}
