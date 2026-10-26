using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using NLog;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>诊断信息的一行（分组 / 项 / 值）。</summary>
    public sealed record DebugInfoRow(string Group, string Name, string Value);

    /// <summary>
    /// 调试面板：一块只读诊断信息 + 一个「调试模式」开关。
    ///
    /// <b>面板本身不判权限</b>：能不能打开由宿主（ShellViewModel 经 <see cref="DebugModeService"/>）判，
    /// 开关能不能改也由同一个服务判。这里只负责"把拒绝原因原样说给用户"，
    /// 免得面板与宿主各写一套判据（两份判据迟早会分叉，表现是"菜单能进、开关点不动"）。
    ///
    /// <b>调试模式不绕过任何权限</b>：开启后只多出"附加诊断行"与状态栏徽标，
    /// 流程启动 / 硬件配置 / 用户管理仍各走各的权限 Key。这条必须写在面板上，
    /// 否则现场会把它理解成"开发者后门"。
    ///
    /// <b>不按时间失效</b>：开关只有开 / 关两态，没有有效期；面板上也刻意不显示"剩余时间"。
    /// </summary>
    public sealed class DebugPanelViewModel : BindableBase, IDialogAware
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly DebugModeService _debugMode;
        private readonly AppSettingsService _settings;
        private readonly IScadaAccessPolicy _accessPolicy;
        private readonly IReadOnlyWorkspaceContext _workspace;
        private bool _subscribed;

        public DebugPanelViewModel(
            DebugModeService debugMode,
            AppSettingsService settings,
            IScadaAccessPolicy accessPolicy,
            IReadOnlyWorkspaceContext workspace)
        {
            _debugMode = debugMode ?? throw new ArgumentNullException(nameof(debugMode));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            ToggleCommand = new DelegateCommand(Toggle);
            RefreshCommand = new DelegateCommand(Refresh);
            CopyCommand = new DelegateCommand(CopyDiagnostics);
            OpenLogFolderCommand = new DelegateCommand(() => OpenFolder(LogDirectory));
            OpenAppFolderCommand = new DelegateCommand(() => OpenFolder(AppContext.BaseDirectory));
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke());
        }

        public string Title => "调试面板";

        public DelegateCommand ToggleCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand CopyCommand { get; }
        public DelegateCommand OpenLogFolderCommand { get; }
        public DelegateCommand OpenAppFolderCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public ObservableCollection<DebugInfoRow> Rows { get; } = new();

        public bool IsEnabled => _debugMode.IsEnabled;

        /// <summary>阶段说明：现在凭什么能开关（未上线免权限 / 已上线需用户管理）。</summary>
        public string StageText => _debugMode.StageText;

        public string ToggleText => IsEnabled ? "关闭调试模式" : "开启调试模式";

        /// <summary>
        /// 最近一次开启记录（只给人看）。<b>刻意不显示"有效期"</b>——没有这个语义，
        /// 显示一个倒计时会让人以为"到点会自己关"。
        /// </summary>
        public string EnabledInfoText
        {
            get
            {
                if (!IsEnabled) return "当前未开启";

                var by = string.IsNullOrWhiteSpace(_debugMode.EnabledBy) ? "未登录" : _debugMode.EnabledBy;
                var at = string.IsNullOrWhiteSpace(_debugMode.EnabledAt) ? "未知时间" : _debugMode.EnabledAt;
                return $"已开启 · {by} · {at}（不会自动失效，只有手动关闭才失效）";
            }
        }

        public string SummaryText => IsEnabled
            ? "调试模式已开启：下方附带进程/内存/权限明细。它不绕过任何权限或安全闸门。"
            : "调试模式未开启：只显示基础诊断。开启后附带进程/内存/权限明细，并常驻到手动关闭为止。";

        public string LogDirectory
        {
            get
            {
                var configured = _settings.Current?.LogDirectory;
                return string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(AppContext.BaseDirectory, "Logs")
                    : configured!;
            }
        }

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            if (!_subscribed) return;
            _debugMode.Changed -= OnModeChanged;
            _subscribed = false;
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (!_subscribed)
            {
                _debugMode.Changed += OnModeChanged;
                _subscribed = true;
            }

            Refresh();
        }

        private void OnModeChanged()
        {
            RaisePropertyChanged(nameof(IsEnabled));
            RaisePropertyChanged(nameof(ToggleText));
            RaisePropertyChanged(nameof(EnabledInfoText));
            RaisePropertyChanged(nameof(SummaryText));
            RaisePropertyChanged(nameof(StageText));
            Refresh();
        }

        private void Toggle()
        {
            var target = !_debugMode.IsEnabled;
            if (!_debugMode.TrySetEnabled(target, out var error))
            {
                Notifier.ShowWarning(error ?? "调试模式切换失败");
                return;
            }

            Notifier.ShowInfo(target
                ? "调试模式已开启：会一直有效，直到手动关闭。"
                : "调试模式已关闭。");
        }

        private void Refresh()
        {
            Rows.Clear();
            var group = "基础";

            void Add(string name, string value) => Rows.Add(new DebugInfoRow(group, name, value));

            Add("版本", EntryVersion());
            Add("进程", $"{SafePid()}（{(Environment.Is64BitProcess ? "64" : "32")} 位）");
            Add("运行时", RuntimeInformation.FrameworkDescription);
            Add("操作系统", RuntimeInformation.OSDescription);
            Add("程序目录", AppContext.BaseDirectory);
            Add("配置文件", DescribeConfigFile());
            Add("日志目录", LogDirectory);

            Add("当前用户", _accessPolicy.IsLoggedIn
                ? $"{_accessPolicy.CurrentUserName}（已登录）"
                : $"{_accessPolicy.CurrentUserName}（未登录）");
            Add("用户组", string.IsNullOrWhiteSpace(_accessPolicy.CurrentGroupId)
                ? "—"
                : $"{_accessPolicy.CurrentGroupName}（{_accessPolicy.CurrentGroupId}）");
            Add("权限数", _accessPolicy.Permissions?.Count.ToString() ?? "0");

            var solution = _workspace.CurrentSolution;
            var flow = _workspace.CurrentFlow;
            Add("当前方案", solution == null ? "未加载" : solution.SolutionName);
            Add("当前流程", flow == null ? "未选择" : flow.FlowName);

            if (!IsEnabled)
            {
                RaisePropertyChanged(nameof(IsEnabled));
                RaisePropertyChanged(nameof(ToggleText));
                RaisePropertyChanged(nameof(EnabledInfoText));
                RaisePropertyChanged(nameof(SummaryText));
                return;
            }

            // ---- 以下只在调试模式开启时出现：证明"这个开关确实多给了一块信息" ----
            group = "调试附加";
            Add("权限明细", _accessPolicy.Permissions is { Count: > 0 } permissions
                ? string.Join("、", permissions)
                : "（无）");
            Add("工作集", $"{SafeWorkingSetMb():0.0} MB");
            Add("托管内存", $"{GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d:0.0} MB");
            Add("线程数", SafeThreadCount().ToString());
            Add("句柄数", SafeHandleCount().ToString());
            Add("启动参数", DescribeCommandLine());
            Add("配置修改时间", DescribeConfigWriteTime());

            RaisePropertyChanged(nameof(IsEnabled));
            RaisePropertyChanged(nameof(ToggleText));
            RaisePropertyChanged(nameof(EnabledInfoText));
            RaisePropertyChanged(nameof(SummaryText));
        }

        private void CopyDiagnostics()
        {
            try
            {
                Clipboard.SetText(BuildDiagnosticsText());
                Notifier.ShowSuccess("诊断信息已复制到剪贴板");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "复制调试诊断信息失败");
                Notifier.ShowWarning($"复制失败：{ex.Message}");
            }
        }

        private string BuildDiagnosticsText()
        {
            var text = new StringBuilder();
            text.AppendLine("==== VisionMaster 调试诊断 ====");
            text.AppendLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            text.AppendLine($"阶段：{StageText}");
            text.AppendLine($"调试模式：{(IsEnabled ? "已开启" : "未开启")}");
            text.AppendLine();

            string? currentGroup = null;
            foreach (var row in Rows)
            {
                if (!string.Equals(currentGroup, row.Group, StringComparison.Ordinal))
                {
                    currentGroup = row.Group;
                    text.AppendLine($"[{row.Group}]");
                }

                text.AppendLine($"  {row.Name}: {row.Value}");
            }

            return text.ToString();
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    Notifier.ShowWarning($"目录不存在：{path}");
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "打开目录失败：{0}", path);
                Notifier.ShowWarning($"打开目录失败：{ex.Message}");
            }
        }

        private static string EntryVersion()
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetName().Version;
            return version == null ? "未知" : version.ToString();
        }

        private static string SafePid()
        {
            try { return Environment.ProcessId.ToString(); }
            catch { return "未知"; }
        }

        private static double SafeWorkingSetMb()
        {
            try { return Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d; }
            catch { return 0; }
        }

        private static int SafeThreadCount()
        {
            try { return Process.GetCurrentProcess().Threads.Count; }
            catch { return 0; }
        }

        private static int SafeHandleCount()
        {
            try { return Process.GetCurrentProcess().HandleCount; }
            catch { return 0; }
        }

        private static string DescribeCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            return args.Length <= 1 ? "（无）" : string.Join(" ", args.Skip(1));
        }

        private static string DescribeConfigFile()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "AppConfig.json");
            return File.Exists(path) ? path : $"{path}（不存在）";
        }

        private static string DescribeConfigWriteTime()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "AppConfig.json");
                return File.Exists(path)
                    ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss")
                    : "—";
            }
            catch
            {
                return "—";
            }
        }
    }
}
