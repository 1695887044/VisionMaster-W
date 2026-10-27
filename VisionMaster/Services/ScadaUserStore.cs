using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Core.Interfaces.Operations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>账号本状态机：决定「能不能登录 / 能不能改账号 / 会不会写盘」。</summary>
    public enum ScadaAccountBookState
    {
        /// <summary>尚未打开方案（或绑定失败被 fail-closed 清空）：空账号本，登录与改账号都不行。</summary>
        NoSolution = 0,
        /// <summary>新建方案、首次保存前：内存里有出厂管理员，能登录，<b>不能改账号</b>。</summary>
        UnsavedSolution = 1,
        /// <summary>已落盘方案：能登录、能改账号、写盘走三文件 + 失败回滚。</summary>
        PersistedSolution = 2
    }

    /// <summary>内存账号从哪来。B3 没有 LegacyImport；UnsupportedLegacy = 旧 5 字段 Role 文件，fail-closed。</summary>
    public enum ScadaBookSource
    {
        Unbound = 0,
        Solution = 1,
        Factory = 2,
        UnsupportedLegacy = 3
    }

    /// <summary>
    /// 方案级三文件账号本（B3）：<c>Users\ScadaUsers.json</c> / <c>ScadaGroups.json</c> /
    /// <c>ScadaPermissions.json</c>。
    ///
    /// 账号文件只有五个字段：<c>Name/Salt/Hash/Iterations/GroupId</c>，<b>没有 Role</b>。
    /// 旧 5 字段 Role 文件不迁移、不导入，直接 <see cref="ScadaBookSource.UnsupportedLegacy"/>：
    /// 身份照常绑定，但账号本为空、登录恒拒、任何写操作都被拒绝；唯一出路是
    /// <see cref="TryRebuildAccountBook"/>（旧文件改名保留 + 重建 admin）。
    ///
    /// 三文件写盘顺序固定：权限目录 → 组 → 用户；任一失败按写前字节回滚三者。
    /// 锁序固定 <see cref="_writeGate"/> → <see cref="_gate"/>；PBKDF2 与文件 I/O 在锁外。
    /// </summary>
    public sealed class ScadaUserStore : IScadaUserAuthenticator, IScadaUserChangeNotifier, IScadaPermissionCatalog
    {
        public const string DefaultAdminName = "admin";
        public const string DefaultAdminPassword = "admin123";
        public const int DefaultMinPasswordLength = AppConfigModel.DefaultMinPasswordLength;
        public const int DefaultIterations = 100_000;

        public const string UsersFileName = "ScadaUsers.json";
        public const string GroupsFileName = "ScadaGroups.json";
        public const string PermissionsFileName = "ScadaPermissions.json";

        private const int SaltBytes = 16;
        private const int HashBytes = 32;
        private const string AuditEventText = "账号管理";

        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        public event Action<string, ScadaUserChangeKind>? UserChanged;
        public event Action? BookChanged;
        public event Action? GroupChanged;

        /// <summary>
        /// 属性面板的目录刷新信号：目录写入会同时抬 <see cref="GroupChanged"/> 与
        /// <see cref="BookChanged"/>，这里把两者都转出去。重复通知无副作用（面板只重算候选），
        /// 漏通知才会让"刚新建的权限在组态面板里选不到"。
        /// </summary>
        event Action? IScadaPermissionCatalog.CatalogChanged
        {
            add { BookChanged += value; GroupChanged += value; }
            remove { BookChanged -= value; GroupChanged -= value; }
        }

        private readonly List<ScadaUserRecord> _users = new();
        private readonly List<ScadaGroupInfo> _groups = new();
        private readonly List<ScadaPermissionInfo> _catalog = new();
        private readonly object _gate = new();
        private readonly object _writeGate = new();

        private string? _storePath;
        private string? _groupsPath;
        private string? _permissionsPath;

        private ScadaAccountBookState _bookState;
        private ScadaBookSource _bookSource;
        private Guid? _boundSolutionId;
        private string? _boundSolutionRoot;
        private string? _pendingBadFile;

        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;
        private readonly Func<int>? _minPasswordLengthProvider;
        private readonly Func<string, int>? _permissionReferenceCounter;

        public ScadaUserStore(string? storePath = null,
                              ScadaAuditWriter? audit = null,
                              Func<string?>? actorProvider = null,
                              Func<int>? minPasswordLengthProvider = null,
                              bool startUnbound = false,
                              Func<string, int>? permissionReferenceCounter = null)
        {
            _audit = audit;
            _actorProvider = actorProvider;
            _minPasswordLengthProvider = minPasswordLengthProvider;
            _permissionReferenceCounter = permissionReferenceCounter;

            if (startUnbound)
            {
                lock (_gate)
                {
                    _users.Clear();
                    _groups.Clear();
                    _groups.AddRange(ScadaGroupDefaults.CreateDefaultTemplates());
                    _catalog.Clear();
                    _catalog.AddRange(ScadaPermissionKeys.BuiltIn);
                    _bookState = ScadaAccountBookState.NoSolution;
                    _bookSource = ScadaBookSource.Unbound;
                    _boundSolutionId = null;
                    _boundSolutionRoot = null;
                    _storePath = _groupsPath = _permissionsPath = null;
                    _pendingBadFile = null;
                    LastLoadError = null;
                }
            }
            else
            {
                _storePath = storePath;
                if (!string.IsNullOrEmpty(storePath))
                {
                    var dir = Path.GetDirectoryName(storePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        _groupsPath = Path.Combine(dir, GroupsFileName);
                        _permissionsPath = Path.Combine(dir, PermissionsFileName);
                    }
                }
                _bookState = ScadaAccountBookState.PersistedSolution;
                _bookSource = ScadaBookSource.Solution;
                Load();
            }
        }

        public static string UsersFilePath(string solutionRoot)
            => Path.Combine(solutionRoot, SolutionPaths.UsersFolderName, UsersFileName);

        public static string GroupsFilePath(string solutionRoot)
            => Path.Combine(solutionRoot, SolutionPaths.UsersFolderName, GroupsFileName);

        public static string PermissionsFilePath(string solutionRoot)
            => Path.Combine(solutionRoot, SolutionPaths.UsersFolderName, PermissionsFileName);

        public ScadaAccountBookState BookState { get { lock (_gate) return _bookState; } }
        public ScadaBookSource BookSource { get { lock (_gate) return _bookSource; } }
        public Guid? BoundSolutionId { get { lock (_gate) return _boundSolutionId; } }
        public string? BoundSolutionRoot { get { lock (_gate) return _boundSolutionRoot; } }

        public bool CanWriteAccounts
            => BookState == ScadaAccountBookState.PersistedSolution
               && BookSource != ScadaBookSource.UnsupportedLegacy;

        public string StorePath => _storePath ?? string.Empty;
        public string? LastLoadError { get; private set; }

        public int MinPasswordLength
        {
            get
            {
                var configured = _minPasswordLengthProvider?.Invoke() ?? DefaultMinPasswordLength;
                return configured > 0 ? configured : DefaultMinPasswordLength;
            }
        }

        public IReadOnlyList<ScadaUserInfo> Users
        {
            get
            {
                lock (_gate)
                {
                    var result = new List<ScadaUserInfo>(_users.Count);
                    foreach (var user in _users)
                    {
                        var group = FindGroupLocked(user.GroupId);
                        result.Add(new ScadaUserInfo(
                            user.Name,
                            user.GroupId,
                            group?.Permissions?.ToArray() ?? Array.Empty<string>(),
                            group?.Name ?? string.Empty));
                    }
                    return result.OrderBy(u => u.Name, NameComparer).ToArray();
                }
            }
        }

        public IReadOnlyList<ScadaGroupInfo> Groups
        {
            get { lock (_gate) return _groups.ToArray(); }
        }

        public IReadOnlyList<ScadaPermissionInfo> PermissionCatalog
        {
            get { lock (_gate) return _catalog.ToArray(); }
        }

        internal Action? AfterSnapshotForTest { get; set; }

        public bool TryValidate(string? userName, string? password,
                                out ScadaUserInfo? user, out string? error)
            => TryValidate(userName, (password ?? string.Empty).AsSpan(), out user, out error);

        /// <summary>
        /// 与 <see cref="TryValidate(string?, string?, out ScadaUserInfo?, out string?)"/> 同一条判据，
        /// 但密码以 <see cref="ReadOnlySpan{Char}"/> 传入。
        ///
        /// 为什么需要这一份：跨方案保持登录要拿"本次会话的明文密码"去目标方案的账号本重验，
        /// 而凭据仓保存的是可擦除的 <c>char[]</c>（见 <c>ScadaSessionCredentialVault</c>）。
        /// 为了调这个重载把 <c>char[]</c> 转成 <c>string</c>，等于把刚擦掉的那份明文又造一遍，
        /// 所以验证链路直接吃 span，PBKDF2 也吃 span（<c>Rfc2898DeriveBytes.Pbkdf2</c> 有该重载）。
        /// </summary>
        public bool TryValidate(string? userName, ReadOnlySpan<char> password,
                                out ScadaUserInfo? user, out string? error)
        {
            user = null;
            var name = Normalize(userName);
            if (name.Length == 0) { error = "请输入用户名"; return false; }
            if (password.IsEmpty) { error = "请输入密码"; return false; }

            if (BookState == ScadaAccountBookState.NoSolution)
            {
                error = "尚未打开方案，无法登录；请先打开或新建方案";
                return false;
            }

            if (BookSource == ScadaBookSource.UnsupportedLegacy)
            {
                error = "旧账号文件格式不受支持，请重建账号";
                return false;
            }

            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindUserLocked(name);
                if (record == null) { error = "用户名或密码不正确"; return false; }
                snapshot = UserSnapshot.From(record);
            }

            AfterSnapshotForTest?.Invoke();

            if (!Verify(password, snapshot))
            {
                error = "用户名或密码不正确";
                return false;
            }

            lock (_gate)
            {
                var record = FindUserLocked(name);
                if (record == null) { error = "账号已被删除"; return false; }
                if (!string.Equals(record.Hash, snapshot.Hash, StringComparison.Ordinal))
                {
                    error = "密码已被他人修改";
                    return false;
                }
                if (!string.Equals(record.GroupId, snapshot.GroupId, StringComparison.Ordinal))
                {
                    error = "账号权限已变化，请重新登录";
                    return false;
                }

                var group = FindGroupLocked(record.GroupId);
                if (group == null)
                {
                    error = $"账号「{record.Name}」的用户组「{record.GroupId}」不存在，请管理员修正";
                    return false;
                }

                user = new ScadaUserInfo(record.Name, record.GroupId, group.Permissions.ToArray(), group.Name);
                error = null;
                return true;
            }
        }

        public bool IsUsingDefaultPassword(string? userName)
        {
            var name = Normalize(userName);
            if (!NameComparer.Equals(name, DefaultAdminName)) return false;

            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindUserLocked(name);
                if (record == null) return false;
                snapshot = UserSnapshot.From(record);
            }
            return Verify(DefaultAdminPassword, snapshot);
        }

        public bool TryChangePassword(string? userName, string? currentPassword,
                                      string? newPassword, out string? error)
        {
            var name = Normalize(userName);
            var ok = ChangePassword(name, currentPassword, newPassword, out error, out var detail);
            Audit("修改密码", name, ok, detail, error);
            RecordOperation("修改密码", name, name, ok, detail, error);
            if (ok) NotifyUserChanged(name, ScadaUserChangeKind.PasswordChanged);
            return ok;
        }

        private bool ChangePassword(string name, string? currentPassword,
                                    string? newPassword, out string? error, out string? detail)
        {
            detail = null;
            if (!EnsureWritable(out error)) return false;

            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindUserLocked(name);
                if (record == null) { error = "用户名或密码不正确"; return false; }
                snapshot = UserSnapshot.From(record);
            }

            if (!Verify(currentPassword ?? string.Empty, snapshot))
            {
                error = "用户名或密码不正确";
                return false;
            }

            if (!CheckNewPassword(newPassword, out error)) return false;
            var (salt, hash) = HashPassword(newPassword!);

            lock (_writeGate)
            {
                lock (_gate)
                {
                    var record = FindUserLocked(name);
                    if (record == null) { error = $"账号「{name}」不存在"; return false; }
                    if (!string.Equals(record.Hash, snapshot.Hash, StringComparison.Ordinal))
                    {
                        error = "密码已被他人修改";
                        return false;
                    }
                    ApplyPassword(record, salt, hash);
                }

                var ok = PersistLocked(out error);
                if (ok) detail = "密码已修改";
                return ok;
            }
        }

        public bool TryResetPassword(string? userName, string? newPassword, out string? error)
        {
            var name = Normalize(userName);
            var ok = ResetPassword(name, newPassword, out error, out var detail);
            Audit("重置密码", name, ok, detail, error);
            RecordOperation("重置密码", name, name, ok, detail, error);
            if (ok) NotifyUserChanged(name, ScadaUserChangeKind.PasswordReset);
            return ok;
        }

        private bool ResetPassword(string name, string? newPassword, out string? error, out string? detail)
        {
            detail = null;
            if (!EnsureWritable(out error)) return false;

            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindUserLocked(name);
                if (record == null) { error = $"账号「{name}」不存在"; return false; }
                snapshot = UserSnapshot.From(record);
            }

            if (!CheckNewPassword(newPassword, out error)) return false;
            AfterSnapshotForTest?.Invoke();
            var (salt, hash) = HashPassword(newPassword!);

            lock (_writeGate)
            {
                lock (_gate)
                {
                    var record = FindUserLocked(name);
                    if (record == null) { error = $"账号「{name}」不存在"; return false; }
                    if (!string.Equals(record.Hash, snapshot.Hash, StringComparison.Ordinal))
                    {
                        error = "密码已被他人修改";
                        return false;
                    }
                    ApplyPassword(record, salt, hash);
                }

                var ok = PersistLocked(out error);
                if (ok) detail = "密码已重置（管理员路径，未校验旧密码）";
                return ok;
            }
        }

        public bool TryAddUser(string? userName, string? password, string groupId, out string? error)
        {
            var name = Normalize(userName);
            var ok = AddUser(name, password, groupId, out error, out var detail);
            Audit("创建账号", name, ok, detail, error);
            RecordOperation("创建账号", name, name, ok, detail, error);
            if (ok) NotifyUserChanged(name, ScadaUserChangeKind.Added);
            return ok;
        }

        private bool AddUser(string name, string? password, string groupId,
                             out string? error, out string? detail)
        {
            detail = null;
            if (!EnsureWritable(out error)) return false;
            if (name.Length == 0) { error = "用户名不能为空"; return false; }
            if (name.Length > 32) { error = "用户名不能超过 32 个字符"; return false; }

            lock (_gate)
            {
                if (FindUserLocked(name) != null) { error = $"账号「{name}」已存在"; return false; }
                if (FindGroupLocked(groupId) == null) { error = $"用户组「{groupId}」不存在"; return false; }
            }

            if (!CheckNewPassword(password, out error)) return false;
            var (salt, hash) = HashPassword(password!);

            lock (_writeGate)
            {
                lock (_gate)
                {
                    if (FindUserLocked(name) != null) { error = $"账号「{name}」已存在"; return false; }
                    var group = FindGroupLocked(groupId);
                    if (group == null) { error = $"用户组「{groupId}」不存在"; return false; }
                    _users.Add(new ScadaUserRecord
                    {
                        Name = name,
                        Salt = salt,
                        Hash = hash,
                        Iterations = DefaultIterations,
                        GroupId = group.Id,
                    });
                    detail = $"用户组：{group.Name}";
                }

                return PersistLocked(out error);
            }
        }

        public bool TryRemoveUser(string? userName, out string? error)
        {
            var name = Normalize(userName);
            var ok = RemoveUser(name, out error, out var detail);
            Audit("删除账号", name, ok, detail, error);
            RecordOperation("删除账号", name, name, ok, detail, error);
            if (ok) NotifyUserChanged(name, ScadaUserChangeKind.Removed);
            return ok;
        }

        private bool RemoveUser(string name, out string? error, out string? detail)
        {
            detail = null;
            if (!EnsureWritable(out error)) return false;

            lock (_writeGate)
            {
                string formerGroup;
                lock (_gate)
                {
                    var record = FindUserLocked(name);
                    if (record == null) { error = $"账号「{name}」不存在"; return false; }

                    var actor = _actorProvider?.Invoke();
                    if (!string.IsNullOrWhiteSpace(actor) && NameComparer.Equals(actor!, name))
                    {
                        error = "不能删除当前登录的账号；请先用其它管理员账号登录，再删除它";
                        return false;
                    }

                    if (IsAdministrationGroupLocked(record.GroupId)
                        && CountUsersWithAdministrationLocked() <= 1)
                    {
                        error = "至少要保留一个含「用户管理」权限的账号，否则以后没人能再管理账号";
                        return false;
                    }

                    formerGroup = FindGroupLocked(record.GroupId)?.Name ?? record.GroupId;
                    _users.Remove(record);
                }

                var ok = PersistLocked(out error);
                if (ok) detail = $"原用户组：{formerGroup}";
                return ok;
            }
        }

        public bool TryChangeGroup(string? userName, string groupId, out string? error)
        {
            var name = Normalize(userName);
            var ok = ChangeGroup(name, groupId, out error, out var detail);
            Audit("变更用户组", name, ok, detail, error);
            RecordOperation("变更用户组", name, name, ok, detail, error);
            if (ok && detail != null && !detail.Contains("未变", StringComparison.Ordinal))
                NotifyUserChanged(name, ScadaUserChangeKind.GroupChanged);
            return ok;
        }

        private bool ChangeGroup(string name, string groupId, out string? error, out string? detail)
        {
            detail = null;
            if (!EnsureWritable(out error)) return false;

            lock (_writeGate)
            {
                string oldGroupName;
                string newGroupName;
                lock (_gate)
                {
                    var record = FindUserLocked(name);
                    if (record == null) { error = $"账号「{name}」不存在"; return false; }

                    var group = FindGroupLocked(groupId);
                    if (group == null) { error = $"用户组「{groupId}」不存在"; return false; }

                    if (string.Equals(record.GroupId, group.Id, StringComparison.Ordinal))
                    {
                        error = null;
                        detail = $"用户组未变（仍是{group.Name}），未写盘";
                        return true;
                    }

                    if (IsAdministrationGroupLocked(record.GroupId)
                        && !group.Permissions.Contains(ScadaPermissionKeys.UserAdministration)
                        && CountUsersWithAdministrationLocked() <= 1)
                    {
                        error = "至少要保留一个含「用户管理」权限的账号，否则以后没人能再管理账号";
                        return false;
                    }

                    oldGroupName = FindGroupLocked(record.GroupId)?.Name ?? record.GroupId;
                    newGroupName = group.Name;
                    record.GroupId = group.Id;
                }

                var ok = PersistLocked(out error);
                if (ok) detail = $"用户组：{oldGroupName} → {newGroupName}";
                return ok;
            }
        }

        public bool TryAddGroup(string name, IEnumerable<string> permissions, out string? error)
        {
            if (!EnsureWritable(out error)) return false;
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) { error = "组名不能为空"; return false; }

            var normalized = NormalizePermissions(permissions, out error);
            if (normalized == null) return false;

            string id;
            lock (_writeGate)
            {
                lock (_gate)
                {
                    if (_groups.Any(g => NameComparer.Equals(g.Name, trimmed)))
                    {
                        error = $"用户组「{trimmed}」已存在";
                        return false;
                    }

                    id = CreateGroupIdLocked(trimmed);
                    _groups.Add(new ScadaGroupInfo(id, trimmed, normalized, false));
                }

                var ok = PersistLocked(out error);
                if (ok)
                {
                    RaiseGroupChanged();
                    RecordOperation("新建用户组", trimmed, id, true, "已创建", null);
                }
                return ok;
            }
        }

        public bool TryUpdateGroup(string id, string name, IEnumerable<string> permissions, out string? error)
        {
            if (!EnsureWritable(out error)) return false;
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) { error = "组名不能为空"; return false; }

            var normalized = NormalizePermissions(permissions, out error);
            if (normalized == null) return false;

            lock (_writeGate)
            {
                lock (_gate)
                {
                    var index = _groups.FindIndex(g => string.Equals(g.Id, id, StringComparison.Ordinal));
                    if (index < 0) { error = $"用户组「{id}」不存在"; return false; }
                    if (_groups[index].IsBuiltIn)
                    {
                        error = "内置模板组不可改名 / 改权限 / 删除";
                        return false;
                    }
                    if (_groups.Any(g => !string.Equals(g.Id, id, StringComparison.Ordinal)
                                         && NameComparer.Equals(g.Name, trimmed)))
                    {
                        error = $"用户组「{trimmed}」已存在";
                        return false;
                    }

                    _groups[index] = new ScadaGroupInfo(id, trimmed, normalized, false);
                }

                var ok = PersistLocked(out error);
                if (ok)
                {
                    RaiseGroupChanged();
                    RecordOperation("修改用户组", trimmed, id, true, "已保存", null);
                }
                return ok;
            }
        }

        public bool RevokeGroup(string id, out string? error)
        {
            if (!EnsureWritable(out error)) return false;

            lock (_writeGate)
            {
                lock (_gate)
                {
                    var group = FindGroupLocked(id);
                    if (group == null) { error = $"用户组「{id}」不存在"; return false; }
                    if (group.IsBuiltIn) { error = "内置模板组不可撤销"; return false; }

                    var userCount = _users.Count(u => string.Equals(u.GroupId, id, StringComparison.Ordinal));
                    if (userCount > 0)
                    {
                        error = $"该组仍有 {userCount} 个账号引用，先改到其它组再撤销";
                        return false;
                    }

                    if (group.Permissions.Contains(ScadaPermissionKeys.UserAdministration)
                        && _groups.Count(g => g.Permissions.Contains(ScadaPermissionKeys.UserAdministration)) <= 1)
                    {
                        error = "撤销后没有任何组含「用户管理」权限，管理入口会被锁死";
                        return false;
                    }

                    _groups.Remove(group);
                }

                var ok = PersistLocked(out error);
                if (ok)
                {
                    RaiseGroupChanged();
                    RecordOperation("撤销用户组", id, id, true, "已撤销", null);
                }
                return ok;
            }
        }

        /// <summary>
        /// 预览「新建权限」会拿到的 Key（只读：不写盘、不改目录）。
        /// 与 <see cref="TryAddPermission"/> 走同一个 <see cref="CreatePermissionKeyLocked"/>，
        /// 界面预览与落盘真值因此逐字相同——预览自己算一份，迟早会和真值分叉。
        /// </summary>
        public string PreviewPermissionKey(string? displayName)
        {
            var name = (displayName ?? string.Empty).Trim();
            lock (_writeGate)
            {
                lock (_gate)
                {
                    return CreatePermissionKeyLocked(name);
                }
            }
        }

        public bool TryAddPermission(string displayName, out string? key, out string? error)
        {            key = null;
            if (!EnsureWritable(out error)) return false;
            var name = (displayName ?? string.Empty).Trim();
            if (name.Length == 0) { error = "权限显示名不能为空"; return false; }

            lock (_writeGate)
            {
                lock (_gate)
                {
                    if (_catalog.Count >= ScadaPermissionKeys.MaxCatalogCount)
                    {
                        error = $"权限目录已达上限 {ScadaPermissionKeys.MaxCatalogCount} 条，删除或合并后再建";
                        return false;
                    }
                    if (_catalog.Any(p => NameComparer.Equals(p.Name, name)))
                    {
                        error = $"权限「{name}」已存在";
                        return false;
                    }

                    key = CreatePermissionKeyLocked(name);
                    _catalog.Add(new ScadaPermissionInfo(key, name, false));
                }

                var ok = PersistLocked(out error);
                if (ok) RaiseGroupChanged();
                return ok;
            }
        }

        public bool TryRenamePermission(string key, string displayName, out string? error)
        {
            if (!EnsureWritable(out error)) return false;
            var name = (displayName ?? string.Empty).Trim();
            if (name.Length == 0) { error = "权限显示名不能为空"; return false; }

            lock (_writeGate)
            {
                lock (_gate)
                {
                    var index = _catalog.FindIndex(p => string.Equals(p.Key, key, StringComparison.Ordinal));
                    if (index < 0) { error = $"权限「{key}」不存在"; return false; }
                    if (_catalog.Any(p => !string.Equals(p.Key, key, StringComparison.Ordinal)
                                          && NameComparer.Equals(p.Name, name)))
                    {
                        error = $"权限「{name}」已存在";
                        return false;
                    }
                    _catalog[index] = _catalog[index] with { Name = name };
                }

                var ok = PersistLocked(out error);
                if (ok) RaiseGroupChanged();
                return ok;
            }
        }

        public bool TryRemovePermission(string key, out string? error)
        {
            if (ScadaPermissionKeys.IsBuiltIn(key))
            {
                error = "内置权限不可删除";
                return false;
            }

            if (_permissionReferenceCounter == null)
            {
                error = "未接线按钮引用扫描，删除权限被拒绝（fail-closed）";
                return false;
            }

            if (!EnsureWritable(out error)) return false;

            var buttonRefs = _permissionReferenceCounter(key);
            lock (_writeGate)
            {
                lock (_gate)
                {
                    var index = _catalog.FindIndex(p => string.Equals(p.Key, key, StringComparison.Ordinal));
                    if (index < 0) { error = $"权限「{key}」不存在"; return false; }

                    var groupRefs = _groups.Count(g => g.Permissions.Contains(key, StringComparer.Ordinal));
                    if (groupRefs > 0 || buttonRefs > 0)
                    {
                        error = $"{groupRefs} 个组 / {buttonRefs} 个按钮引用，不能删除";
                        return false;
                    }

                    _catalog.RemoveAt(index);
                }

                var ok = PersistLocked(out error);
                if (ok) RaiseGroupChanged();
                return ok;
            }
        }

        public bool BindSolution(Guid id, string? solutionName, string? root, out string? error)
        {
            error = null;
            lock (_writeGate)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    var seed = CreateDefaultAdminRecord();
                    lock (_gate)
                    {
                        _users.Clear(); _users.Add(seed);
                        _groups.Clear(); _groups.AddRange(ScadaGroupDefaults.CreateDefaultTemplates());
                        _catalog.Clear(); _catalog.AddRange(ScadaPermissionKeys.BuiltIn);
                        _bookState = ScadaAccountBookState.UnsavedSolution;
                        _bookSource = ScadaBookSource.Factory;
                        _boundSolutionId = id;
                        _boundSolutionRoot = null;
                        _storePath = _groupsPath = _permissionsPath = null;
                        _pendingBadFile = null;
                        LastLoadError = null;
                    }
                }
                else
                {
                    if (!Directory.Exists(root))
                    {
                        error = $"方案根目录不可访问，账号本未绑定。路径：{root}";
                        return false;
                    }

                    var usersPath = UsersFilePath(root);
                    var groupsPath = GroupsFilePath(root);
                    var permsPath = PermissionsFilePath(root);

                    var users = new List<ScadaUserRecord>();
                    var groups = new List<ScadaGroupInfo>();
                    var catalog = new List<ScadaPermissionInfo>();

                    var userOutcome = ReadUsersFile(usersPath, users, out var userError);
                    if (userOutcome == BookLoadOutcome.IoFailed)
                    {
                        error = $"账号文件读取失败，账号本未绑定。路径：{usersPath}。原因：{userError}";
                        return false;
                    }

                    var groupOutcome = ReadGroupsFile(groupsPath, groups, out var groupError);
                    if (groupOutcome == BookLoadOutcome.IoFailed)
                    {
                        error = $"组文件读取失败，账号本未绑定。路径：{groupsPath}。原因：{groupError}";
                        return false;
                    }

                    var permOutcome = ReadPermissionsFile(permsPath, catalog, out var permError);
                    if (permOutcome == BookLoadOutcome.IoFailed)
                    {
                        error = $"权限目录读取失败，账号本未绑定。路径：{permsPath}。原因：{permError}";
                        return false;
                    }

                    if (groups.Count == 0)
                        groups.AddRange(ScadaGroupDefaults.CreateDefaultTemplates());
                    if (catalog.Count == 0)
                        catalog.AddRange(ScadaPermissionKeys.BuiltIn);

                    var source = userOutcome switch
                    {
                        BookLoadOutcome.Loaded => ScadaBookSource.Solution,
                        BookLoadOutcome.UnsupportedLegacy => ScadaBookSource.UnsupportedLegacy,
                        _ => ScadaBookSource.Factory
                    };

                    if (userOutcome == BookLoadOutcome.UnsupportedLegacy)
                    {
                        users.Clear();
                    }
                    else if (userOutcome is BookLoadOutcome.Missing or BookLoadOutcome.Corrupt)
                    {
                        users.Clear();
                        users.Add(CreateDefaultAdminRecord());
                    }

                    lock (_gate)
                    {
                        _users.Clear(); _users.AddRange(users);
                        _groups.Clear(); _groups.AddRange(groups);
                        _catalog.Clear(); _catalog.AddRange(catalog);
                        _bookState = ScadaAccountBookState.PersistedSolution;
                        _bookSource = source;
                        _boundSolutionId = id;
                        _boundSolutionRoot = root;
                        _storePath = usersPath;
                        _groupsPath = groupsPath;
                        _permissionsPath = permsPath;
                        _pendingBadFile = userOutcome == BookLoadOutcome.Corrupt ? usersPath : null;
                        LastLoadError = userOutcome switch
                        {
                            BookLoadOutcome.UnsupportedLegacy => "旧账号文件格式不受支持，请重建账号",
                            BookLoadOutcome.Corrupt => userError,
                            BookLoadOutcome.IoFailed => userError,
                            _ => groupOutcome == BookLoadOutcome.Corrupt ? groupError
                                 : permOutcome == BookLoadOutcome.Corrupt ? permError : null
                        };
                    }
                }
            }

            RaiseBookChanged();
            return true;
        }

        public bool TryRebuildAccountBook(out string? error)
        {
            error = null;
            lock (_writeGate)
            {
                if (BookSource != ScadaBookSource.UnsupportedLegacy)
                {
                    error = "当前账号本不是旧格式，无需重建";
                    return false;
                }

                var path = _storePath;
                if (string.IsNullOrEmpty(path))
                {
                    error = "账号文件路径未知，无法重建";
                    return false;
                }

                try
                {
                    File.Move(path, path + ".legacy-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                        overwrite: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = $"旧账号文件无法改名保留：{ex.GetType().Name}：{ex.Message}";
                    return false;
                }

                lock (_gate)
                {
                    _users.Clear();
                    _users.Add(CreateDefaultAdminRecord());
                    _groups.Clear(); _groups.AddRange(ScadaGroupDefaults.CreateDefaultTemplates());
                    _catalog.Clear(); _catalog.AddRange(ScadaPermissionKeys.BuiltIn);
                    _bookSource = ScadaBookSource.Solution;
                    _pendingBadFile = null;
                    LastLoadError = null;
                }

                var ok = WriteAllCore(out error);
                if (ok)
                {
                    lock (_gate) _bookState = ScadaAccountBookState.PersistedSolution;
                }
                RaiseBookChanged();
                return ok;
            }
        }

        public bool AttachSolutionFolder(string root, out string? error)
        {
            error = null;
            lock (_writeGate)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    error = "方案根路径为空，无法写入账号文件";
                    return false;
                }

                var usersPath = UsersFilePath(root);
                var existed = File.Exists(usersPath);

                lock (_gate)
                {
                    _storePath = usersPath;
                    _groupsPath = GroupsFilePath(root);
                    _permissionsPath = PermissionsFilePath(root);
                    _boundSolutionRoot = root;
                }

                if (WriteAllCore(out error))
                {
                    lock (_gate)
                    {
                        _bookState = ScadaAccountBookState.PersistedSolution;
                        _bookSource = ScadaBookSource.Solution;
                    }
                    RaiseBookChanged();
                    return true;
                }

                if (existed)
                {
                    LoadCore();
                    lock (_gate) _bookState = ScadaAccountBookState.PersistedSolution;
                }
                else
                {
                    lock (_gate) _bookState = ScadaAccountBookState.UnsavedSolution;
                }

                RaiseBookChanged();
                return false;
            }
        }

        public void Unbind()
        {
            lock (_writeGate)
            {
                lock (_gate)
                {
                    _users.Clear();
                    _groups.Clear(); _groups.AddRange(ScadaGroupDefaults.CreateDefaultTemplates());
                    _catalog.Clear(); _catalog.AddRange(ScadaPermissionKeys.BuiltIn);
                    _bookState = ScadaAccountBookState.NoSolution;
                    _bookSource = ScadaBookSource.Unbound;
                    _boundSolutionId = null;
                    _boundSolutionRoot = null;
                    _storePath = _groupsPath = _permissionsPath = null;
                    _pendingBadFile = null;
                    LastLoadError = null;
                }
            }
            RaiseBookChanged();
        }

        public void Load()
        {
            lock (_writeGate) LoadCore();
            RaiseBookChanged();
        }

        public bool Save(out string? error)
        {
            if (!EnsureWritable(out error)) return false;
            lock (_writeGate) return WriteAllCore(out error);
        }

        private void LoadCore()
        {
            var users = new List<ScadaUserRecord>();
            var groups = new List<ScadaGroupInfo>();
            var catalog = new List<ScadaPermissionInfo>();
            var outcome = ReadUsersFile(_storePath, users, out var loadError);
            ReadGroupsFile(_groupsPath, groups, out _);
            ReadPermissionsFile(_permissionsPath, catalog, out _);

            if (outcome == BookLoadOutcome.IoFailed)
                loadError = "账号文件读取失败，已回落到出厂账号。原因：" + loadError;

            var seed = outcome is BookLoadOutcome.Loaded ? null : CreateDefaultAdminRecord();

            lock (_gate)
            {
                _users.Clear();
                if (seed != null) _users.Add(seed); else _users.AddRange(users);
                _groups.Clear();
                _groups.AddRange(groups.Count > 0 ? groups : ScadaGroupDefaults.CreateDefaultTemplates());
                _catalog.Clear();
                _catalog.AddRange(catalog.Count > 0 ? catalog : ScadaPermissionKeys.BuiltIn);
                _bookSource = outcome == BookLoadOutcome.UnsupportedLegacy
                    ? ScadaBookSource.UnsupportedLegacy
                    : (outcome == BookLoadOutcome.Loaded ? ScadaBookSource.Solution : ScadaBookSource.Factory);
                LastLoadError = outcome is BookLoadOutcome.Loaded or BookLoadOutcome.Missing ? null : loadError;
            }
        }

        private bool EnsureWritable(out string? error)
        {
            if (BookSource == ScadaBookSource.UnsupportedLegacy)
            {
                error = "旧账号文件格式不受支持，请重建账号";
                return false;
            }

            switch (BookState)
            {
                case ScadaAccountBookState.PersistedSolution:
                    error = null;
                    return true;
                case ScadaAccountBookState.UnsavedSolution:
                    error = "方案尚未保存，账号修改会在保存方案后生效；请先保存方案";
                    return false;
                default:
                    error = "尚未打开方案，账号本未绑定；请先打开或新建方案";
                    return false;
            }
        }

        private bool PersistLocked(out string? error)
        {
            if (WriteAllCore(out error)) return true;
            LoadCore();
            return false;
        }

        private bool WriteAllCore(out string? error)
        {
            string permsJson, groupsJson, usersJson;
            string? permsPath, groupsPath, usersPath;
            lock (_gate)
            {
                permsPath = _permissionsPath;
                groupsPath = _groupsPath;
                usersPath = _storePath;
                permsJson = JsonConvert.SerializeObject(_catalog, JsonOptions);
                groupsJson = JsonConvert.SerializeObject(_groups, JsonOptions);
                usersJson = JsonConvert.SerializeObject(_users, JsonOptions);
            }

            if (string.IsNullOrEmpty(usersPath) || string.IsNullOrEmpty(groupsPath) || string.IsNullOrEmpty(permsPath))
            {
                error = "账号文件路径未知，无法写入；请先保存方案";
                return false;
            }

            var plan = new[] { (permsPath!, permsJson), (groupsPath!, groupsJson), (usersPath!, usersJson) };
            var snapshots = new List<(string Path, byte[]? Before)>();

            try
            {
                var dir = Path.GetDirectoryName(usersPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (!QuarantineBadFile(usersPath, out error)) return false;

                foreach (var (path, json) in plan)
                {
                    snapshots.Add((path, File.Exists(path) ? File.ReadAllBytes(path) : null));
                    WriteAtomic(path, json);
                }

                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Rollback(snapshots);
                error = $"账号本保存失败：{ex.GetType().Name}：{ex.Message}";
                return false;
            }
        }

        private static void WriteAtomic(string path, string json)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static void Rollback(List<(string Path, byte[]? Before)> snapshots)
        {
            foreach (var (path, before) in snapshots)
            {
                try
                {
                    if (before == null) { if (File.Exists(path)) File.Delete(path); }
                    else File.WriteAllBytes(path, before);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ScadaUserStore] 回滚 {path} 失败：{ex.GetType().Name}：{ex.Message}");
                }
            }
        }

        private bool QuarantineBadFile(string path, out string? error)
        {
            string? pending;
            lock (_gate) pending = _pendingBadFile;

            if (pending == null || !File.Exists(pending))
            {
                lock (_gate) _pendingBadFile = null;
                error = null;
                return true;
            }

            try
            {
                File.Move(pending, pending + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                lock (_gate) _pendingBadFile = null;
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"原账号文件已损坏但无法改名保留（{ex.GetType().Name}：{ex.Message}）；" +
                        $"为不覆盖故障现场，本次未写入。路径：{pending}";
                return false;
            }
        }

        private enum BookLoadOutcome { Loaded, Missing, Corrupt, IoFailed, UnsupportedLegacy }

        private static BookLoadOutcome ReadUsersFile(string? path,
                                                     List<ScadaUserRecord> accepted,
                                                     out string? loadError)
        {
            loadError = null;
            if (string.IsNullOrEmpty(path)) return BookLoadOutcome.Missing;

            string json;
            try
            {
                if (!File.Exists(path)) return BookLoadOutcome.Missing;
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loadError = $"{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.IoFailed;
            }

            JToken root;
            try { root = JToken.Parse(json); }
            catch (Exception ex)
            {
                loadError = $"账号文件内容无法解析：{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.Corrupt;
            }

            if (root is not JArray array)
            {
                loadError = "旧账号文件格式不受支持，请重建账号";
                return BookLoadOutcome.UnsupportedLegacy;
            }

            if (array.OfType<JObject>().Any(o => o.Property("Role") != null
                                                 || string.IsNullOrWhiteSpace((string?)o["GroupId"])))
            {
                loadError = "旧账号文件格式不受支持，请重建账号";
                return BookLoadOutcome.UnsupportedLegacy;
            }

            List<ScadaUserRecord>? loaded;
            try
            {
                loaded = array.ToObject<List<ScadaUserRecord>>(JsonSerializer.Create(JsonOptions));
            }
            catch (Exception ex)
            {
                loadError = $"账号文件内容无法解析：{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.Corrupt;
            }

            if (loaded == null || loaded.Count == 0)
            {
                loadError = "账号文件里没有任何账号，已回落到出厂账号";
                return BookLoadOutcome.Corrupt;
            }

            var seen = new HashSet<string>(NameComparer);
            foreach (var record in loaded)
            {
                if (record == null) continue;
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                if (string.IsNullOrWhiteSpace(record.Salt)) continue;
                if (string.IsNullOrWhiteSpace(record.Hash)) continue;
                if (record.Iterations <= 0) continue;
                if (!seen.Add(record.Name)) continue;
                accepted.Add(record);
            }

            if (accepted.Count == 0)
            {
                loadError = "账号文件里没有一条可用记录，已回落到出厂账号";
                return BookLoadOutcome.Corrupt;
            }

            return BookLoadOutcome.Loaded;
        }

        private static BookLoadOutcome ReadGroupsFile(string? path, List<ScadaGroupInfo> accepted, out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                accepted.AddRange(ScadaGroupDefaults.CreateDefaultTemplates());
                return BookLoadOutcome.Missing;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<List<ScadaGroupInfo>>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (loaded == null || loaded.Count == 0) throw new InvalidDataException("组文件为空");
                accepted.AddRange(loaded.Where(g => !string.IsNullOrWhiteSpace(g.Id)));
                if (accepted.Count == 0) throw new InvalidDataException("组文件没有可用记录");
                return BookLoadOutcome.Loaded;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.IoFailed;
            }
            catch (Exception ex)
            {
                error = $"组文件内容无法解析：{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.Corrupt;
            }
        }

        private static BookLoadOutcome ReadPermissionsFile(string? path,
                                                           List<ScadaPermissionInfo> accepted,
                                                           out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                accepted.AddRange(ScadaPermissionKeys.BuiltIn);
                return BookLoadOutcome.Missing;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<List<ScadaPermissionInfo>>(
                    File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                if (loaded == null || loaded.Count == 0) throw new InvalidDataException("权限目录为空");
                accepted.AddRange(loaded.Where(p => !string.IsNullOrWhiteSpace(p.Key)));
                if (accepted.Count == 0) throw new InvalidDataException("权限目录没有可用记录");
                return BookLoadOutcome.Loaded;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.IoFailed;
            }
            catch (Exception ex)
            {
                error = $"权限目录无法解析：{ex.GetType().Name}：{ex.Message}";
                return BookLoadOutcome.Corrupt;
            }
        }

        private IReadOnlyList<string>? NormalizePermissions(IEnumerable<string> permissions, out string? error)
        {
            var list = new List<string>();
            lock (_gate)
            {
                foreach (var permission in permissions ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(permission)) continue;
                    if (!_catalog.Any(p => string.Equals(p.Key, permission, StringComparison.Ordinal)))
                    {
                        error = $"权限「{permission}」不在目录里";
                        return null;
                    }
                    if (!list.Contains(permission, StringComparer.Ordinal)) list.Add(permission);
                }
            }
            error = null;
            return list;
        }

        private string CreateGroupIdLocked(string name)
        {
            var slug = new string(name.Where(char.IsLetterOrDigit).ToArray());
            if (slug.Length == 0) slug = "Group";
            var baseId = "G-" + slug;
            var id = baseId;
            var suffix = 2;
            while (_groups.Any(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)))
                id = baseId + suffix++;
            return id;
        }

        private string CreatePermissionKeyLocked(string name)
        {
            var ascii = new string(name.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray());
            var baseKey = ascii.Length > 0
                ? char.ToUpperInvariant(ascii[0]) + ascii.Substring(1)
                : "CustomPerm";
            var key = baseKey;
            var suffix = 2;
            while (_catalog.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)))
                key = baseKey + suffix++;
            return key;
        }

        private ScadaUserRecord? FindUserLocked(string name)
            => name.Length == 0 ? null : _users.FirstOrDefault(u => NameComparer.Equals(u.Name, name));

        private ScadaGroupInfo? FindGroupLocked(string? id)
            => string.IsNullOrWhiteSpace(id)
                ? null
                : _groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal));

        private bool IsAdministrationGroupLocked(string groupId)
            => FindGroupLocked(groupId)?.Permissions.Contains(ScadaPermissionKeys.UserAdministration) == true;

        private int CountUsersWithAdministrationLocked()
            => _users.Count(u => IsAdministrationGroupLocked(u.GroupId));

        private static (string Salt, string Hash) HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            return (Convert.ToBase64String(salt),
                    Convert.ToBase64String(Derive(password, salt, DefaultIterations)));
        }

        private static void ApplyPassword(ScadaUserRecord record, string salt, string hash)
        {
            record.Salt = salt;
            record.Hash = hash;
            record.Iterations = DefaultIterations;
        }

        private static bool Verify(string password, in UserSnapshot snapshot)
            => Verify(password.AsSpan(), snapshot);

        private static bool Verify(ReadOnlySpan<char> password, in UserSnapshot snapshot)
            => Verify(password, snapshot.Salt, snapshot.Hash, snapshot.Iterations);

        private static bool Verify(ReadOnlySpan<char> password,
                                   string saltBase64, string hashBase64, int iterations)
        {
            try
            {
                var salt = Convert.FromBase64String(saltBase64);
                var expected = Convert.FromBase64String(hashBase64);
                var actual = Derive(password, salt, iterations);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
            => Derive(password.AsSpan(), salt, iterations);

        private static byte[] Derive(ReadOnlySpan<char> password, byte[] salt, int iterations)
            => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);

        private bool CheckNewPassword(string? password, out string? error)
        {
            if (string.IsNullOrWhiteSpace(password)) { error = "密码不能为空"; return false; }
            if (password.Length < MinPasswordLength)
            {
                error = $"密码至少 {MinPasswordLength} 个字符";
                return false;
            }
            error = null;
            return true;
        }

        private static ScadaUserRecord CreateDefaultAdminRecord()
        {
            var (salt, hash) = HashPassword(DefaultAdminPassword);
            return new ScadaUserRecord
            {
                Name = DefaultAdminName,
                Salt = salt,
                Hash = hash,
                Iterations = DefaultIterations,
                GroupId = ScadaGroupDefaults.AdminTemplateId,
            };
        }

        private static string Normalize(string? userName) => (userName ?? string.Empty).Trim();

        private void NotifyUserChanged(string userName, ScadaUserChangeKind kind)
        {
            var handler = UserChanged;
            if (handler == null) return;
            try { handler(userName, kind); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ScadaUserStore] 账号变更通知失败（{userName}）：{ex.GetType().Name}：{ex.Message}");
            }
        }

        private void RaiseBookChanged()
        {
            var handler = BookChanged;
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ScadaUserStore] 账号本变更通知失败：{ex.GetType().Name}：{ex.Message}");
            }
        }

        private void RaiseGroupChanged()
        {
            var handler = GroupChanged;
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ScadaUserStore] 组/目录变更通知失败：{ex.GetType().Name}：{ex.Message}");
            }
        }

        private void Audit(string actionText, string subject, bool ok, string? detail, string? error)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                subject,
                AuditEventText,
                actionText,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        /// <summary>
        /// 软件级操作记录（SQLite）。与 <see cref="Audit"/> 并行的第二条出口：
        /// 方案审计 CSV 跟着方案走，账号本被删/换方案后就查不到；而"谁把谁的权限改了"
        /// 是必须跨方案、跨重启能查的事实，所以同时写软件库。
        /// 记录失败不影响账号操作（fail-open）。
        /// </summary>
        private void RecordOperation(string actionText, string subject, string subjectId, bool ok,
                                     string? detail, string? error)
        {
            SoftwareOperationLogHost.SafeRecord(entry =>
            {
                entry.Category = "Permission";
                entry.Action = actionText;
                entry.Outcome = ok ? "成功" : "拒绝";
                entry.Actor = _actorProvider?.Invoke() ?? string.Empty;
                entry.Subject = subject;
                entry.SubjectId = subjectId;
                entry.Detail = ok ? (detail ?? string.Empty) : (error ?? string.Empty);
            });
        }

        private readonly record struct UserSnapshot(string Name, string Salt, string Hash, int Iterations, string GroupId)
        {
            public static UserSnapshot From(ScadaUserRecord record)
                => new(record.Name, record.Salt, record.Hash, record.Iterations, record.GroupId);
        }

        private sealed class ScadaUserRecord
        {
            public string Name { get; set; } = string.Empty;
            public string Salt { get; set; } = string.Empty;
            public string Hash { get; set; } = string.Empty;
            public int Iterations { get; set; }
            public string GroupId { get; set; } = string.Empty;
        }
    }
}
