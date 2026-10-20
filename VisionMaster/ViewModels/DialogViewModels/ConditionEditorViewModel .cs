using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

        /// <summary>等待节点靶（IsWaitMode 时的活模型引用；条件/轮询/超时写回用）</summary>
        private WaitStep? _targetWaitNode;
        /// <summary>分支匹配靶节点（IsCaseMode 时的活模型引用；判据写回用）</summary>
        private CaseStep _targetCaseNode;
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
                // 分支匹配模式：切换分支要同时刷新"匹配值框 / 布尔编辑器 / 标题"三处显隐与文案
                RaisePropertyChanged(nameof(ShowBooleanEditor));
                RaisePropertyChanged(nameof(ShowCaseValueEditor));
                RaisePropertyChanged(nameof(EditorTitle));
                // v2：卡内错误条与底栏摘要要跟着选中分支换（空表达式判定是分支自己的 IsConditionMissing）
                RaisePropertyChanged(nameof(SelectedBranchMissingCondition));
                RaisePropertyChanged(nameof(HasCardError));
                RaisePropertyChanged(nameof(CardErrorText));
                RaisePropertyChanged(nameof(ValidationSummaryText));
                RaisePropertyChanged(nameof(HasValidationErrors));
                RaisePropertyChanged(nameof(ValidationErrorCount));
                RaisePropertyChanged(nameof(ValidationSummaryLevel));
                BranchChanged?.Invoke(this, value);
            }
        }

        public bool SelectedBranchNeedsExpression =>
            !IsForMode && SelectedBranch != null && SelectedBranch.RequiresExpression;

        /// <summary>
        /// P1-⑥：For 节点并入本弹窗——为 true 时隐藏"变量+表达式"区，显示"循环次数"卡片
        /// </summary>
        public bool IsForMode
        {
            get => field;
            set
            {
                if (!SetProperty(ref field, value))
                    return;
                RaisePropertyChanged(nameof(ModeChipText));
                RefreshDraftDiagnostics();
            }
        }

        // ------------------------------------------------------------------
        //  等待节点模式（BuiltIn_Wait）：与 If/While 共用"草稿→校验→写回→Version++"事务管线，
        //  只是除条件表达式之外还要编辑轮询间隔 / 超时 / 超时处置 / 超时说明 / 报警变量。
        //
        //  【为什么复用本弹窗而不是新建视图】条件表达式编辑器（AvalonEdit + 变量表 + 快捷插入）
        //  已经把这套东西做全了；等待节点缺的只是几个数字框。Wait 模式下 Branches 里只有
        //  **一条合成草稿**（BranchType.If、StepName="等待条件"），code-behind 写
        //  SelectedBranch.Expression 的既有路径零改动即可用。
        // ------------------------------------------------------------------

        /// <summary>等待模式（靶节点是 WaitStep）</summary>
        public bool IsWaitMode
        {
            get => field;
            set
            {
                if (!SetProperty(ref field, value))
                    return;
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(ModeChipText));
                RefreshDraftDiagnostics();
            }
        }

        /// <summary>轮询间隔草稿（字符串承载：输入中途非数字也不会被绑定引擎吞掉，保存时统一 TryParse）</summary>
        public string PollIntervalText
        {
            get => field;
            set { if (SetProperty(ref field, value)) RefreshDraftDiagnostics(); }
        } = "50";

        /// <summary>最大超时草稿（毫秒；0 = 不限）</summary>
        public string TimeoutText
        {
            get => field;
            set { if (SetProperty(ref field, value)) RefreshDraftDiagnostics(); }
        } = "60000";

        /// <summary>超时说明草稿（追加到超时日志/异常文案）</summary>
        public string TimeoutMessageDraft
        {
            get => field;
            set => SetProperty(ref field, value);
        } = string.Empty;

        /// <summary>报警全局变量名草稿（可空；超时时写 true）</summary>
        public string AlarmVariableNameDraft
        {
            get => field;
            set { if (SetProperty(ref field, value)) RefreshDraftDiagnostics(); }
        } = string.Empty;

        /// <summary>
        /// 超时处置草稿（**成员名**，不是显示文本）。
        /// 值必须仍是成员名：写进流程的是枚举成员名，改一次中文显示名不该打断已配好的流程（R40 口径①）。
        /// </summary>
        public string TimeoutActionValue
        {
            get => field;
            set => SetProperty(ref field, value);
        } = nameof(WaitTimeoutAction.TerminateFlow);

        /// <summary>
        /// 超时处置候选：显示走 [Display(Name)]、值走成员名（StepConfigOption.ForEnum，R40）。
        /// 绑定口径：DisplayMemberPath="Display" + SelectedValuePath="Value" + SelectedValue="{Binding TimeoutActionValue}"。
        /// </summary>
        public IReadOnlyList<StepConfigOption> TimeoutActionOptions { get; } =
            Enum.GetValues(typeof(WaitTimeoutAction))
                .Cast<WaitTimeoutAction>()
                .Select(v => StepConfigOption.ForEnum(typeof(WaitTimeoutAction), v.ToString()))
                .ToList();

        /// <summary>等待模式的固定提示文案（UI 直接绑这个属性，避免文案散落在 xaml 里）</summary>
        public string WaitHintText { get; } =
            "条件成立后继续；超时=0 表示不限；报警变量必须是已存在的全局 bool 变量";

        /// <summary>
        /// 报警变量候选：**用户可写的全局 bool 变量**名（系统变量是运行态派生的只读量，写不进去）。
        /// 打开等待节点时现取（变量可能在方案里随时新增/改名）；UI 用它做下拉候选，
        /// SaveWait 用它做"名字必须存在且类型正确"的校验——两处同一个来源，避免
        /// "下拉里选不到但手填能过"或反过来。
        /// </summary>
        public ObservableCollection<string> AlarmVariableCandidates { get; } = new();

        // ------------------------------------------------------------------
        //  分支匹配（Case）模式：与 If/While 共用"草稿→校验→写回→Version++"事务管线，
        //  只是分支编辑区从"布尔表达式"换成"匹配值"，并在顶部多一个判据输入。
        //  判据一次只写一次（容器级），匹配值按分支各写各的——编译期把它们合成「判据 == 匹配值」。
        // ------------------------------------------------------------------

        /// <summary>
        /// 分支匹配模式（靶节点是 CaseStep）：表达式区换成"判据 + 匹配值"。
        /// </summary>
        public bool IsCaseMode
        {
            get => field;
            set
            {
                if (!SetProperty(ref field, value))
                    return;
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(EditorTitle));
                RaisePropertyChanged(nameof(ShowBooleanEditor));
                RaisePropertyChanged(nameof(ShowCaseValueEditor));
                // 兜底分支灰条的文案随模式切换（“该分支无需编写条件” ↔ “该分支是默认分支”）：
                // 漏发这两条，宿主复用同一视图时灰条会停在非 Case 措辞（review 2026-10-10 低危 2）
                RaisePropertyChanged(nameof(NoConditionTitle));
                RaisePropertyChanged(nameof(NoConditionHintText));
                RaisePropertyChanged(nameof(ModeChipText));
                RefreshJudgeCandidates();
                RefreshDraftDiagnostics();
            }
        }

        /// <summary>
        /// 判据草稿（事务：弹窗期间只改草稿，点"保存并应用"才写回活模型，取消即丢弃）。
        /// 内容 = 变量表里的一个别名，或一个运行时变量名。
        /// </summary>
        public string JudgeExpressionDraft
        {
            get => field;
            set { if (SetProperty(ref field, value)) RefreshDraftDiagnostics(); }
        }

        /// <summary>
        /// 布尔表达式编辑器（AvalonEdit + 快捷插入面板）的显隐。
        /// 分支匹配模式下一律隐藏——那里的输入是"匹配值"，不是布尔表达式
        ///（让用户在表达式框里写 1，只会换来一句看不懂的类型错）。
        /// </summary>
        public bool ShowBooleanEditor => !IsCaseMode && SelectedBranchNeedsExpression;

        /// <summary>匹配值输入框的显隐：分支匹配模式下、非兜底分支（Case 分支）。</summary>
        public bool ShowCaseValueEditor =>
            IsCaseMode && SelectedBranch != null && SelectedBranch.RequiresExpression;

        /// <summary>分支编辑区标题：普通模式写"逻辑判断表达式"，分支匹配模式写"匹配值"。</summary>
        public string EditorTitle
            => IsCaseMode
                ? (ShowCaseValueEditor ? $"为分支 [ {SelectedBranch.StepName} ] 填写匹配值" : "匹配值")
                : (
                    SelectedBranchNeedsExpression
                        ? $"为分支 [ {SelectedBranch.StepName} ] 编写逻辑判断表达式"
                        : "逻辑判断表达式"
                );

        /// <summary>兜底分支灰条的标题：两种模式的措辞不同（Else 分支 / 默认分支）。</summary>
        public string NoConditionTitle => IsCaseMode ? "该分支是默认分支" : "该分支无需编写条件";

        /// <summary>兜底分支灰条的说明文案（措辞随模式切换）。</summary>
        public string NoConditionHintText
            => IsCaseMode
                ? "系统自动将其视为“真”：只有其他 Case 的匹配值都不命中时，才会执行本分支的步骤。"
                : "系统自动将其视为“真”：只有其他分支的条件都不满足时，才会执行本分支的步骤。";

        // ==================================================================
        //  v2 展示层（只读，不参与保存判定）
        //
        //  这些属性只为「Web 化 v2」的标题胶囊 / 左栏徽标 / 卡内错误条 / 底栏摘要供数：
        //   · 判定规则与 OnSave 里的 ValidateDraft / ValidateCaseBranches / SaveWait **同源**，
        //     但不解析表达式（不跑 DynamicExpresso）、不拦保存、不改任何模型状态；
        //   · 真正拦保存的仍然是 OnSave 那一套（点「保存并应用」才会执行），
        //     所以这里的诊断只影响"显示什么"，绝不影响"能不能存"。
        // ==================================================================

        /// <summary>当前模式对应的标题胶囊文案（只读标识，不可点击）。</summary>
        public string ModeChipText
            => IsForMode ? "FOR"
                : IsWaitMode ? "WAIT"
                : IsCaseMode ? "CASE"
                : "IF / WHILE";

        /// <summary>当前选中分支是否缺条件（空表达式在编译期是硬错误）。</summary>
        public bool SelectedBranchMissingCondition =>
            !IsForMode && !IsWaitMode && SelectedBranch != null && SelectedBranch.IsConditionMissing;

        /// <summary>草稿诊断清单（按当前模式计算，顺序即显示顺序）。</summary>
        public ObservableCollection<string> DraftIssues { get; } = new();

        /// <summary>是否有草稿问题（驱动底栏红/绿）。</summary>
        public bool HasValidationErrors => DraftIssues.Count > 0;

        /// <summary>问题条数（底栏「N 处问题」）。</summary>
        public int ValidationErrorCount => DraftIssues.Count;

        /// <summary>卡内错误条文案（多条用「；」连接）；无问题返回空串。</summary>
        public string CardErrorText => string.Join("；", DraftIssues);

        /// <summary>卡内错误条是否显示（只在有错时出现，成功摘要归底栏）。</summary>
        public bool HasCardError => DraftIssues.Count > 0;

        /// <summary>底栏摘要文案：与设计稿 v2 的四种口径一一对应。</summary>
        public string ValidationSummaryText
        {
            get
            {
                if (DraftIssues.Count > 0)
                    return $"校验未通过：{DraftIssues.Count} 处问题 · 点「保存并应用」会再次提示并停在弹窗";
                if (IsForMode && HasLoopCountLink)
                    return "循环次数由数据源驱动 · 草稿已就绪";
                if (!IsForMode && !IsWaitMode && SelectedBranch != null && !SelectedBranch.RequiresExpression)
                    return IsCaseMode ? "默认分支无需匹配值，可直接保存" : "兜底分支无需条件，可直接保存";
                return "草稿已校验，可保存";
            }
        }

        /// <summary>底栏摘要级别：<c>ok</c> / <c>err</c>（视图用它切颜色与图标）。</summary>
        public string ValidationSummaryLevel => DraftIssues.Count > 0 ? "err" : "ok";

        /// <summary>分支匹配「已用匹配值」列表（展示层：把 Branches 投影成只读行）。</summary>
        public ObservableCollection<CaseMatchRow> CaseMatchRows { get; } = new();

        /// <summary>判据候选：变量表别名 + 运行时变量名（名字语义，手填 + 候选，R33）。</summary>
        public IReadOnlyList<string> JudgeVariableCandidates { get; private set; } = Array.Empty<string>();

        /// <summary>For 模式摘要：循环体步骤数 / 默认次数（展示层）。</summary>
        public string LoopSummaryText
        {
            get
            {
                int steps = _targetForNode?.TotalStepCount ?? 0;
                string count = string.IsNullOrWhiteSpace(LoopCountText) ? "—" : LoopCountText.Trim();
                string source = HasLoopCountLink ? (LoopCountSourceText ?? "已连线") : "未连线";
                return $"循环体 {steps} 个步骤 · 默认次数 {count} · 数据源 {source}";
            }
        }

        /// <summary>
        /// 重算草稿诊断（只读）。调用点：分支切换、变量增删/改名、判据/等待参数变化、
        /// 表达式文本变化（code-behind 写回后）、打开弹窗末尾。
        /// </summary>
        public void RefreshDraftDiagnostics()
        {
            DraftIssues.Clear();

            // 变量表：别名非法 / 绑定却空别名 / 别名重复（与 VariableItem 的 IDataErrorInfo 同一规则）
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Variables.Count; i++)
            {
                var v = Variables[i];
                string alias = v?.AliasName?.Trim();
                bool hasBinding = v?.OriginalLink != null || !string.IsNullOrEmpty(v?.SourceAddress);

                if (string.IsNullOrEmpty(alias))
                {
                    if (hasBinding)
                        DraftIssues.Add($"第 {i + 1} 行：变量已绑定数据源但别名为空");
                    continue;
                }
                if (!VariableItem.IdentifierRegex.IsMatch(alias))
                    DraftIssues.Add($"第 {i + 1} 行：别名「{alias}」不是合法标识符");
                else if (!seen.Add(alias))
                    DraftIssues.Add($"第 {i + 1} 行：别名「{alias}」与上一行重复，表达式将无法区分引用哪个");
            }

            if (!IsForMode)
            {
                if (IsCaseMode)
                    CollectCaseIssues();
                else if (!IsWaitMode)
                    CollectConditionIssues();
                else
                    CollectWaitIssues();
            }
            else
            {
                CollectForIssues();
            }

            RefreshCaseMatchRows();
            RaiseDiagnosticChanged();
        }

        /// <summary>条件模式：必需分支的空条件（编译期硬错误，与 StepCollection.IsConditionMissing 同源）。</summary>
        private void CollectConditionIssues()
        {
            foreach (var br in Branches)
            {
                if (br != null && br.IsConditionMissing)
                    DraftIssues.Add($"分支「{br.StepName}」的条件表达式为空（空条件在编译时是硬错误，流程将无法运行）");
            }
        }

        /// <summary>分支匹配：判据必须已声明且类型可等值比较；匹配值按判据类型转换并查重。</summary>
        private void CollectCaseIssues()
        {
            string judgeText = JudgeExpressionDraft?.Trim();
            Type judgeType = null;

            if (string.IsNullOrEmpty(judgeText))
            {
                DraftIssues.Add("判据表达式为空：请填写「拿哪个变量去比对」");
            }
            else
            {
                var judgeVar = Variables.FirstOrDefault(
                    v => string.Equals((v.AliasName ?? string.Empty).Trim(), judgeText, StringComparison.Ordinal));

                if (judgeVar != null)
                {
                    judgeType = TypeHelper.GetActualTypeFromLink(judgeVar.DataTypeName);
                }
                else
                {
                    var runtimeVar = _targetNode?.RuntimeVariableRefs?.FirstOrDefault(
                        r => string.Equals((r?.Name ?? string.Empty).Trim(), judgeText, StringComparison.Ordinal));

                    if (runtimeVar != null)
                        judgeType = TypeHelper.GetActualTypeFromLink(runtimeVar.DataTypeName);
                    else
                        DraftIssues.Add($"判据「{judgeText}」不是本容器已声明的变量");
                }
            }

            if (judgeType != null && !CaseValueHelper.IsSupportedJudgeType(judgeType))
            {
                DraftIssues.Add($"判据「{judgeText}」的类型 [{judgeType.Name}] 不支持等于匹配");
                judgeType = null;
            }

            var seenValues = new HashSet<object>();
            foreach (var br in Branches)
            {
                if (br == null) continue;
                if (br.BranchType != BranchType.Case)
                {
                    if (br.RequiresExpression)
                        DraftIssues.Add($"分支「{br.StepName}」不是 Case 分支，分支匹配容器里不能混用其它分支类型");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(br.Expression))
                {
                    DraftIssues.Add($"分支「{br.StepName}」的匹配值为空（编译时是硬错误）");
                    continue;
                }
                if (judgeType == null) continue;
                if (!CaseValueHelper.TryConvert(br.Expression, judgeType, out var value, out var convertError))
                {
                    DraftIssues.Add($"分支「{br.StepName}」{convertError}");
                    continue;
                }
                if (!seenValues.Add(value))
                    DraftIssues.Add($"分支「{br.StepName}」的匹配值与前面某条 Case 分支重复，永远不会被执行");
            }
        }

        /// <summary>等待模式：轮询间隔 / 超时 / 报警变量（与 SaveWait 同规则，只做展示）。</summary>
        private void CollectWaitIssues()
        {
            foreach (var br in Branches)
            {
                if (br != null && br.IsConditionMissing)
                    DraftIssues.Add("等待条件为空（编译时是硬错误）");
            }

            if (!int.TryParse((PollIntervalText ?? string.Empty).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int poll))
                DraftIssues.Add("轮询间隔必须是整数（毫秒）");
            else if (poll < 10)
                DraftIssues.Add($"轮询间隔不能小于 10ms（当前 {poll}ms）");

            if (!int.TryParse((TimeoutText ?? string.Empty).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int timeout))
                DraftIssues.Add("超时时间必须是整数（毫秒；0 表示不限）");
            else if (timeout < 0)
                DraftIssues.Add($"超时时间不能为负（当前 {timeout}ms）");

            string alarm = (AlarmVariableNameDraft ?? string.Empty).Trim();
            if (alarm.Length > 0 && !AlarmVariableCandidates.Contains(alarm))
                DraftIssues.Add($"报警变量「{alarm}」不存在或不是全局 bool 变量");
        }

        /// <summary>For 模式：循环次数范围（已连线且留空时允许，与 SaveForLoop 同规则）。</summary>
        private void CollectForIssues()
        {
            string text = (LoopCountText ?? string.Empty).Trim();
            if (HasLoopCountLink && text.Length == 0) return;

            if (!int.TryParse(text, out int count) || count < 1 || count > 99999)
                DraftIssues.Add("循环次数必须是 1 ~ 99999 之间的整数");
        }

        /// <summary>把 Branches 投影成「已用匹配值」只读行（当前 / 已占用 / 兜底 / 重复）。</summary>
        private void RefreshCaseMatchRows()
        {
            CaseMatchRows.Clear();
            if (!IsCaseMode) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Branches.Count; i++)
            {
                var br = Branches[i];
                if (br == null) continue;

                if (!br.RequiresExpression)
                {
                    CaseMatchRows.Add(new CaseMatchRow
                    {
                        StepName = br.StepName ?? string.Empty,
                        Value = "无需匹配值",
                        Kind = "兜底",
                        IsCurrent = ReferenceEquals(br, SelectedBranch),
                        IsDuplicate = false,
                        IsPlaceholder = true,
                    });
                    continue;
                }

                string value = (br.Expression ?? string.Empty).Trim();
                bool dup = value.Length > 0 && !seen.Add(value);
                CaseMatchRows.Add(new CaseMatchRow
                {
                    StepName = br.StepName ?? string.Empty,
                    Value = value.Length == 0 ? "（空）" : value,
                    Kind = dup ? "重复" : (ReferenceEquals(br, SelectedBranch) ? "当前" : "已占用"),
                    IsCurrent = ReferenceEquals(br, SelectedBranch),
                    IsDuplicate = dup,
                    IsPlaceholder = value.Length == 0,
                });
            }
        }

        /// <summary>判据候选重建（变量表别名 + 运行时变量名，去重保序）。</summary>
        private void RefreshJudgeCandidates()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var v in Variables)
            {
                string alias = v?.AliasName?.Trim();
                if (!string.IsNullOrEmpty(alias) && seen.Add(alias)) list.Add(alias);
            }
            if (_targetNode?.RuntimeVariableRefs != null)
            {
                foreach (var r in _targetNode.RuntimeVariableRefs)
                {
                    string name = r?.Name?.Trim();
                    if (!string.IsNullOrEmpty(name) && seen.Add(name)) list.Add(name);
                }
            }
            JudgeVariableCandidates = list;
            RaisePropertyChanged(nameof(JudgeVariableCandidates));
        }

        private void RaiseDiagnosticChanged()
        {
            RaisePropertyChanged(nameof(HasValidationErrors));
            RaisePropertyChanged(nameof(ValidationErrorCount));
            RaisePropertyChanged(nameof(CardErrorText));
            RaisePropertyChanged(nameof(HasCardError));
            RaisePropertyChanged(nameof(ValidationSummaryText));
            RaisePropertyChanged(nameof(ValidationSummaryLevel));
            RaisePropertyChanged(nameof(SelectedBranchMissingCondition));
            RaisePropertyChanged(nameof(LoopSummaryText));
            RaisePropertyChanged(nameof(ModeChipText));
        }

        /// <summary>
        /// 循环次数草稿（字符串承载：输入中途非数字也不会被绑定引擎吞掉，保存时统一 TryParse）
        /// </summary>
        public string LoopCountText { get => field; set { if (SetProperty(ref field, value)) RefreshDraftDiagnostics(); } } = "10";

        /// <summary>
        /// 循环次数连线草稿（与 LoopCountText 同属"草稿→校验→写回"事务，取消即丢弃）。
        /// 键固定 "LoopCount"：画布输入脚、编译期挂接、FlowQueryHelper 候选都用这一个名字。
        /// </summary>
        private LinkReference _loopCountLinkDraft;

        /// <summary>已连线的显示串（如 "Runtime.loopN"、"计数.Result"），仅用于展示</summary>
        public string LoopCountSourceText
        {
            get => field;
            set { if (SetProperty(ref field, value)) RefreshDraftDiagnostics(); }
        }

        /// <summary>是否已给 LoopCount 连线（驱动"已连线/未连线"两种提示与解除按钮显隐）</summary>
        public bool HasLoopCountLink => _loopCountLinkDraft != null;

        public ObservableCollection<VariableItem> Variables { get; } = new ObservableCollection<VariableItem>();

        public DelegateCommand AddVariableCommand { get; }
        public DelegateCommand<VariableItem> RemoveVariableCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand CancelCommand { get; }
        public DelegateCommand<VariableItem> BindVariableCommand { get; }

        /// <summary>For 模式：给循环次数挂一个数据源（走 DataBindView 单绑通道）</summary>
        public DelegateCommand LinkLoopCountCommand { get; }

        /// <summary>For 模式：解除循环次数连线（只清草稿，保存时才动活模型）</summary>
        public DelegateCommand UnlinkLoopCountCommand { get; }

        public string Title => IsForMode
            ? "For 循环配置"
            : IsWaitMode
                ? "等待条件配置"
                : (IsCaseMode ? "分支匹配配置" : "条件逻辑配置中心");
        public DialogCloseListener RequestClose { get; set; }
        public event EventHandler<StepCollection> BranchChanged;

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (!parameters.TryGetValue<StepModel>("Node", out var nodeModel) || nodeModel == null)
                return;

            // 模式先归零：Prism 一般每弹一次给一个新实例，但宿主复用同一实例时
            // 上一轮的 Case/For/等待模式会残留（标题、编辑区显隐全错），这里显式复位
            IsCaseMode = false;
            _targetCaseNode = null;
            IsWaitMode = false;
            _targetWaitNode = null;

            // P1-⑥：For 节点并入——同一弹窗、同一套"草稿→校验→写回→Version++"事务管线，只是编辑内容换成循环次数
            if (nodeModel is ForStep forStep)
            {
                _targetForNode = forStep;
                IsForMode = true;
                LoopCountText = forStep.DefaultLoopCount.ToString();
                // 连线草稿从活模型"借"一份引用：弹窗期间换线/解除只动草稿，取消即丢弃
                forStep.LinkedSources.TryGetValue("LoopCount", out _loopCountLinkDraft);
                LoopCountSourceText = _loopCountLinkDraft?.DisplayAddress;
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(HasLoopCountLink));
                RefreshDraftDiagnostics();
                return;
            }

            // 等待节点：不是 ConditionStep（无子分支），走同一个编辑器的「等待模式」。
            // 条件表达式复用现有编辑器：Branches 里只放一条合成草稿，SelectedBranch 指向它，
            // code-behind 的 TxtExpression_TextChanged（写 SelectedBranch.Expression）零改动可用。
            if (nodeModel is WaitStep waitStep)
            {
                IsForMode = false;
                _targetWaitNode = waitStep;
                IsWaitMode = true;

                var waitDraft = new StepCollection
                {
                    BranchType = BranchType.If,
                    StepName = "等待条件",
                    Expression = waitStep.ConditionExpression,
                };
                Branches = new ObservableCollection<StepCollection> { waitDraft };
                SelectedBranch = waitDraft;

                Variables.Clear();
                foreach (var localVar in waitStep.LocalVariables)
                {
                    var item = new VariableItem
                    {
                        Id = localVar.Id,
                        AliasName = localVar.Name,
                        DataTypeName = localVar.DataTypeName,
                    };
                    if (waitStep.LinkedSources.TryGetValue(item.Id.ToString(), out var link))
                    {
                        item.SourceAddress = link.DisplayAddress;
                        item.OriginalLink = link;
                    }
                    AttachDupCheck(item);
                    Variables.Add(item);
                }

                // 五个参数抄进草稿（条件表达式已随合成分支进 Branches）：弹窗期间只改草稿，取消即丢弃
                PollIntervalText = waitStep.PollIntervalMs.ToString(CultureInfo.InvariantCulture);
                TimeoutText = waitStep.TimeoutMs.ToString(CultureInfo.InvariantCulture);
                TimeoutMessageDraft = waitStep.TimeoutMessage ?? string.Empty;
                AlarmVariableNameDraft = waitStep.AlarmVariableName ?? string.Empty;
                TimeoutActionValue = waitStep.TimeoutAction.ToString();

                // 报警变量候选现取：只列**用户可写**的全局 bool 变量。
                // 系统变量（VariableType.System）是运行态派生只读量，写不进去，列出来只会让人选了保存失败。
                AlarmVariableCandidates.Clear();
                if (_workspace?.GlobalVariables != null)
                {
                    foreach (var variable in _workspace.GlobalVariables)
                    {
                        if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
                            continue;
                        if (variable.VariableType == VariableType.System)
                            continue;
                        if (variable.DataType != typeof(bool))
                            continue;
                        if (!AlarmVariableCandidates.Contains(variable.Name))
                            AlarmVariableCandidates.Add(variable.Name);
                    }
                }

                RaisePropertyChanged(nameof(Title));
                RefreshDraftDiagnostics();
                return;
            }

            if (nodeModel is not ConditionStep node)
                return;

            IsForMode = false;
            _targetNode = node;

            // 分支匹配（Case）容器：同一套草稿管线，编辑器切成"判据 + 匹配值"模式。
            // 判据从活模型抄进草稿——弹窗期间打字只落草稿，取消即丢弃（与分支表达式同一条事务）。
            if (node is CaseStep caseStep)
            {
                _targetCaseNode = caseStep;
                IsCaseMode = true;
                JudgeExpressionDraft = caseStep.JudgeExpression;
            }

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
            RefreshJudgeCandidates();
            RefreshDraftDiagnostics();
        }

        public ConditionEditorViewModel(IDialogService dialogService, IWorkspaceManager workspace)
        {
            this.dialogService = dialogService;
            _workspace = workspace;

            // IsWaitMode / IsCaseMode 的 setter 会先调 RefreshDraftDiagnostics()，而 OnDialogOpened
            // 里 Branches 要到后面才赋实际草稿（等待模式在 IsWaitMode=true 之后才 new）。
            // 不先给空集合，CollectWaitIssues/CollectCaseIssues 会在打开等待/分支匹配弹窗时
            // 直接 foreach(null) 抛 NRE（真机 2026-10-20 17:24 的 [UI] Object reference）。
            Branches = new ObservableCollection<StepCollection>();

            AddVariableCommand = new DelegateCommand(OnAddVariable);
            RemoveVariableCommand = new DelegateCommand<VariableItem>(OnRemoveVariable);
            SaveCommand = new DelegateCommand(OnSave);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.Cancel)));
            BindVariableCommand = new DelegateCommand<VariableItem>(OnBindVariable);
            LinkLoopCountCommand = new DelegateCommand(OnLinkLoopCount);
            UnlinkLoopCountCommand = new DelegateCommand(OnUnlinkLoopCount);
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
                RefreshJudgeCandidates();
                RefreshDraftDiagnostics();
            });
        }

        /// <summary>
        /// For 模式：给 LoopCount 挂数据源。
        /// 复用单绑通道（弹窗只回传 LinkReference，不写模型），写回留到 SaveForLoop，
        /// 这样"取消"能干净地丢弃连线，与 LoopCountText 处在同一个事务里。
        /// </summary>
        private void OnLinkLoopCount()
        {
            if (_targetForNode == null) return;

            var p = new DialogParameters
            {
                { "IsSingleBindMode", true },
                // 弹窗左侧的"目标端口"只是展示用的 mock 节点，键名必须与画布输入脚、
                // 编译期挂接（FlowCompiler 公共赋值段）用的是同一个 "LoopCount"
                { "TargetPortName", "LoopCount" },
                { "TargetTypeName", "System.Int32" },
                // 候选树锚定到"被双击的这个 For 步骤"：下钻进容器时 Workspace.CurrentStep 指向的是
                // 外层容器而不是本步骤，拿到的上游集合就是错的（循环体外定义的变量会选不到）。
                { "TargetStep", _targetForNode },
            };

            dialogService.ShowDialog("DataBindView", p, (s) =>
            {
                if (s.Result != ButtonResult.OK) return;
                if (
                    !s.Parameters.TryGetValue<LinkReference>("BoundLink", out var link)
                    || link == null
                )
                    return;

                _loopCountLinkDraft = link;
                LoopCountSourceText = link.DisplayAddress;
                RaisePropertyChanged(nameof(HasLoopCountLink));
                RefreshDraftDiagnostics();
            });
        }

        private void OnUnlinkLoopCount()
        {
            _loopCountLinkDraft = null;
            LoopCountSourceText = null;
            RaisePropertyChanged(nameof(HasLoopCountLink));
            RefreshDraftDiagnostics();
        }

        private void OnAddVariable()
        {
            // P0-④：自动别名跳过已占用的名字，从源头避免 Var_N 撞名
            int n = Variables.Count + 1;
            while (Variables.Any(v => v.AliasName == $"Var_{n}")) n++;
            var item = new VariableItem { AliasName = $"Var_{n}" };
            AttachDupCheck(item);
            Variables.Add(item);
            RefreshJudgeCandidates();
            RefreshDraftDiagnostics();
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
            // v2：变量行的别名/绑定变化要驱动卡内错误条与底栏摘要（只读展示，不拦保存）
            item.DiagnosticsInvalidated = () =>
            {
                RefreshJudgeCandidates();
                RefreshDraftDiagnostics();
            };
        }

        private void OnRemoveVariable(VariableItem item)
        {
            if (item != null) Variables.Remove(item);
            RefreshJudgeCandidates();
            RefreshDraftDiagnostics();
        }

        private void OnSave()
        {
            if (IsForMode)
            {
                SaveForLoop();
                return;
            }
            if (IsWaitMode)
            {
                SaveWait();
                return;
            }
            if (_targetNode == null) return;

            // ==============================================
            // P0-④：保存前校验（快速失败）——语法、别名、空条件全部在弹窗内说清楚，
            // 而不是让脏数据流进 FlowCompiler，等到运行时才以"编译失败"的面目爆出来
            // ==============================================
            var errors = ValidateDraft(_targetNode.RuntimeVariableRefs);

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

            // 分支匹配：判据写回（走 setter → FlowModel 版本链自动递增；末尾还有一次手动 Version++
            // 兜底，与分支表达式的既有口径一致）
            if (_targetCaseNode != null)
                _targetCaseNode.JudgeExpression = (JudgeExpressionDraft ?? string.Empty).Trim();

            WriteBackVariables(_targetNode.LocalVariables, _targetNode.LinkedSources);

            // P0-①：FlowModel.Version 只订阅了顶层 Steps 的 PropertyChanged，
            // 条件表达式/变量都在 Children、LocalVariables 里，引擎永远感知不到变更 →
            // 保存成功后手动推进版本号，触发"需要重新编译"提示，杜绝"试运行对、正式跑错"
            if (_workspace?.CurrentFlow != null)
                _workspace.CurrentFlow.Version++;

            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }

        /// <summary>
        /// 保存前校验（If/While/Case/等待共用）：变量表（别名/重复/类型）+ 分支表达式语法。
        /// runtimeVariableRefs 由调用方给——等待节点不是 ConditionStep，运行时变量表在它自己身上。
        /// </summary>
        private List<string> ValidateDraft(IEnumerable<LocalVariableItem> runtimeVariableRefs)
        {
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
            if (runtimeVariableRefs != null)
            {
                foreach (var runtimeVar in runtimeVariableRefs)
                {
                    if (string.IsNullOrWhiteSpace(runtimeVar?.Name)) continue;
                    Type rt = TypeHelper.GetActualTypeFromLink(runtimeVar.DataTypeName);
                    if (usedNames.Add(runtimeVar.Name))
                        delegateParams.Add(new Parameter(runtimeVar.Name, rt));
                }
            }

            // 条件必填判定统一走 StepCollection.RequiresExpression（与流程栏红灯、弹窗灰条同一标准）：
            // If/ElseIf/Case/WhileLoop 必须有表达式；Else/Default(For 循环体) 由编译器强制视为 true
            if (IsCaseMode)
            {
                ValidateCaseBranches(errors);
            }
            else
            {
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
            }

            return errors;
        }

        /// <summary>
        /// 变量表写回（If/While/Case/等待共用）：LocalVariables 与 LinkedSources 的变量 Guid 键
        /// 一次性重建——两处各写一份的下场是"变量存了、连线没存"（条件恒取默认值）。
        /// </summary>
        private void WriteBackVariables(
            ObservableCollection<LocalVariableItem> localVariables,
            IDictionary<string, LinkReference> linkedSources)
        {
            localVariables.Clear();
            linkedSources.Clear();

            foreach (var v in Variables)
            {
                string alias = v.AliasName?.Trim();
                if (string.IsNullOrEmpty(alias)) continue;

                localVariables.Add(new LocalVariableItem
                {
                    Id = v.Id,
                    Name = alias,
                    DataTypeName = v.DataTypeName
                });

                if (v.OriginalLink != null)
                {
                    linkedSources[v.Id.ToString()] = v.OriginalLink;
                }
            }
        }

        /// <summary>
        /// 等待模式保存：条件表达式（复用 ValidateDraft 的分支校验）+ 轮询/超时/超时处置/说明/报警变量。
        /// 校验失败一律停在弹窗（草稿保留），绝不把"50ms 写成 5"这种错误流进编译器。
        /// </summary>
        private void SaveWait()
        {
            if (_targetWaitNode == null) return;

            var errors = ValidateDraft(_targetWaitNode.RuntimeVariableRefs);

            // 数值解析一律 InvariantCulture：区域设置不该改变"5000 是五千"这件事
            if (!int.TryParse(
                    (PollIntervalText ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int pollIntervalMs))
            {
                errors.Add("轮询间隔必须是整数（毫秒）");
            }
            else if (pollIntervalMs < 10)
            {
                errors.Add($"轮询间隔不能小于 10ms（当前 {pollIntervalMs}ms）");
            }

            if (!int.TryParse(
                    (TimeoutText ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int timeoutMs))
            {
                errors.Add("超时时间必须是整数（毫秒；0 表示不限）");
            }
            else if (timeoutMs < 0)
            {
                errors.Add($"超时时间不能为负（当前 {timeoutMs}ms；0 表示不限）");
            }

            var timeoutAction = WaitTimeoutAction.TerminateFlow;
            if (!string.IsNullOrWhiteSpace(TimeoutActionValue))
            {
                // TryParse 会把数字串（"99"）也认成"成功"，所以必须再判 IsDefined——
                // 否则手填/旧数据里的非法值会静默落进流程，运行期按"不是 Continue 就终止"处理。
                if (!Enum.TryParse(TimeoutActionValue, out timeoutAction)
                    || !Enum.IsDefined(typeof(WaitTimeoutAction), timeoutAction))
                {
                    errors.Add($"超时处理方式 '{TimeoutActionValue}' 不是有效的选项");
                }
            }

            // 报警变量：非空就必须是"已存在的用户全局 bool 变量"。
            // 这里与打开时填充的 AlarmVariableCandidates 是同一来源——
            // 不做这一步的后果是"手打一个不存在的名字，保存成功、跑起来只落一条 Warn"，
            // 现场以为报警配好了，实际超时那一刻什么都没写。
            string alarmVariableName = (AlarmVariableNameDraft ?? string.Empty).Trim();
            if (alarmVariableName.Length > 0 && !AlarmVariableCandidates.Contains(alarmVariableName))
            {
                errors.Add(
                    $"报警变量「{alarmVariableName}」不存在或不是全局 bool 变量（请先在变量管理里创建）");
            }

            if (errors.Count > 0)
            {
                EasyDialog.ShowSync("校验未通过", string.Join("\n", errors));
                return; // 停留在弹窗，草稿保留
            }

            _targetWaitNode.ConditionExpression =
                (Branches.Count > 0 ? Branches[0].Expression : string.Empty)?.Trim() ?? string.Empty;
            _targetWaitNode.PollIntervalMs = pollIntervalMs;
            _targetWaitNode.TimeoutMs = timeoutMs;
            _targetWaitNode.TimeoutAction = timeoutAction;
            _targetWaitNode.TimeoutMessage = (TimeoutMessageDraft ?? string.Empty).Trim();
            _targetWaitNode.AlarmVariableName = alarmVariableName;

            WriteBackVariables(_targetWaitNode.LocalVariables, _targetWaitNode.LinkedSources);

            // 版本号兜底推进：六个语义属性（条件表达式 + 轮询/超时/处置/说明/报警变量）
            // 各自会触发一次版本链，这里多推一次是"打开又直接确认"也必然重编译的保险
            //（与 ConditionStep 保存段同款）
            if (_workspace?.CurrentFlow != null)
                _workspace.CurrentFlow.Version++;

            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }

        /// <summary>
        /// 分支匹配（Case）模式的保存前校验：判据必须是已声明变量、类型可匹配；
        /// 每条 Case 分支的匹配值按判据类型归一，重复值直接拦下（编译器同样报"不可达分支"，
        /// 这里先拦是为了让用户停在弹窗里改，而不是保存后拿一句编译错误）。
        /// 值的转换口径与编译器共用 CaseValueHelper —— 单一判据，不各写一份。
        /// </summary>
        private void ValidateCaseBranches(List<string> errors)
        {
            string judgeText = JudgeExpressionDraft?.Trim();
            Type judgeType = null;

            if (string.IsNullOrEmpty(judgeText))
            {
                errors.Add("判据表达式为空：请填写“拿哪个变量去比对”（用变量表里的别名，或运行时变量名）");
            }
            else
            {
                // 判据来源与编译器一致：LocalVariables 的别名，或 RuntimeVariableRefs 的变量名
                var judgeVar = Variables.FirstOrDefault(
                    v => string.Equals((v.AliasName ?? "").Trim(), judgeText, StringComparison.Ordinal)
                );

                if (judgeVar != null)
                {
                    judgeType = TypeHelper.GetActualTypeFromLink(judgeVar.DataTypeName);
                }
                else
                {
                    var runtimeVar = _targetNode.RuntimeVariableRefs.FirstOrDefault(
                        r => string.Equals((r?.Name ?? "").Trim(), judgeText, StringComparison.Ordinal)
                    );

                    if (runtimeVar != null)
                        judgeType = TypeHelper.GetActualTypeFromLink(runtimeVar.DataTypeName);
                    else
                        errors.Add($"判据 '{judgeText}' 不是本容器已声明的变量（请在上方变量表里新增该变量，或改用运行时变量名）");
                }
            }

            if (judgeType != null && !CaseValueHelper.IsSupportedJudgeType(judgeType))
            {
                errors.Add($"判据 '{judgeText}' 的类型 [{judgeType.Name}] 不支持等于匹配（仅支持数值 / 布尔 / 字符串等值类型）");
                judgeType = null;
            }

            var seenValues = new HashSet<object>();
            foreach (var br in Branches)
            {
                if (br.BranchType != BranchType.Case)
                {
                    // 混装是编译器硬错误，这里提前告知（弹窗内不能新增/改造分支类型，只可能来自手改数据）
                    if (br.RequiresExpression)
                        errors.Add($"分支 '{br.StepName}' 不是 Case 分支，分支匹配容器里不能混用其它分支类型");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(br.Expression))
                {
                    errors.Add($"分支 '{br.StepName}' 的匹配值为空（空值在编译时是硬错误，流程将无法运行）");
                    continue;
                }

                if (judgeType == null)
                    continue; // 判据本身有问题时已统一报错，不再连带刷屏

                if (!CaseValueHelper.TryConvert(br.Expression, judgeType, out var value, out var convertError))
                {
                    errors.Add($"分支 '{br.StepName}' {convertError}");
                    continue;
                }

                if (!seenValues.Add(value))
                    errors.Add($"分支 '{br.StepName}' 的匹配值与前面某条 Case 分支重复，永远不会被执行");
            }
        }

        /// <summary>
        /// P1-⑥：For 模式保存——校验循环次数并写回活模型，
        /// 复用条件编辑同一套"草稿→校验→写回→Version++"事务管线
        /// </summary>
        private void SaveForLoop()
        {
            if (_targetForNode == null) return;

            string text = (LoopCountText ?? "").Trim();

            // 已连线时，文本框里的数字退化成"连线取不到值时的回落次数"，
            // 留空就表示"沿用模型上原有的默认值"，不该因此卡住保存。
            if (!(_loopCountLinkDraft != null && text.Length == 0))
            {
                if (
                    !int.TryParse(text, out int count)
                    || count < 1
                    || count > 99999
                )
                {
                    EasyDialog.ShowSync(
                        "校验未通过",
                        "循环次数必须是 1 ~ 99999 之间的整数！\n（引擎对 For 无迭代上限保护，防止手滑天文数字拖死流程）"
                    );
                    return;
                }
                _targetForNode.DefaultLoopCount = count;
            }

            // 连线写回：草稿与活模型不是同一份才动手，避免打开又直接保存也刷一遍版本号
            var liveLink = _targetForNode.GetLink("LoopCount");
            if (!ReferenceEquals(liveLink, _loopCountLinkDraft))
            {
                if (_loopCountLinkDraft != null)
                    _targetForNode.SetLink("LoopCount", _loopCountLinkDraft);
                else
                    _targetForNode.RemoveLink("LoopCount");
            }

            if (_workspace?.CurrentFlow != null)
                _workspace.CurrentFlow.Version++;

            RequestClose.Invoke(new DialogResult(ButtonResult.OK));
        }
    }

    /// <summary>
    /// 分支匹配「已用匹配值」只读行（v2 设计稿的列表）。
    /// 纯展示：由 <see cref="ConditionEditorViewModel.CaseMatchRows"/> 从 Branches 投影，
    /// 不参与保存判定、不写模型。
    /// </summary>
    public class CaseMatchRow
    {
        public string StepName { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        /// <summary>当前 / 已占用 / 兜底 / 重复</summary>
        public string Kind { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public bool IsDuplicate { get; set; }
        /// <summary>值为空或「无需匹配值」时用斜体弱化。</summary>
        public bool IsPlaceholder { get; set; }
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

        /// <summary>VM 注入的查重回调：单行看不见集合里的其他行，跨行视野由外部提供</summary>
        public Func<string, bool> DuplicateChecker { get; set; }

        /// <summary>VM 注入的诊断刷新回调（别名/类型/绑定变化后重算卡内错误条与底栏摘要）。</summary>
        public Action DiagnosticsInvalidated { get; set; }

        public Guid Id { get; set; } = Guid.NewGuid();

        public string AliasName
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    RaisePropertyChanged("Error"); // 通知 WPF 重跑 IDataErrorInfo（附1：打字即校验）
                    DiagnosticsInvalidated?.Invoke();
                }
            }
        }

        public string SourceAddress
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    DiagnosticsInvalidated?.Invoke();
            }
        }
        public LinkReference OriginalLink { get; set; }

        public string DataTypeName { get; set; } = "System.Double";

        /// <summary>类型短名（v2 变量表「类型」列）：System.Double → DOUBLE，System.String → STRING。</summary>
        public string DataTypeShortName
        {
            get
            {
                string name = DataTypeName ?? string.Empty;
                int dot = name.LastIndexOf('.');
                if (dot >= 0 && dot < name.Length - 1) name = name.Substring(dot + 1);
                return name.ToUpperInvariant();
            }
        }

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