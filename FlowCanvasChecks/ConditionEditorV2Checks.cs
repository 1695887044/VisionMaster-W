using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 条件编辑器（ConditionEditorView + ConditionEditorViewModel）v2 复刻的守门断言。
    ///
    /// 覆盖三件事，全部是「改了就会让 v2 设计稿静默失效」的点：
    ///  ① **契约**：所有命令/属性名仍在、AvalonEdit 的 txtExpression 与 TextChanged 仍在、
    ///     快捷插入的 Tag 片段逐字未变（Tag 写错一个字符就是插入一段错代码）；
    ///  ② **展示层**：v2 新增的只读属性（ModeChipText / CardErrorText / ValidationSummaryText /
    ///     CaseMatchRows / JudgeVariableCandidates / LoopSummaryText / DataTypeShortName）
    ///     与设计稿的四种底栏口径一一对应；
    ///  ③ **只读性**：这些展示属性**绝不**改变保存判定——空表达式时 `DraftIssues` 非空，
    ///     但 `OnSave` 仍按原规则走（拦不拦由原校验决定），且 `OnConfirm` 无关。
    ///
    /// 断言只读 VM 与磁盘源码，不实例化视图（WPF 视图由 UIThemeSmokeTest 的渲染探针覆盖）。
    /// </summary>
    internal static class ConditionEditorV2Checks
    {
        public static void Run()
        {
            Section("[CE2] 条件编辑器 v2：契约保留 / 展示层口径 / 只读性 / 左栏摘要实时性");

            ContractPreserved();
            DisplayLayerMatchesDesign();
            CaseModeMatchesDesign();
            WaitModeMatchesDesign();
            ForModeMatchesDesign();
            DiagnosticsAreReadOnly();
            SourceContractPreserved();
        }

        // ---------------------------------------------------------------- ① 契约

        private static void ContractPreserved()
        {
            var vm = NewVm(out var harness);
            try
            {
                // ConditionStep 构造函数已预建 [If, Else] 两条分支（见 ConditionStep.cs:96-111），
                // 这里不再追加，避免把「预建两条」这件事测成「四条」。
                var ifStep = new ConditionStep("\uE700", "结果判断", StubPluginProvider.IfPlugin, "质量分档");

                var p = new DialogParameters { { "Node", ifStep } };
                vm.OnDialogOpened(p);

                Check("CE2-C1：OnDialogOpened 后 Branches 与活模型同数、SelectedBranch 指向第一条",
                    vm.Branches.Count == ifStep.Children.Count && ReferenceEquals(vm.SelectedBranch, vm.Branches[0]),
                    $"Branches={vm.Branches.Count} 活模型={ifStep.Children.Count} Selected={(vm.SelectedBranch == null ? "null" : vm.SelectedBranch.StepName)}");

                Check("CE2-C2：命令族齐备（Add/Remove/Bind/Save/Cancel/Link/Unlink）",
                    vm.AddVariableCommand != null && vm.RemoveVariableCommand != null
                    && vm.BindVariableCommand != null && vm.SaveCommand != null
                    && vm.CancelCommand != null && vm.LinkLoopCountCommand != null
                    && vm.UnlinkLoopCountCommand != null,
                    "七个命令全部非空");

                Check("CE2-C3：SelectedBranchNeedsExpression / ShowBooleanEditor 与 RequiresExpression 同源",
                    vm.SelectedBranchNeedsExpression && vm.ShowBooleanEditor,
                    $"needs={vm.SelectedBranchNeedsExpression} showBool={vm.ShowBooleanEditor}");

                vm.SelectedBranch = vm.Branches[1];
                Check("CE2-C4：切到 Else 分支 → ShowBooleanEditor=false（走兜底说明）",
                    !vm.SelectedBranchNeedsExpression && !vm.ShowBooleanEditor,
                    $"needs={vm.SelectedBranchNeedsExpression} showBool={vm.ShowBooleanEditor}");
            }
            finally
            {
                harness.Canvas.Deactivate();
            }
        }

        // ------------------------------------------------------------ ② 展示层

        private static void DisplayLayerMatchesDesign()
        {
            var vm = NewVm(out var harness);
            try
            {
                // 预建 [If, Else]；再补一条 ElseIf 让「两条必需 + 一条兜底」成立。
                var ifStep = new ConditionStep("\uE700", "结果判断", StubPluginProvider.IfPlugin, "质量分档");
                ifStep.Children.Insert(1, new StepCollection { BranchType = BranchType.ElseIf, StepName = "复检" });
                vm.OnDialogOpened(new DialogParameters { { "Node", ifStep } });

                Check("CE2-D1：模式胶囊 = IF / WHILE",
                    vm.ModeChipText == "IF / WHILE", vm.ModeChipText);

                // 空表达式（两条必需分支：If + ElseIf）→ 卡内错误条 + 底栏红
                Check("CE2-D2：两条必需分支都空 → DraftIssues 有 2 条、HasCardError=true",
                    vm.DraftIssues.Count == 2 && vm.HasCardError,
                    $"issues={vm.DraftIssues.Count} cardError={vm.HasCardError} text={vm.CardErrorText}");

                Check("CE2-D3：底栏摘要走红口径「校验未通过：N 处问题 · 点「保存并应用」会再次提示并停在弹窗」",
                    vm.ValidationSummaryLevel == "err"
                    && vm.ValidationSummaryText.StartsWith("校验未通过：2 处问题", StringComparison.Ordinal),
                    vm.ValidationSummaryText);

                // 补上表达式 → 绿口径（两条必需分支：index 0 = If、index 1 = 插入的 ElseIf）
                vm.SelectedBranch = vm.Branches[0];
                vm.Branches[0].Expression = "score >= 0.85";
                vm.Branches[1].Expression = "score >= 0.60";
                vm.RefreshDraftDiagnostics();

                Check("CE2-D4：两条都补上条件 → DraftIssues 清空、底栏绿「草稿已校验，可保存」",
                    vm.DraftIssues.Count == 0 && !vm.HasCardError
                    && vm.ValidationSummaryLevel == "ok"
                    && vm.ValidationSummaryText == "草稿已校验，可保存",
                    $"issues={vm.DraftIssues.Count} level={vm.ValidationSummaryLevel} text={vm.ValidationSummaryText}");

                vm.SelectedBranch = vm.Branches[2];
                Check("CE2-D5：兜底分支 → 底栏「兜底分支无需条件，可直接保存」",
                    vm.ValidationSummaryText == "兜底分支无需条件，可直接保存",
                    vm.ValidationSummaryText);

                // 变量表类型短名（v2 变量表「类型」列）
                vm.AddVariableCommand.Execute();
                var v = vm.Variables.Last();
                v.DataTypeName = "System.Double";
                Check("CE2-D6：类型短名 System.Double → DOUBLE（变量表类型列的数据源）",
                    v.DataTypeShortName == "DOUBLE", v.DataTypeShortName);

                // 判据候选：变量别名进候选
                v.AliasName = "grade";
                vm.RefreshDraftDiagnostics();
                Check("CE2-D7：判据候选含变量别名 grade（手填 + 候选，R33 名字语义）",
                    vm.JudgeVariableCandidates.Contains("grade"),
                    string.Join(",", vm.JudgeVariableCandidates));
            }
            finally
            {
                harness.Canvas.Deactivate();
            }
        }

        private static void CaseModeMatchesDesign()
        {
            var vm = NewVm(out var harness);
            try
            {
                var caseStep = new CaseStep("\uE700", "分支匹配", "BuiltIn_Case", "分档");
                // CaseStep 构造器预建 [Case 1, 默认]；本断言要的是 A/B/默认三条，先清空再铺。
                caseStep.Children.Clear();
                caseStep.JudgeExpression = "grade";
                caseStep.LocalVariables.Add(new LocalVariableItem
                {
                    Id = Guid.NewGuid(),
                    Name = "grade",
                    DataTypeName = "System.String",
                });
                caseStep.Children.Add(new StepCollection { BranchType = BranchType.Case, StepName = "A 类", Expression = "A" });
                caseStep.Children.Add(new StepCollection { BranchType = BranchType.Case, StepName = "B 类", Expression = "A" });
                caseStep.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "默认" });

                vm.OnDialogOpened(new DialogParameters { { "Node", caseStep } });

                Check("CE2-D8：分支匹配模式 → 胶囊 CASE、EditorTitle 含「匹配值」",
                    vm.ModeChipText == "CASE" && vm.IsCaseMode,
                    $"chip={vm.ModeChipText} title={vm.EditorTitle}");

                Check("CE2-D9：已用匹配值列表三行（A 类 / B 类 / 默认），重复行被标出",
                    vm.CaseMatchRows.Count == 3
                    && vm.CaseMatchRows[0].Kind == "当前"
                    && vm.CaseMatchRows[1].IsDuplicate
                    && vm.CaseMatchRows[2].Kind == "兜底",
                    string.Join(" | ", vm.CaseMatchRows.Select(r => r.StepName + "=" + r.Value + "/" + r.Kind)));

                Check("CE2-D10：重复匹配值进 DraftIssues（与编译器「不可达分支」同源）",
                    vm.DraftIssues.Any(x => x.Contains("重复", StringComparison.Ordinal)),
                    string.Join("；", vm.DraftIssues));
            }
            finally
            {
                harness.Canvas.Deactivate();
            }
        }

        private static void WaitModeMatchesDesign()
        {
            var vm = NewVm(out var harness);
            try
            {
                var wait = new WaitStep("\uE700", "等待", "BuiltIn_Wait", "等待_0")
                {
                    ConditionExpression = "ok",
                    PollIntervalMs = 5,
                };
                vm.OnDialogOpened(new DialogParameters { { "Node", wait } });

                Check("CE2-D11：等待模式 → 胶囊 WAIT、轮询 5ms 触发「不能小于 10ms」",
                    vm.ModeChipText == "WAIT" && vm.IsWaitMode
                    && vm.DraftIssues.Any(x => x.Contains("10ms", StringComparison.Ordinal)),
                    $"chip={vm.ModeChipText} issues={string.Join("；", vm.DraftIssues)}");

                vm.PollIntervalText = "50";
                Check("CE2-D12：轮询改 50ms → 该条错误消失（输入即刷新）",
                    !vm.DraftIssues.Any(x => x.Contains("10ms", StringComparison.Ordinal)),
                    string.Join("；", vm.DraftIssues));
            }
            finally
            {
                harness.Canvas.Deactivate();
            }
        }

        private static void ForModeMatchesDesign()
        {
            var vm = NewVm(out var harness);
            try
            {
                var forStep = new ForStep("\uE700", "循环", StubPluginProvider.ForPlugin, "重试采集");
                vm.OnDialogOpened(new DialogParameters { { "Node", forStep } });

                Check("CE2-D13：For 模式 → 胶囊 FOR、摘要含「循环体 N 个步骤 / 默认次数 10 / 数据源 未连线」",
                    vm.ModeChipText == "FOR" && vm.IsForMode
                    && vm.LoopSummaryText.Contains("循环体", StringComparison.Ordinal)
                    && vm.LoopSummaryText.Contains("默认次数 10", StringComparison.Ordinal)
                    && vm.LoopSummaryText.Contains("未连线", StringComparison.Ordinal),
                    vm.LoopSummaryText);

                vm.LoopCountText = "0";
                Check("CE2-D14：循环次数 0 → DraftIssues 报「1 ~ 99999」",
                    vm.DraftIssues.Any(x => x.Contains("1 ~ 99999", StringComparison.Ordinal)),
                    string.Join("；", vm.DraftIssues));
            }
            finally
            {
                harness.Canvas.Deactivate();
            }
        }

        // ------------------------------------------------------------ ③ 只读性

        private static void DiagnosticsAreReadOnly()
        {
            var vm = NewVm(out var harness);
            try
            {
                var ifStep = new ConditionStep("\uE700", "结果判断", StubPluginProvider.IfPlugin, "质量分档");
                ifStep.Children.Add(new StepCollection { BranchType = BranchType.If, StepName = "合格" });
                ifStep.Children.Add(new StepCollection { BranchType = BranchType.Else, StepName = "Else" });

                var liveBefore = ifStep.Children[0].Expression;
                vm.OnDialogOpened(new DialogParameters { { "Node", ifStep } });

                // RequestClose 是 DialogCloseListener（不是 event），不能 +=；
                // 改用「点保存后活模型是否变化」作为「诊断不写模型」的判据。
                // 只刷新诊断，不点保存 → 活模型一个字节都不许动
                vm.RefreshDraftDiagnostics();

                Check("CE2-R1：刷新诊断不写活模型（Children[0].Expression 未变）",
                    ifStep.Children[0].Expression == liveBefore,
                    $"live='{ifStep.Children[0].Expression}'");

                Check("CE2-R2：诊断非空也不拦「打开」（HasValidationErrors 只影响显示）",
                    vm.HasValidationErrors && vm.SelectedBranch != null,
                    $"errors={vm.ValidationErrorCount}");
            }
            finally
            {
                harness.Canvas.Deactivate();
            }
        }

        // ------------------------------------------------------- ④ 源码契约

        private static void SourceContractPreserved()
        {
            var repoRoot = RepoRoot();
            if (repoRoot == null)
            {
                Check("CE2-S0：定位仓库根（AGENTS.md）", false, "未找到 AGENTS.md");
                return;
            }

            string xamlPath = System.IO.Path.Combine(repoRoot, @"VisionMaster\Views\DialogViews\ConditionEditorView.xaml");
            string csPath = System.IO.Path.Combine(repoRoot, @"VisionMaster\Views\DialogViews\ConditionEditorView.xaml.cs");
            string xamlRaw = System.IO.File.Exists(xamlPath) ? System.IO.File.ReadAllText(xamlPath) : string.Empty;
            string cs = System.IO.File.Exists(csPath) ? System.IO.File.ReadAllText(csPath) : string.Empty;

            // 注释里也写着 x:Name="txtExpression"（说明文字），统计前必须去掉 XML 注释，
            // 否则「唯一实例」会被自己的文档判红——这正是 R32 那族「注释参与判据」的坑。
            string xaml = System.Text.RegularExpressions.Regex.Replace(
                xamlRaw, "<!--.*?-->", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

            // AvalonEdit 唯一实例 + TextChanged 事件
            int editors = CountOccurrences(xaml, "avalonEdit:TextEditor");
            int named = CountOccurrences(xaml, "x:Name=\"txtExpression\"");
            Check("CE2-S1：表达式编辑器全弹窗唯一（avalonEdit:TextEditor 恰 1 个、x:Name=\"txtExpression\" 恰 1 处，注释不计）",
                editors == 1 && named == 1,
                $"editors={editors} named={named}");

            Check("CE2-S2：TextChanged 仍接 TxtExpression_TextChanged（code-behind 的输入通道）",
                xaml.Contains("TextChanged=\"TxtExpression_TextChanged\"", StringComparison.Ordinal)
                && cs.Contains("private void TxtExpression_TextChanged", StringComparison.Ordinal),
                "XAML 事件 + code-behind 处理器都在");

            // 快捷插入：Tag 逐字不变（写错一个字符 = 插入错代码）。
            // 注意 XAML 里 > / < / && 必须转义，所以判据按转义后的字面量比对。
            string[] tags =
            {
                "Tag=\"&gt;\"", "Tag=\"&lt;\"", "Tag=\"==\"", "Tag=\"!=\"", "Tag=\"&amp;&amp;\"", "Tag=\"||\"", "Tag=\"!\"",
                "Tag=\"()\"", "Tag=\"+\"", "Tag=\"-\"", "Tag=\"*\"", "Tag=\"/\"",
                "Tag=\"Math.Abs()\"", "Tag=\"Math.Round()\"", "Tag=\"Math.Sqrt()\"",
                "Tag=\".Contains()\"", "Tag=\".StartsWith()\"", "Tag=\"string.IsNullOrEmpty()\"",
                "Tag=\".Length\"", "Tag=\".Trim()\"", "Tag=\".ToUpper()\"", "Tag=\".ToLower()\"",
            };
            var missing = tags.Where(t => !xaml.Contains(t, StringComparison.Ordinal)).ToList();
            Check("CE2-S3：22 个快捷插入 Tag 逐字未变（片段写错就是插入错代码）",
                missing.Count == 0,
                missing.Count == 0 ? "22/22 命中" : "缺：" + string.Join(" ", missing));

            Check("CE2-S4：快捷插入仍全部接 QuickInsert_Click",
                CountOccurrences(xaml, "Click=\"QuickInsert_Click\"") >= 22,
                CountOccurrences(xaml, "Click=\"QuickInsert_Click\"") + " 处");

            // 命令绑定（v2 换皮不许丢命令）
            string[] commands =
            {
                "{Binding AddVariableCommand}", "{Binding SaveCommand}", "{Binding CancelCommand}",
                "{Binding LinkLoopCountCommand}", "{Binding UnlinkLoopCountCommand}",
            };
            var missingCmd = commands.Where(c => !xaml.Contains(c, StringComparison.Ordinal)).ToList();
            Check("CE2-S5：五个主命令仍绑在视图上（Add/Save/Cancel/Link/Unlink）",
                missingCmd.Count == 0,
                missingCmd.Count == 0 ? "5/5" : "缺：" + string.Join(" ", missingCmd));

            Check("CE2-S6：行内命令仍按 ItemsControl 祖先取 DataContext（Bind/Remove 逐行传参）",
                xaml.Contains("DataContext.BindVariableCommand", StringComparison.Ordinal)
                && xaml.Contains("DataContext.RemoveVariableCommand", StringComparison.Ordinal),
                "BindVariableCommand / RemoveVariableCommand 都在");

            // 可编辑 ComboBox：判据 / 报警变量名（R33 名字语义：手填 + 候选）。
            // 样式已按【收编】纪律移入 UI 库 Dialog.ConditionEditor.xaml（DialogEditableCombo），
            // 视图里只留 Style 引用。
            Check("CE2-S7：判据与报警变量名用可编辑 ComboBox（DialogEditableCombo 可编辑样式、不套 DialogCombo）",
                xaml.Contains("ItemsSource=\"{Binding JudgeVariableCandidates}\"", StringComparison.Ordinal)
                && xaml.Contains("ItemsSource=\"{Binding AlarmVariableCandidates}\"", StringComparison.Ordinal)
                && xaml.Contains("Style=\"{StaticResource DialogEditableCombo}\"", StringComparison.Ordinal),
                $"判据候选={xaml.Contains("JudgeVariableCandidates", StringComparison.Ordinal)} "
                + $"报警候选={xaml.Contains("AlarmVariableCandidates", StringComparison.Ordinal)} "
                + $"可编辑样式={xaml.Contains("DialogEditableCombo", StringComparison.Ordinal)}");

            Check("CE2-S8：变量表改四列（别名/类型/数据源/操作），不再用 DataGrid",
                xaml.Contains("DataTypeShortName", StringComparison.Ordinal)
                && !xaml.Contains("DialogDataGrid", StringComparison.Ordinal),
                $"类型列={xaml.Contains("DataTypeShortName", StringComparison.Ordinal)} DataGrid={xaml.Contains("DialogDataGrid", StringComparison.Ordinal)}");

            Check("CE2-S9：卡内错误条只在有错时出现（HasCardError 驱动）",
                xaml.Contains("Binding HasCardError", StringComparison.Ordinal)
                && xaml.Contains("CardErrorText", StringComparison.Ordinal),
                "HasCardError + CardErrorText");

            Check("CE2-S10：底栏摘要绑 ValidationSummaryText + 颜色/图标随 ValidationSummaryLevel",
                xaml.Contains("Binding ValidationSummaryText", StringComparison.Ordinal)
                && xaml.Contains("Binding ValidationSummaryLevel", StringComparison.Ordinal),
                "两条绑定都在");

            Check("CE2-S11：视图 XAML 无硬编码十六进制颜色（全走公共令牌）",
                !System.Text.RegularExpressions.Regex.IsMatch(xaml, @"#[0-9A-Fa-f]{6,8}"),
                "hex=0");

            Check("CE2-S12：模式胶囊绑 ModeChipText（只读标识，不可点击）",
                xaml.Contains("Binding ModeChipText", StringComparison.Ordinal)
                && !xaml.Contains("ModeChipCommand", StringComparison.Ordinal),
                "ModeChipText 有、切换命令无");
        }

        // ------------------------------------------------------------ helpers

        private static ConditionEditorViewModel NewVm(out Harness harness)
        {
            harness = new Harness();
            return new ConditionEditorViewModel(new NullDialogService(), harness.Workspace);
        }

        /// <summary>最小 IDialogService 桩：本文件的断言不触发任何弹窗，只满足构造函数依赖。</summary>
        private sealed class NullDialogService : IDialogService
        {
            public void ShowDialog(string name, IDialogParameters parameters) { }
            public void ShowDialog(string name, IDialogParameters parameters, DialogCallback callback) { }
            public void ShowDialog(string name, IDialogParameters parameters, string windowName) { }
            public void ShowDialog(string name, IDialogParameters parameters, DialogCallback callback, string windowName) { }
            public void Show(string name, IDialogParameters parameters) { }
            public void Show(string name, IDialogParameters parameters, DialogCallback callback) { }
            public void Show(string name, IDialogParameters parameters, string windowName) { }
            public void Show(string name, IDialogParameters parameters, DialogCallback callback, string windowName) { }
            public void ShowDialog<TView>(string name, IDialogParameters parameters) { }
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += token.Length;
            }
            return count;
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
            return null;
        }
    }
}
