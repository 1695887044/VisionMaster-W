using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;

// 领域层有两个同名快照：剪贴板/模板用的 VisionMaster.Scada.ScadaElementSnapshot（槽位号语义）
// 与渲染器用的 VisionMaster.Scada.Snapshots.ScadaElementSnapshot（Guid 文本 + 图层可见性）。
// 权限判定要的是后者（caps 就填在它上面），这里用别名钉死，别让编译器在两者之间"选一个"。
using ScadaElementSnapshot = VisionMaster.Scada.Snapshots.ScadaElementSnapshot;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// Web 命令的权限策略（<b>逐命令、服务端</b>）：回答"此刻这个会话能不能操作这个图元"。
    ///
    /// <b>四道闸的顺序是刻意的</b>
    /// ---------
    ///   ① 总闸：<see cref="ScadaWebSettings.AllowCommands"/>（false = 只读镜像，一切命令都拒）；
    ///   ② 认证：未登录 → <c>E_UNAUTHENTICATED</c>（前端据此回登录卡片）；
    ///   ③ 只读会话：<b>显式只读票</b>（匿名票已在 ② 拒绝）→ <c>E_POLICY</c>；
    ///      "签票时总闸关着"造成的只读<b>不</b>在此处挡——它由 ① 按当前配置判定，
    ///      所以 <c>AllowCommands</c> 由 false 改回 true 后同一会话立即可操作；
    ///   ④ 权限 Key：图元上的 <see cref="ScadaElementSnapshot.RequiredPermission"/>
    ///      （未配则按事件的运动/停止族兜底，见 <see cref="Can"/>）。
    /// 先答"要不要管"、再答"你是谁"、最后答"这个对象要求什么"——反过来先判权限的话，
    /// "只读模式"会被报成"权限不足"，现场照着日志去加权限，越改越乱。
    ///
    /// <b>没有「谁比谁高」这回事</b>
    /// ---------
    /// 账号本改成「用户组 + 权限 Key 集合」之后（B3），权限之间不存在大小关系：
    /// 「能点停止」与「能点启动」是两件独立的事，没有一把尺子能量出谁更高。
    /// 所以这里<b>不做</b>任何"当前在线账号里最高档才能操作"的裁决——
    /// 那种口径（旧 <c>ClickPolicy=HighestRoleOnly</c>）在按 Key 的模型里无法定义，
    /// 留着只会让现场分不清「我没权限」到底是 Key 没配还是角色不够高。
    /// 要限制谁能操作，就把 Key 配到组上；要单人操作，请开租约。
    ///
    /// <b>为什么把判定写成静态核心 + 实例包装</b>
    /// ---------
    /// 在线列表既被命令路由用（执行时再判一次，防 TOCTOU），也被在线列表（presence）用
    /// （给每一行算 canOperate）。两处各写一份就会分头演化，而这类分叉的表现是
    /// "列表里显示可操作、点下去被拒"。核心只写一遍，实例与 presence 都调它。
    ///
    /// <b>线程</b>：只在 UI 线程（命令泵）或 HTTP 线程（presence 查询）调用；
    /// 在线列表由调用方以快照形式传入（<c>Func&lt;IReadOnlyList&lt;ScadaWebSession&gt;&gt;</c>），
    /// 本类不持有可变的会话字典。
    /// </summary>
    public sealed class ScadaWebAccessPolicy
    {
        private readonly ScadaWebSettings _settings;
        private readonly Func<IReadOnlyList<ScadaWebSession>> _onlineSessions;
        private readonly ScadaWebLease? _lease;

        /// <param name="settings">配置快照（改完重启生效；策略类字段运行期也可重读）</param>
        /// <param name="onlineSessions">在线会话快照提供者（宿主传 <c>() =&gt; presence.Sessions</c>）</param>
        /// <param name="lease">操作权租约（M2b；<c>null</c> = 未启用租约）</param>
        public ScadaWebAccessPolicy(
            ScadaWebSettings settings,
            Func<IReadOnlyList<ScadaWebSession>> onlineSessions,
            ScadaWebLease? lease = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _onlineSessions = onlineSessions ?? (() => Array.Empty<ScadaWebSession>());
            _lease = lease;
        }

        /// <summary>以在线列表（presence）为数据源的便捷构造</summary>
        public ScadaWebAccessPolicy(
            ScadaWebSettings settings,
            ScadaWebPresence presence,
            ScadaWebLease? lease = null)
            : this(settings, OnlineOf(presence), lease)
        {
        }

        private static Func<IReadOnlyList<ScadaWebSession>> OnlineOf(ScadaWebPresence presence)
        {
            if (presence == null) throw new ArgumentNullException(nameof(presence));

            return () => presence.Sessions;
        }

        /// <summary>当前配置快照（只读引用；改配置需重启，见 <see cref="ScadaWebSettings"/> 类注释）</summary>
        public ScadaWebSettings Settings => _settings;

        /// <summary>
        /// 这个会话能不能操作这个图元（caps 计算与命令执行共用同一个判据）。
        /// 失败时 <paramref name="reason"/> 是可直接展示给操作员的一句话。
        /// </summary>
        public bool CanOperate(ScadaWebSession session, ScadaElementSnapshot element, out string reason)
            => CanOperate(session, element, out _, out reason);

        /// <summary>
        /// 带回执码的重载：<paramref name="code"/> 取 <see cref="ScadaWebErrorCodes"/> 里的值，
        /// 路由据此回 <c>error</c>，presence/caps 只用 <paramref name="reason"/>。
        /// </summary>
        public bool CanOperate(
            ScadaWebSession session,
            ScadaElementSnapshot element,
            out string code,
            out string reason)
        {
            if (element == null)
            {
                code = ScadaWebErrorCodes.BadRequest;
                reason = "图元快照为空";
                return false;
            }

            // 旧 .vms 只写了 RequiredRole：按 B3 的映射表折成一个 Key。
            // 只接受成员名——数字串（"3"）虽然 Enum.TryParse 也认，但那是给"手改快照 /
            // 高版本写了别的编码"留一条按数值提权的路径（"3" 恰好是管理员）。
            var key = element.RequiredPermission;
            if (string.IsNullOrWhiteSpace(key)
                && element.RequiredRole is { Length: > 0 } legacyText
                && !char.IsDigit(legacyText.Trim()[0])
                && Enum.TryParse<ScadaRole>(legacyText.Trim(), ignoreCase: true, out var legacyRole)
                && legacyRole.IsDefined())
            {
                key = ScadaPermissionKeys.LegacyRolePermission(legacyRole);
            }

            return Can(session, key, out code, out reason);
        }

        /// <summary>B3：按权限 Key 判定（Always 恒真；空 Key = 不限制；其余查登录快照）。</summary>
        public bool Can(ScadaWebSession session, string? requiredKey, out string code, out string reason)
        {
            code = string.Empty;
            reason = string.Empty;

            // 先过全局闸（只读模式 / 未登录 / 匿名），再查权限 Key——否则 caps 会绕过 AllowCommands。
            if (!Evaluate(session, _settings, out code, out reason))
                return false;

            if (!string.IsNullOrWhiteSpace(requiredKey)
                && !string.Equals(requiredKey, ScadaPermissionKeys.Always, StringComparison.Ordinal)
                && !session.Permissions.Contains(requiredKey!, StringComparer.Ordinal))
            {
                code = ScadaWebErrorCodes.Policy;
                reason = $"需要「{ScadaPermissionKeys.DisplayName(requiredKey)}」权限";
                return false;
            }

            // 租约是"可选加强"，与权限 Key 正交：Key 有权限的人之间再分"此刻谁能动手"。
            // 只有 OperationLeaseEnabled + AllOperatorsRequiresLease 同时为 true 才接进判权。
            if (_settings.OperationLeaseEnabled && _settings.AllOperatorsRequiresLease)
            {
                if (_lease == null)
                {
                    code = ScadaWebErrorCodes.LeaseHeld;
                    reason = "租约已启用但服务端未接入租约状态，按只读处理";
                    return false;
                }

                if (!_lease.IsHolder(session))
                {
                    var holder = _lease.HolderName;
                    code = ScadaWebErrorCodes.LeaseHeld;
                    reason = string.IsNullOrEmpty(holder)
                        ? "操作权租约当前无人持有：请先取得租约再操作"
                        : $"操作权租约由「{holder}」持有：请先取得租约再操作";
                    return false;
                }
            }

            return true;
        }

        public bool CanOperate(
            ScadaWebSession session,
            ScadaElementSnapshot element,
            IReadOnlyList<ScadaWebSession> online,
            out string code,
            out string reason)
        {
            // 在线快照参数刻意保留（签名稳定、调用点不用改），但它**不再参与判定**：
            // 「谁比谁高」在按 Key 的模型里不存在。判定只看本会话自己的权限快照。
            _ = online;

            return CanOperate(session, element, out code, out reason);
        }

        /// <summary>
        /// 只看策略层（不看具体图元）：这个会话<b>此刻有没有操作资格</b>。
        /// 在线列表的 <c>canOperate</c> 用它——那里没有"某一个图元"可言。
        /// </summary>
        public bool CanOperateAtAll(ScadaWebSession session, out string reason)
            => CanOperateAtAll(session, out _, out reason);

        /// <inheritdoc cref="CanOperateAtAll(ScadaWebSession, out string)"/>
        public bool CanOperateAtAll(ScadaWebSession session, out string code, out string reason)
            => Can(session, requiredKey: null, out code, out reason);

        #region 判定核心

        /// <summary>
        /// <b>权限判定的唯一实现</b>（实例方法、presence、断言都调它）。
        ///
        /// 四道闸：总闸 → 认证 → 显式只读 → 权限 Key。
        /// <b>没有角色闸、没有点击策略</b>：权限之间不存在高低，见类注释。
        /// </summary>
        public static bool Evaluate(
            ScadaWebSession? session,
            ScadaWebSettings? settings,
            out string code,
            out string reason)
        {
            code = ScadaWebErrorCodes.Policy;
            reason = string.Empty;

            if (session == null)
            {
                code = ScadaWebErrorCodes.BadRequest;
                reason = "会话为空";
                return false;
            }

            if (settings == null)
            {
                reason = "缺少 Web 配置（ScadaWeb 节），按只读处理";
                return false;
            }

            // ① 总闸：默认只读。它排在身份之前——"关掉命令"这件事与你是谁无关。
            if (!settings.AllowCommands)
            {
                reason = "当前为只读模式（ScadaWeb.AllowCommands=false）";
                return false;
            }

            // ② 认证：未登录一律拒绝，且码单独给 E_UNAUTHENTICATED（前端据此回登录卡片）。
            //    这一条必须排在 ReadOnly 之前：匿名票本来就是只读的，先判只读会把"请先登录"
            //    报成"当前为只读"，用户只会去找配置，不会去登录。
            if (!session.IsAuthenticated)
            {
                code = ScadaWebErrorCodes.Unauthenticated;
                reason = "未登录会话不可操作";
                return false;
            }

            // ③ 会话：只挡<b>显式只读</b>（调用方明确要求只读的票）。
            //    "签票/核销那一刻 AllowCommands=false 导致的只读"不在这里挡：总闸 ① 已按
            //    <b>当前</b>配置判过，配置由 false 改回 true 后同一会话必须立即可操作
            //    （否则热切换要等到重连才生效，现场看到的是"改了配置没反应"）。
            //    匿名会话在 ② 已被拒；ExplicitReadOnly=null（手工构造 / 旧路径）回退到
            //    ReadOnly——来源不明的票按只读处理（fail-safe）。
            if (session.Grant?.ExplicitReadOnly ?? session.ReadOnly)
            {
                reason = "当前会话为只读（ReadOnly）";
                return false;
            }

            code = ScadaWebErrorCodes.Ok;
            reason = string.Empty;
            return true;
        }

        /// <summary>当前在线快照（给批量判权用；一次复制，多次复用）</summary>
        public IReadOnlyList<ScadaWebSession> OnlineSnapshot(ScadaWebSession? self = null)
            => SnapshotOnline(self);

        private IReadOnlyList<ScadaWebSession> SnapshotOnline(ScadaWebSession? self)
        {
            var online = _onlineSessions();

            if (online == null || online.Count == 0)
                return self == null ? Array.Empty<ScadaWebSession>() : new[] { self };

            return online;
        }

        #endregion

        #region 配置夹取

        /// <summary>
        /// 加载期夹取（把非法配置改成安全值），返回逐条说明（空 = 没改任何东西）。
        ///
        /// <b>目前夹两条</b>：
        ///   · <see cref="ScadaWebSettings.RequireAudit"/> 只能是 true——审计是"谁按的"唯一凭证，
        ///     配置成 false 属于非法配置，夹回 true（不是只报错留 false）；
        ///   · <see cref="ScadaWebSettings.AllOperatorsRequiresLease"/>=true 但
        ///     <see cref="ScadaWebSettings.OperationLeaseEnabled"/>=false 是非法组合，夹回 false
        ///     （它是"可选加强"，不能单独成立）。
        ///
        /// 调用方（宿主启动链）把返回的每一条按 Error 级别落日志——夹取本身不能静默，
        /// 否则现场永远不知道自己配错了一项。
        /// </summary>
        public static IReadOnlyList<string> Normalize(ScadaWebSettings settings)
        {
            var messages = new List<string>();

            if (settings == null)
                return messages;

            if (!settings.RequireAudit)
            {
                settings.RequireAudit = true;
                messages.Add("ScadaWeb.RequireAudit=false 属非法配置，已夹回 true（审计不可关闭）");
            }

            if (settings.AllOperatorsRequiresLease && !settings.OperationLeaseEnabled)
            {
                settings.AllOperatorsRequiresLease = false;
                messages.Add("ScadaWeb.AllOperatorsRequiresLease=true 但 OperationLeaseEnabled=false，已夹回 false");
            }

            return messages;
        }

        #endregion
    }
}
