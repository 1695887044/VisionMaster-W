using Prism.Commands;
using Prism.Mvvm;
using Prism.Dialogs;
using System;
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
    /// </summary>
    public class SolutionListViewModel : BindableBase, IDialogAware
    {
        private readonly SolutionService solutionService;
        private readonly IWorkspaceManager workspace;
        private readonly AppSettingsService appSettings;

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

        public SolutionListViewModel(
            SolutionService solutionService,
            IWorkspaceManager workspace,
            AppSettingsService appSettings)
        {
            this.solutionService = solutionService;
            this.workspace = workspace;
            this.appSettings = appSettings;

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
            appSettings.Current.StartupSolutionPath = StartupSolutionPath;
            appSettings.Current.Solutions = Solutions.ToList();
            appSettings.Save();
            RequestClose.Invoke(ButtonResult.OK);
        }

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
