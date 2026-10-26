using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组编辑（v4）：左侧「模板（只读）/ 自定义」两段、右侧组名 + 权限复选（名称 + Key）。
    ///
    /// <para>三条与 v3 实现的差别：① 模板与自定义分组展示，模板行带锁；② 权限行带 Key，
    /// 与权限目录对得上号；③ 删除守卫在按钮上就给出原因（模板 / 被 N 个用户引用），
    /// 不再"可点、点了才报错"。取消 Stop 的二次确认走 <see cref="MessageBox"/>（v3.1 §6 #12）。</para>
    /// </summary>
    public sealed class ScadaGroupEditorViewModel : BindableBase, IDialogAware
    {
        private readonly ScadaUserStore _store;
        private readonly IDialogService? _dialogs;
        private GroupRow? _selected;
        private string _editName = string.Empty;
        private string? _message;

        public ScadaGroupEditorViewModel(ScadaUserStore store, IDialogService? dialogs = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _dialogs = dialogs;

            NewCommand = new DelegateCommand(BeginNew);
            SaveCommand = new DelegateCommand(Save, CanSave);
            DeleteCommand = new DelegateCommand(Delete, CanDelete);
            CatalogCommand = new DelegateCommand(OpenCatalog);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
            Refresh();
        }

        public DialogCloseListener RequestClose { get; set; }
        public string Title => "用户组编辑";

        public ObservableCollection<GroupRow> TemplateGroups { get; } = new();
        public ObservableCollection<GroupRow> CustomGroups { get; } = new();
        public ObservableCollection<PermissionRow> Permissions { get; } = new();

        public DelegateCommand NewCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand DeleteCommand { get; }
        public DelegateCommand CatalogCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public GroupRow? SelectedGroup
        {
            get => _selected;
            set { if (SetProperty(ref _selected, value)) Load(value); }
        }

        public string EditName
        {
            get => _editName;
            set
            {
                if (SetProperty(ref _editName, value))
                    SaveCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsBuiltIn => _selected?.IsBuiltIn == true;

        public bool IsNew => _selected == null;

        /// <summary>模板组只读：组名、权限、保存、删除全部不可动。</summary>
        public bool CanEditName => !IsBuiltIn;

        public bool CanDeleteGroup => !IsBuiltIn && (_selected?.UserCount ?? 0) == 0;

        /// <summary>删除键禁用时旁边那句原因（模板 / 被引用）。空串 = 可以删。</summary>
        public string DeleteReason =>
            IsBuiltIn ? "内置模板不可删除"
            : _selected is { UserCount: > 0 } row ? $"有 {row.UserCount} 个用户引用，先迁移再删除"
            : string.Empty;

        /// <summary>取消 Stop 的红字提示。**始终显示**（基线要求固定一行），勾掉 Stop 时加框强调。</summary>
        public bool NeedsStopWarning => !IsNew && !IsBuiltIn
            && Permissions.Any(p => p.Key == ScadaPermissionKeys.Stop && !p.IsChecked);

        public string StopWarningText =>
            "取消「停止（Stop）」后该组将无法停止设备；若含「轴点动（AxisJog）」，点动中只能靠系统自动 JogStop。";

        public string PermissionCountText
        {
            get
            {
                var total = Permissions.Count;
                var done = Permissions.Count(p => p.IsChecked);
                return $"权限（{done}/{total}）";
            }
        }

        public string? Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        public void Refresh()
        {
            var keepId = _selected?.Id;

            TemplateGroups.Clear();
            CustomGroups.Clear();

            foreach (var group in _store.Groups)
            {
                var users = _store.Users.Count(u =>
                    string.Equals(u.GroupId, group.Id, StringComparison.Ordinal));
                var row = new GroupRow(group, users);
                (group.IsBuiltIn ? TemplateGroups : CustomGroups).Add(row);
            }

            var all = TemplateGroups.Concat(CustomGroups).ToList();
            SelectedGroup = all.FirstOrDefault(r => string.Equals(r.Id, keepId, StringComparison.Ordinal))
                            ?? CustomGroups.FirstOrDefault()
                            ?? all.FirstOrDefault();
        }

        private void Load(GroupRow? row)
        {
            Permissions.Clear();
            var builtIn = row?.IsBuiltIn == true;
            foreach (var permission in _store.PermissionCatalog)
            {
                var isChecked = row?.Permissions.Contains(permission.Key) == true;
                Permissions.Add(new PermissionRow(permission, isChecked, builtIn, OnPermissionChanged));
            }

            EditName = row?.Name ?? string.Empty;
            Message = null;

            RaisePropertyChanged(nameof(IsBuiltIn));
            RaisePropertyChanged(nameof(IsNew));
            RaisePropertyChanged(nameof(CanEditName));
            RaisePropertyChanged(nameof(CanDeleteGroup));
            RaisePropertyChanged(nameof(DeleteReason));
            RaisePropertyChanged(nameof(NeedsStopWarning));
            RaisePropertyChanged(nameof(PermissionCountText));

            SaveCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
        }

        private void BeginNew()
        {
            SelectedGroup = null;
            Permissions.Clear();
            foreach (var permission in _store.PermissionCatalog)
                Permissions.Add(new PermissionRow(permission, false, false, OnPermissionChanged));

            EditName = "新用户组";
            Message = null;

            RaisePropertyChanged(nameof(IsBuiltIn));
            RaisePropertyChanged(nameof(IsNew));
            RaisePropertyChanged(nameof(CanEditName));
            RaisePropertyChanged(nameof(CanDeleteGroup));
            RaisePropertyChanged(nameof(DeleteReason));
            RaisePropertyChanged(nameof(NeedsStopWarning));
            RaisePropertyChanged(nameof(PermissionCountText));

            SaveCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
        }

        private void OnPermissionChanged()
        {
            RaisePropertyChanged(nameof(NeedsStopWarning));
            RaisePropertyChanged(nameof(PermissionCountText));
            SaveCommand.RaiseCanExecuteChanged();
        }

        private bool CanSave()
            => !IsBuiltIn && !string.IsNullOrWhiteSpace(EditName);

        private void Save()
        {
            if (IsBuiltIn) return;

            if (NeedsStopWarning)
            {
                var answer = MessageBox.Show(
                    StopWarningText + "\n\n仍然保存？",
                    "取消停止权限",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.OK) return;
            }

            var permissions = Permissions.Where(p => p.IsChecked).Select(p => p.Key).ToArray();
            var ok = IsNew
                ? _store.TryAddGroup(EditName, permissions, out var error)
                : _store.TryUpdateGroup(_selected!.Id, EditName, permissions, out error);
            if (!ok)
            {
                Message = error;
                return;
            }

            Message = "已保存";
            Refresh();
        }

        private bool CanDelete()
            => _selected != null && !IsBuiltIn && _selected.UserCount == 0;

        private void Delete()
        {
            if (_selected == null || IsBuiltIn || _selected.UserCount > 0) return;

            if (!_store.RevokeGroup(_selected.Id, out var error))
            {
                Message = error;
                return;
            }

            Message = "已撤销";
            Refresh();
        }

        private void OpenCatalog()
        {
            _dialogs?.ShowDialog("ScadaPermissionCatalogView");
            Refresh();
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) => Refresh();
    }

    public sealed class GroupRow
    {
        public GroupRow(ScadaGroupInfo info, int userCount)
        {
            Id = info.Id;
            Name = info.Name;
            IsBuiltIn = info.IsBuiltIn;
            Permissions = info.Permissions.ToArray();
            UserCount = userCount;
        }

        public string Id { get; }
        public string Name { get; }
        public bool IsBuiltIn { get; }
        public IReadOnlyList<string> Permissions { get; }
        public int UserCount { get; }

        public string PermissionCountText => $"{Permissions.Count}";
        public string KindText => IsBuiltIn ? "模板" : "自定义";
    }

    public sealed class PermissionRow : BindableBase
    {
        private bool _checked;
        private readonly Action? _changed;

        public PermissionRow(ScadaPermissionInfo info, bool isChecked, bool readOnly, Action? changed = null)
        {
            Key = info.Key;
            Name = info.Name;
            IsBuiltIn = info.IsBuiltIn;
            IsReadOnly = readOnly;
            _changed = changed;
            _checked = isChecked;
        }

        public string Key { get; }
        public string Name { get; }
        public bool IsBuiltIn { get; }
        public bool IsReadOnly { get; }
        public bool CanEdit => !IsReadOnly;

        public bool IsChecked
        {
            get => _checked;
            set
            {
                if (SetProperty(ref _checked, value)) _changed?.Invoke();
            }
        }
    }
}
