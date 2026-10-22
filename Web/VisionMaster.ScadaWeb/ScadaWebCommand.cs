using System;
using VisionMaster.Scada;

namespace VisionMaster.ScadaWeb
{
    /// <summary>
    /// Web 命令的种类（协议字段 <c>type</c> 的取值）。
    ///
    /// M2a 是四条命令；M2b 增加 <c>lease.acquire / renew / release</c>（操作权租约）。
    /// <c>view.attach</c>、<c>auth.login</c>、<c>lease.request/approve/steal</c> 等仍属于后续批次，
    /// 不在这里先占名字——一个协议常量一旦出现在代码里，就会有人以为它已经能用了。
    ///
    /// 值必须与 <c>wwwroot\app.js</c> 发的字符串逐字一致（前端没有共享的类型定义，
    /// 拼错的表现是"点了没反应"，而服务端只会回 <c>E_UNSUPPORTED</c>）。
    /// </summary>
    public static class ScadaWebCommandKinds
    {
        /// <summary>按图元事件执行钩子动作（点击/按下/释放）</summary>
        public const string Element = "cmd.element";

        /// <summary>把输入框里的文本写回图元绑定变量</summary>
        public const string Input = "cmd.input";

        /// <summary>确认一条报警 / 全部报警</summary>
        public const string AlarmAck = "cmd.alarm.ack";

        /// <summary>为危险动作申请一次性确认令牌</summary>
        public const string ConfirmRequest = "cmd.confirm.request";

        /// <summary>按 targetRequestId 查询一条命令的结果（ack 丢失后的只读查询）</summary>
        public const string Result = "cmd.result";

        /// <summary>取得操作权租约（M2b）</summary>
        public const string LeaseAcquire = "lease.acquire";

        /// <summary>续租（M2b）</summary>
        public const string LeaseRenew = "lease.renew";

        /// <summary>释放租约（M2b）</summary>
        public const string LeaseRelease = "lease.release";
    }

    /// <summary>
    /// Web 命令的回执码（<c>ack.code</c> / <c>error.code</c> 的取值）。
    ///
    /// 与方案 §9.8 的码表同名同义；这里只收录本批（命令泵/路由）会产生的那些。
    /// <b>不要新增"含义相近的第二个码"</b>：前端按码决定"重试 / 回登录 / 提示原因"，
    /// 多一个码就等于多一条前端不认识的分支（表现为 toast 里只有原始码）。
    /// </summary>
    public static class ScadaWebErrorCodes
    {
        /// <summary>成功（回执 <c>ok=true</c> 时使用）</summary>
        public const string Ok = "OK";

        /// <summary>队列满 / 命令泵已停止</summary>
        public const string Busy = "E_BUSY";

        /// <summary>只读模式（AllowCommands=false）、会话只读、未登录、或最高角色策略不允许</summary>
        public const string Policy = "E_POLICY";

        /// <summary>角色不足（原因逐字来自 <see cref="ScadaRoleExtensions.Allows"/>）</summary>
        public const string Role = "E_ROLE";

        /// <summary>未登录会话发命令（与 E_POLICY 分开：前端据此回登录卡片）</summary>
        public const string Unauthenticated = "E_UNAUTHENTICATED";

        /// <summary>超过每会话 / 每图元速率上限</summary>
        public const string RateLimited = "E_RATE_LIMITED";

        /// <summary>图元不在当前页（已删除 / 切页 / 不是这一轮）</summary>
        public const string NotFound = "E_NOT_FOUND";

        /// <summary>图元存在，但没配该事件的钩子 / 该属性不可输入</summary>
        public const string NotOperable = "E_NOT_OPERABLE";

        /// <summary>动作不在白名单（含运动默认禁用），或全部动作被镜像模式剔除</summary>
        public const string ActionForbidden = "E_ACTION_FORBIDDEN";

        /// <summary>写变量失败（原因逐字来自宿主 <see cref="IScadaValueWriter"/>）</summary>
        public const string Value = "E_VALUE";

        /// <summary>
        /// 目标是流程触发变量：Web 写入会经 <c>FlowVariableTriggerService</c> 的上升沿
        /// 启动流程（流程里可以含轴运动）。A 范围下 Web 不能走这条间接路径。
        /// </summary>
        public const string FlowTrigger = "E_FLOW_TRIGGER";

        /// <summary>页不符或页版本不符</summary>
        public const string StalePage = "E_STALE_PAGE";

        /// <summary>轮次不符 / 当前没有运行中的画面</summary>
        public const string StaleRun = "E_STALE_RUN";

        /// <summary>危险动作缺 / 错 / 过期 / 已用 / 他人的确认令牌</summary>
        public const string Confirm = "E_CONFIRM";

        /// <summary>操作权租约由他人持有（AllOperatorsRequiresLease=true 时）</summary>
        public const string LeaseHeld = "E_LEASE_HELD";

        /// <summary>命令体缺字段或字段非法</summary>
        public const string BadRequest = "E_BAD_REQUEST";

        /// <summary>
        /// 同一 <c>requestId</c> 的重放，且已超出幂等窗口（5 分钟）：<b>本次不执行</b>。
        /// 窗口内重放回原回执（同一个 ack / error），窗口外一律这个码（方案 §9.8、矩阵 C17/F18）。
        /// </summary>
        public const string Duplicate = "E_DUPLICATE";

        /// <summary>快照超过硬上限（不发半截、不推进页键；连接按 1009 关闭）</summary>
        public const string SnapshotTooLarge = "E_SNAPSHOT_TOO_LARGE";

        /// <summary>本版本不认识的命令类型</summary>
        public const string Unsupported = "E_UNSUPPORTED";

        /// <summary>服务端内部异常（泵捕获后回这个码，不把栈回给客户端）</summary>
        public const string Internal = "E_INTERNAL";
    }

    /// <summary>
    /// 一条 Web 命令（网络线程入队 → 命令泵在 UI 线程执行）。
    ///
    /// <b>为什么命令上带着 <see cref="Session"/> 而不是 sessionId 字符串</b>
    /// ---------
    /// 命令要回答的四件事全都只有会话对象知道：是谁（用户名/角色）、是不是只读、
    /// 从哪个 IP 来（审计）、这条连接还活着吗（回执往哪送）。只传一个 id 的话，
    /// 命令泵执行时还得回查一张会话表——而那张表在断连时会被摘掉，
    /// "命令排到了、人已经走了"就会变成空指针或静默丢弃；带着对象引用，至少能确定
    /// "入队时这个人还在"，执行侧再用 <see cref="ScadaWebSession.TrySend"/> 判断还能不能回话。
    ///
    /// <b>身份字段全部由服务端填</b>
    /// ---------
    /// 用户名 / 角色 / 只读 一律从会话对象派生，<b>不</b>从网络消息里读（方案 §4.3 规则 8）。
    /// 客户端字段只进诊断日志，不参与判定——否则"改一个 JSON 字段就变管理员"。
    ///
    /// <b>不可变</b>：入队之后任何线程都不该改它。命令泵是单读者，但审计与回执可能在不同时刻读同一份。
    /// </summary>
    public sealed class ScadaWebCommand
    {
        /// <summary>命令种类（<see cref="ScadaWebCommandKinds"/> 之一）</summary>
        public string Kind { get; init; } = string.Empty;

        /// <summary>
        /// 客户端生成的请求号（用于回执配对 + <b>幂等去重</b>）。
        /// 路由按 <c>(SessionId, RequestId)</c> 记 5 分钟：窗口内重放回原回执、不重复执行；
        /// 窗口外（仍在保留期内）回 <see cref="ScadaWebErrorCodes.Duplicate"/> 且不执行。
        /// 空白 = 不做去重（断言/探针的裸命令）。
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        /// <summary>发起命令的会话（身份、只读口径、审计地址、回执通道都从这里取）</summary>
        public ScadaWebSession Session { get; init; } = null!;

        /// <summary>目标图元 Id（Guid "D" 文本）；报警"全部确认"时可为 <c>null</c></summary>
        public string? ElementId { get; init; }

        /// <summary>
        /// <c>cmd.element</c> 的事件类型；其它命令忽略。
        /// 取值必须与图元钩子上配的 <see cref="ScadaEventType"/> 同名（前端发成员名）。
        /// </summary>
        public ScadaEventType? Event { get; init; }

        /// <summary>
        /// <c>cmd.input</c> 的目标属性键（如 <c>Value</c>）；<c>null</c>/空白按 <c>"Value"</c> 处理。
        ///
        /// 为什么要有它：一个可输入图元未必只有一个绑定（数值域还可能绑 <c>Foreground</c> 做报警色），
        /// 只认 <c>"Value"</c> 会让"输入框到底写哪个属性"变成 web 侧的硬编码猜测。
        /// </summary>
        public string? Property { get; init; }

        /// <summary><c>cmd.input</c> 的操作员原文（不做 trim、不预转换，规则由宿主写入器统一）</summary>
        public string? Value { get; init; }

        /// <summary>
        /// 命令发出时客户端认为的页 Id（Guid "D"）。<b>M2a 起为必填</b>：空 → 路由回 <c>E_BAD_REQUEST</c>
        /// （"缺省 = 跳过闸门"等于给过期命令留一条静默旁路）。
        /// </summary>
        public string? PageId { get; init; }

        /// <summary>命令发出时客户端认为的页版本。<b>M2a 起为必填</b>：<c>&lt;= 0</c> → <c>E_BAD_REQUEST</c></summary>
        public int PageVersion { get; init; }

        /// <summary>命令发出时客户端认为的运行轮次。<b>M2a 起为必填</b>：<c>&lt;= 0</c> → <c>E_BAD_REQUEST</c></summary>
        public long RunGeneration { get; init; }

        /// <summary>危险动作的一次性确认令牌（<c>cmd.element</c> 用；其它命令忽略）</summary>
        public string? ConfirmToken { get; init; }

        /// <summary><c>cmd.alarm.ack</c> 的目标报警；与 <see cref="All"/> 二选一</summary>
        public Guid? AlarmId { get; init; }

        /// <summary><c>cmd.alarm.ack</c>：true = 确认全部待确认报警</summary>
        public bool All { get; init; }

        /// <summary>入队时刻（UTC）。泵用它算排队耗时，超过 2 秒记 Warn（"命令在队列里躺太久"）</summary>
        public DateTime EnqueuedAtUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 一条命令的执行结果（泵把它交给宿主的回执通道，序列化成 <c>ack</c> / <c>error</c>）。
    ///
    /// <b>为什么不直接回字符串 JSON</b>
    /// ---------
    /// 路由要能断言"回的是哪个码、带没带原因"，而这些断言不该去解析 JSON（字段名一改全红）。
    /// 序列化只发生在最后一步（宿主），路由与泵只处理结构化结果。
    /// </summary>
    public sealed class ScadaWebCommandResult
    {
        /// <summary>是否成功（<c>false</c> 时 <see cref="Code"/> 必有值且不是 <see cref="ScadaWebErrorCodes.Ok"/>）</summary>
        public bool Ok { get; init; }

        /// <summary>回执码（成功为 <see cref="ScadaWebErrorCodes.Ok"/>）</summary>
        public string Code { get; init; } = ScadaWebErrorCodes.Ok;

        /// <summary>可直接展示给操作员的中文说明；成功时通常为 <c>null</c></summary>
        public string? Message { get; init; }

        /// <summary>附加结果（动作条数 / 跳过条数 / 确认令牌 / 旧值→新值……），由宿主原样序列化</summary>
        public object? Result { get; init; }

        /// <summary>成功结果</summary>
        public static ScadaWebCommandResult Success(object? result = null, string? message = null)
            => new() { Ok = true, Code = ScadaWebErrorCodes.Ok, Message = message, Result = result };

        /// <summary>失败结果（<paramref name="code"/> 必须取 <see cref="ScadaWebErrorCodes"/> 里的值）</summary>
        public static ScadaWebCommandResult Fail(string code, string? message, object? result = null)
            => new()
            {
                Ok = false,
                Code = string.IsNullOrWhiteSpace(code) ? ScadaWebErrorCodes.Internal : code,
                Message = message,
                Result = result
            };
    }
}
