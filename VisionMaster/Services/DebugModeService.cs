using System;
using System.Globalization;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 一次权限探测的结果（<c>Allowed</c> + 拒绝原因）。
    ///
    /// 为什么不用 <c>bool Can(string key, out string? reason)</c> 委托：lambda 不能声明 out 参数，
    /// 调用方就得为了一次探测多写一个包装类。这个只读结构体把"允许与否 + 为什么"装在一起，
    /// 断言侧也能直接造一个假结果，不必真的登录。
    /// </summary>
    public readonly record struct DebugPermissionResult(bool Allowed, string? Reason);

    /// <summary>
    /// 项目阶段（构建期常量）。
    ///
    /// <b>发版时改这里并重建</b>：<see cref="IsPreRelease"/> = false 之后，
    /// 调试模式一律按"已上线"判定（仅管理员可开关），配置文件里的
    /// <c>DebugMode.ProjectReleased</c> 再也翻不回去——否则改一行配置就能绕过管理员闸门。
    /// </summary>
    public static class DebugModeBuild
    {
        /// <summary>
        /// 项目是否仍处于<b>未上线</b>阶段。
        ///
        /// 现在（2026-10-26）项目未上线，所以是 true：调试模式任何身份都能开关。
        /// 正式发版时改成 false 并重建——这是"上线就是管理员"的唯一开关点。
        /// </summary>
        public const bool IsPreRelease = true;
    }

    /// <summary>
    /// 调试模式的唯一一份判定与写入口。
    ///
    /// 三条口径
    /// ---------
    ///   1. <b>未上线</b>：任何身份都能开关（不需要登录）——现在的项目阶段；
    ///   2. <b>已上线</b>：开关需要「用户管理」权限（<see cref="ScadaPermissionKeys.UserAdministration"/>），
    ///      未登录 / 无权限一律拒绝，拒绝原因直接给人看；
    ///   3. <b>不按时间失效</b>：本类<b>没有</b>任何时间判据——只要 <c>Enabled</c> 为 true 就一直有效，
    ///      只有显式关闭才会失效。加"有效期/到期日"之类的逻辑属于改变需求，不是修 bug。
    ///
    /// 为什么"是否已上线"由构建常量与配置<b>或</b>起来判
    /// ---------
    /// 构建常量管发版（发出去之后永远按上线处理），配置项管开发期演练
    /// （在未上线的构建里写 <c>ProjectReleased=true</c>，就能提前验证"仅管理员可开"这条路）。
    /// 反过来（构建说已上线、配置说未上线）不许生效：那等于给现场留了一个后门。
    ///
    /// 为什么开关<b>不绕过任何权限</b>
    /// ---------
    /// 调试模式只是"多一块诊断面板 + 一个状态栏徽标"，它不改任何权限判定、
    /// 不放宽任何安全闸门（流程启动 / 硬件配置 / 用户管理仍各走各的 Key）。
    /// 现场最容易误解的一点就是"开了调试模式是不是就能随便点"——答案必须是"不能"。
    /// </summary>
    public sealed class DebugModeService
    {
        /// <summary>审计文件里的对象名（"谁"那一列）</summary>
        public const string AuditSubject = "调试模式";

        /// <summary>审计文件里的事件名（"怎么触发的"那一列）</summary>
        public const string AuditEventText = "调试模式";

        private const string EnableAction = "开启调试模式";
        private const string DisableAction = "关闭调试模式";

        private readonly Func<DebugModeSettings?> _settings;
        private readonly Func<bool, string, string, bool> _persist;
        private readonly Func<string, DebugPermissionResult> _permissionProbe;
        private readonly Func<string?> _actorProvider;
        private readonly ScadaAuditWriter? _audit;
        private readonly bool _preReleaseBuild;
        private readonly object _gate = new();

        /// <param name="settings">
        /// 配置节读取口。<b>每次现读</b>而不是构造时抓一份引用：<c>AppSettingsService.Load()</c>
        /// 在配置文件损坏时会整体替换 <c>Current</c>，抓引用会让开关写到一份已经没人读的旧对象上。
        /// </param>
        /// <param name="persist">落盘口：<c>(enabled, actor, at) =&gt; 是否写成功</c></param>
        /// <param name="permissionProbe">权限探测口（上线后判「用户管理」Key；断言侧可造假结果）</param>
        /// <param name="actorProvider">"此刻登录着谁"（未登录由审计侧回落「未登录」）</param>
        /// <param name="audit">审计落盘（可为 null：断言宿主/无审计装配）</param>
        /// <param name="preReleaseBuild">构建期阶段；默认取 <see cref="DebugModeBuild.IsPreRelease"/>，断言侧可传</param>
        public DebugModeService(
            Func<DebugModeSettings?> settings,
            Func<bool, string, string, bool> persist,
            Func<string, DebugPermissionResult> permissionProbe,
            Func<string?> actorProvider,
            ScadaAuditWriter? audit = null,
            bool preReleaseBuild = DebugModeBuild.IsPreRelease)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _persist = persist ?? throw new ArgumentNullException(nameof(persist));
            _permissionProbe = permissionProbe ?? throw new ArgumentNullException(nameof(permissionProbe));
            _actorProvider = actorProvider ?? (() => null);
            _audit = audit;
            _preReleaseBuild = preReleaseBuild;
        }

        /// <summary>开关被成功改写后触发（状态栏徽标、面板自身据此刷新）。</summary>
        public event Action? Changed;

        /// <summary>调试模式是否开启（<b>只看落盘值</b>，不看时间）。</summary>
        public bool IsEnabled => _settings()?.Enabled == true;

        /// <summary>
        /// 是否按"项目已上线"判定：构建常量说上线了，或者开发期配置显式演练上线。
        /// </summary>
        public bool ProjectReleased => !_preReleaseBuild || _settings()?.ProjectReleased == true;

        /// <summary>阶段文案（面板顶部那一行，说清"现在凭什么能开"）。</summary>
        public string StageText => ProjectReleased
            ? "项目已上线：开启 / 关闭调试模式需要「用户管理」权限"
            : "项目未上线：任何身份都能开启调试模式，无需登录";

        /// <summary>最近一次开启的操作者（不参与判定）</summary>
        public string EnabledBy => _settings()?.EnabledBy ?? string.Empty;

        /// <summary>最近一次开启的时间（不参与判定）</summary>
        public string EnabledAt => _settings()?.EnabledAt ?? string.Empty;

        /// <summary>
        /// 当前身份能不能使用调试功能（打开面板 / 改开关走同一条判据）。
        /// </summary>
        public bool CanUse(out string? reason)
        {
            if (!ProjectReleased)
            {
                reason = null;
                return true;
            }

            var result = _permissionProbe(ScadaPermissionKeys.UserAdministration);
            if (result.Allowed)
            {
                reason = null;
                return true;
            }

            reason = string.IsNullOrWhiteSpace(result.Reason)
                ? $"需要「{ScadaPermissionKeys.DisplayName(ScadaPermissionKeys.UserAdministration)}」权限"
                : result.Reason;
            return false;
        }

        /// <summary>
        /// 改写调试模式开关并立即落盘。<b>没有时间参数</b>——不存在"开多久"这回事。
        /// </summary>
        /// <returns>true = 已落盘；false = 无权限或写盘失败（<paramref name="error"/> 是可直接上屏的原因）</returns>
        public bool TrySetEnabled(bool enabled, out string? error)
        {
            lock (_gate)
            {
                var action = enabled ? EnableAction : DisableAction;

                if (!CanUse(out var denyReason))
                {
                    error = denyReason ?? "没有权限修改调试模式";
                    Audit(action, ScadaAuditOutcome.Denied, error);
                    return false;
                }

                var actor = _actorProvider() ?? string.Empty;
                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                try
                {
                    if (!_persist(enabled, actor, stamp))
                    {
                        error = "调试模式配置节缺失，未能写入 AppConfig.json";
                        Audit(action, ScadaAuditOutcome.Failed, error);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    // 写盘失败不能让调用方（UI 线程）崩：这与账号本/审计的处置口径一致——
                    // 配置写不成只该"这次开关没生效"，不该让整个界面退出。
                    error = $"调试模式写入配置失败：{ex.GetType().Name}：{ex.Message}";
                    Audit(action, ScadaAuditOutcome.Failed, error);
                    return false;
                }

                error = null;
                Audit(action, ScadaAuditOutcome.Success,
                    enabled
                        ? $"已开启（操作者：{(string.IsNullOrWhiteSpace(actor) ? "未登录" : actor)}）"
                        : "已关闭");
                RaiseChanged();
                return true;
            }
        }

        private void Audit(string action, ScadaAuditOutcome outcome, string? detail)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider(),
                AuditSubject,
                AuditEventText,
                action,
                outcome,
                detail));

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;

            try { handler(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DebugModeService] 变更通知失败：{ex.GetType().Name}：{ex.Message}");
            }
        }
    }
}
