using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Core.Interfaces;
using Prism.Dialogs;
using VisionMaster.Models;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class ConditionEditorViewModel : BindableBase, IDialogAware
    {
        private ConditionStep _targetNode;
        private readonly IDialogService dialogService;

        // 左侧：分支列表 (If, ElseIf, Else)
        public ObservableCollection<StepCollection> Branches
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public StepCollection SelectedBranch
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                BranchChanged?.Invoke(this, value);
            }
        }

        public ObservableCollection<VariableItem> Variables { get; } =
            new ObservableCollection<VariableItem>();

        // 命令
        public DelegateCommand AddVariableCommand { get; }
        public DelegateCommand<VariableItem> RemoveVariableCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand<VariableItem> BindVariableCommand { get; }
        public string Title => "条件逻辑配置中心";

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        // 给 View 用的事件，解决 AvalonEdit 无法直接双向绑定的痛点
        public event EventHandler<StepCollection> BranchChanged;

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters.TryGetValue<ConditionStep>("Node", out var node))
            {
                _targetNode = node;

                // 此时赋值会触发 SetProperty，通知 UI 渲染左侧列表
                Branches = node.Children;

                // 恢复变量表
                Variables.Clear();
                foreach (var varName in node.LocalVariableNames)
                {
                    var item = new VariableItem { AliasName = varName };
                    if (node.LinkedSources.TryGetValue(varName, out var link))
                    {
                        item.SourceAddress = link.DisplayAddress;
                        item.OriginalLink = link;
                    }
                    Variables.Add(item);
                }

                // 恢复选中的分支
                var initialBranch = parameters.GetValue<StepCollection>("Branch");
                SelectedBranch = initialBranch ?? Branches.FirstOrDefault();
            }
        }

        public ConditionEditorViewModel(IDialogService dialogService)
        {
            AddVariableCommand = new(OnAddVariable);

            RemoveVariableCommand = new DelegateCommand<VariableItem>(item =>
            {
                if (item != null)
                    Variables.Remove(item);
            });

            SaveCommand = new DelegateCommand(OnSave);

            CancelCommand = new DelegateCommand(() =>
                RequestClose.Invoke(new DialogResult(ButtonResult.Cancel))
            );
            BindVariableCommand = new DelegateCommand<VariableItem>(OnBindVariable);
            this.dialogService = dialogService;
        }

        private void OnBindVariable(VariableItem item)
        {
            if (item == null)
                return;
            //使用插件中唯一的一个输入变量作为公共变量
            // _targetNode.LinkedSources
            _targetNode.LinkedSources.Clear();
            dialogService.ShowDialog(
                "DataBindView",
                (s) =>
                {
                    if (s.Result != ButtonResult.OK) return;
                    var data = _targetNode.LinkedSources.ElementAt(0).Value;
                    item.SourceAddress = data.DisplayAddress;
                    item.OriginalLink = data;
                }
            );
        }
        private void OnAddVariable()
        {
            Variables.Add(new VariableItem { AliasName = $"Var_{Variables.Count + 1}" });
            _targetNode.LocalVariableNames.Add($"Var_{Variables.Count}");
        }

        private void OnRemoveVariable(VariableItem item)
        {
            if (item != null)
                Variables.Remove(item);
            _targetNode.LocalVariableNames.Add($"Var_{item.AliasName}");
        }

        private void OnSave()
        {
            if (_targetNode == null)
                return;
            _targetNode.LocalVariableNames.Clear();
            _targetNode.LinkedSources.Clear();

            foreach (var v in Variables)
            {
                if (string.IsNullOrWhiteSpace(v.AliasName))
                    continue;
                _targetNode.LocalVariableNames.Add(v.AliasName);
                if (v.OriginalLink != null)
                    _targetNode.LinkedSources[v.AliasName] = v.OriginalLink;
            }

            // Prism 标准关闭并返回成功
            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }
    }

    public class VariableItem : BindableBase
    {
        public string AliasName
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public string SourceAddress
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public LinkReference OriginalLink { get; set; }
    }
}
