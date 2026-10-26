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
    /// 「用户管理 · 账号与用户组」页签：<b>增账号、删账号、改所属用户组、重置密码</b>。
    ///
    /// 本类是<b>页签内容</b>，不是独立弹窗
    /// ---------
    /// B5 曾把它当 760px 独立弹窗复用进页签，结果页面上出现两层标题、两个「关闭」、
    /// 一张居中窄卡片（2026-10-25 UI 审查 P1-1）。v4 起它只负责两张卡的内容：
    /// 外壳（标题栏 / 页签 / 底栏）归 <c>UserManagementView</c>，本类不再有
    /// <see cref="IDialogAware"/>、<c>RequestClose</c> 或「关闭」命令。
    ///
    /// 本类只做搬运，规则一条都不自己定
    /// ---------
    /// 所有写操作都过 <see cref="ScadaUserStore"/>：
    /// <list type="bullet">
    /// <item>账号存在与否、密码长短、"至少留一个管理员"——都在存储里，本类不重写一遍。</item>
    /// <item>本类只负责<b>问对问题、把答案说出来</b>：谁被选中、改了什么、失败原因是什么。</item>
    /// </list>
    ///
    /// 权限：界面灰掉 + 动作出口再拒一次
    /// ---------
    /// <see cref="IsAdmin"/> 只决定"看起来能不能点"，真正的拒绝在 <see cref="EnsureAdmin"/>——
    /// 每个写命令的第一句都是它。<b>界面隐藏挡不住直接调命令</b>。
    /// </summary>
    public class ScadaUserManagerViewModel : BindableBase
    {
        private readonly ScadaUserStore _userStore;
        private readonly ScadaAccessPolicy _accessPolicy;
        private readonly Func<int> _auditRetentionProvider;
        private readonly List<ScadaUserRow> _allUsers = new();

        private ScadaUserRow? _selectedUser;
        private string _searchText = string.Empty;
        private string _editName = string.Empty;
        private string _editPassword = string.Empty;
        private string _editPasswordConfirm = string.Empty;
        private string _editGroupId = ScadaGroupDefaults.OperatorTemplateId;
        private bool _isNewMode;
        private string? _message;
        private bool _succeeded;
        private bool _confirmingDelete;

        public ScadaUserManagerViewModel(
            ScadaUserStore userStore,
            ScadaAccessPolicy accessPolicy,
            Func<int>? auditRetentionProvider = null)
        {
            _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _auditRetentionProvider = auditRetentionProvider
                                      ?? (() => VisionMaster.Models.AppConfigModel.DefaultAuditRetentionDays);

            NewUserCommand = new DelegateCommand(OnNewUser);
            SaveCommand = new DelegateCommand(OnSave);
            DeleteCommand = new DelegateCommand(OnDelete);
            CancelNewCommand = new DelegateCommand(OnCancelNew);
            OpenGroupEditorCommand = new DelegateCommand(OnOpenGroupEditor);
            OpenSecuritySettingsCommand = new DelegateCommand(OnOpenSecuritySettings);
        }

        #region 清单

        /// <summary>当前可见的账号（搜索过滤后）。ListView 绑它。</summary>
        public ObservableCollection<ScadaUserRow> Users { get; } = new();

        /// <summary>搜索框。按用户名 / 组名做不区分大小写的包含匹配。</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter(null);
            }
        }

        public string UserCountText => $"{Users.Count} 个";

        public bool HasUsers => Users.Count > 0;

        /// <summary>空态文案分两种：一个账号都没有 vs 搜索没命中。</summary>
        public string EmptyText => _allUsers.Count == 0 ? "账号清单为空。" : "没有匹配的账号。";

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

        #endregion

        #region 权限

        /// <summary>
        /// 是不是管理员。<b>只是一道界面门</b>（决定表单灰不灰、警告条显不显），
        /// 真正的拒绝见 <see cref="EnsureAdmin"/>。
        /// </summary>
        public bool IsAdmin => _accessPolicy.Can(ScadaPermissionKeys.UserAdministration, out _);

        /// <summary>非管理员时的提示语。</summary>
        public string AdminHint
        {
            get
            {
                _accessPolicy.Can(ScadaPermissionKeys.UserAdministration, out var reason);
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
                    RaisePropertyChanged(nameof(PasswordHint));
                    RaisePropertyChanged(nameof(SelectedGroupName));
                }
            }
        }

        /// <summary>表单抬头，如"编辑账号：admin"</summary>
        public string HeaderText =>
            _isNewMode
                ? "新建账号"
                : _selectedUser == null ? "未选择账号" : $"编辑账号：{_selectedUser.Name}";

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

        /// <summary>确认密码。只做本机一致性校验，不落盘、不参与哈希。</summary>
        public string EditPasswordConfirm
        {
            get => _editPasswordConfirm;
            set => SetProperty(ref _editPasswordConfirm, value);
        }

        /// <summary>下拉里选中的用户组 Id。</summary>
        public string EditGroupId
        {
            get => _editGroupId;
            set
            {
                if (SetProperty(ref _editGroupId, value))
                    RaisePropertyChanged(nameof(SelectedGroupName));
            }
        }

        public IReadOnlyList<ScadaGroupOption> GroupOptions
            => _userStore.Groups.Select(g => new ScadaGroupOption(g.Id, g.Name)).ToArray();

        /// <summary>右卡抬头旁的组名 chip。</summary>
        public string SelectedGroupName
        {
            get
            {
                var option = GroupOptions.FirstOrDefault(o =>
                    string.Equals(o.GroupId, EditGroupId, StringComparison.Ordinal));
                return option?.Name ?? EditGroupId;
            }
        }

        /// <summary>密码框下方的提示。长度取账号本的实际配置，不写死。</summary>
        public string PasswordHint => IsNewMode
            ? $"密码至少 {MinPasswordLength} 位；两次输入必须一致。"
            : $"重置密码时填写，留空表示不修改；最短 {MinPasswordLength} 位，两次输入必须一致。";

        public int MinPasswordLength => _userStore.MinPasswordLength;

        /// <summary>
        /// 右卡底部的只读安全策略摘要。<b>数字全部来自当前配置</b>，不是写死的示例值——
        /// 现场改了「系统设置 → 安全与留档」之后，这一行要跟着变。
        /// </summary>
        public string PolicySummary
        {
            get
            {
                var idle = _accessPolicy.IdleTimeout.TotalMinutes;
                var idleText = idle <= 0 ? "不自动登出" : $"空闲 {idle:0.#} 分钟自动登出";
                return $"安全策略：密码最短 {MinPasswordLength} 位 · {idleText} · 审计保留 {AuditRetentionDays} 天";
            }
        }

        public int AuditRetentionDays => _auditRetentionProvider();

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

        public DelegateCommand OpenGroupEditorCommand { get; }

        public DelegateCommand OpenSecuritySettingsCommand { get; }

        #region 命令实现

        private void OnNewUser()
        {
            if (!EnsureAdmin()) return;

            // 顺序要紧：先把表单复位，最后才进新模式——LoadIntoForm 会把 IsNewMode 置回 false，
            // 反过来写会被它抹掉。
            ConfirmingDelete = false;
            EditName = string.Empty;
            EditPassword = string.Empty;
            EditPasswordConfirm = string.Empty;
            EditGroupId = ScadaGroupDefaults.OperatorTemplateId;
            SetMessage(null, false);

            IsNewMode = true;
        }

        /// <summary>「放弃修改」：把当前选中账号重新读进表单，丢弃未保存的改动。</summary>
        private void OnCancelNew()
        {
            ConfirmingDelete = false;
            LoadIntoForm(_selectedUser);
        }

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
            if (!ValidatePasswordPair()) return;

            if (!_userStore.TryAddUser(EditName, EditPassword, EditGroupId, out var error))
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

            if (!string.Equals(EditGroupId, row.GroupId, StringComparison.Ordinal))
            {
                if (!_userStore.TryChangeGroup(row.Name, EditGroupId, out var roleError))
                {
                    Finish(row.Name, Compose(roleError, applied), false);
                    return;
                }

                var group = _userStore.Groups.FirstOrDefault(g => g.Id == EditGroupId);
                applied.Add($"用户组 → {group?.Name ?? EditGroupId}");

                if (IsCurrentUser(row.Name) && group != null)
                    _accessPolicy.RefreshPermissions(group.Id, group.Name, group.Permissions);
            }

            // 空与纯空白都算"没填"：留空表示不修改，敲进一个空格不该被当成想改成空格。
            if (!string.IsNullOrWhiteSpace(EditPassword))
            {
                if (!ValidatePasswordPair()) return;

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

        /// <summary>两次密码必须一致。留空（编辑态）直接通过；新建态由存储判长度。</summary>
        private bool ValidatePasswordPair()
        {
            if (string.IsNullOrEmpty(EditPassword) && string.IsNullOrEmpty(EditPasswordConfirm))
                return true;

            if (!string.Equals(EditPassword, EditPasswordConfirm, StringComparison.Ordinal))
            {
                SetMessage("两次输入的密码不一致，请重新输入。", false);
                return false;
            }

            return true;
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

            // 二次确认做在页内，不弹系统 MessageBox："要删的是谁"这句话就写在按钮旁边。
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
            if (IsCurrentUser(name))
                _accessPolicy.Logout();

            RefreshList(null);
            SetMessage($"账号「{name}」已删除。", true);
        }

        private void OnOpenGroupEditor()
        {
            try
            {
                Prism.Ioc.ContainerLocator.Container.Resolve<IDialogService>()
                    .ShowDialog("ScadaGroupEditorView");
            }
            catch
            {
                // 容器不可用（断言宿主 / 探针）：不弹窗，保持页面可用。
            }

            Reload();
        }

        /// <summary>策略摘要右侧的「在系统设置中修改」：跳系统设置，回来后重算摘要数字。</summary>
        private void OnOpenSecuritySettings()
        {
            try
            {
                Prism.Ioc.ContainerLocator.Container.Resolve<IDialogService>()
                    .ShowDialog(ScadaSystemSettingsViewModel.DialogName);
            }
            catch
            {
                // 容器不可用（断言宿主 / 探针）：保持页面可用。
            }

            Reload();
        }

        #endregion

        #region 内部

        /// <summary>
        /// <b>动作出口的权限把关</b>。每个写命令的第一句都是它。
        /// </summary>
        private bool EnsureAdmin()
        {
            if (_accessPolicy.Can(ScadaPermissionKeys.UserAdministration, out var reason)) return true;

            SetMessage(reason, false);
            return false;
        }

        /// <summary>此刻登录着的账号是不是 <paramref name="name"/>（忽略大小写，与账号存储同一口径）</summary>
        private bool IsCurrentUser(string name)
            => _accessPolicy.IsLoggedIn
            && string.Equals(_accessPolicy.CurrentUserName, name, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 外部（用户管理大窗）打开/刷新时重读账号本。
        /// <b>保留当前选中项</b>：刷新是"重读文件"，不是"把用户踢回第一个账号"。
        /// </summary>
        public void Reload()
        {
            var keep = _selectedUser?.Name
                       ?? (_accessPolicy.IsLoggedIn ? _accessPolicy.CurrentUserName : null);
            RefreshList(keep);

            RaisePropertyChanged(nameof(IsAdmin));
            RaisePropertyChanged(nameof(AdminHint));
            RaisePropertyChanged(nameof(GroupOptions));
            RaisePropertyChanged(nameof(SelectedGroupName));
            RaisePropertyChanged(nameof(MinPasswordLength));
            RaisePropertyChanged(nameof(PasswordHint));
            RaisePropertyChanged(nameof(PolicySummary));

            // 订阅会话变更：页面开着的时候超时登出，界面要立刻退回"只读 + 警告条"。
            // 先摘再挂，防止同一个 VM 被刷新多次时挂上多份。
            _accessPolicy.PropertyChanged -= OnAccessPolicyPropertyChanged;
            _accessPolicy.PropertyChanged += OnAccessPolicyPropertyChanged;
        }

        /// <summary>
        /// 重建全量清单，再按当前搜索词过滤，并把 <paramref name="selectName"/> 选回来。
        /// </summary>
        private void RefreshList(string? selectName)
        {
            _allUsers.Clear();
            foreach (var info in _userStore.Users)
                _allUsers.Add(new ScadaUserRow(info, IsCurrentUser(info.Name)));

            ApplyFilter(selectName);
        }

        /// <summary>按 <see cref="SearchText"/> 过滤；选中项尽量保持不变。</summary>
        private void ApplyFilter(string? selectName)
        {
            var query = (SearchText ?? string.Empty).Trim();

            Users.Clear();
            foreach (var user in _allUsers)
            {
                if (query.Length == 0
                    || user.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || user.GroupText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    Users.Add(user);
                }
            }

            ScadaUserRow? target;
            if (selectName != null)
            {
                target = Users.FirstOrDefault(u =>
                    string.Equals(u.Name, selectName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                target = _selectedUser != null && Users.Contains(_selectedUser) ? _selectedUser : null;
            }

            target ??= Users.FirstOrDefault();

            // 走属性 setter 而不是直接调 LoadIntoForm：选中项变了，ListView 的选中态也得跟着变。
            // 唯一的例外是"清单为空"——那时新旧选中项都是 null，SetProperty 判定"没变"直接返回，
            // 表单就会留着上一个账号的内容，所以这一种情况要手工复位。
            if (ReferenceEquals(target, _selectedUser))
                LoadIntoForm(target);
            else
                SelectedUser = target;

            RaisePropertyChanged(nameof(UserCountText));
            RaisePropertyChanged(nameof(HasUsers));
            RaisePropertyChanged(nameof(EmptyText));
        }

        /// <summary>
        /// 把一个账号读进表单。
        ///
        /// 顺带把两个密码框清空——<b>这是明文密码的唯一归宿</b>：换账号、保存成功、退出新建模式，
        /// 三条路径都经过这里，所以不会有哪个入口漏清。
        /// </summary>
        private void LoadIntoForm(ScadaUserRow? row)
        {
            IsNewMode = false;
            ConfirmingDelete = false;
            EditPassword = string.Empty;
            EditPasswordConfirm = string.Empty;
            EditName = row?.Name ?? string.Empty;

            // 组 Id 悬空（组被撤销）时回落成操作员模板并提示，保存时 TryChangeGroup 会修正。
            var missingGroup = row != null
                               && _userStore.Groups.All(g => g.Id != row.GroupId);
            EditGroupId = missingGroup ? ScadaGroupDefaults.OperatorTemplateId
                : row?.GroupId ?? ScadaGroupDefaults.OperatorTemplateId;

            SetMessage(
                missingGroup
                    ? $"账号「{row!.Name}」的用户组「{row.GroupId}」不存在，请重新指定后保存。"
                    : null,
                false);

            RaisePropertyChanged(nameof(HeaderText));
            RaisePropertyChanged(nameof(SelectedGroupName));
        }

        /// <summary>收尾：<b>先刷清单再落消息</b>——刷新会重建表单并清空消息，顺序反了就什么都看不到</summary>
        private void Finish(string? selectName, string message, bool succeeded)
        {
            RefreshList(selectName);
            SetMessage(message, succeeded);
        }

        /// <summary>
        /// 拼失败文案：若本次保存已经改成了别的项，得把它一并说出来——
        /// 否则界面只报"密码不能少于 4 位"，而列表里用户组明明已经变了，用户会以为整次保存都没生效。
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

        /// <summary>会话变了 → 重算权限相关的派生属性（提示语本身就是会话的函数）</summary>
        private void OnAccessPolicyPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(IsAdmin));
            RaisePropertyChanged(nameof(AdminHint));
            RaisePropertyChanged(nameof(PolicySummary));
        }

        #endregion
    }

    /// <summary>
    /// 账号清单里的一行。
    ///
    /// 为什么不直接把 <see cref="ScadaUserInfo"/> 塞进列表：它是领域侧的脱敏视图，
    /// 只该回答"有哪些账号、各属于哪个组"。"组 Id 怎么显示成组名"是显示问题，
    /// 摊平这一步留在视图模型侧，领域记录就不必为界面负责。
    /// </summary>
    public sealed class ScadaUserRow
    {
        public ScadaUserRow(ScadaUserInfo info, bool isCurrent)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            IsCurrent = isCurrent;
        }

        public ScadaUserInfo Info { get; }

        public string Name => Info.Name;

        public string GroupId => Info.GroupId;

        /// <summary>组显示名（列表里直接上屏）。组名缺失时回落到 Id，绝不显示空白。</summary>
        public string GroupText => string.IsNullOrWhiteSpace(Info.GroupName) ? Info.GroupId : Info.GroupName;

        /// <summary>当前登录账号：清单里挂「当前」chip，同时也是删除守卫要提示的那个。</summary>
        public bool IsCurrent { get; }

        /// <summary>头像圆片里的首字母。</summary>
        public string Initial => Name.Length == 0 ? "?" : Name.Substring(0, 1).ToUpperInvariant();
    }

    /// <summary>用户组下拉项：值 = GroupId，显示名 = 组名。</summary>
    public sealed record ScadaGroupOption(string GroupId, string Name)
    {
        public string Text => Name;

        /// <summary>
        /// 下拉在少数模板路径下会退化成"对象 ToString"（ContentPresenter 拿不到 SelectionBoxItemTemplate）。
        /// 让 ToString 返回显示名，最差情况也还是组名，不会把记录结构摊到界面上。
        /// </summary>
        public override string ToString() => Name;
    }
}
