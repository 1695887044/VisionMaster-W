using System;
using System.Collections.Generic;
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
    /// <b>五道闸的顺序是刻意的</b>（方案 §8.2）
    /// ---------
    ///   ① 总闸：<see cref="ScadaWebSettings.AllowCommands"/>（false = 只读镜像，一切命令都拒）；
    ///   ② 认证：未登录 → <c>E_UNAUTHENTICATED</c>（前端据此回登录卡片）；
    ///   ③ 只读会话：<b>显式只读票</b>（匿名票已在 ② 拒绝）→ <c>E_POLICY</c>；
    ///      "签票时总闸关着"造成的只读<b>不</b>在此处挡——它由 ① 按当前配置判定，
    ///      所以 <c>AllowCommands</c> 由 false 改回 true 后同一会话立即可操作；
    ///   ④ 角色：<see cref="ScadaElement.RequiredRole"/> 经 <see cref="ScadaRoleExtensions.Allows"/>
    ///      （<b>原因文案原样透传</b>，不在 web 侧重写措辞）；
    ///   ⑤ 策略：<see cref="ScadaWebSettings.ClickPolicy"/>。
    /// 先答"要不要管"、再答"你是谁"、最后答"这个对象要求什么"——反过来先比角色的话，
    /// "只读模式"会被报成"角色不足"，现场照着日志去加权限，越改越乱。
    ///
    /// <b>HighestRoleOnly 的平局规则</b>
    /// ---------
    /// 取<b>在线已认证会话</b>里的最高角色；达到该角色的会话都可点，<b>平局都可点</b>
    /// （方案 §8.3：工业现场常见两个工程师同时在场，强行只让一个人点是人为故障）。
    /// 没有已认证会话 → 全员只读。这里<b>不做</b>"最早连接优先 / sessionId 最小"之类的裁决：
    /// 那等于让"谁先打开浏览器"决定谁能操作，且角色变化后无法稳定重算。要单人操作请开租约。
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

            return CanOperate(session, ParseRequiredRole(element.RequiredRole), out code, out reason);
        }

        public bool CanOperate(
            ScadaWebSession session,
            ScadaElementSnapshot element,
            IReadOnlyList<ScadaWebSession> online,
            out string code,
            out string reason)
        {
            if (element == null)
            {
                code = ScadaWebErrorCodes.BadRequest;
                reason = "图元快照为空";
                return false;
            }

            return CanOperate(session, ParseRequiredRole(element.RequiredRole), online, out code, out reason);
        }

        /// <summary>按"图元要求的角色"判定（路由侧用活模型上的 <see cref="ScadaElement.RequiredRole"/>）</summary>
        public bool CanOperate(ScadaWebSession session, ScadaRole? requiredRole, out string code, out string reason)
            => CanOperate(session, requiredRole, SnapshotOnline(session), out code, out reason);

        /// <summary>
        /// 带在线快照的重载：<c>ApplyCaps</c> 一页只取一次在线列表，
        /// 避免每个图元都 <c>presence.Sessions</c> 复制一次数组（1000 图元 × N 会话会变成 O(n²)）。
        /// </summary>
        public bool CanOperate(
            ScadaWebSession session,
            ScadaRole? requiredRole,
            IReadOnlyList<ScadaWebSession> online,
            out string code,
            out string reason)
        {
            if (!Evaluate(session, requiredRole, _settings, online, out code, out reason))
                return false;

            // 租约是"可选加强"：只有 OperationLeaseEnabled + AllOperatorsRequiresLease 同时为 true
            // 才把"必须持有租约"接进判权。两项缺一都不改变原有行为（C77 ①② 的配置校验仍在
            // ScadaWebAccessPolicy.ValidateSupervision / Normalize 里）。
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

        /// <summary>
        /// 只看策略层（不看具体图元）：这个会话<b>此刻有没有操作资格</b>。
        /// 在线列表的 <c>canOperate</c> 用它——那里没有"某一个图元"可言。
        /// </summary>
        public bool CanOperateAtAll(ScadaWebSession session, out string reason)
            => CanOperateAtAll(session, out _, out reason);

        /// <inheritdoc cref="CanOperateAtAll(ScadaWebSession, out string)"/>
        public bool CanOperateAtAll(ScadaWebSession session, out string code, out string reason)
            => CanOperate(session, requiredRole: null, out code, out reason);

        /// <summary>
        /// 当前在线已认证会话里的最高角色；一个都没有时为 <c>null</c>（全员只读）。
        /// <see cref="ScadaRole.Undefined"/>（非法值）不参与比较——它比谁都"小"，
        /// 让它参与会把"文件里有个坏数字"变成"谁都别点"。
        /// </summary>
        public ScadaRole? HighestOnlineRole()
        {
            ScadaRole? highest = null;

            foreach (var session in SnapshotOnline(null))
            {
                if (session == null || !session.IsAuthenticated || !session.Role.IsDefined())
                    continue;

                if (highest == null || session.Role > highest.Value)
                    highest = session.Role;
            }

            return highest;
        }

        #region 判定核心

        /// <summary>
        /// <b>权限判定的唯一实现</b>（实例方法、presence、断言都调它）。
        ///
        /// <paramref name="online"/> 是"此刻在线的会话快照"；传空列表等价于"没有别的操作者"。
        /// 被测会话即使不在快照里也会被计入最高角色（连接登记与命令到达之间有一个极小窗口，
        /// 把"刚连上就点"判成"没有已登录操作者"是纯粹的误伤）。
        /// </summary>
        public static bool Evaluate(
            ScadaWebSession? session,
            ScadaRole? requiredRole,
            ScadaWebSettings? settings,
            IReadOnlyList<ScadaWebSession>? online,
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

            // ① 总闸：默认只读。它排在角色之前——"关掉命令"这件事与你是谁无关。
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

            // ④ 角色：原因逐字来自 ScadaRoleExtensions.Allows（唯一一份实现）。
            if (!session.Role.Allows(requiredRole, out var roleReason))
            {
                code = ScadaWebErrorCodes.Role;
                reason = roleReason ?? "当前角色没有操作权限";
                return false;
            }

            // ⑤ 策略
            switch (settings.ClickPolicy)
            {
                case ScadaWebClickPolicy.HighestRoleOnly:
                    return EvaluateHighestRoleOnly(session, online, out code, out reason);

                case ScadaWebClickPolicy.AllOperators:
                    // ①~③ 已过；但监管项必须"用时再判"：
                    // 模块启动时做的夹取会被随后的 ConfigCheck.Load() 原地回写覆盖
                    // （2026-10-21 终审 P1-1），所以不能只信启动那一刻的结果。
                    if (!ValidateSupervision(settings, out _))
                        return EvaluateHighestRoleOnly(session, online, out code, out reason);

                    break;

                default:
                    // 不认识的策略值（高版本配置被低版本读到）：fail-safe 只读。
                    reason = $"不认识的点击策略（{(int)settings.ClickPolicy}），已按只读处理";
                    return false;
            }

            code = ScadaWebErrorCodes.Ok;
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// HighestRoleOnly 的判定体：没有已登录操作者 → 全员只读；低于当前最高档 → 只读；
        /// 平局都可点。AllOperators 监管项不全时也回退到这里（fail-safe）。
        /// </summary>
        private static bool EvaluateHighestRoleOnly(
            ScadaWebSession session,
            IReadOnlyList<ScadaWebSession>? online,
            out string code,
            out string reason)
        {
            code = ScadaWebErrorCodes.Policy;
            reason = string.Empty;

            var highest = HighestOf(online, session);

            if (highest == null)
            {
                reason = "当前没有已登录的操作者，全员只读";
                return false;
            }

            // 平局都可点：这里只判"低于最高档"，不判"是不是唯一的那一个"。
            if (!session.Role.Satisfies(highest.Value))
            {
                reason = $"只读：当前只有「{highest.Value.DisplayName()}」可以操作"
                         + $"（{DisplayActor(session)} 是「{session.Role.DisplayName()}」）";
                return false;
            }

            code = ScadaWebErrorCodes.Ok;
            return true;
        }

        /// <summary>当前在线快照（给批量判权用；一次复制，多次复用）</summary>
        public IReadOnlyList<ScadaWebSession> OnlineSnapshot(ScadaWebSession? self = null)
            => SnapshotOnline(self);

        private static ScadaRole? HighestOf(IReadOnlyList<ScadaWebSession>? online, ScadaWebSession self)
        {
            ScadaRole? highest = null;

            if (online != null)
            {
                foreach (var item in online)
                {
                    if (item == null || !item.IsAuthenticated || !item.Role.IsDefined())
                        continue;

                    if (highest == null || item.Role > highest.Value)
                        highest = item.Role;
                }
            }

            // 自己在窗口里没登记上也算（理由见 Evaluate 的注释）。
            if (self.IsAuthenticated && self.Role.IsDefined()
                && (highest == null || self.Role > highest.Value))
            {
                highest = self.Role;
            }

            return highest;
        }

        /// <summary>把 <see cref="ScadaElementSnapshot.RequiredRole"/> 的成员名解析成枚举</summary>
        private static ScadaRole? ParseRequiredRole(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();

            // 只接受成员名。数字串（"3"）虽然 Enum.TryParse 也认，但快照里的 RequiredRole
            // 是 ToString() 出来的成员名；接受数字等于给"手改快照 / 高版本写了别的编码"
            // 留一条按数值提权的路径（"3" 恰好是管理员）。认不出的一律 Undefined，交给
            // Allows 的 fail-safe 分支报"图元要求的权限值非法"。
            if (char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '+')
                return ScadaRole.Undefined;

            return Enum.TryParse<ScadaRole>(trimmed, ignoreCase: true, out var parsed)
                ? parsed
                : ScadaRole.Undefined;
        }

        private static string DisplayActor(ScadaWebSession session)
            => string.IsNullOrWhiteSpace(session.UserName) ? "当前用户" : $"「{session.UserName}」";

        private IReadOnlyList<ScadaWebSession> SnapshotOnline(ScadaWebSession? self)
        {
            var online = _onlineSessions();

            if (online == null || online.Count == 0)
                return self == null ? Array.Empty<ScadaWebSession>() : new[] { self };

            return online;
        }

        #endregion

        #region 监管闸门（AllOperators）

        /// <summary>
        /// 校验 <see cref="ScadaWebClickPolicy.AllOperators"/> 的监管闸门（方案 §8.4）。
        ///
        /// <b>为什么缺项要拒绝启用、而不是悄悄跳过</b>
        /// ---------
        /// 五项各自对应一种现场事故（身份、留痕、限速、危险操作确认、在线可见性）。
        /// "缺哪项就跳过哪项"的结果是：配置看着是放开的，实际少了一层护栏，
        /// 而这种事<b>事后</b>才会被发现（出事时查不到是谁按的 / 连点把变量刷爆）。
        ///
        /// 返回 false 时 <paramref name="error"/> 逐项列出缺哪一条（带配置键名）。
        /// <see cref="ScadaWebClickPolicy.HighestRoleOnly"/> 不需要任何监管项，恒为 true。
        /// </summary>
        public bool ValidateSupervision(out string error)
            => ValidateSupervision(_settings, out error);

        /// <summary>
        /// 静态重载：供 <see cref="Evaluate"/> 在<b>每次判权时</b>复核监管项。
        /// 配置对象可能在启动后被 ConfigCheck.Load 原地改写，不能只信启动时的夹取结果。
        /// </summary>
        public static bool ValidateSupervision(ScadaWebSettings? settings, out string error)
        {
            if (settings == null || settings.ClickPolicy != ScadaWebClickPolicy.AllOperators)
            {
                error = string.Empty;
                return true;
            }

            var missing = new List<string>();

            if (!settings.RequireOnlineUsers)
                missing.Add("ScadaWeb.RequireOnlineUsers=false（在线用户列表被关闭）");

            if (!settings.RequireAudit)
                missing.Add("ScadaWeb.RequireAudit=false（命令审计被关闭；该项只能是 true）");

            if (!settings.RequireRateLimit)
                missing.Add("ScadaWeb.RequireRateLimit=false（速率限制被关闭）");

            if (!settings.RequireConfirmForDangerous)
                missing.Add("ScadaWeb.RequireConfirmForDangerous=false（危险动作二次确认被关闭）");

            if (settings.AllOperatorsRequiresLease && !settings.OperationLeaseEnabled)
                missing.Add("ScadaWeb.AllOperatorsRequiresLease=true 但 ScadaWeb.OperationLeaseEnabled=false（租约未启用）");

            error = missing.Count == 0
                ? string.Empty
                : "AllOperators 监管闸门缺项：" + string.Join("；", missing);

            return missing.Count == 0;
        }

        /// <summary>
        /// 加载期夹取（把非法配置改成安全值），返回逐条说明（空 = 没改任何东西）。
        ///
        /// <b>目前夹两条</b>：
        ///   · <see cref="ScadaWebSettings.RequireAudit"/> 只能是 true——审计是"谁按的"唯一凭证，
        ///     配置成 false 属于非法配置，夹回 true（不是只报错留 false）；
        ///   · <see cref="ScadaWebSettings.AllOperatorsRequiresLease"/>=true 但
        ///     <see cref="ScadaWebSettings.OperationLeaseEnabled"/>=false 是非法组合，夹回 false
        ///     （方案 §8.4：它是"可选加强"，不能单独成立）。
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
