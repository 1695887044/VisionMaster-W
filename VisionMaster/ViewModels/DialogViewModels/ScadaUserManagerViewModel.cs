using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「用户管理」弹窗：<b>增账号、删账号、改角色、重置密码</b>。
    ///
    /// 为什么它是独立弹窗，而不是登录框里的一个页签
    /// ---------
    /// ① <b>它是管理员专属</b>。混进登录框，等于让"未登录"的人也能看到全部账号名与角色分布——
    ///    那是一份免费送给试探者的名单。
    /// ② <b>它要能一边登录着一边管</b>。管理员改完账号往往要立刻回去干活，不该被关掉登录框。
    /// 登录框只留一个「用户管理」入口按钮（按 <see cref="ScadaLoginViewModel.CanManageUsers"/> 显隐）。
    ///
    /// 本类只做搬运，规则一条都不自己定
    /// ---------
    /// 所有写操作都过 <see cref="ScadaUserStore"/>：
    /// <list type="bullet">
    /// <item>账号存在与否、密码长短、"至少留一个管理员"——都在存储里，本类不重写一遍。</item>
    /// <item>本类只负责<b>问对问题、把答案说出来</b>：谁被选中、改了什么、失败原因是什么。</item>
    /// </list>
    /// 于是"规则改口径"这件事永远只改一处（存储），弹窗跟着自动对。
    ///
    /// 权限：界面灰掉 + 动作出口再拒一次
    /// ---------
    /// <see cref="IsAdmin"/> 只决定"看起来能不能点"，真正的拒绝在 <see cref="EnsureAdmin"/>——
    /// 每个写命令的第一句都是它。<b>界面隐藏挡不住直接调命令</b>，这是 S12 一开始就定下的口径：
    /// 权限必须在动作执行处校验。
    ///
    /// 本弹窗<b>没有</b>"刷新"按钮
    /// ---------
    /// 刷新要走 <see cref="ScadaUserStore.Load"/>，而它读坏文件时会<b>回落成出厂账号</b>。
    /// 一个手滑的刷新按钮能把内存里的账号全换成 admin 一个，再随便保存一下就把文件覆盖了。
    /// 账号清单只在打开弹窗时读一次；要重读，重开弹窗（那时人是清醒的）。
    /// </summary>
    public class ScadaUserManagerViewModel : BindableBase, IDialogAware
    {
        /// <summary>
        /// 下拉里的三档合法角色。刻意<b>不含</b> <see cref="ScadaRole.Undefined"/>：
        /// 它是"文件里存了个不认识的数字"的标记，不是一种能授予的角色。
        /// </summary>
        private static readonly IReadOnlyList<ScadaRoleOption> Roles = new[]
        {
            new ScadaRoleOption(ScadaRole.Operator),
            new ScadaRoleOption(ScadaRole.Engineer),
            new ScadaRoleOption(ScadaRole.Administrator),
        };

        private readonly ScadaUserStore _userStore;
        private readonly ScadaAccessPolicy _accessPolicy;

        private ScadaUserRow? _selectedUser;
        private string _editName = string.Empty;
        private string _editPassword = string.Empty;
        private ScadaRole _editRole = ScadaRole.Operator;
        private bool _isNewMode;
        private string? _message;
        private bool _succeeded;
        private bool _confirmingDelete;

        public ScadaUserManagerViewModel(ScadaUserStore userStore, ScadaAccessPolicy accessPolicy)
        {
            _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));

            NewUserCommand = new DelegateCommand(OnNewUser);
            SaveCommand = new DelegateCommand(OnSave);
            DeleteCommand = new DelegateCommand(OnDelete);
            CancelNewCommand = new DelegateCommand(OnCancelNew);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>窗口标题。视图标题栏绑它，免得同一个字符串写两遍</summary>
        public string Title => "用户管理";

        #region 清单

        /// <summary>账号清单。<b>每次写操作后整表重建</b>——账号存储是数据不是视图模型，
        /// 不实现变更通知，所以"改完自己刷"是唯一的同步方式（清单顶多几十条，重建的代价可以忽略）</summary>
        public ObservableCollection<ScadaUserRow> Users { get; } = new();

        /// <summary>当前选中项。改它即把该账号读进右侧表单</summary>
        public ScadaUserRow? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetProperty(ref _selectedUser, value))
                    LoadIntoForm(value);
            }
        }

        public bool HasUsers => Users.Count > 0;

        #endregion

        #region 权限

        /// <summary>
        /// 是不是管理员。<b>只是一道界面门</b>（决定表单灰不灰、警告条显不显），
        /// 真正的拒绝见 <see cref="EnsureAdmin"/>。
        /// </summary>
        public bool IsAdmin => _accessPolicy.CurrentRole.Satisfies(ScadaRole.Administrator);

        /// <summary>非管理员时的提示语。用 <c>CanOperate</c> 生成，与运行态拒绝用的是同一句话</summary>
        public string AdminHint
        {
            get
            {
                _accessPolicy.CanOperate(ScadaRole.Administrator, out var reason);
                return $"{reason}。请先以管理员账号登录，再回来管理账号。";
            }
        }

        #endregion

        #region 右侧表单

        /// <summary>true = 新建（用户名字段可编辑、密码必填）；false = 编辑已选中的账号</summary>
        public bool IsNewMode
        {
            get => _isNewMode;
            private set
            {
                if (SetProperty(ref _isNewMode, value))
                {
                    RaisePropertyChanged(nameof(HeaderText));
                    RaisePropertyChanged(nameof(PasswordLabel));
                }
            }
        }

        /// <summary>表单抬头，如"编辑账号：admin"</summary>
        public string HeaderText =>
            _isNewMode
                ? "新建账号"
                : _selectedUser == null ? "未选择账号" : $"编辑账号：{_selectedUser.Name}";

        /// <summary>密码框的标题。两种模式的含义完全不同，必须说清</summary>
        public string PasswordLabel =>
            _isNewMode ? "密码（至少 4 位）" : "重置密码（留空表示不修改）";

        public string EditName
        {
            get => _editName;
            set => SetProperty(ref _editName, value);
        }

        /// <summary>密码明文。<b>保存成功即清</b>（见 <see cref="LoadIntoForm"/>）</summary>
        public string EditPassword
        {
            get => _editPassword;
            set => SetProperty(ref _editPassword, value);
        }

        /// <summary>下拉里选中的角色。绑 <c>SelectedValue</c>，故这里是纯枚举值</summary>
        public ScadaRole EditRole
        {
            get => _editRole;
            set => SetProperty(ref _editRole, value);
        }

        public IReadOnlyList<ScadaRoleOption> RoleOptions => Roles;

        #endregion

        #region 提示

        /// <summary>结果提示（成功绿 / 失败红）。无事发生时为 <c>null</c></summary>
        public string? Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        /// <summary>只用来决定 <see cref="Message"/> 的颜色</summary>
        public bool Succeeded
        {
            get => _succeeded;
            private set => SetProperty(ref _succeeded, value);
        }

        /// <summary>
        /// 删除键的二次确认状态。视图据此在「删除账号 / 确认删除」两个按钮之间切换
        /// （两态同位置、只差一次点击，所以按钮文案写死在 XAML 里，不从这里出）。
        /// </summary>
        public bool ConfirmingDelete
        {
            get => _confirmingDelete;
            private set => SetProperty(ref _confirmingDelete, value);
        }

        #endregion

        #region 账号文件位置（出问题时现场要能查到文件在哪）

        public string StoreFileName => Path.GetFileName(_userStore.StorePath);

        public string StorePath => _userStore.StorePath;

        #endregion

        public DelegateCommand NewUserCommand { get; }

        public DelegateCommand SaveCommand { get; }

        public DelegateCommand DeleteCommand { get; }

        public DelegateCommand CancelNewCommand { get; }

        public DelegateCommand CloseCommand { get; }

        #region 命令实现

        private void OnNewUser()
        {
            if (!EnsureAdmin()) return;

            // 顺序要紧：先把表单复位，最后才进新模式——LoadIntoForm 会把 IsNewMode 置回 false，
            // 反过来写会被它抹掉。
            ConfirmingDelete = false;
            EditName = string.Empty;
            EditPassword = string.Empty;
            EditRole = ScadaRole.Operator;
            SetMessage(null, false);

            IsNewMode = true;
        }

        private void OnCancelNew() => LoadIntoForm(_selectedUser);

        private void OnSave()
        {
            if (!EnsureAdmin()) return;

            ConfirmingDelete = false;

            if (IsNewMode)
                SaveNew();
            else
                SaveEdit();
        }

        private void SaveNew()
        {
            if (!_userStore.TryAddUser(EditName, EditPassword, EditRole, out var error))
            {
                SetMessage(error, false);
                return;
            }

            var name = EditName.Trim();

            // RefreshList 会把新账号选上，顺带走 LoadIntoForm 复位表单（密码明文就此清掉）。
            Finish(name, $"账号「{name}」已创建。", true);
        }

        private void SaveEdit()
        {
            var row = _selectedUser;
            if (row == null)
            {
                SetMessage("请先在左侧选择一个账号。", false);
                return;
            }

            var applied = new List<string>();

            if (EditRole != row.Role)
            {
                if (!_userStore.TrySetRole(row.Name, EditRole, out var roleError))
                {
                    Finish(row.Name, Compose(roleError, applied), false);
                    return;
                }

                applied.Add($"角色 → {EditRole.DisplayName()}");

                // 改的正好是"此刻登录着的这个人"：会话里的角色必须立刻跟着变。
                // 不跟着变的后果是界面写着"操作员"、实际还能按管理员的按钮，
                // 直到下一次登录——那正是 S12 要消灭的那类漏洞。
                if (IsCurrentUser(row.Name))
                    _accessPolicy.Login(row.Name, EditRole);
            }

            // 空与纯空白都算"没填"：留空表示不修改，敲进一个空格不该被当成想改成空格。
            if (!string.IsNullOrWhiteSpace(EditPassword))
            {
                if (!_userStore.TryResetPassword(row.Name, EditPassword, out var passwordError))
                {
                    Finish(row.Name, Compose(passwordError, applied), false);
                    return;
                }

                applied.Add("密码已重置");
            }

            if (applied.Count == 0)
            {
                SetMessage("没有需要保存的改动。", false);
                return;
            }

            Finish(row.Name, $"账号「{row.Name}」已更新：{string.Join("、", applied)}。", true);
        }

        private void OnDelete()
        {
            if (!EnsureAdmin()) return;

            var row = _selectedUser;
            if (row == null)
            {
                SetMessage("请先在左侧选择一个账号。", false);
                return;
            }

            // 二次确认做在弹窗内，不弹系统 MessageBox：
            // 本视图模型由容器按 AutoWireViewModel 装配，构造期拿不到"由哪个窗口来弹"这个口子
            // （对比 ScadaAlarmHistoryView 是手工装配，所以它能注入 Confirm 委托）。
            // 就地确认还多一个好处——"要删的是谁"这句话就写在按钮旁边，不会被模态框盖住。
            if (!ConfirmingDelete)
            {
                ConfirmingDelete = true;
                SetMessage($"再按一次「确认删除」将永久删除账号「{row.Name}」，此操作不可撤销。", false);
                return;
            }

            ConfirmingDelete = false;

            if (!_userStore.TryRemoveUser(row.Name, out var error))
            {
                SetMessage(error, false);
                return;
            }

            var name = row.Name;

            // 删掉的正好是此刻登录着的账号：会话必须立刻断掉。
            // 留着的话就是"账号已经不存在了，人还以那个身份在里面按按钮"。
            if (IsCurrentUser(name))
                _accessPolicy.Logout();

            RefreshList(null);
            SetMessage($"账号「{name}」已删除。", true);
        }

        #endregion

        #region 内部

        /// <summary>
        /// <b>动作出口的管理员把关</b>。每个写命令的第一句都是它。
        ///
        /// 走 <see cref="ScadaAccessPolicy.CanOperate"/> 而不是自己比较角色：拒绝理由与
        /// 运行态图元被拒时用的是同一句话（"需要「管理员」权限，当前是「操作员」"），
        /// 现场看到两种场合的两句话是同一种说法，不用去想它们是不是一回事。
        /// </summary>
        private bool EnsureAdmin()
        {
            if (_accessPolicy.CanOperate(ScadaRole.Administrator, out var reason)) return true;

            SetMessage(reason, false);
            return false;
        }

        /// <summary>此刻登录着的账号是不是 <paramref name="name"/>（忽略大小写，与账号存储同一口径）</summary>
        private bool IsCurrentUser(string name)
            => _accessPolicy.IsLoggedIn
            && string.Equals(_accessPolicy.CurrentUserName, name, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 重建清单并把 <paramref name="selectName"/> 选回来（找不到就落到第一条）。
        /// 传 <c>null</c> = 不指定，直接落到第一条。
        /// </summary>
        private void RefreshList(string? selectName)
        {
            Users.Clear();

            foreach (var info in _userStore.Users)
                Users.Add(new ScadaUserRow(info));

            var keep = selectName == null
                ? null
                : Users.FirstOrDefault(u =>
                    string.Equals(u.Name, selectName, StringComparison.OrdinalIgnoreCase));

            var target = keep ?? Users.FirstOrDefault();

            // 走属性 setter 而不是直接调 LoadIntoForm：选中项变了，ListView 的选中态也得跟着变。
            // 唯一的例外是"清单为空"——那时新旧选中项都是 null，SetProperty 判定"没变"直接返回，
            // 表单就会留着上一个账号的内容，所以这一种情况要手工复位。
            if (ReferenceEquals(target, _selectedUser))
                LoadIntoForm(target);
            else
                SelectedUser = target;

            RaisePropertyChanged(nameof(HasUsers));
        }

        /// <summary>
        /// 把一个账号读进表单。
        ///
        /// 顺带把密码框清空——<b>这是明文密码的唯一归宿</b>：换账号、保存成功、退出新建模式，
        /// 三条路径都经过这里，所以不会有哪个入口漏清。
        /// </summary>
        private void LoadIntoForm(ScadaUserRow? row)
        {
            IsNewMode = false;
            ConfirmingDelete = false;
            EditPassword = string.Empty;
            EditName = row?.Name ?? string.Empty;

            // 角色值非法（文件被改坏、或者高版本存低版本读）时回落成"操作员"并报出来：
            // 下拉框里没有"非法值"这一项，直接绑非法值会选不中任何项、显示成一片空白，
            // 用户根本看不出问题在哪。这里把话说清楚，保存时 TrySetRole 会把它修正掉。
            var illegal = row != null && !row.Role.IsDefined();
            EditRole = illegal ? ScadaRole.Operator : row?.Role ?? ScadaRole.Operator;

            SetMessage(
                illegal
                    ? $"账号「{row!.Name}」的角色值非法（{(int)row.Role}），请重新指定角色后保存。"
                    : null,
                false);

            RaisePropertyChanged(nameof(HeaderText));
        }

        /// <summary>收尾：<b>先刷清单再落消息</b>——刷新会重建表单并清空消息，顺序反了就什么都看不到</summary>
        private void Finish(string? selectName, string message, bool succeeded)
        {
            RefreshList(selectName);
            SetMessage(message, succeeded);
        }

        /// <summary>
        /// 拼失败文案：若本次保存已经改成了别的项，得把它一并说出来——
        /// 否则界面只报"密码不能少于 4 位"，而列表里角色明明已经变了，用户会以为整次保存都没生效。
        /// </summary>
        private static string Compose(string? error, List<string> applied)
            => applied.Count == 0
                ? error ?? "操作失败"
                : $"{error}（{string.Join("、", applied)} 已保存）";

        private void SetMessage(string? message, bool succeeded)
        {
            Message = message;
            Succeeded = succeeded;
        }

        #endregion

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() => _accessPolicy.PropertyChanged -= OnAccessPolicyPropertyChanged;

        /// <summary>
        /// 打开时现读账号文件与当前会话，<b>不依赖"容器每次都新建一个 VM"</b>
        /// （与登录框同一条口径：万一哪天注册方式变成复用实例，字段里留着的就是上一次的输入）。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            ConfirmingDelete = false;
            IsNewMode = false;
            EditPassword = string.Empty;

            // 默认选中"当前登录的这个账号"：管理员打开用户管理，多数时候就是来管自己或看自己的。
            RefreshList(_accessPolicy.IsLoggedIn ? _accessPolicy.CurrentUserName : null);

            // 订阅会话变更：弹窗开着的时候超时登出，界面要立刻退回"只读 + 警告条"。
            // 先摘再挂，防止同一个 VM 被打开两次时挂上两份。
            _accessPolicy.PropertyChanged -= OnAccessPolicyPropertyChanged;
            _accessPolicy.PropertyChanged += OnAccessPolicyPropertyChanged;
        }

        /// <summary>会话变了 → 重算权限相关的派生属性（提示语本身就是会话的函数）</summary>
        private void OnAccessPolicyPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(IsAdmin));
            RaisePropertyChanged(nameof(AdminHint));
        }

        #endregion
    }

    /// <summary>
    /// 账号清单里的一行。
    ///
    /// 为什么不直接把 <see cref="ScadaUserInfo"/> 塞进列表：它是领域侧的脱敏视图，
    /// 只该回答"有哪些账号、各是什么角色"。"角色怎么写成中文"是显示问题，
    /// 摊平这一步留在视图模型侧，领域记录就不必为界面负责
    /// （与 <c>ScadaVariableRow</c> 同一条口径）。
    /// </summary>
    public sealed class ScadaUserRow
    {
        public ScadaUserRow(ScadaUserInfo info)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
        }

        public ScadaUserInfo Info { get; }

        public string Name => Info.Name;

        public ScadaRole Role => Info.Role;

        /// <summary>角色显示名。非法值回落成"未知角色(N)"而不是抛异常——一条坏数据不该让清单打不开</summary>
        public string RoleText => Info.Role.DisplayName();
    }

    /// <summary>
    /// 角色下拉框的一项。
    ///
    /// 为什么要包一层：<see cref="ScadaRole"/> 是枚举，直接当 <c>SelectedItem</c> 只能显示成
    /// <c>Operator</c> 这样的标识符。包一层把"值"与"显示名"分开——下拉里走
    /// <c>DisplayMemberPath="Text"</c>，回写走 <c>SelectedValuePath="Value"</c>，
    /// 于是视图模型那边拿到的始终是纯枚举值，不必认识"怎么显示"。
    /// </summary>
    /// <param name="Value">该选项代表的角色</param>
    public sealed record ScadaRoleOption(ScadaRole Value)
    {
        /// <summary>下拉里显示的词（"操作员"/"工程师"/"管理员"），与属性面板、状态栏同源</summary>
        public string Text => Value.DisplayName();
    }
}
