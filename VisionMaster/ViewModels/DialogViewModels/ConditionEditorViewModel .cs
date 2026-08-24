using System;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Dialogs;
using VisionMaster.Models;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class ConditionEditorViewModel : BindableBase, IDialogAware
    {
        private ConditionStep _targetNode;
        private readonly IDialogService dialogService;

        public ObservableCollection<StepCollection> Branches { get => field; set => SetProperty(ref field, value); }
        public StepCollection SelectedBranch
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                BranchChanged?.Invoke(this, value);
            }
        }

        public ObservableCollection<VariableItem> Variables { get; } = new ObservableCollection<VariableItem>();

        public DelegateCommand AddVariableCommand { get; }
        public DelegateCommand<VariableItem> RemoveVariableCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand<VariableItem> BindVariableCommand { get; }

        public string Title => "条件逻辑配置中心";
        public DialogCloseListener RequestClose { get; set; }
        public event EventHandler<StepCollection> BranchChanged;

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.TryGetValue<ConditionStep>("Node", out var node))
            {
                _targetNode = node;
                Branches = node.Children;
                Variables.Clear();

                foreach (var localVar in node.LocalVariables)
                {
                    var item = new VariableItem { Id = localVar.Id, AliasName = localVar.Name , DataTypeName = localVar.DataTypeName };
                    if (node.LinkedSources.TryGetValue(item.Id.ToString(), out var link))
                    {
                        item.SourceAddress = link.DisplayAddress;
                        item.OriginalLink = link;
                    }
                    Variables.Add(item);
                }

                var initialBranch = parameters.GetValue<StepCollection>("Branch");
                SelectedBranch = initialBranch ?? Branches.FirstOrDefault();
            }
        }

        public ConditionEditorViewModel(IDialogService dialogService)
        {
            this.dialogService = dialogService;
            AddVariableCommand = new DelegateCommand(OnAddVariable);
            RemoveVariableCommand = new DelegateCommand<VariableItem>(OnRemoveVariable);
            SaveCommand = new DelegateCommand(OnSave);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.Cancel)));
            BindVariableCommand = new DelegateCommand<VariableItem>(OnBindVariable);
        }

        private void OnBindVariable(VariableItem item)
        {
            if (item == null) return;

            // 🌟 开启单绑模式，告诉弹窗：“你不要碰源数据，只要把用户选的变量还给我就行！”
            var p = new DialogParameters { { "IsSingleBindMode", true } };

            dialogService.ShowDialog("DataBindView", p, (s) =>
            {
                if (s.Result != ButtonResult.OK) return;

                // 🌟 安全取值
                if (s.Parameters.TryGetValue<LinkReference>("BoundLink", out var link))
                {
                    item.SourceAddress = link.DisplayAddress;
                    item.OriginalLink = link;
                }
                if (s.Parameters.TryGetValue<string>("DataTypeName", out var typeName))
                {
                    item.DataTypeName = typeName;
                }
            });
        }

        private void OnAddVariable()
        {
            Variables.Add(new VariableItem { AliasName = $"Var_{Variables.Count + 1}" });
        }

        private void OnRemoveVariable(VariableItem item)
        {
            if (item != null) Variables.Remove(item);
        }

        private void OnSave()
        {
            if (_targetNode == null) return;

            _targetNode.LocalVariables.Clear();
            _targetNode.LinkedSources.Clear();

            foreach (var v in Variables)
            {
                if (string.IsNullOrWhiteSpace(v.AliasName)) continue;

                _targetNode.LocalVariables.Add(new LocalVariableItem
                {
                    Id = v.Id,
                    Name = v.AliasName,
                    DataTypeName = v.DataTypeName
                });

                if (v.OriginalLink != null)
                {
                    _targetNode.LinkedSources[v.Id.ToString()] = v.OriginalLink;
                }
            }

            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }
    }

    public class VariableItem : BindableBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string AliasName { get => field; set => SetProperty(ref field, value); }
        public string SourceAddress { get => field; set => SetProperty(ref field, value); }
        public LinkReference OriginalLink { get; set; }

        public string DataTypeName { get; set; } = "System.Double";
    }
}