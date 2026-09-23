using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「运行窗口落在哪块屏」下拉里的一项。
    ///
    /// 为什么不直接把 <see cref="ScadaMonitor"/> 摆进下拉：那是服务层的"一块屏"（物理矩形 + 设备名），
    /// 不含任何展示口径。界面要的是"叫什么、显示成什么、鼠标停上去提示什么"——
    /// 把 Label 拼装写在这里，服务层就不必认识中文，也不必为了换个说法而改。
    /// </summary>
    public sealed class ScadaMonitorOption
    {
        public ScadaMonitorOption(string deviceName, string label, string detail)
        {
            DeviceName = deviceName;
            Label = label;
            Detail = detail;
        }

        /// <summary>设备名（如 <c>\\.\DISPLAY2</c>）；<b>空串 = 跟随主屏</b>。这就是落进配置的那个值</summary>
        public string DeviceName { get; }

        /// <summary>下拉里显示的文字</summary>
        public string Label { get; }

        /// <summary>鼠标停上去的提示（放设备名，方便对得上系统显示设置）</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// 「运行窗口设置」弹窗：选组态运行窗口的显示形态（依附主窗口 / 独立窗口），
    /// 以及它落在哪块显示器上。
    ///
    /// 口径
    /// ---------
    /// ① **写进软件级配置**：落盘到 AppConfig.json（<c>AppConfigModel.RunWindowMode</c> 与
    ///    <c>RunWindowMonitor</c>），不是 .vms。它们是"这台机器怎么用"的偏好，不是"这份方案长什么样"
    ///    的一部分——同一份方案在开发机上想看窗口化、在现场机要全屏、在双屏机上还要指定副屏，
    ///    几台机器不该互相改。
    /// ② **取消不留痕**：只有确定才写盘。改到一半点取消，配置保持原样。
    /// ③ **打开时现读**：编辑态除了构造函数读一次，<see cref="OnDialogOpened"/> 再读一次。
    ///    不依赖"容器每次都新建一个 VM"这个假设——万一哪天注册方式变成复用实例，
    ///    字段里留着的就是上一次点开的那个选项，界面显示的将不是当前真正生效的形态。
    /// ④ **即时生效**：宿主每次 Start 现读配置（见 ScadaRuntimeHost），所以改完不必重启，
    ///    下一次点「运行」就是新形态、新落屏。已经开着的运行窗口不动——它已经在跑了。
    ///
    /// 显示器清单为什么在构造函数里取一次
    /// ---------
    /// 它是"点开这一刻机器上接着什么"，不是配置：弹窗活着的时候插拔显示器属于极端操作，
    /// 为它做实时刷新只会引入一个没人测过的刷新路径。每次点开都重新构造 VM，清单自然是新的。
    ///
    /// 为什么要落审计（S13-f）
    /// ---------
    /// 这一项决定"现场跑生产时看到的是不是只有运行画面"：从依附切到独立，主界面（含图像、
    /// 日志）就重新露出来了。属于"改了没人在意、出事时又要问是谁改的"那一类，与
    /// <see cref="ScadaSystemParametersViewModel"/> 同为软件级配置，审计口径逐条对齐。
    /// </summary>
    public class ScadaRunWindowSettingsViewModel : BindableBase, IDialogAware
    {
        /// <summary>审计"事件"列写类别名（口径见 <see cref="ScadaUserStore"/> 的"账号管理"）</summary>
        private const string AuditEventText = "运行窗口";

        /// <summary>审计"对象"列：这一项改的是它</summary>
        private const string AuditSubject = "AppConfig.json";

        /// <summary>审计"动作"列：形态与目标屏一次确定，所以只有一个动作名</summary>
        private const string AuditActionText = "修改显示设置";

        private readonly AppSettingsService _settings;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;

        /// <summary>编辑中的形态（未点确定之前只在内存里，不碰配置）</summary>
        private ScadaRunWindowMode _mode;

        /// <summary>编辑中的目标屏设备名（空串 = 跟随主屏）；同样只在内存里</summary>
        private string _monitorDeviceName = "";

        /// <summary>写盘失败的原因（红字）。没有问题时为 <c>null</c></summary>
        private string? _errorMessage;

        /// <param name="audit">
        /// 操作审计落盘端。默认 null = 不记（单机调试 / 断言里直接 new 的路径）。
        /// <b>可选参数必须由宿主显式工厂注册</b>，否则编译能过、运行起来一条审计都没有且不报错。
        /// </param>
        /// <param name="actorProvider">"此刻登录着谁"（取不到时回落"未登录"，见 <see cref="ScadaAuditEntry"/>）</param>
        public ScadaRunWindowSettingsViewModel(
            AppSettingsService settings,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _audit = audit;
            _actorProvider = actorProvider;

            Monitors = BuildMonitorOptions();
            Reload();

            ConfirmCommand = new DelegateCommand(OnConfirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "运行窗口设置";

        /// <summary>
        /// 可选的目标屏：第一项永远是「跟随主屏」，后面是当前接着的真实显示器。
        /// 顺序由 <see cref="ScadaMonitors.All"/> 保证（主屏第一、其余按设备名），每次点开都一样。
        /// </summary>
        public IReadOnlyList<ScadaMonitorOption> Monitors { get; }

        /// <summary>
        /// 下拉选中项，绑的是设备名而不是选项对象（<c>SelectedValue</c> + <c>SelectedValuePath</c>）：
        /// 这样配置里存什么、界面绑什么、确定时写什么，始终是同一个字符串，
        /// 不会出现"选中的对象换了但字段没跟着换"那一类只在特定点击顺序下才复现的错。
        ///
        /// 配置里那台显示器不在了（拔线 / 换口 / 别的机器）时读回来是空串——
        /// 界面显示「跟随主屏」，与运行时真正的回落结果一致，不会出现"看着选了副屏、实际跑在主屏"。
        /// </summary>
        public string SelectedMonitorDeviceName
        {
            get => _monitorDeviceName;
            set
            {
                // ComboBox 在 ItemsSource 变化等时机可能写回 null，那一写忽略掉
                var name = value ?? "";
                if (string.Equals(name, _monitorDeviceName, StringComparison.OrdinalIgnoreCase)) return;
                _monitorDeviceName = name;
                ErrorMessage = null;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 写盘失败的原因（红字）。与「系统参数设置」同一手法：<b>不弹全局通知</b>——
        /// 通知飘一下就没了，而这句要留在弹窗上，让用户看着它决定"重试还是先不管"。
        /// 也因此这个 VM 不碰 <c>Notifier</c>（它要 <c>Application.Current</c>），
        /// 断言宿主里没有 WPF 应用实例，这一条路径才测得了。
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        /// <summary>只用来决定错误条显示与否（与「系统参数设置」同口径）</summary>
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        /// <summary>
        /// 独立窗口：带系统标题栏、可缩放，与主界面（含视觉图像面板）并排显示。
        /// 两个选项各自一个布尔属性（而不是一个枚举属性 + 转换器）：
        /// WPF 的 RadioButton 只认 <c>IsChecked</c> 这个布尔，多一个转换器就多一处可能失配的地方。
        /// </summary>
        public bool IsIndependent
        {
            get => _mode == ScadaRunWindowMode.IndependentWindow;
            set { if (value) SetMode(ScadaRunWindowMode.IndependentWindow); }
        }

        /// <summary>依附主窗口：无边框 + 最大化，盖住整块屏（现场运行形态）</summary>
        public bool IsAttached
        {
            get => _mode == ScadaRunWindowMode.AttachedToMainWindow;
            set { if (value) SetMode(ScadaRunWindowMode.AttachedToMainWindow); }
        }

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>
        /// 换形态并通知两个选项。
        /// setter 里只在 <c>value == true</c> 时进来，是因为 RadioButton 失去选中时会往
        /// <c>IsChecked</c> 写 <c>false</c>——那一写必须忽略，否则"选中新的"会紧接着被
        /// "旧的取消选中"覆盖回原值（两个 setter 抢着写同一个字段）。
        /// </summary>
        private void SetMode(ScadaRunWindowMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            ErrorMessage = null;
            RaisePropertyChanged(nameof(IsIndependent));
            RaisePropertyChanged(nameof(IsAttached));
        }

        /// <summary>
        /// 把配置里的当前值读进编辑态（形态 + 目标屏）。
        /// 目标屏要做一次"列表里还认不认得出来"的校验：认不出就归到空串（跟随主屏），
        /// 否则下拉会停在一个列表里根本没有的值上，界面看着像"没选"，实际确定时写下去的却是别的。
        /// </summary>
        private void Reload()
        {
            SetMode(_settings.Current.RunWindowMode);

            var configured = _settings.Current.RunWindowMonitor ?? "";
            SelectedMonitorDeviceName = Monitors.Any(o => string.Equals(o.DeviceName, configured, StringComparison.OrdinalIgnoreCase))
                ? configured
                : "";

            ErrorMessage = null;
        }

        /// <summary>
        /// 拼下拉项。第一项固定是「跟随主屏」——它不是"选主屏"的别名：
        /// 跟随是"主屏换了我跟着换"，与"钉死在某块屏上"不是一回事。
        /// 而且它是默认值：升级后所有现场机器读到的都是空串，落屏行为与 S13-e 之前逐字一致。
        /// </summary>
        private static IReadOnlyList<ScadaMonitorOption> BuildMonitorOptions()
        {
            var options = new List<ScadaMonitorOption>
            {
                new ScadaMonitorOption(
                    "",
                    "跟随主屏（默认）",
                    "不指定显示器：独立窗口落在主屏右下角，依附形态铺满主屏")
            };

            var all = ScadaMonitors.All();
            var primary = all.FirstOrDefault(m => m.IsPrimary);

            for (int i = 0; i < all.Count; i++)
            {
                var m = all[i];
                options.Add(new ScadaMonitorOption(
                    m.DeviceName,
                    // 物理分辨率（如 1920 × 1080）就是用户熟悉的那组数字，别拿 DIP 去换它
                    $"显示器 {i + 1}（{ScadaMonitors.DescribePosition(m, primary)}，{m.Bounds.Width:0} × {m.Bounds.Height:0}）",
                    m.DeviceName));
            }

            return options;
        }

        private void OnConfirm()
        {
            var config = _settings.Current;

            // 先留旧值：审计"说明"列要写净效果（旧值 → 新值），落盘之后就读不到了。
            var oldMode = config.RunWindowMode;
            var oldMonitor = config.RunWindowMonitor ?? "";

            // 只列**真变了的**项：两项一股脑列出来，事后就分不清"他改了哪一项"和"他只是点了一下确定"。
            var changes = new List<string>();
            if (_mode != oldMode)
                changes.Add($"形态：{DescribeMode(oldMode)} → {DescribeMode(_mode)}");
            if (!string.Equals(SelectedMonitorDeviceName, oldMonitor, StringComparison.OrdinalIgnoreCase))
                changes.Add($"目标屏：{DescribeMonitor(oldMonitor)} → {DescribeMonitor(SelectedMonitorDeviceName)}");

            if (changes.Count == 0)
            {
                // 一个值都没动就不写盘（理由与「系统参数设置」逐字相同），但仍然关窗——
                // 用户点的是「确定」，不是「取消」。
                Audit(ok: true,
                    detail: $"形态与目标屏均未变（{DescribeMode(oldMode)} / {DescribeMonitor(oldMonitor)}），未写盘",
                    error: null);
                RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
                return;
            }

            config.RunWindowMode = _mode;
            config.RunWindowMonitor = SelectedMonitorDeviceName;

            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                // 与「系统参数设置」逐条对齐：失败时**不关弹窗**——用户刚选的两项还在界面上，
                // 关掉就等于让他重选一遍；并如实说清"没保存成功"，不能只说"失败"。
                // 说明留在弹窗的红字区里（不是飘一下就走的全局通知）：用户要看着它决定重试还是先不管。
                ErrorMessage = $"保存失败，设置没有写入 AppConfig.json（重启后会回到原值）：{ex.Message}";
                Audit(ok: false, detail: null, error: ErrorMessage);
                return;
            }

            Audit(ok: true, detail: string.Join("；", changes), error: null);
            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
        }

        /// <summary>审计"说明"列里怎么称呼一种形态。不认识的档位（高版本存下的）就写"未知"，不编一个中文名</summary>
        private static string DescribeMode(ScadaRunWindowMode mode)
            => mode switch
            {
                ScadaRunWindowMode.AttachedToMainWindow => "依附主窗口",
                ScadaRunWindowMode.IndependentWindow => "独立窗口",
                _ => "未知"
            };

        /// <summary>
        /// 审计"说明"列里怎么称呼一块屏。空串 = 跟随主屏（配置里就是这么存的），
        /// 其余直接写设备名——它才是与系统「显示设置」对得上的那个值，
        /// 换成"显示器 2"反而要多绕一层。
        /// </summary>
        private static string DescribeMonitor(string deviceName)
            => string.IsNullOrEmpty(deviceName) ? "跟随主屏" : deviceName;

        /// <summary>
        /// 落一条审计。落盘端自己吞掉 IO 异常并报一次诊断（见 <see cref="ScadaAuditWriter"/>），
        /// 所以这里不 try、也不看返回值：审计写不成不该让"设置改没改成"这件事变样。
        /// 成功与失败都记：只记成功的那一半，事后问"他到底动过没有"答案会是"没有"。
        /// </summary>
        private void Audit(bool ok, string? detail, string? error)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                AuditSubject,
                AuditEventText,
                AuditActionText,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>打开时把配置里的当前形态与目标屏读进编辑态，让界面显示"现在生效的是哪个"</summary>
        public void OnDialogOpened(IDialogParameters parameters) => Reload();

        #endregion
    }
}
