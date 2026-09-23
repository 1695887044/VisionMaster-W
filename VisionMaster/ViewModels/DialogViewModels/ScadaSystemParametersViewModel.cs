using System;
using System.Collections.Generic;
using System.Globalization;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「系统参数设置」弹窗：把原先写死在代码里的几项运行参数交给现场。
    ///
    /// 为什么要做这个弹窗
    /// ---------
    /// 空闲 10 分钟自动登出、审计留 90 天、报警历史留 365 天——这三个数字原来都是
    /// <b>代码里的常量</b>。它们都是"换一个现场就该换一个值"的东西：
    /// 有的工位走开半小时是常态（按 10 分钟踢人只会逼人用管理员账号永不登出），
    /// 有的机台磁盘紧张（留 365 天报警历史纯属浪费），有的客户有留档要求（一天都不能删）。
    /// 写死的结果是"每换一个现场改一次源码、重编一次软件"。
    ///
    /// 口径（与 <see cref="ScadaRunWindowSettingsViewModel"/> 逐条对齐）
    /// ---------
    /// ① **写软件级配置**：落盘到 AppConfig.json，不是 .vms。这三个值描述的是"这台机器怎么用"，
    ///    与"当前打开的是哪份方案"无关——Audit / Alarms 目录本来就是全局共享的。
    /// ② **取消不留痕**：只有确定才写盘。改到一半点取消，配置保持原样。
    /// ③ **打开时现读**：<see cref="OnDialogOpened"/> 每次都从配置重新灌一遍编辑态，
    ///    不依赖"容器每次都新建一个 VM"这个假设（万一注册方式变成复用实例，
    ///    界面上显示的将是上一次点开时的旧值，而不是此刻真正生效的值）。
    /// ④ **改完立即生效、无需重启**：宿主不是"把新值推给谁"，而是让用它的地方
    ///    <b>每次现读配置</b>——空闲计时器每一拍读一次（<c>ScadaAccessPolicy.IdleTimeout</c>），
    ///    两个落盘端每次清理读一次（<c>RetentionDays</c>）。于是"改完生效"是结构上保证的，
    ///    不靠谁记得在某处同步一下。
    ///
    /// 为什么用<b>字符串</b>属性接输入框，而不是直接绑 <c>int</c>
    /// ---------
    /// 直接把 <c>int</c> 绑到 <c>TextBox.Text</c>，用户敲进"3O"（字母 O）时 WPF 会静默地
    /// 把绑定置为无效、属性保持旧值——界面上什么都看不出来，点了确定还写盘成功，
    /// 而实际保存的是他上一次的值。这里收字符串、在点确定时统一解析并给出明确原因，
    /// 是"宁可不让他确定，也不能让他以为改成了"。
    ///
    /// 校验口径与配置模型一致（<c>0 或负数 = 不自动登出 / 永不清理</c>），
    /// 不在界面这一层另立更严的规则：两处规则不一致时，界面上"填 0 就能不清理"的提示
    /// 会变成假话。
    ///
    /// 为什么要落审计（S13-f）
    /// ---------
    /// 这三个数字是"这台机器的安全策略"：把空闲登出改成 0 等于**关掉自动登出**，
    /// 把审计保留改成 1 等于**自己给自己缩短追责窗口**。都是"改完现场看不出来、
    /// 事后才想起来要问"的一类操作，正是审计存在的理由。
    /// 落笔点选在这里（配置的唯一界面出口）而不是 <see cref="AppSettingsService"/>：
    /// 服务层只认"写盘"，认不出"这次写盘是用户点的确定还是程序内部的自动保存"，
    /// 在那里记会把程序自己的动作也记成"某人改了参数"。
    /// </summary>
    public class ScadaSystemParametersViewModel : BindableBase, IDialogAware
    {
        /// <summary>
        /// 审计"事件"列写<b>类别名</b>而不是动作名——与 <see cref="ScadaUserStore"/> 的
        /// "账号管理"、运行态越权的"权限校验"同一个口径：这一列答的是"哪一类事"，
        /// "具体哪一件"在动作列。
        /// </summary>
        private const string AuditEventText = "系统参数";

        /// <summary>审计"对象"列：这三项改的都是它</summary>
        private const string AuditSubject = "AppConfig.json";

        /// <summary>审计"动作"列：三格一次确定，所以只有一个动作名</summary>
        private const string AuditActionText = "修改参数";

        private readonly AppSettingsService _settings;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;

        private string _idleTimeoutText = string.Empty;
        private string _auditRetentionText = string.Empty;
        private string _alarmRetentionText = string.Empty;
        private string? _errorMessage;

        /// <param name="audit">
        /// 操作审计落盘端。默认 null = 不记（单机调试 / 断言里直接 new 的路径）。
        /// <b>可选参数必须由宿主显式工厂注册</b>：交给容器去猜的结果是编译能过、
        /// 运行起来"改了参数一条审计都没有"而且不报错（见 App.xaml.cs 的注册处）。
        /// </param>
        /// <param name="actorProvider">
        /// "此刻登录着谁"。本类不认识会话，所以由宿主把 ScadaAccessPolicy.CurrentUserName 递进来；
        /// 取不到时 <see cref="ScadaAuditEntry"/> 自己回落"未登录"——那本身也是一条线索。
        /// </param>
        public ScadaSystemParametersViewModel(
            AppSettingsService settings,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _audit = audit;
            _actorProvider = actorProvider;
            ReloadFromConfig();

            ConfirmCommand = new DelegateCommand(OnConfirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "系统参数设置";

        /// <summary>空闲多久自动登出（分钟）。0 或负数 = 不自动登出</summary>
        public string IdleTimeoutText
        {
            get => _idleTimeoutText;
            set { if (SetProperty(ref _idleTimeoutText, value)) ErrorMessage = null; }
        }

        /// <summary>操作审计保留天数。0 或负数 = 永不清理</summary>
        public string AuditRetentionText
        {
            get => _auditRetentionText;
            set { if (SetProperty(ref _auditRetentionText, value)) ErrorMessage = null; }
        }

        /// <summary>报警历史保留天数。0 或负数 = 永不清理</summary>
        public string AlarmRetentionText
        {
            get => _alarmRetentionText;
            set { if (SetProperty(ref _alarmRetentionText, value)) ErrorMessage = null; }
        }

        /// <summary>校验 / 写盘失败的原因（红字）。没有问题时为 <c>null</c></summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        /// <summary>只用来决定错误条显示与否（<c>string</c> 转可见性要额外一个转换器，不值当）</summary>
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 把配置里的当前值灌进编辑态。
        /// 构造函数与 <see cref="OnDialogOpened"/> 共用一份——两处各写一遍，
        /// 早晚出现"新加一个字段只在其中一处灌"（表现为：首次打开是对的，再打开就退回旧值）。
        /// </summary>
        private void ReloadFromConfig()
        {
            var config = _settings.Current;
            IdleTimeoutText = config.IdleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);
            AuditRetentionText = config.AuditRetentionDays.ToString(CultureInfo.InvariantCulture);
            AlarmRetentionText = config.AlarmHistoryRetentionDays.ToString(CultureInfo.InvariantCulture);
            ErrorMessage = null;
        }

        private void OnConfirm()
        {
            // 三个字段一起校验、一起报错：逐条弹窗式的"错一个改一个"在设置页上很烦人。
            // 短路求值顺带保证 error 里留下的是**第一条**出错的原因。
            if (!TryParseField(IdleTimeoutText, "空闲自动登出", out var idleMinutes, out var error)
                || !TryParseField(AuditRetentionText, "操作审计保留", out var auditDays, out error)
                || !TryParseField(AlarmRetentionText, "报警历史保留", out var alarmDays, out error))
            {
                ErrorMessage = error;
                return;
            }

            var config = _settings.Current;

            // 先留旧值：审计"说明"列要写净效果（旧值 → 新值），落盘之后就读不到了。
            var oldIdle = config.IdleTimeoutMinutes;
            var oldAudit = config.AuditRetentionDays;
            var oldAlarm = config.AlarmHistoryRetentionDays;

            // 只列**真变了的**项：三项一股脑列出来，事后就分不清"他改了哪一项"和"他只是点了一下确定"。
            var changes = new List<string>();
            if (idleMinutes != oldIdle) changes.Add($"空闲自动登出：{oldIdle} → {idleMinutes} 分钟");
            if (auditDays != oldAudit) changes.Add($"操作审计保留：{oldAudit} → {auditDays} 天");
            if (alarmDays != oldAlarm) changes.Add($"报警历史保留：{oldAlarm} → {alarmDays} 天");

            if (changes.Count == 0)
            {
                // 一个值都没动就不写盘：配置内容与盘上那份逐字相同，重写一遍只是白动一次磁盘
                // （而 AppConfig.json 在程序目录，某些部署下这一步本来就会失败——为一个没改的东西弹失败最没道理）。
                // 但仍然关窗：用户点的是「确定」，不是「取消」。
                Audit(ok: true, detail: $"三项参数均未变（{oldIdle} 分钟 / {oldAudit} 天 / {oldAlarm} 天），未写盘", error: null);
                RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
                return;
            }

            config.IdleTimeoutMinutes = idleMinutes;
            config.AuditRetentionDays = auditDays;
            config.AlarmHistoryRetentionDays = alarmDays;

            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                // 配置写在程序目录下：装在 Program Files 里、或以普通用户跑，
                // 这里就是会失败的。失败时**不关弹窗**——用户刚敲进去的三个值还在界面上，
                // 关掉就等于让他重填一遍。配置在内存里已被改过，但没落盘，
                // 所以这一句要如实说清"没保存成功"，不能只说"失败"。
                ErrorMessage = $"保存失败，参数没有写入 AppConfig.json（重启后会回到原值）：{ex.Message}";
                Audit(ok: false, detail: null, error: ErrorMessage);
                return;
            }

            Audit(ok: true, detail: string.Join("；", changes), error: null);
            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
        }

        /// <summary>
        /// 落一条审计。落盘端自己吞掉 IO 异常并报一次诊断（见 <see cref="ScadaAuditWriter"/>），
        /// 所以这里不 try、也不看返回值：审计写不成不该让"参数改没改成"这件事变样。
        ///
        /// 成功与失败都记：<b>"谁试着把空闲登出改成 0 但没成"与"改成了"是两条不同的线索</b>，
        /// 只记成功的那一半，事后问"他到底动过没有"答案会是"没有"。
        /// 失败那一格的说明直接用 <paramref name="error"/>——那是给操作员看的原因，
        /// 与界面上红字同源，审计与现场口述才对得上。
        /// </summary>
        private void Audit(bool ok, string? detail, string? error)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                AuditSubject,
                AuditEventText,
                AuditActionText,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        /// <summary>
        /// 解析一个整数字段。空串也走失败分支——"留空"不该被当成 0 悄悄变成"永不清理"，
        /// 那正好是最不该猜错的一个值。
        /// </summary>
        private static bool TryParseField(string? text, string fieldName, out int value, out string? error)
        {
            error = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            error = string.IsNullOrEmpty(trimmed)
                ? $"「{fieldName}」不能留空，请填整数（0 表示不限制）。"
                : $"「{fieldName}」请填整数，当前填的是「{trimmed}」。";
            return false;
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>打开时把配置里的当前值读进编辑态，让界面显示"现在生效的是多少"</summary>
        public void OnDialogOpened(IDialogParameters parameters) => ReloadFromConfig();

        #endregion
    }
}
