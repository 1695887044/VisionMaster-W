using System;
using System.IO;
using System.Linq;
using System.Windows;
using VisionMaster.Models;
using VisionMaster.Scada;

using Core.Interfaces.Operations;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案切换 = 会话边界。唯一职责：切方案时登出旧身份、重绑账号本、更新审计标签；
    /// 组/用户变更时重判权限或强制登出。Shell 只订阅 <see cref="SessionBoundaryChanged"/> 刷命令。
    ///
    /// 唯一的例外是"同一账号密码"：切方案前先把本次会话的凭据取出来，绑定新账号本后
    /// 用它在<b>新方案</b>重验一次，验过就按新方案的组重新登录（见 <see cref="TryCarryOverSession"/>）。
    /// 这不是"跳过登录"，而是"用同一份凭据在新方案再登一次"——权限永远来自新方案的账号本。
    ///
    /// 另有一条<b>只在未上线构建</b>生效的便利：首次打开方案时若没人登录，用出厂管理员
    /// <c>admin/admin123</c> 自动登录（见 <see cref="TryAutoLoginDebugAdmin"/>）。
    /// 发版构建（<see cref="DebugModeBuild.IsPreRelease"/> = false）永远不自动登录。
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
        private readonly ScadaSessionCredentialVault? _credentials;
        private readonly Func<bool>? _debugModeEnabledProvider;
        private bool _started;
        private bool _debugAutoLoginAttempted;

        /// <summary>
        /// 是否允许"开发构建默认管理员"自动登录。默认取 <see cref="DebugModeBuild.IsPreRelease"/>；
        /// 断言宿主会关掉它——否则每个夹具在绑定方案时都会自动登成 admin，把被测路径掩盖掉。
        /// </summary>
        internal bool DebugAutoLoginAllowed { get; set; } = DebugModeBuild.IsPreRelease;

        public SolutionSessionCoordinator(
            IReadOnlyWorkspaceContext? workspace,
            ScadaUserStore store,
            ScadaAccessPolicy accessPolicy,
            SolutionIdentityHolder identity,
            ScadaAuditWriter? audit = null,
            Func<IScadaWebSessionAdmin?>? webSessionAdminProvider = null,
            Action<string>? onNotify = null,
            ScadaSessionCredentialVault? credentials = null,
            Func<bool>? debugModeEnabledProvider = null)
        {
            _workspace = workspace;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _audit = audit;
            _webSessionAdminProvider = webSessionAdminProvider;
            _onNotify = onNotify;
            _credentials = credentials;
            _debugModeEnabledProvider = debugModeEnabledProvider;
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

            // 软件级记录：方案生命周期是"跟机器走"的事实，必须留在软件库里
            // （方案自己的审计 CSV 会随方案搬走，事后没人能回答"这台机器上周开过哪几个方案"）。
            SoftwareOperationLogHost.SafeRecord(entry =>
            {
                entry.Category = "Solution";
                entry.Action = solution == null ? "关闭方案" : "打开方案";
                entry.Outcome = "成功";
                entry.Actor = oldUser ?? string.Empty;
                entry.Subject = newName;
                entry.SolutionId = newId;
                entry.SolutionName = newName;
                entry.Detail = string.IsNullOrWhiteSpace(oldName) ? string.Empty : "上一个：" + oldName;
            });

            // 凭据必须在 Logout 之前取走：Logout 会经 LoggedOut 事件清空凭据仓
            // （见 ScadaSessionCredentialVault 的三条边界②③）。顺序反了就拿不到材料，
            // 表现是"明明两边账号密码一样，切过去还是登出"。
            using var credential = wasLoggedIn ? _credentials?.Capture() : null;

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

            var bound = false;
            if (newId == null)
            {
                _store.Unbind();
            }
            else
            {
                bound = _store.BindSolution(newId.Value, newName, newRoot, out var error);
                if (!bound)
                {
                    _store.Unbind();
                    _onNotify?.Invoke("账号本绑定失败：" + (error ?? "未知原因"));
                }
            }

            // 同一账号密码 → 在新方案里重新登录（权限取新方案的组）；否则维持旧行为（未登录）。
            // 绑定失败 / 关闭方案时不做：没有可验的账号本，不能凭旧凭据凭空登录。
            if (bound && credential != null)
                TryCarryOverSession(credential, newName);

            // 开发构建的便利：没人登录时用出厂管理员自动登录（仅未上线构建；每进程一次）。
            // 放在 carry-over 之后：能保持登录就保持，不能才回落到"默认管理员"。
            TryAutoLoginDebugAdmin();

            SessionBoundaryChanged?.Invoke();
        }

        /// <summary>
        /// 切方案时尝试"保持登录"：拿旧会话的凭据去<b>新方案的账号本</b>重验一次。
        ///
        /// 为什么必须重验而不是搬旧快照
        /// ---------
        /// 账号本按方案隔离，每个账号的盐是随机的；同一个密码在两个方案里算出的哈希不同。
        /// 旧会话的权限快照属于旧方案（组 Id 可能在新方案里不存在），直接搬过去就是
        /// "在新方案里按旧方案的权限放行"——这是权限越界，不是体验优化。
        ///
        /// 验过就 <see cref="ScadaAccessPolicy.Login"/>（用新方案返回的组与权限），
        /// 验不过就保持未登录，并在审计里写清"为什么没保持"（不写密码本身）。
        /// </summary>
        private void TryCarryOverSession(ScadaCredentialSnapshot credential, string newName)
        {
            if (!credential.TryValidate(_store, out var user, out var error) || user == null)
            {
                var reason = string.IsNullOrWhiteSpace(error) ? "账号或密码与目标方案不一致" : error!;
                _audit?.Append(new ScadaAuditEntry(
                    credential.UserName,
                    newName,
                    "方案切换",
                    "保持登录",
                    ScadaAuditOutcome.Failed,
                    "目标方案没有同账号同密码，已按未登录处理：" + reason));
                _onNotify?.Invoke($"方案已切换：账号「{credential.UserName}」未保持登录（{reason}）");
                return;
            }

            _accessPolicy.Login(user);

            // 回填凭据：下一次切方案还要用（否则只能保持一次）。
            _credentials?.Remember(user.Name, credential.Password);

            _audit?.Append(new ScadaAuditEntry(
                user.Name,
                newName,
                "方案切换",
                "保持登录",
                ScadaAuditOutcome.Success,
                $"同一账号密码，已在新方案重新登录；用户组：{user.GroupName}"));
        }

        /// <summary>
        /// 开发构建的便利（"调试模式默认是管理员"）：首次打开方案时若还没人登录，
        /// 用出厂管理员 <c>admin/admin123</c> 自动登录。
        ///
        /// 三条边界（缺一条就会变成现场后门）
        /// ---------
        ///   1. <b>只在未上线构建 + 调试模式开启时生效</b>：<see cref="DebugModeBuild.IsPreRelease"/>
        ///      为 false 的发版构建永远不自动登录；未上线构建里也要 <c>DebugMode.Enabled = true</c>
        ///      （面板开关），关着就当作开发者不想被自动登录；
        ///   2. <b>不是绕过密码</b>：仍走 <see cref="ScadaUserStore.TryValidate"/>；
        ///      管理员一旦改过出厂密码，就登不进来（此时请正常登录）；
        ///   3. <b>每个进程只尝试一次</b>：手动登出后不会被"切个方案又登回来"。
        ///      第一次拿到已绑定账号本时就算尝试过（即使失败），行为可预测。
        ///
        /// 它<b>不改变权限判定</b>：登录成功后拿到的是 admin 所在组的真实权限快照，
        /// 与手动登录 admin 完全一致（审计里记「调试自动登录」，谁都能看出来）。
        /// </summary>
        public void TryAutoLoginDebugAdmin()
        {
            if (_debugAutoLoginAttempted) return;
            if (!DebugAutoLoginAllowed) return;
            if (_debugModeEnabledProvider?.Invoke() == false) return;
            if (_store.BoundSolutionId == null) return;   // 还没有可登录的账号本：等第一次打开方案

            _debugAutoLoginAttempted = true;

            if (_accessPolicy.IsLoggedIn) return;

            if (!_store.TryValidate(ScadaUserStore.DefaultAdminName, ScadaUserStore.DefaultAdminPassword,
                    out var user, out _) || user == null)
                return;

            _accessPolicy.Login(user);
            _credentials?.Remember(user.Name, ScadaUserStore.DefaultAdminPassword);

            _audit?.Append(new ScadaAuditEntry(
                user.Name,
                _identity.SolutionName,
                "会话边界",
                "调试自动登录",
                ScadaAuditOutcome.Success,
                "开发构建：出厂管理员仍使用默认密码，已自动登录"));
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

            // 密码变了，旧凭据不能再用于跨方案自动登录。
            // 登录弹窗的"本人改密"拿得到新明文，会在本事件之后立刻回填；
            // 管理员在用户管理里重置自己的密码没有明文可回填，下一次切方案按未登录处理。
            if (kind is ScadaUserChangeKind.PasswordChanged or ScadaUserChangeKind.PasswordReset)
            {
                _credentials?.Clear();
                return;
            }

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
            // 先清凭据：ForceLogout 在"策略侧已经不算登录"时也会被调用，
            // 不能只依赖 LoggedOut 事件（那个只在真正 Logout 时触发）。
            _credentials?.Clear();

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
