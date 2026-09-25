using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using Prism.Dialogs;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「变量绑定」窗口（VariableBindingView / VariableBindingViewModel）的断言。
    ///
    /// 为什么值得单独钉住
    /// ---------
    /// 这个窗口的确定逻辑是「常量框非空就写常量」，而它**不管用户有没有换过端口**。
    /// 于是只要换端口时不重置常量框，就会出现：
    ///   选中 A 端口填了值 → 又去点 B 端口看看 → 点确定 → A 的值被写到 B 上。
    /// 界面上一切正常，被写坏的是另一个端口的绑定 —— 事后极难定位。
    /// 同时反过来也危险：端口绑的是上游连线时，若把连线文本回显进常量框，
    /// 点确定就会把连线改写成常量，等于悄悄把线拆了。
    ///
    /// 这两条都只有真跑一遍 VM 才看得出来，所以这里直接驱动 ViewModel（不碰 WPF 控件）。
    /// </summary>
    internal static class VariableBindingCheck
    {
        /// <summary>断言用的「变量赋值」类型名（只在本用例里注册，避免污染画布断言用的桩插件）</summary>
        private const string AssignType = "VM.BindingCheck.VariableAssignment";

        public static void Run()
        {
            Section("[V] 变量绑定窗口：回显当前值 / 换端口不串值 / 不把连线当常量");

            var provider = new StubPluginProvider();
            provider.RegisterModule(new ToolItemModel
            {
                Name = "变量赋值",
                ModuleTypeName = AssignType,
                Category = "变量操作",
                Description = "断言用变量赋值",
                InputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "Name", Description = "目标变量名称", DataTypeName = "System.String" },
                    new() { Name = "Value", Description = "要赋的值", DataTypeName = "System.Object" },
                    new() { Name = "CreateIfNotExists", Description = "变量不存在时是否自动创建", DataTypeName = "System.Boolean" },
                },
            });

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "绑定窗口断言" };
            var flow = new FlowModel { FlowName = "绑定断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            // 三个端口各代表一种"值从哪来"，这正是最容易搞错的地方：
            //   Name              → InputValues（手填常量，插件 OnConfirm 写回的那份 —— 真实方案就是这个）
            //   Value             → LinkedSources 里的常量连线（在绑定窗口里填过常量）
            //   CreateIfNotExists → LinkedSources 里的上游端口连线（必须清空，不能回显）
            var step = new ActionStep("", "变量赋值", AssignType, "赋值_Expected");
            step.SetInputValue("Name", "Expected");
            step.LinkedSources["Value"] = Constant("黑,棕,玫红,红,黄");
            step.LinkedSources["CreateIfNotExists"] = new LinkReference(
                LinkKind.StepPort, Guid.NewGuid(), "Out", "上游_0.Out");
            flow.Steps.Add(step);

            // 前提断言：Name 走的必须是 InputValues 这条路。
            // 上一版用例把三个端口都造成"常量连线"，于是绕开了真实路径 —— 代码明明不对却全绿。
            Check("用例前提：Name 的值存在 InputValues 里（不是 LinkedSources）",
                !step.LinkedSources.ContainsKey("Name") && step.InputValues.ContainsKey("Name"),
                $"LinkedSources 有 Name = {step.LinkedSources.ContainsKey("Name")}；"
                + $"InputValues 有 Name = {step.InputValues.ContainsKey("Name")}");

            var vm = new VariableBindingViewModel(workspace, provider);
            vm.OnDialogOpened(new DialogParameters
            {
                { "IsSingleBindMode", false },
                { "TargetStep", step },
            });

            Check("弹窗打开后，默认选中端口的手填值被回显出来",
                vm.ConstantValue == "Expected", $"ConstantValue=[{vm.ConstantValue}]");

            vm.SelectedInputPort = vm.DisplayDataPort.FirstOrDefault(p => p.Definition.Name == "Value");
            Check("换到「常量连线」的端口：回显连线里的常量",
                vm.ConstantValue == "黑,棕,玫红,红,黄", $"ConstantValue=[{vm.ConstantValue}]");

            vm.SelectedInputPort = vm.DisplayDataPort.FirstOrDefault(p => p.Definition.Name == "CreateIfNotExists");
            Check("换到「绑的是上游端口」的端口：常量框必须清空（否则点确定会把连线改写成常量）",
                vm.ConstantValue == "", $"ConstantValue=[{vm.ConstantValue}]");

            vm.SelectedInputPort = vm.DisplayDataPort.FirstOrDefault(p => p.Definition.Name == "Name");
            Check("再换回来值还在（回显可重复，不是一次性）",
                vm.ConstantValue == "Expected", $"ConstantValue=[{vm.ConstantValue}]");
        }

        /// <summary>造一个常量连线 —— 与 Confirm 写入时的构造方式保持一致</summary>
        private static LinkReference Constant(string value)
            => new(LinkKind.Constant, Guid.Empty, value, $"{LinkProtocol.ConstantDisplayPrefix}{value}");
    }
}
