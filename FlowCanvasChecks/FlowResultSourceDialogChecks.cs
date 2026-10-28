using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [DLG] 「运行统计 · 结果源」配置弹窗：候选口径 / 校验 / 保存 / 只读态。
    ///
    /// 弹窗只写 <see cref="FlowModel"/> 的结果源字段（不改流程图），所以这里全部是
    /// 纯 VM 断言：不加载 WPF、不弹窗、不依赖登录会话（权限走 IFlowConfigAccess 桩）。
    /// </summary>
    internal static class FlowResultSourceDialogChecks
    {
        public static void Run()
        {
            Program.Section("[DLG] 结果源配置弹窗：候选 / 校验 / 保存 / 只读");

            ModesAndDefaults();
            VariableCandidates();
            VariableValidationAndSave();
            StepPortCandidatesAndSave();
            BranchStepsAreListed();
            StalePortIsCleared();
            AutoKeepsFallbackConfig();
            NoneOnMainWarns();
            MissingResultStepWarns();
            ReadOnlyBlocksSave();
        }

        // ---------------------------------------------------------------- ① 模式

        private static void ModesAndDefaults()
        {
            var (vm, flow, _) = NewVm();

            Program.Check("DLG-1 五种模式齐全、默认自动（推荐）",
                vm.Modes.Count == 5
                && vm.Modes[0].Kind == FlowResultSourceKind.Auto
                && vm.Modes[0].IsRecommended
                && vm.SelectedMode?.Kind == FlowResultSourceKind.Auto
                && vm.IsAutoMode,
                $"count={vm.Modes.Count} selected={vm.SelectedMode?.Kind} auto={vm.IsAutoMode}");

            Program.Check("DLG-2 标题带流程名与角色",
                vm.FlowTitle.Contains(flow.FlowName) && vm.FlowTitle.Contains("普通流程"),
                vm.FlowTitle);
        }

        // ---------------------------------------------------------------- ② 变量候选

        private static void VariableCandidates()
        {
            var (vm, _, workspace) = NewVm();

            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("DLG_Text", typeof(string), "", "OK"));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("DLG_Flag", typeof(bool), "", true));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("DLG_Score", typeof(double), "", 1.5));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("DLG_When", typeof(DateTime), "", DateTime.Now));

            vm.OnDialogOpened(NewParams("DLG_工位"));

            Program.Check("DLG-3 变量候选只列 bool/string/int/long/double（DateTime 被排除）",
                vm.Variables.Any(v => v.Name == "DLG_Flag")
                && vm.Variables.Any(v => v.Name == "DLG_Text")
                && vm.Variables.Any(v => v.Name == "DLG_Score")
                && !vm.Variables.Any(v => v.Name == "DLG_When"),
                string.Join(",", vm.Variables.Select(v => v.Name + ":" + v.TypeText)));

            Program.Check("DLG-4 bool 排在最前（Q2：bool 是默认选择）",
                vm.Variables.Count > 0 && vm.Variables[0].TypeText == "bool",
                vm.Variables.Count == 0 ? "<空>" : vm.Variables[0].Name + ":" + vm.Variables[0].TypeText);
        }

        private static void VariableValidationAndSave()
        {
            var (vm, flow, workspace) = NewVm();
            var flag = VariableFactory.CreateLocal("DLG_Flag", typeof(bool), "", false);
            workspace.GlobalVariables.Add(flag);
            vm.OnDialogOpened(NewParams("DLG_工位"));

            vm.SelectedMode = vm.Modes.First(m => m.Kind == FlowResultSourceKind.Variable);
            vm.SelectedVariable = null;

            Program.Check("DLG-5 变量模式未选变量 → 阻断保存并给原因",
                !vm.CanSave && vm.ValidationText == "请选择结果变量" && vm.HasVariableError,
                $"canSave={vm.CanSave} text={vm.ValidationText}");

            vm.SelectedVariable = vm.Variables.First(v => v.VariableId == flag.VariableId);
            Program.Check("DLG-6 选中变量 → 可保存、摘要带当前值",
                vm.CanSave && vm.CurrentSummary.Contains("DLG_Flag"),
                $"canSave={vm.CanSave} summary={vm.CurrentSummary}");

            bool applied = vm.Apply();

            Program.Check("DLG-7 保存写回 ResultSource / 变量 Id / 变量名",
                applied
                && flow.ResultSource == FlowResultSourceKind.Variable
                && flow.ResultVariableId == flag.VariableId
                && flow.ResultVariableName == "DLG_Flag",
                $"applied={applied} source={flow.ResultSource} id={flow.ResultVariableId} name={flow.ResultVariableName}");
        }

        // ---------------------------------------------------------------- ③ 步骤端口

        private static void StepPortCandidatesAndSave()
        {
            var (vm, flow, _) = NewVm();
            vm.OnDialogOpened(NewParams("DLG_工位"));

            var judge = vm.Steps.First(s => s.StepName == "判定表");
            vm.SelectedMode = vm.Modes.First(m => m.Kind == FlowResultSourceKind.StepPort);
            vm.SelectedStep = judge;

            Program.Check("DLG-8 端口候选只列 bool，且排除基类 Success",
                vm.Ports.Count == 1 && vm.Ports[0].Name == "IsOk",
                string.Join(",", vm.Ports.Select(p => p.Name + ":" + p.TypeText)));

            Program.Check("DLG-9 只选步骤未选端口 → 阻断保存",
                !vm.CanSave && vm.ValidationText == "请选择 bool 输出端口" && vm.HasPortError,
                $"canSave={vm.CanSave} text={vm.ValidationText}");

            vm.SelectedPort = vm.Ports[0];
            bool applied = vm.Apply();

            Program.Check("DLG-10 保存写回步骤 Id 与端口名",
                applied
                && flow.ResultSource == FlowResultSourceKind.StepPort
                && flow.ResultStepId == judge.StepId
                && flow.ResultPortName == "IsOk",
                $"applied={applied} step={flow.ResultStepId} port={flow.ResultPortName}");
        }

        private static void BranchStepsAreListed()
        {
            var (vm, _, _) = NewVm();
            vm.OnDialogOpened(NewParams("DLG_工位"));

            var branchStep = vm.Steps.FirstOrDefault(s => s.StepName == "判定表");
            Program.Check("DLG-11 分支内步骤照样列出并标记（Q5：允许选）",
                branchStep != null && branchStep.IsBranch,
                branchStep == null ? "<未列出>" : branchStep.StepName + " branch=" + branchStep.IsBranch);
        }

        private static void StalePortIsCleared()
        {
            var (vm, _, _) = NewVm();
            vm.OnDialogOpened(NewParams("DLG_工位"));

            vm.SelectedMode = vm.Modes.First(m => m.Kind == FlowResultSourceKind.StepPort);
            vm.SelectedStep = vm.Steps.First(s => s.StepName == "判定表");
            vm.SelectedPort = vm.Ports.First();

            vm.SelectedStep = vm.Steps.First(s => s.StepName == "空算子");

            Program.Check("DLG-12 换到没有 bool 端口的步骤 → 旧端口被清掉（不会留一个看不见的选中值）",
                vm.SelectedPort == null && vm.Ports.Count == 0 && !vm.CanSave,
                $"port={(vm.SelectedPort == null ? "<null>" : vm.SelectedPort.Name)} ports={vm.Ports.Count} canSave={vm.CanSave}");
        }

        // ---------------------------------------------------------------- ④ 回退与警告

        private static void AutoKeepsFallbackConfig()
        {
            var (vm, flow, workspace) = NewVm();
            var flag = VariableFactory.CreateLocal("DLG_Flag", typeof(bool), "", true);
            workspace.GlobalVariables.Add(flag);
            flow.ResultSource = FlowResultSourceKind.Auto;
            flow.ResultVariableId = flag.VariableId;
            flow.ResultVariableName = flag.Name;
            vm.OnDialogOpened(NewParams("DLG_工位"));

            Program.Check("DLG-13 自动模式预选已配置的变量（回退链成员）",
                vm.SelectedVariable != null && vm.SelectedVariable.VariableId == flag.VariableId,
                vm.SelectedVariable?.Name ?? "<null>");

            vm.Apply();
            Program.Check("DLG-14 自动模式保存不清空变量配置（切回变量模式不丢）",
                flow.ResultSource == FlowResultSourceKind.Auto
                && flow.ResultVariableId == flag.VariableId
                && flow.ResultVariableName == "DLG_Flag",
                $"source={flow.ResultSource} id={flow.ResultVariableId}");
        }

        private static void NoneOnMainWarns()
        {
            var (vm, flow, workspace) = NewVm();
            // 2026-10-28 起没有"主流程角色"：线级汇总来源是方案级显式配置
            workspace.CurrentSolution.LineVerdictFlowId = flow.FlowID;
            vm.OnDialogOpened(NewParams("DLG_工位"));

            vm.SelectedMode = vm.Modes.First(m => m.Kind == FlowResultSourceKind.None);

            Program.Check("DLG-15 线级汇总来源选「不参与」→ 允许保存但给警告（Q3）",
                vm.CanSave && vm.HasWarning && vm.WarningText.Contains("线级汇总来源")
                && vm.IsLineVerdictSource,
                $"canSave={vm.CanSave} warning={vm.WarningText} isLine={vm.IsLineVerdictSource}");
        }

        private static void MissingResultStepWarns()
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            var flow = new FlowModel { FlowName = "DLG_无结果步骤", IsEnabled = true };
            flow.Steps.Add(new ActionStep("J", "判定表", "stub.judge", "判定表"));
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            var vm = new FlowResultSourceDialogViewModel(workspace, new DialogPluginProvider(), new AllowAccess());
            vm.OnDialogOpened(NewParams(flow.FlowName));

            Program.Check("DLG-16 没有结果输出步骤 → 自动模式给琥珀提示、不阻断（Q4）",
                !vm.HasResultStep && vm.CanSave && vm.HasWarning,
                $"hasStep={vm.HasResultStep} canSave={vm.CanSave} warning={vm.WarningText}");
        }

        // ---------------------------------------------------------------- ⑤ 只读

        private static void ReadOnlyBlocksSave()
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            var flow = new FlowModel { FlowName = "DLG_只读", IsEnabled = true };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            var vm = new FlowResultSourceDialogViewModel(workspace, new DialogPluginProvider(), new DenyAccess());
            vm.OnDialogOpened(NewParams(flow.FlowName));

            Program.Check("DLG-17 无编辑权限 → 只读态、保存禁用、文案可上屏",
                vm.IsReadOnly && !vm.IsEditable && !vm.CanSave
                && vm.ReadOnlyText.StartsWith("只读：", StringComparison.Ordinal),
                $"readonly={vm.IsReadOnly} editable={vm.IsEditable} text={vm.ReadOnlyText}");
        }

        // ---------------------------------------------------------------- 夹具

        private static (FlowResultSourceDialogViewModel Vm, FlowModel Flow, WorkspaceContext Workspace) NewVm()
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();

            var flow = new FlowModel { FlowName = "DLG_工位", IsEnabled = true };
            flow.Steps.Add(new ActionStep("R", "结果输出", "stub.result", "结果输出"));

            // 分支容器：判定表放在第一条分支里（Q5：分支步骤也允许选）
            var ifStep = new ConditionStep("I", "条件判断", "BuiltIn_If", "If_0");
            ifStep.Children[0].Steps.Add(new ActionStep("J", "判定表", "stub.judge", "判定表"));
            flow.Steps.Add(ifStep);

            flow.Steps.Add(new ActionStep("N", "空算子", "stub.none", "空算子"));

            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            var vm = new FlowResultSourceDialogViewModel(workspace, new DialogPluginProvider(), new AllowAccess());
            vm.OnDialogOpened(NewParams(flow.FlowName));
            return (vm, flow, workspace);
        }

        private static DialogParameters NewParams(string flowName)
            => new() { { FlowResultSourceDialogViewModel.FlowNameKey, flowName } };

        private sealed class AllowAccess : IFlowConfigAccess
        {
            public bool CanEdit(out string reason)
            {
                reason = string.Empty;
                return true;
            }
        }

        private sealed class DenyAccess : IFlowConfigAccess
        {
            public bool CanEdit(out string reason)
            {
                reason = "需要「编辑流程结构」权限";
                return false;
            }
        }

        /// <summary>只服务本组断言的最小插件目录：结果输出 / 判定表 / 空算子。</summary>
        private sealed class DialogPluginProvider : IPluginProvider
        {
            private readonly Dictionary<string, ToolItemModel> _modules = new()
            {
                ["stub.result"] = new ToolItemModel
                {
                    Name = "结果输出",
                    ModuleTypeName = "stub.result",
                    OutputDefinitions = new List<PortDefinition>
                    {
                        new() { Name = "IsOk", DataTypeName = "System.Boolean" },
                        new() { Name = "Reason", DataTypeName = "System.String" },
                    },
                },
                ["stub.judge"] = new ToolItemModel
                {
                    Name = "判定表",
                    ModuleTypeName = "stub.judge",
                    OutputDefinitions = new List<PortDefinition>
                    {
                        new() { Name = "IsOk", DataTypeName = "System.Boolean" },
                        new() { Name = "Count", DataTypeName = "System.Int32" },
                        // 基类端口：弹窗必须排除（技术状态不是产品判定）
                        new() { Name = "Success", DataTypeName = "System.Boolean" },
                    },
                },
                ["stub.none"] = new ToolItemModel
                {
                    Name = "空算子",
                    ModuleTypeName = "stub.none",
                    OutputDefinitions = new List<PortDefinition>
                    {
                        new() { Name = "Out", DataTypeName = "System.Double" },
                    },
                },
            };

            public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins => _modules;
            public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins { get; } = new Dictionary<string, ToolItemModel>();
            public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins { get; } = new Dictionary<string, ToolItemModel>();
            public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins { get; } = new Dictionary<string, ToolItemModel>();

            public void RegisterModule(ToolItemModel plugin) { }
            public void RegisterCamera(ToolItemModel plugin) { }
            public void RegisterLaser(ToolItemModel plugin) { }
            public void RegisterMotion(ToolItemModel plugin) { }

            public ToolItemModel GetModule(string name) => _modules.TryGetValue(name, out var v) ? v : null!;
            public ToolItemModel GetCamera(string name) => null!;
            public ToolItemModel GetLaser(string name) => null!;
            public ToolItemModel GetMotion(string name) => null!;
        }
    }
}
