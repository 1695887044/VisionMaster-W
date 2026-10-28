using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「选择流程」弹窗：从当前方案的流程清单里挑一条，确认后回传 (流程 Id, 流程名)。
    ///
    /// 三条口径（与运行态寻址保持一致，避免"选得到却跑不过去"这类错位）
    /// ---------
    /// ① **清单就是可寻址的那一份**：只列当前方案的 <c>CurrentSolution.Flows</c>。
    ///    运行态 <c>ScadaFlowRunner</c> 背后的寻址正是同一份集合（按 FlowID 优先、名字兜底），
    ///    所以这里列出来的每一条，运行时都跑得过去；不在清单里的（别方案/已删除）选了也是白选。
    /// ② **Id 是权威**：确认时回传的是 <c>FlowID</c>（外加当时的名字，仅作旧数据兜底与展示）。
    ///    流程改名不会让已配好的动作断链——名字只是"快照"，Id 才是锚点。
    /// ③ **预选走 Id 优先、名字兜底**：与运行态寻址同一顺序，
    ///    让"面板上看到的是哪条"和"运行时会跑到哪条"永远对得上。
    ///
    /// 停用的流程照样列出来、照样可选：配置期不拦（拦了用户就不知道"为什么列表里没有它"），
    /// 名字边上标一枚「已停用」徽标；选了它运行侧会照实拒绝并说原因——
    /// 与画面清单"列全部、运行侧把关"同一取舍。
    ///
    /// 取消语义：本弹窗不产生任何回写（<see cref="ScadaFlowPicker.Pick"/> 只在 OK 时回调），
    /// 所以"取消"不会把已配的流程清掉——那是破坏性动作，不能藏在取消键里。
    /// </summary>
    public class ScadaFlowPickerViewModel : BindableBase, IDialogAware
    {
        private readonly IWorkspaceManager _workspace;

        /// <summary>全量清单（打开那一刻的快照）</summary>
        private readonly List<ScadaFlowRow> _all = new();

        private ScadaFlowRow? _selectedFlow;

        public ScadaFlowPickerViewModel(IWorkspaceManager workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            ConfirmCommand = new DelegateCommand(OnConfirm, () => SelectedFlow != null)
                .ObservesProperty(() => SelectedFlow);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "选择流程";

        /// <summary>清单（打开那一刻的快照；本弹窗条目少，不做关键字过滤）</summary>
        public ObservableCollection<ScadaFlowRow> Flows { get; } = new();

        /// <summary>当前选中项（未选时确认键不可用）</summary>
        public ScadaFlowRow? SelectedFlow
        {
            get => _selectedFlow;
            set => SetProperty(ref _selectedFlow, value);
        }

        /// <summary>列表为空时的提示文案（有内容时为空串）</summary>
        public string EmptyHint =>
            _all.Count == 0
                ? "当前方案里还没有流程。请先新建流程，再回来配置这条动作。"
                : string.Empty;

        /// <summary>列表是否有内容（空态提示靠它显隐）</summary>
        public bool HasFlows => Flows.Count > 0;

        public DelegateCommand ConfirmCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>确认：回传 (Id, 名字)。Id 拿不到就什么也不做（不许退化成"按名字猜一个"）</summary>
        private void OnConfirm()
        {
            var row = SelectedFlow;
            if (row == null || string.IsNullOrWhiteSpace(row.FlowId))
                return;

            var parameters = new DialogParameters
            {
                { Services.ScadaFlowPicker.PickedIdKey, row.FlowId },
                { Services.ScadaFlowPicker.PickedNameKey, row.Name },
            };
            RequestClose.Invoke(parameters, ButtonResult.OK);
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时：现取一份流程清单快照，并按入参预选。
        /// 每次打开都重新取，是因为两次打开之间用户可能已经增删过流程；
        /// 缓存在字段里跨弹窗复用，就会给出"清单里没有它，却怎么也选不上"的幽灵条目。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            var flows = _workspace.CurrentSolution?.Flows;

            _all.Clear();
            if (flows != null)
            {
                foreach (var flow in flows)
                {
                    if (flow != null)
                        _all.Add(new ScadaFlowRow(flow));
                }
            }

            Flows.Clear();
            foreach (var row in _all)
                Flows.Add(row);

            RaisePropertyChanged(nameof(EmptyHint));
            RaisePropertyChanged(nameof(HasFlows));

            parameters.TryGetValue<string>(Services.ScadaFlowPicker.CurrentIdKey, out var currentId);
            parameters.TryGetValue<string>(Services.ScadaFlowPicker.CurrentNameKey, out var currentName);

            // Id 优先预选（Ordinal——流程 Id 本来就是区分大小写的身份文本），名字兜底（忽略大小写）
            SelectedFlow =
                !string.IsNullOrWhiteSpace(currentId)
                    ? _all.FirstOrDefault(r => string.Equals(r.FlowId, currentId, StringComparison.Ordinal))
                    : null;
            SelectedFlow ??= string.IsNullOrWhiteSpace(currentName)
                ? null
                : _all.FirstOrDefault(r =>
                    string.Equals(r.Name, currentName, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }

    /// <summary>
    /// 清单里的一行：把 <see cref="FlowModel"/> 摊成"给人看的三列"（流程名 / 角色 / 启停态）。
    ///
    /// 为什么不直接把 <c>FlowModel</c> 塞进列表：流程模型是领域对象，它的 Steps、触发方式、
    /// 布局存储直接上屏对配置者毫无意义；摊平这一步刻意留在视图模型里，
    /// 领域模型就不必为"怎么显示"负责（与 <c>ScadaPageRow</c> 同一取舍）。
    /// </summary>
    public sealed class ScadaFlowRow
    {
        public ScadaFlowRow(FlowModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }

        /// <summary>底层流程模型（确认时取 Id 与名字的唯一出处）</summary>
        public FlowModel Model { get; }

        public string FlowId => Model.FlowID ?? string.Empty;

        public string Name => Model.FlowName ?? string.Empty;

        public string Description => Model.Description ?? string.Empty;

        /// <summary>角色名（Home / End 两条骨架 + 普通流程），与流程管理列表同一套叫法</summary>
        public string RoleText => Model.Role switch
        {
            FlowRole.Home => "回原",
            FlowRole.End => "收尾",
            _ => Model.IsLineStartMember ? "整线" : "普通",
        };

        /// <summary>停用徽标文案（启用中则为空串，模板据空串收掉徽标）</summary>
        public string StateBadge => Model.IsEnabled ? string.Empty : "已停用";

        /// <summary>关键字匹配：流程名 / 说明，任一命中即算（大小写不敏感）</summary>
        public bool Matches(string keyword) =>
            Contains(Name, keyword)
            || Contains(Description, keyword);

        private static bool Contains(string? text, string keyword) =>
            !string.IsNullOrEmpty(text)
            && text!.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
