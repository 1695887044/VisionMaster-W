using System;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 权限目录（v4）：15 内置 + 自定义，上限 32；内置不可删、被引用不可删。
    ///
    /// <para>三条与 v3 实现的差别：① 列表带「使用组」与「类型」两列；② 新建走行内表单
    /// （点「新建权限」才展开），Key 预览与落盘走<b>同一个生成函数</b>；③ 删除按钮的
    /// <c>CanExecute</c> 真的拦内置行——v3 是按钮可点、点了才报错。</para>
    /// </summary>
    public sealed class ScadaPermissionCatalogViewModel : BindableBase, IDialogAware
    {
        private const string StaticNote =
            "删除前扫描所有组权限与全方案页面/图元的 RequiredPermission；任一引用即拒绝，并提示「N 个组 / M 个按钮引用」。"
            + "内置不可删；改名只改显示名；上限 32 条。";

        private readonly ScadaUserStore _store;
        private PermissionCatalogRow? _selected;
        private string _newName = string.Empty;
        private bool _isCreating;
        private string? _message;
        private bool _succeeded;

        public ScadaPermissionCatalogViewModel(ScadaUserStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
            BeginCreateCommand = new DelegateCommand(BeginCreate, () => !IsFull);
            CancelCreateCommand = new DelegateCommand(CancelCreate);
            CreateCommand = new DelegateCommand(Create, CanCreate);
            BeginRenameCommand = new DelegateCommand<PermissionCatalogRow>(BeginRename);
            CommitRenameCommand = new DelegateCommand<PermissionCatalogRow>(CommitRename);
            CancelRenameCommand = new DelegateCommand<PermissionCatalogRow>(r => { if (r != null) r.IsRenaming = false; });
            DeleteCommand = new DelegateCommand<PermissionCatalogRow>(Delete, CanDelete);

            Refresh();
        }

        public DialogCloseListener RequestClose { get; set; }
        public string Title => "权限目录";

        public ObservableCollection<PermissionCatalogRow> Rows { get; } = new();

        public DelegateCommand CloseCommand { get; }
        public DelegateCommand BeginCreateCommand { get; }
        public DelegateCommand CancelCreateCommand { get; }
        public DelegateCommand CreateCommand { get; }
        public DelegateCommand<PermissionCatalogRow> BeginRenameCommand { get; }
        public DelegateCommand<PermissionCatalogRow> CommitRenameCommand { get; }
        public DelegateCommand<PermissionCatalogRow> CancelRenameCommand { get; }
        public DelegateCommand<PermissionCatalogRow> DeleteCommand { get; }

        public string CountText => $"{Rows.Count}/{ScadaPermissionKeys.MaxCatalogCount}";

        public bool IsFull => Rows.Count >= ScadaPermissionKeys.MaxCatalogCount;

        public string CountHint
        {
            get
            {
                var builtIn = Rows.Count(r => r.IsBuiltIn);
                return $"已定义 {Rows.Count} 条（内置 {builtIn} + 自定义 {Rows.Count - builtIn}），上限 {ScadaPermissionKeys.MaxCatalogCount} 条";
            }
        }

        public bool IsCreating
        {
            get => _isCreating;
            private set
            {
                if (SetProperty(ref _isCreating, value))
                {
                    CreateCommand.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(NoteText));
                }
            }
        }

        public string NewName
        {
            get => _newName;
            set
            {
                if (SetProperty(ref _newName, value))
                {
                    RaisePropertyChanged(nameof(NewKeyPreview));
                    CreateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>与落盘同一个生成函数（<see cref="ScadaUserStore.PreviewPermissionKey"/>），预览与真值不分叉。</summary>
        public string NewKeyPreview => _store.PreviewPermissionKey(NewName);

        public PermissionCatalogRow? SelectedRow
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                    DeleteCommand.RaiseCanExecuteChanged();
            }
        }

        public string? Message
        {
            get => _message;
            private set
            {
                if (SetProperty(ref _message, value))
                {
                    RaisePropertyChanged(nameof(NoteText));
                    RaisePropertyChanged(nameof(MessageIsError));
                }
            }
        }

        public bool MessageIsError => !string.IsNullOrEmpty(Message) && !_succeeded;

        /// <summary>底栏固定说明；有操作回执时先显示回执（回执比说明更急）。</summary>
        public string NoteText => string.IsNullOrEmpty(Message) ? StaticNote : Message!;

        public void Refresh()
        {
            var keepKey = _selected?.Key;

            Rows.Clear();
            foreach (var info in _store.PermissionCatalog)
            {
                var used = _store.Groups.Count(g =>
                    g.Permissions.Contains(info.Key, StringComparer.Ordinal));
                Rows.Add(new PermissionCatalogRow(info, used));
            }

            RaisePropertyChanged(nameof(CountText));
            RaisePropertyChanged(nameof(IsFull));
            RaisePropertyChanged(nameof(CountHint));
            BeginCreateCommand.RaiseCanExecuteChanged();
            CreateCommand.RaiseCanExecuteChanged();

            SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Key, keepKey, StringComparison.Ordinal))
                         ?? Rows.FirstOrDefault();
            DeleteCommand.RaiseCanExecuteChanged();
        }

        private void BeginCreate()
        {
            if (IsFull) return;
            NewName = string.Empty;
            SetMessage(null, false);
            IsCreating = true;
        }

        private void CancelCreate()
        {
            IsCreating = false;
            NewName = string.Empty;
            SetMessage(null, false);
        }

        private bool CanCreate()
            => IsCreating && !IsFull && !string.IsNullOrWhiteSpace(NewName);

        private void Create()
        {
            if (!_store.TryAddPermission(NewName, out var key, out var error))
            {
                SetMessage(error, false);
                return;
            }

            SetMessage("已创建：" + key, true);
            IsCreating = false;
            NewName = string.Empty;
            Refresh();

            SelectedRow = Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal))
                          ?? SelectedRow;
        }

        private void BeginRename(PermissionCatalogRow? row)
        {
            if (row == null) return;

            // 一次只开一行：两行同时可编辑，保存后另一行的草稿就成了幽灵。
            foreach (var other in Rows)
                other.IsRenaming = false;

            row.RenameName = row.Name;
            row.IsRenaming = true;
            SelectedRow = row;
        }

        private void CommitRename(PermissionCatalogRow? row)
        {
            if (row == null) return;

            if (!_store.TryRenamePermission(row.Key, row.RenameName, out var error))
            {
                SetMessage(error, false);
                return;
            }

            SetMessage("已改名：" + row.RenameName.Trim(), true);
            Refresh();
        }

        private bool CanDelete(PermissionCatalogRow? row)
            => row != null && !row.IsBuiltIn;

        private void Delete(PermissionCatalogRow? row)
        {
            if (row == null) return;
            if (row.IsBuiltIn)
            {
                SetMessage("内置权限不可删除", false);
                return;
            }

            if (!_store.TryRemovePermission(row.Key, out var error))
            {
                SetMessage(error, false);
                return;
            }

            SetMessage("已删除：" + row.Key, true);
            Refresh();
        }

        private void SetMessage(string? message, bool succeeded)
        {
            _succeeded = succeeded;
            Message = message;
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) => Refresh();
    }

    /// <summary>权限目录的一行。<see cref="IsRenaming"/> 打开时，名称格换成输入框、操作格换成保存/取消。</summary>
    public sealed class PermissionCatalogRow : BindableBase
    {
        private bool _isRenaming;
        private string _renameName = string.Empty;

        public PermissionCatalogRow(ScadaPermissionInfo info, int usedGroupCount)
        {
            Key = info.Key;
            Name = info.Name;
            IsBuiltIn = info.IsBuiltIn;
            UsedGroupCount = usedGroupCount;
        }

        public string Key { get; }
        public string Name { get; }
        public bool IsBuiltIn { get; }
        public int UsedGroupCount { get; }

        public string TypeText => IsBuiltIn ? "内置" : "自定义";

        /// <summary>内置不可删；自定义是否被引用由存储判（它会报「N 个组 / M 个按钮」）。</summary>
        public bool CanDelete => !IsBuiltIn;

        public bool IsRenaming
        {
            get => _isRenaming;
            set => SetProperty(ref _isRenaming, value);
        }

        public string RenameName
        {
            get => _renameName;
            set => SetProperty(ref _renameName, value);
        }
    }
}
