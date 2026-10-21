using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 变量显示分组编辑器（变量管理弹窗内的覆盖层，不是独立 Prism 弹窗）。
    ///
    /// 三条口径
    /// ---------
    /// ① **只做显示分类**：不碰采集 / 轮询 / 写入；与扫描组（连接级调度资源）是两条正交的轴。
    /// ② **方案级分组表**：存在 <c>SolutionModel.VariableGroups</c>，随 .vms 落盘；
    ///    变量按 <see cref="VariableGroupDescriptor.Id"/> 引用，改名不需要级联。
    /// ③ **即时生效**：与变量管理弹窗的其它动作（写值 / 删变量）同一事务模型，没有"确定/取消"；
    ///    删除有二次确认，变量回落到「未分组」，变量本身不删。
    ///
    /// 名称唯一性（用户明确要求）
    /// ---------
    /// trim 后不区分大小写比较；「未分组」是内置保留名；新增与改名两处共用
    /// <see cref="ValidateGroupName"/> 一份规则，避免"新增拦住了、改名绕过去"。
    /// </summary>
    public sealed class VariableGroupEditorViewModel : BindableBase
    {
        /// <summary>内置分组的显示名（保留名，不允许新建/改名成它）。</summary>
        public const string UngroupedName = "未分组";

        /// <summary>分组名最大长度（与设计稿一致）。</summary>
        public const int MaxNameLength = 20;

        private readonly IWorkspaceManager _workspace;
        private bool _isOpen;
        private string _newGroupName = string.Empty;
        private string? _newGroupError;
        private bool _canAddGroup;

        public VariableGroupEditorViewModel(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            AddGroupCommand = new DelegateCommand(AddGroup, () => CanAddGroup);
            BeginRenameCommand = new DelegateCommand<VariableGroupRow>(BeginRename);
            CommitRenameCommand = new DelegateCommand<VariableGroupRow>(CommitRename);
            CancelRenameCommand = new DelegateCommand<VariableGroupRow>(CancelRename);
            BeginDeleteCommand = new DelegateCommand<VariableGroupRow>(BeginDelete);
            ConfirmDeleteCommand = new DelegateCommand<VariableGroupRow>(ConfirmDelete);
            CancelDeleteCommand = new DelegateCommand<VariableGroupRow>(CancelDelete);
        }

        /// <summary>分组表发生实际变更（新增 / 改名 / 删除）后触发，由变量管理 VM 刷新左栏与明细。</summary>
        public event Action? Changed;

        public ObservableCollection<VariableGroupRow> Rows { get; } = new();

        public bool IsOpen
        {
            get => _isOpen;
            private set => SetProperty(ref _isOpen, value);
        }

        public string NewGroupName
        {
            get => _newGroupName;
            set
            {
                if (!SetProperty(ref _newGroupName, value)) return;
                RefreshNewGroupState();
            }
        }

        public string? NewGroupError
        {
            get => _newGroupError;
            private set => SetProperty(ref _newGroupError, value);
        }

        public bool CanAddGroup
        {
            get => _canAddGroup;
            private set
            {
                if (SetProperty(ref _canAddGroup, value))
                    AddGroupCommand.RaiseCanExecuteChanged();
            }
        }

        public DelegateCommand AddGroupCommand { get; }
        public DelegateCommand<VariableGroupRow> BeginRenameCommand { get; }
        public DelegateCommand<VariableGroupRow> CommitRenameCommand { get; }
        public DelegateCommand<VariableGroupRow> CancelRenameCommand { get; }
        public DelegateCommand<VariableGroupRow> BeginDeleteCommand { get; }
        public DelegateCommand<VariableGroupRow> ConfirmDeleteCommand { get; }
        public DelegateCommand<VariableGroupRow> CancelDeleteCommand { get; }

        #region 打开 / 关闭 / 重建

        /// <summary>打开覆盖层：每次从方案重新读分组表（方案可能在弹窗存活期间被别处改过）。</summary>
        public void Open()
        {
            NewGroupName = string.Empty;
            NewGroupError = null;
            Reload();
            IsOpen = true;
        }

        /// <summary>关闭覆盖层：行内草稿直接丢弃（没有已提交的改动需要回滚）。</summary>
        public void Close()
        {
            IsOpen = false;
            foreach (var row in Rows)
            {
                row.IsEditing = false;
                row.EditError = null;
                row.IsConfirmingDelete = false;
            }
        }

        /// <summary>
        /// 按方案分组表重建行（末尾追加内置「未分组」）。
        /// 变量数按**全部连接**统计（分组表是方案级资源，不按设备拆）。
        /// </summary>
        public void Reload()
        {
            Rows.Clear();

            var groups = Groups;
            var validIds = new HashSet<Guid>(groups.Select(g => g.Id));
            var counts = new Dictionary<Guid, int>();
            foreach (var variable in NetworkVariables)
            {
                // 悬空 Id（分组被外部删掉 / 方案来自别处）按「未分组」统计，与界面显示一致
                var id = validIds.Contains(variable.GroupId) ? variable.GroupId : Guid.Empty;
                counts[id] = counts.TryGetValue(id, out var count) ? count + 1 : 1;
            }

            foreach (var group in groups)
            {
                var row = new VariableGroupRow(group.Id, group.Name, counts.TryGetValue(group.Id, out var count) ? count : 0);
                row.EditNameChanged = ValidateRenameRow;
                Rows.Add(row);
            }

            Rows.Add(new VariableGroupRow(Guid.Empty, UngroupedName, counts.TryGetValue(Guid.Empty, out var ungrouped) ? ungrouped : 0)
            {
                IsBuiltIn = true
            });

            RefreshNewGroupState();
        }

        // 与 GlobalVariableManagerViewModel 同口径：方案未挂上时按空表处理，避免打开弹窗就 NRE。
        private List<VariableGroupDescriptor> Groups
            => _workspace.CurrentSolution?.VariableGroups ?? new List<VariableGroupDescriptor>();

        private bool HasSolution => _workspace.CurrentSolution != null;

        private IEnumerable<NetworkVariableModel> NetworkVariables
            => _workspace.GlobalVariables.OfType<NetworkVariableModel>();

        #endregion

        #region 新增

        private void AddGroup()
        {
            if (!HasSolution)
            {
                NewGroupError = "当前没有打开的方案，无法保存分组";
                return;
            }

            var error = ValidateGroupName(NewGroupName, Groups, exceptId: null);
            if (error != null)
            {
                NewGroupError = error;
                return;
            }

            Groups.Add(new VariableGroupDescriptor
            {
                Id = Guid.NewGuid(),
                Name = NewGroupName.Trim()
            });

            NewGroupName = string.Empty;
            NewGroupError = null;
            Reload();
            Changed?.Invoke();
        }

        private void RefreshNewGroupState()
        {
            // 没有方案就没有落点：明确给出原因并禁用「添加」，
            // 不能让用户输入完名字、点一下却什么都不发生（临时 List 被加完即丢）。
            if (!HasSolution)
            {
                NewGroupError = "当前没有打开的方案，无法保存分组";
                CanAddGroup = false;
                return;
            }

            var raw = NewGroupName;
            if (string.IsNullOrWhiteSpace(raw))
            {
                NewGroupError = null;
                CanAddGroup = false;
                return;
            }

            NewGroupError = ValidateGroupName(raw, Groups, exceptId: null);
            CanAddGroup = NewGroupError == null;
        }

        #endregion

        #region 改名

        private void BeginRename(VariableGroupRow? row)
        {
            if (row == null || row.IsBuiltIn) return;

            foreach (var other in Rows)
            {
                if (!ReferenceEquals(other, row))
                {
                    other.IsEditing = false;
                    other.EditError = null;
                }
                other.IsConfirmingDelete = false;
            }

            row.EditName = row.Name;
            row.IsEditing = true;
            ValidateRenameRow(row);
        }

        private void ValidateRenameRow(VariableGroupRow row)
        {
            var error = ValidateGroupName(row.EditName, Groups, row.Id);
            row.EditError = error;
            row.CanCommitRename = error == null;
        }

        private void CommitRename(VariableGroupRow? row)
        {
            if (row == null || row.IsBuiltIn || !row.IsEditing) return;

            var error = ValidateGroupName(row.EditName, Groups, row.Id);
            if (error != null)
            {
                row.EditError = error;
                row.CanCommitRename = false;
                return;
            }

            if (!RenameGroupCore(Groups, row.Id, row.EditName))
            {
                // 组在方案里已不存在（极端并发/外部改写）：留在编辑态并说明原因，
                // 让用户能点取消退出，而不是"点确认没反应"。
                row.EditError = "分组已不存在，请取消后重新打开";
                row.CanCommitRename = false;
                return;
            }

            row.IsEditing = false;
            row.EditError = null;
            Reload();
            Changed?.Invoke();
        }

        private void CancelRename(VariableGroupRow? row)
        {
            if (row == null) return;
            row.IsEditing = false;
            row.EditError = null;
        }

        #endregion

        #region 删除

        private void BeginDelete(VariableGroupRow? row)
        {
            if (row == null || row.IsBuiltIn) return;

            foreach (var other in Rows)
            {
                other.IsConfirmingDelete = ReferenceEquals(other, row);
                other.IsEditing = false;
                other.EditError = null;
            }
        }

        private void ConfirmDelete(VariableGroupRow? row)
        {
            if (row == null || row.IsBuiltIn) return;

            DeleteGroupCore(Groups, NetworkVariables, row.Id);
            Reload();
            Changed?.Invoke();
        }

        private void CancelDelete(VariableGroupRow? row)
        {
            if (row == null) return;
            row.IsConfirmingDelete = false;
        }

        #endregion

        #region 纯函数（可被断言直接驱动）

        /// <summary>
        /// 分组名校验（新增与改名共用）。
        /// 返回可直接上屏的中文原因；合法返回 null。
        /// </summary>
        public static string? ValidateGroupName(
            string? raw, IEnumerable<VariableGroupDescriptor> groups, Guid? exceptId)
        {
            var name = (raw ?? string.Empty).Trim();
            if (name.Length == 0) return "请输入分组名称";
            if (name.Length > MaxNameLength) return $"分组名称最多 {MaxNameLength} 个字符";
            if (string.Equals(name, UngroupedName, StringComparison.Ordinal))
                return $"「{UngroupedName}」是系统内置分组，不能作为分组名";

            foreach (var group in groups)
            {
                if (exceptId.HasValue && group.Id == exceptId.Value) continue;
                if (string.Equals(group.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return $"已存在同名分组「{group.Name}」";
            }

            return null;
        }

        /// <summary>
        /// 删除分组：先把引用它的变量回落到「未分组」，再从组表移除。返回受影响的变量数。
        /// 内置「未分组」不可删除（返回 0，组表不动）。
        /// </summary>
        public static int DeleteGroupCore(
            List<VariableGroupDescriptor> groups,
            IEnumerable<NetworkVariableModel> variables,
            Guid groupId)
        {
            if (groupId == Guid.Empty) return 0;

            int affected = 0;
            foreach (var variable in variables)
            {
                if (variable.GroupId != groupId) continue;
                variable.GroupId = Guid.Empty;
                affected++;
            }

            groups.RemoveAll(g => g.Id == groupId);
            return affected;
        }

        /// <summary>改名：只改显示名，变量按 Id 引用所以无需级联。找不到分组返回 false。</summary>
        public static bool RenameGroupCore(List<VariableGroupDescriptor> groups, Guid groupId, string newName)
        {
            var group = groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return false;

            group.Name = (newName ?? string.Empty).Trim();
            return true;
        }

        #endregion
    }

    /// <summary>分组编辑器的一行（分组 + 变量数 + 行内编辑/删除确认状态）。</summary>
    public sealed class VariableGroupRow : BindableBase
    {
        private string _name;
        private string _editName;
        private string? _editError;
        private bool _canCommitRename = true;
        private bool _isEditing;
        private bool _isConfirmingDelete;

        public VariableGroupRow(Guid id, string name, int count)
        {
            Id = id;
            _name = name;
            _editName = name;
            Count = count;
        }

        public Guid Id { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Count { get; }

        public bool IsBuiltIn { get; init; }

        public bool IsEditing
        {
            get => _isEditing;
            set => SetProperty(ref _isEditing, value);
        }

        public string EditName
        {
            get => _editName;
            set
            {
                if (!SetProperty(ref _editName, value)) return;
                EditError = null;
                CanCommitRename = true;
                EditNameChanged?.Invoke(this);
            }
        }

        public string? EditError
        {
            get => _editError;
            set => SetProperty(ref _editError, value);
        }

        public bool CanCommitRename
        {
            get => _canCommitRename;
            set => SetProperty(ref _canCommitRename, value);
        }

        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            set => SetProperty(ref _isConfirmingDelete, value);
        }

        /// <summary>删除确认条上的文案（含影响面：多少变量会回落到未分组）。</summary>
        public string DeleteConfirmText
            => $"删除「{Name}」？{Count} 个变量将归入「{VariableGroupEditorViewModel.UngroupedName}」，变量本身不会被删除。";

        /// <summary>行内输入变更回调（由编辑器注入，用于实时校验重名）。</summary>
        public Action<VariableGroupRow>? EditNameChanged { get; set; }
    }
}
