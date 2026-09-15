using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Interfaces;
using DynamicExpresso;
using Prism.Dialogs;
using UI.CustomControl;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class ConditionEditorViewModel : BindableBase, IDialogAware
    {
        private ConditionStep _targetNode;
        private ForStep _targetForNode;
        private readonly IDialogService dialogService;
        private readonly IWorkspaceManager _workspace;

        public ObservableCollection<StepCollection> Branches { get => field; set => SetProperty(ref field, value); }
        public StepCollection SelectedBranch
        {
            get => field;
            set
            {
                SetProperty(ref field, value);
                // P1-⑤：当前选中分支是否需要写条件——驱动表达式编辑区/Else 灰条的显隐，
                // UI 与 FlowCompiler"Else/Default 强制 true"的规则共用同一判定源，不再各说各话
                RaisePropertyChanged(nameof(SelectedBranchNeedsExpression));
                BranchChanged?.Invoke(this, value);
            }
        }

        public bool SelectedBranchNeedsExpression =>
            !IsForMode && SelectedBranch != null && SelectedBranch.RequiresExpression;

        /// <summary>
        /// P1-⑥：For 节点并入本弹窗——为 true 时隐藏"变量+表达式"区，显示"循环次数"卡片
        /// </summary>
        public bool IsForMode { get => field; set => SetProperty(ref field, value); }

        /// <summary>
        /// 循环次数草稿（字符串承载：输入中途非数字也不会被绑定引擎吞掉，保存时统一 TryParse）
        /// </summary>
        public string LoopCountText { get => field; set => SetProperty(ref field, value); } = "10";

        public ObservableCollection<VariableItem> Variables { get; } = new ObservableCollection<VariableItem>();

        public DelegateCommand AddVariableCommand { get; }
        public DelegateCommand<VariableItem> RemoveVariableCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand<VariableItem> BindVariableCommand { get; }

        public string Title => IsForMode ? "For 循环配置" : "条件逻辑配置中心";
        public DialogCloseListener RequestClose { get; set; }
        public event EventHandler<StepCollection> BranchChanged;

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (!parameters.TryGetValue<StepModel>("Node", out var nodeModel) || nodeModel == null)
                return;

            // P1-⑥：For 节点并入——同一弹窗、同一套"草稿→校验→写回→Version++"事务管线，只是编辑内容换成循环次数
            if (nodeModel is ForStep forStep)
            {
                _targetForNode = forStep;
                IsForMode = true;
                LoopCountText = forStep.DefaultLoopCount.ToString();
                RaisePropertyChanged(nameof(Title));
                return;
            }

            if (nodeModel is not ConditionStep node)
                return;

            IsForMode = false;
            _targetNode = node;

            // P0-②（事务草稿）：Branches 不再是活模型 node.Children 的引用，
            // 而是分支"表头"（分支名/类型/表达式）的深拷贝草稿；分支下的步骤列表不属于本弹窗职责，不拷贝。
            // 弹窗期间所有编辑（表达式打字、变量增删）只落在草稿上，点"取消"直接丢弃，天然不污染方案。
            var drafts = new ObservableCollection<StepCollection>();
            foreach (var live in node.Children)
            {
                drafts.Add(new StepCollection
                {
                    BranchType = live.BranchType,
                    StepName = live.StepName,
                    Expression = live.Expression,
                });
            }
            Branches = drafts;
            Variables.Clear();

            foreach (var localVar in node.LocalVariables)
            {
                var item = new VariableItem { Id = localVar.Id, AliasName = localVar.Name , DataTypeName = localVar.DataTypeName };
                if (node.LinkedSources.TryGetValue(item.Id.ToString(), out var link))
                {
                    item.SourceAddress = link.DisplayAddress;
                    item.OriginalLink = link;
                }
                AttachDupCheck(item);
                Variables.Add(item);
            }

            // 调用方若指定了初始分支（活模型对象），需换算成同索引的草稿，防止选中项绕过草稿直写活模型
            StepCollection initialBranch = null;
            if (parameters.TryGetValue<StepCollection>("Branch", out var liveBranch) && liveBranch != null)
            {
                int idx = node.Children.IndexOf(liveBranch);
                if (idx >= 0 && idx < drafts.Count) initialBranch = drafts[idx];
            }
            SelectedBranch = initialBranch ?? drafts.FirstOrDefault();
            RaisePropertyChanged(nameof(Title));
        }

        public ConditionEditorViewModel(IDialogService dialogService, IWorkspaceManager workspace)
        {
            this.dialogService = dialogService;
            _workspace = workspace;
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
            // P0-④：自动别名跳过已占用的名字，从源头避免 Var_N 撞名
            int n = Variables.Count + 1;
            while (Variables.Any(v => v.AliasName == $"Var_{n}")) n++;
            var item = new VariableItem { AliasName = $"Var_{n}" };
            AttachDupCheck(item);
            Variables.Add(item);
        }

        /// <summary>
        /// 附1：给变量行注入"查重"回调——行内实时校验需要跨行视野（单行自己看不见别人），
        /// 由 VM 提供集合级判定，IDataErrorInfo 在别名变化时立即重查
        /// </summary>
        private void AttachDupCheck(VariableItem item)
        {
            item.DuplicateChecker = name =>
                Variables.Count(v => !ReferenceEquals(v, item)
                    && string.Equals((v.AliasName ?? "").Trim(), name, StringComparison.Ordinal)) > 0;
        }

        private void OnRemoveVariable(VariableItem item)
        {
            if (item != null) Variables.Remove(item);
        }

        private void OnSave()
        {
            if (IsForMode)
            {
                SaveForLoop();
                return;
            }
            if (_targetNode == null) return;

            // ==============================================
            // P0-④：保存前校验（快速失败）——语法、别名、空条件全部在弹窗内说清楚，
            // 而不是让脏数据流进 FlowCompiler，等到运行时才以"编译失败"的面目爆出来
            // ==============================================
            var errors = new List<string>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var delegateParams = new List<Parameter>();

            for (int i = 0; i < Variables.Count; i++)
            {
                var v = Variables[i];
                string alias = v.AliasName?.Trim();

                // 整行全空（没别名也没绑定）：视为误点的空行，静默丢弃
                bool isBlankRow = string.IsNullOrEmpty(alias)
                    && v.OriginalLink == null
                    && string.IsNullOrEmpty(v.SourceAddress);
                if (isBlankRow) continue;

                if (string.IsNullOrEmpty(alias))
                {
                    errors.Add($"第 {i + 1} 行：变量已绑定数据源但别名为空，请填写别名或删除该行");
                    continue;
                }
                if (!VariableItem.IdentifierRegex.IsMatch(alias))
                {
                    errors.Add($"第 {i + 1} 行：别名 '{alias}' 不是合法标识符（需字母/下划线开头，仅含字母、数字、下划线）");
                    continue;
                }
                if (!usedNames.Add(alias))
                {
                    errors.Add($"第 {i + 1} 行：别名 '{alias}' 重复，表达式将无法区分引用哪个");
                    continue;
                }

                Type varType = TypeHelper.GetActualTypeFromLink(v.DataTypeName);
                if (!TypeHelper.IsSafeExpressionType(varType))
                {
                    errors.Add($"变量 '{alias}' 的类型 [{v.DataTypeName}] 不允许参与条件运算（仅支持数值/布尔/字符串）");
                    continue;
                }
                delegateParams.Add(new Parameter(alias, varType));
            }

            // 运行时变量引用（本弹窗不编辑它们，但表达式合法性检查必须带上，否则会误报"未知标识符"）
            foreach (var runtimeVar in _targetNode.RuntimeVariableRefs)
            {
                if (string.IsNullOrWhiteSpace(runtimeVar?.Name)) continue;
                Type rt = TypeHelper.GetActualTypeFromLink(runtimeVar.DataTypeName);
                if (usedNames.Add(runtimeVar.Name))
                    delegateParams.Add(new Parameter(runtimeVar.Name, rt));
            }

            // 条件必填判定统一走 StepCollection.RequiresExpression（与流程栏红灯、弹窗灰条同一标准）：
            // If/ElseIf/Case/WhileLoop 必须有表达式；Else/Default(For 循环体) 由编译器强制视为 true
            for (int i = 0; i < Branches.Count; i++)
            {
                var br = Branches[i];

                if (string.IsNullOrWhiteSpace(br.Expression))
                {
                    if (br.RequiresExpression)
                        errors.Add($"分支 '{br.StepName}' 的条件表达式为空（空条件在编译时是硬错误，流程将无法运行）");
                    continue;
                }

                // 与 FlowCompiler 完全同款的解析：DynamicExpresso + Math 引用 + bool 返回类型
                try
                {
                    new Interpreter().Reference(typeof(Math))
                        .Parse(br.Expression, typeof(bool), delegateParams.ToArray());
                }
                catch (Exception ex)
                {
                    errors.Add($"分支 '{br.StepName}' 表达式语法错误：{ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                EasyDialog.ShowSync("校验未通过", string.Join("\n", errors));
                return; // 停留在弹窗，草稿保留，用户改好再存
            }

            // ==============================================
            // 校验全部通过：草稿 → 活模型，一次性写回
            // ==============================================
            for (int i = 0; i < Branches.Count && i < _targetNode.Children.Count; i++)
            {
                // P1-⑤：Else/For 循环体（编译器不在乎条件的分支）写回时强制清空表达式，
                // 抹掉历史遗留的"看起来生效其实被丢弃"的脏数据，重开弹窗不再被旧文字误导
                _targetNode.Children[i].Expression =
                    Branches[i].RequiresExpression ? Branches[i].Expression : string.Empty;
            }

            _targetNode.LocalVariables.Clear();
            _targetNode.LinkedSources.Clear();

            foreach (var v in Variables)
            {
                string alias = v.AliasName?.Trim();
                if (string.IsNullOrEmpty(alias)) continue;

                _targetNode.LocalVariables.Add(new LocalVariableItem
                {
                    Id = v.Id,
                    Name = alias,
                    DataTypeName = v.DataTypeName
                });

                if (v.OriginalLink != null)
                {
                    _targetNode.LinkedSources[v.Id.ToString()] = v.OriginalLink;
                }
            }

            // P0-①：FlowModel.Version 只订阅了顶层 Steps 的 PropertyChanged，
            // 条件表达式/变量都在 Children、LocalVariables 里，引擎永远感知不到变更 →
            // 保存成功后手动推进版本号，触发"需要重新编译"提示，杜绝"试运行对、正式跑错"
            if (_workspace?.CurrentFlow != null)
                _workspace.CurrentFlow.Version++;

            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }

        /// <summary>
        /// P1-⑥：For 模式保存——校验循环次数并写回活模型，
        /// 复用条件编辑同一套"草稿→校验→写回→Version++"事务管线
        /// </summary>
        private void SaveForLoop()
        {
            if (_targetForNode == null) return;

            if (!int.TryParse((LoopCountText ?? "").Trim(), out int count) || count < 1 || count > 99999)
            {
                EasyDialog.ShowSync("校验未通过", "循环次数必须是 1 ~ 99999 之间的整数！\n（引擎对 For 无迭代上限保护，防止手滑天文数字拖死流程）");
                return;
            }

            _targetForNode.DefaultLoopCount = count;

            if (_workspace?.CurrentFlow != null)
                _workspace.CurrentFlow.Version++;

            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }
    }

    public class VariableItem : BindableBase, IDataErrorInfo
    {
        /// <summary>
        /// C# 标识符规则：字母/下划线开头，后跟字母/数字/下划线。
        /// 行内实时校验与保存兜底校验共用这一个判定源（单一事实出处），
        /// 别名会被编译成 DynamicExpresso 的参数名，不合法的名字在编译器里爆炸不如在这里拦截。
        /// </summary>
        public static readonly Regex IdentifierRegex =
            new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        /// <summary>
        /// VM 注入的查重回调：单行看不见集合里的其他行，跨行视野由外部提供
        /// </summary>
        public Func<string, bool> DuplicateChecker { get; set; }

        public Guid Id { get; set; } = Guid.NewGuid();

        public string AliasName
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged("Error"); // 通知 WPF 重跑 IDataErrorInfo（附1：打字即校验）
            }
        }

        public string SourceAddress { get => field; set => SetProperty(ref field, value); }
        public LinkReference OriginalLink { get; set; }

        public string DataTypeName { get; set; } = "System.Double";

        private bool HasBinding => OriginalLink != null || !string.IsNullOrEmpty(SourceAddress);

        // ---- IDataErrorInfo：行内红框 + 悬浮提示 ----
        public string Error => null;

        public string this[string columnName]
        {
            get
            {
                if (columnName != nameof(AliasName)) return null;

                string alias = AliasName?.Trim();
                if (string.IsNullOrEmpty(alias))
                    return HasBinding ? "已绑定数据源，请填写别名（或删除该行）" : null; // 全空行合法，保存时静默丢弃
                if (!IdentifierRegex.IsMatch(alias))
                    return "需字母/下划线开头，仅含字母、数字、下划线";
                if (DuplicateChecker?.Invoke(alias) == true)
                    return "别名重复，表达式将无法区分引用哪个";
                return null;
            }
        }
    }
}