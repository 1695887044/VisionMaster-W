using System;
using System.IO;
using System.Linq;
using System.Windows;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案切换 = 会话边界。唯一职责：切方案时登出旧身份、重绑账号本、更新审计标签；
    /// 组/用户变更时重判权限或强制登出。Shell 只订阅 <see cref="SessionBoundaryChanged"/> 刷命令。
    /// </summary>
    public sealed class SolutionSessionCoordinator
    {
        private readonly IReadOnlyWorkspaceContext? _workspace;
        private readonly ScadaUserStore _store;
        private readonly ScadaAccessPolicy _accessPolicy;
        private readonly SolutionIdentityHolder _identity;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<IScadaWebSessionAdmin?>? _webSessionAdminProvider;
        private readonly Action<string>? _onNotify;
        private bool _started;

        public SolutionSessionCoordinator(
            IReadOnlyWorkspaceContext? workspace,
            ScadaUserStore store,
            ScadaAccessPolicy accessPolicy,
            SolutionIdentityHolder identity,
            ScadaAuditWriter? audit = null,
            Func<IScadaWebSessionAdmin?>? webSessionAdminProvider = null,
            Action<string>? onNotify = null)
        {
            _workspace = workspace;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _audit = audit;
            _webSessionAdminProvider = webSessionAdminProvider;
            _onNotify = onNotify;
        }

        public int RevokedWebSessionCount { get; private set; }

        public event Action? SessionBoundaryChanged;

        public void Start()
        {
            if (_started) return;
            _started = true;
            if (_workspace != null) _workspace.SolutionSwitched += HandleSolutionSwitched;
            _store.UserChanged += HandleUserChanged;
            _store.GroupChanged += OnStoreGroupChanged;
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;
            if (_workspace != null) _workspace.SolutionSwitched -= HandleSolutionSwitched;
            _store.UserChanged -= HandleUserChanged;
            _store.GroupChanged -= OnStoreGroupChanged;
        }

        public void HandleSolutionSwitched(SolutionModel? solution)
            => Run(() => HandleSolutionSwitchedCore(solution));

        private void HandleSolutionSwitchedCore(SolutionModel? solution)
        {
            var newId = solution?.SolutionId;
            var newName = solution?.SolutionName ?? string.Empty;
            var newRoot = solution == null ? null : ResolveRoot(solution);
            var persisted = solution?.IsIdentityPersisted ?? false;

            // 同 id + 同 root：只是再次打开当前方案，不能把用户踢下线；但账号本必须已绑定。
            if (newId == _identity.SolutionId
                && string.Equals(newRoot, _identity.SolutionRoot, StringComparison.OrdinalIgnoreCase)
                && _store.BoundSolutionId == newId)
            {
                _identity.UpdateRoot(newRoot, persisted);
                return;
            }

            var oldName = _identity.SolutionName;
            var oldUser = _accessPolicy.CurrentUserName;
            var wasLoggedIn = _accessPolicy.IsLoggedIn;

            // 审计必须在 identity.Set 之前：这一行记的是「从哪个方案登出」。
            if (wasLoggedIn)
            {
                _audit?.Append(new ScadaAuditEntry(
                    oldUser,
                    string.IsNullOrEmpty(oldName) ? "未打开方案" : oldName,
                    "方案切换",
                    "切换方案",
                    ScadaAuditOutcome.Success,
                    "旧方案：" + (string.IsNullOrEmpty(oldName) ? "<无>" : oldName)));
                _accessPolicy.Logout();
            }

            _identity.Set(newId, newName, newRoot, persisted);

            if (newId == null)
            {
                _store.Unbind();
            }
            else
            {
                if (!_store.BindSolution(newId.Value, newName, newRoot, out var error))
                {
                    _store.Unbind();
                    _onNotify?.Invoke("账号本绑定失败：" + (error ?? "未知原因"));
                }
            }

            SessionBoundaryChanged?.Invoke();
        }

        private static string? ResolveRoot(SolutionModel solution)
        {
            if (string.IsNullOrWhiteSpace(solution.SolutionFilePath)) return null;
            try
            {
                return SolutionPaths.GetSolutionRoot(solution.SolutionFilePath)
                       ?? Path.GetDirectoryName(solution.SolutionFilePath);
            }
            catch
            {
                return Path.GetDirectoryName(solution.SolutionFilePath);
            }
        }

        public void NotifySolutionSaved(SolutionModel solution, string filePath)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            var root = ResolveRoot(solution) ?? Path.GetDirectoryName(filePath);
            solution.IsIdentityPersisted = true;
            _identity.UpdateRoot(root, true);

            if (root == null)
            {
                _onNotify?.Invoke("方案已保存，但取不到方案根目录，账号本未写入");
                return;
            }

            if (!_store.AttachSolutionFolder(root, out var error))
            {
                _onNotify?.Invoke("方案已保存，但账号本写入失败：" + (error ?? "未知原因"));
                return;
            }

            SessionBoundaryChanged?.Invoke();
        }

        public void HandleGroupChanged(string groupId)
        {
            var web = _webSessionAdminProvider?.Invoke();
            if (web != null)
                RevokedWebSessionCount += web.RevokeGroup(groupId);

            if (!string.Equals(groupId, _accessPolicy.CurrentGroupId, StringComparison.Ordinal))
                return;

            var group = _store.Groups.FirstOrDefault(g =>
                string.Equals(g.Id, groupId, StringComparison.Ordinal));
            if (group == null)
            {
                ForceLogout("所在用户组已被撤销");
                return;
            }

            _accessPolicy.RefreshPermissions(group.Id, group.Name, group.Permissions);
            SessionBoundaryChanged?.Invoke();
        }

        private void OnStoreGroupChanged()
        {
            var current = _accessPolicy.CurrentGroupId;
            if (string.IsNullOrEmpty(current)) return;

            var group = _store.Groups.FirstOrDefault(g =>
                string.Equals(g.Id, current, StringComparison.Ordinal));
            if (group == null)
            {
                HandleGroupChanged(current);
                return;
            }

            // 改的是别的组：当前权限快照没变，不触发会话边界（否则改个显示名也会刷命令）。
            if (!_accessPolicy.Permissions.SequenceEqual(group.Permissions))
                HandleGroupChanged(current);
        }

        public void HandleUserChanged(string userName, ScadaUserChangeKind kind)
        {
            if (!string.Equals(userName, _accessPolicy.CurrentUserName, StringComparison.OrdinalIgnoreCase))
                return;

            if (kind == ScadaUserChangeKind.Removed)
            {
                ForceLogout("账号已被删除");
                return;
            }

            if (kind == ScadaUserChangeKind.GroupChanged)
            {
                var user = _store.Users.FirstOrDefault(u =>
                    string.Equals(u.Name, userName, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    ForceLogout("账号已被删除");
                    return;
                }

                var group = _store.Groups.FirstOrDefault(g =>
                    string.Equals(g.Id, user.GroupId, StringComparison.Ordinal));
                if (group == null)
                {
                    ForceLogout("所在用户组不存在");
                    return;
                }

                _accessPolicy.RefreshPermissions(group.Id, group.Name, group.Permissions);
                SessionBoundaryChanged?.Invoke();
            }
        }

        private void ForceLogout(string reason)
        {
            var user = _accessPolicy.CurrentUserName;
            if (_accessPolicy.IsLoggedIn)
            {
                _audit?.Append(new ScadaAuditEntry(
                    user, _identity.SolutionName, "会话边界", "强制登出",
                    ScadaAuditOutcome.Denied, reason));
                _accessPolicy.Logout();
            }
            SessionBoundaryChanged?.Invoke();
        }

        /// <summary>无 Application（断言/探针）时同步执行；有 Application 时投到 UI 队列，避免自锁。</summary>
        private static void Run(Action action)
        {
            var app = Application.Current;
            if (app == null)
            {
                action();
                return;
            }

            app.Dispatcher.BeginInvoke(action);
        }
    }
}
