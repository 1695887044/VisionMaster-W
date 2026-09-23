using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VisionMaster.Models;
using VisionMaster.Services;
using UI.CustomControl;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 方案列表弹窗（软件级配置）：
    /// 管理方案清单（打开/默认启动/增删/排序），确认后持久化到程序目录 AppConfig.json
    ///
    /// 为什么要落审计（S13-f）
    /// ---------
    /// 这个弹窗改的是"开机自动打开哪份方案"与"清单里有哪些方案"。前者在无人值守的机台上
    /// 直接决定"上电后跑的是哪一套程序"——改错了现场要到第二天开机才发现，那时更要知道是谁改的。
    /// 落笔点选在这里（清单的唯一界面出口）而不是 <see cref="AppSettingsService"/>：
    /// 服务层只认"写盘"，在那里记会把程序内部的自动保存也记成"某人改了清单"。
    /// </summary>
    public class SolutionListViewModel : BindableBase, IDialogAware
    {
        /// <summary>审计"事件"列写类别名（口径见 <see cref="ScadaUserStore"/> 的"账号管理"）</summary>
        private const string AuditEventText = "方案清单";

        /// <summary>审计"对象"列：这一项改的是它</summary>
        private const string AuditSubject = "AppConfig.json";

        /// <summary>审计"动作"列：默认启动方案与清单一次确定，所以只有一个动作名</summary>
        private const string AuditActionText = "修改方案清单";

        private readonly SolutionService solutionService;
        private readonly IWorkspaceManager workspace;
        private readonly AppSettingsService appSettings;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "方案列表";

        private ObservableCollection<AppSolutionEntry> _solutions = new();
        /// <summary>方案清单（弹窗内工作副本，确认后才写回配置）</summary>
        public ObservableCollection<AppSolutionEntry> Solutions
        {
            get => _solutions;
            set => SetProperty(ref _solutions, value);
        }

        private AppSolutionEntry _selectedSolution;
        /// <summary>选中的方案条目</summary>
        public AppSolutionEntry SelectedSolution
        {
            get => _selectedSolution;
            set => SetProperty(ref _selectedSolution, value);
        }

        private string _startupSolutionPath = "";
        /// <summary>默认启动方案路径（底部"自动加载路径"只读展示）</summary>
        public string StartupSolutionPath
        {
            get => _startupSolutionPath;
            set => SetProperty(ref _startupSolutionPath, value);
        }

        public DelegateCommand OpenCommand { get; }
        public DelegateCommand SetStartupCommand { get; }
        public DelegateCommand AddCurrentCommand { get; }
        public DelegateCommand AddCommand { get; }
        public DelegateCommand DeleteCommand { get; }
        public DelegateCommand MoveUpCommand { get; }
        public DelegateCommand MoveDownCommand { get; }
        public DelegateCommand ConfirmCommand { get; }
        public DelegateCommand CancelCommand { get; }

        /// <param name="audit">
        /// 操作审计落盘端。默认 null = 不记（单机调试 / 断言里直接 new 的路径）。
        /// <b>可选参数必须由宿主显式工厂注册</b>，否则编译能过、运行起来一条审计都没有且不报错。
        /// </param>
        /// <param name="actorProvider">"此刻登录着谁"（取不到时回落"未登录"，见 <see cref="ScadaAuditEntry"/>）</param>
        public SolutionListViewModel(
            SolutionService solutionService,
            IWorkspaceManager workspace,
            AppSettingsService appSettings,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null)
        {
            this.solutionService = solutionService;
            this.workspace = workspace;
            this.appSettings = appSettings;
            _audit = audit;
            _actorProvider = actorProvider;

            OpenCommand = new DelegateCommand(OnOpen, () => SelectedSolution != null)
                .ObservesProperty(() => SelectedSolution);
            SetStartupCommand = new DelegateCommand(OnSetStartup, () => SelectedSolution != null)
                .ObservesProperty(() => SelectedSolution);
            AddCurrentCommand = new DelegateCommand(OnAddCurrent);
            AddCommand = new DelegateCommand(OnAdd);
            DeleteCommand = new DelegateCommand(OnDelete, () => SelectedSolution != null)
                .ObservesProperty(() => SelectedSolution);
            MoveUpCommand = new DelegateCommand(() => Move(-1), () => CanMove(-1))
                .ObservesProperty(() => SelectedSolution);
            MoveDownCommand = new DelegateCommand(() => Move(1), () => CanMove(1))
                .ObservesProperty(() => SelectedSolution);
            ConfirmCommand = new DelegateCommand(OnConfirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        #region 按钮动作

        /// <summary>打开选中方案进主界面</summary>
        private async void OnOpen()
        {
            var entry = SelectedSolution;
            if (entry == null || !File.Exists(entry.Path))
            {
                Notifier.ShowWarning("方案文件不存在：" + entry?.Path);
                return;
            }

            var loadResult = await solutionService.LoadAsync(entry.Path);
            if (!loadResult.Success)
            {
                Notifier.ShowError(loadResult.Message);
                return;
            }

            loadResult.Data.SolutionFilePath = entry.Path;
            workspace.SwitchSolution(loadResult.Data);
            SolutionConfigApplier.Restore(loadResult.Data.Config);
            Notifier.ShowSuccess($"方案 [{loadResult.Data.SolutionName}] 加载成功");
            RequestClose.Invoke(ButtonResult.OK);
        }

        /// <summary>把选中方案设为默认启动方案</summary>
        private void OnSetStartup()
        {
            StartupSolutionPath = SelectedSolution.Path;
        }

        /// <summary>把当前已打开的方案（需已保存过文件）追加进清单</summary>
        private void OnAddCurrent()
        {
            var current = workspace.CurrentSolution;
            if (current == null)
            {
                Notifier.ShowWarning("当前没有打开的解决方案");
                return;
            }
            if (string.IsNullOrEmpty(current.SolutionFilePath) || !File.Exists(current.SolutionFilePath))
            {
                Notifier.ShowWarning("当前方案尚未保存到磁盘，请先保存方案");
                return;
            }
            if (Solutions.Any(s => string.Equals(s.Path, current.SolutionFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                Notifier.ShowWarning("该方案已在列表中");
                return;
            }

            Solutions.Add(new AppSolutionEntry
            {
                Name = current.SolutionName,
                Comment = "",
                Path = current.SolutionFilePath
            });
            RefreshIndexes();
        }

        /// <summary>浏览添加方案文件</summary>
        private void OnAdd()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "VisionMaster方案 (*.vms)|*.vms|所有文件 (*.*)|*.*",
                Title = "添加方案",
                DefaultExt = ".vms",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog() != true) return;

            foreach (var file in dialog.FileNames)
            {
                if (Solutions.Any(s => string.Equals(s.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
                Solutions.Add(new AppSolutionEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Comment = "",
                    Path = file
                });
            }
            RefreshIndexes();
        }

        /// <summary>从清单移除选中项（不动磁盘文件）</summary>
        private void OnDelete()
        {
            var entry = SelectedSolution;
            if (entry == null) return;

            Solutions.Remove(entry);
            if (string.Equals(StartupSolutionPath, entry.Path, StringComparison.OrdinalIgnoreCase))
            {
                StartupSolutionPath = "";
            }
            RefreshIndexes();
            SelectedSolution = Solutions.FirstOrDefault();
        }

        private bool CanMove(int direction)
        {
            if (SelectedSolution == null) return false;
            var index = Solutions.IndexOf(SelectedSolution);
            return index >= 0 && index + direction >= 0 && index + direction < Solutions.Count;
        }

        /// <summary>上移/下移选中项</summary>
        private void Move(int direction)
        {
            var index = Solutions.IndexOf(SelectedSolution);
            var target = index + direction;
            if (target < 0 || target >= Solutions.Count) return;

            Solutions.Move(index, target);
            RefreshIndexes();
        }

        /// <summary>重排序号并刷新列表显示（条目为 POCO，整体重建触发通知）</summary>
        private void RefreshIndexes()
        {
            for (int i = 0; i < Solutions.Count; i++)
            {
                Solutions[i].Index = i + 1;
            }
            Solutions = new ObservableCollection<AppSolutionEntry>(Solutions);
        }

        /// <summary>确认：写回软件配置并持久化</summary>
        private void OnConfirm()
        {
            var config = appSettings.Current;

            // 先留旧值：审计"说明"列要写净效果（旧值 → 新值），落盘之后就读不到了。
            var oldStartup = config.StartupSolutionPath ?? "";
            var oldList = config.Solutions;
            var newList = Solutions.ToList();

            // 只列**真变了的**项：两项一股脑列出来，事后就分不清"他改了哪一项"和"他只是点了一下确定"。
            var changes = new List<string>();
            if (!string.Equals(oldStartup, StartupSolutionPath, StringComparison.OrdinalIgnoreCase))
                changes.Add($"默认启动方案：{DescribePath(oldStartup)} → {DescribePath(StartupSolutionPath)}");

            if (!SameList(oldList, newList))
            {
                // 项数变了就报项数（最要紧的那个事实）；项数没变只可能是顺序/名称/注释被调整过，
                // 这时报"3 项 → 3 项"等于没说，直接说调过更清楚。
                changes.Add(oldList.Count == newList.Count
                    ? $"方案清单内容或顺序已调整（{newList.Count} 项）"
                    : $"方案清单：{oldList.Count} 项 → {newList.Count} 项");
            }

            if (changes.Count == 0)
            {
                // 一处都没动就不写盘（理由与「系统参数设置」逐字相同），但仍然关窗——
                // 用户点的是「确定」，不是「取消」。
                Audit(ok: true,
                    detail: $"默认启动方案与清单均未变（{DescribePath(oldStartup)} / {oldList.Count} 项），未写盘",
                    error: null);
                RequestClose.Invoke(ButtonResult.OK);
                return;
            }

            config.StartupSolutionPath = StartupSolutionPath;
            config.Solutions = newList;

            try
            {
                appSettings.Save();
            }
            catch (Exception ex)
            {
                // 与「系统参数设置」逐条对齐：失败时**不关弹窗**——用户刚理好的清单还在界面上，
                // 关掉就等于让他重来一遍；并如实说清"没保存成功"，不能只说"失败"。
                var reason = $"保存失败，清单没有写入 AppConfig.json（重启后会回到原值）：{ex.Message}";
                Audit(ok: false, detail: null, error: reason);
                Notifier.ShowError(reason);
                return;
            }

            Audit(ok: true, detail: string.Join("；", changes), error: null);
            RequestClose.Invoke(ButtonResult.OK);
        }

        /// <summary>
        /// 两份清单是否逐项相同（顺序也算）。
        /// 比 Path / Name / Comment 三项：<c>Index</c> 是排出来的序号、不落盘（见 AppSolutionEntry），
        /// 拿它比对会把"只是重排了序号"当成改动。
        /// </summary>
        private static bool SameList(IReadOnlyList<AppSolutionEntry> old, IReadOnlyList<AppSolutionEntry> now)
        {
            if (old.Count != now.Count) return false;
            for (int i = 0; i < old.Count; i++)
            {
                if (!string.Equals(old[i].Path, now[i].Path, StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(old[i].Name, now[i].Name, StringComparison.Ordinal)) return false;
                if (!string.Equals(old[i].Comment, now[i].Comment, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>审计"说明"列里怎么称呼一条方案路径。没设置就写"（未设置）"，不留一格空白</summary>
        private static string DescribePath(string path)
            => string.IsNullOrEmpty(path) ? "（未设置）" : path;

        /// <summary>
        /// 落一条审计。落盘端自己吞掉 IO 异常并报一次诊断（见 <see cref="ScadaAuditWriter"/>），
        /// 所以这里不 try、也不看返回值：审计写不成不该让"清单改没改成"这件事变样。
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

        #endregion

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        /// <summary>打开时：把软件配置拷贝成工作副本（取消不污染配置）</summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            var config = appSettings.Current;
            StartupSolutionPath = config.StartupSolutionPath;
            Solutions = new ObservableCollection<AppSolutionEntry>(
                config.Solutions.Select(s => new AppSolutionEntry
                {
                    Name = s.Name,
                    Comment = s.Comment,
                    Path = s.Path
                }));
            RefreshIndexes();
        }

        #endregion
    }
}
