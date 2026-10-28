using System;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [PD] 端口默认值随步骤创建落盘（<see cref="PortDefaultOnCreateAttribute"/>）。
    ///
    /// 【为什么有这组断言】2026-10-27 现象：拖「延时」进流程栏（默认 500ms）不打开参数窗点「确定」
    /// 就编译，报「[参数缺失] '延时_0' 的必填参数 'Delay' 未配置！」——因为端口默认值只活在
    /// 插件实例上，而编译期必填检查（FlowCompiler）只认 <c>StepModel.InputValues</c>，
    /// 写回 InputValues 的唯一常规入口是参数窗「确定」（VisionPluginBase.OnConfirm）。
    /// 修法是给端口标 [PortDefaultOnCreate]，由 StepFactory 在创建时落盘，等价于点过一次确定。
    ///
    /// 【反向断言是本体，不是陪衬】标了特性的端口落盘只是"正向"；真正要守住的是**没标的端口
    /// 不许落盘**——轴目标位置默认 0 会真走、IO 输出值 true 会真输出，这类必须人工确认。
    /// 只写正向断言的话，把 StepFactory 改成"所有端口默认值都落盘"也照样绿（R38）。
    /// </summary>
    internal static class StepDefaultOnCreateChecks
    {
        public static void Run()
        {
            Section("[PD] 端口默认值随创建落盘（[PortDefaultOnCreate]）");

            VerifyDelayPlugin();
            VerifyFlagIsPerPort();
            VerifyMotionStaysManual();
        }

        /// <summary>真插件正向：延时 500ms 创建即落盘，且能直接编译通过。</summary>
        private static void VerifyDelayPlugin()
        {
            var tool = new ToolItemModel
            {
                Name = "延时",
                ModuleTypeName = typeof(VisionMaster.Plugins.Util.DelayPlugin).AssemblyQualifiedName!,
                IsContainer = false,
            };
            var step = (ActionStep)StepFactory.CreateFromTool(tool, "延时_0");

            bool hasValue = step.InputValues.TryGetValue("Delay", out var value) && Equals(value, 500);
            Check("[PD1] 延时：创建即把默认 500ms 写进 InputValues（等价于点过一次确定）",
                hasValue,
                hasValue ? "Delay=500" : $"InputValues=[{string.Join(",", step.InputValues.Select(kv => kv.Key + "=" + kv.Value))}]");

            var compiled = ExecHarness.Prepare(new StepModel[] { step });
            Check("[PD2] 延时：创建后直接编译通过（不再报 [参数缺失]）",
                compiled.Result?.Success == true,
                compiled.Compiled ? "0 错误" : compiled.Errors);
        }

        /// <summary>
        /// 特性粒度：同一插件里标了的端口落盘、没标的不落盘。
        /// 用专用桩而不是真插件——真插件里"一个标一个不标"的组合会随产品改动漂移。
        /// </summary>
        private static void VerifyFlagIsPerPort()
        {
            var tool = new ToolItemModel
            {
                Name = "默认值桩",
                ModuleTypeName = typeof(DefaultOnCreateStubPlugin).AssemblyQualifiedName!,
                IsContainer = false,
            };
            var step = (ActionStep)StepFactory.CreateFromTool(tool, "默认值桩_0");

            Check("[PD3] 标了特性的端口落盘（Auto=500）",
                step.InputValues.TryGetValue("Auto", out var auto) && Equals(auto, 500),
                string.Join(",", step.InputValues.Select(kv => kv.Key + "=" + kv.Value)));

            // ★ 反向：同一实例上没标特性的端口，默认值 500 一样不许落盘
            Check("[PD4] 没标特性的端口不落盘（Manual 的 500 只留在插件实例上）",
                !step.InputValues.ContainsKey("Manual"),
                step.InputValues.ContainsKey("Manual") ? "Manual 被误落盘" : "Manual 未落盘");

            // ★ 反向的行为后果：未落盘的必填端口仍由编译期判缺
            var compiled = ExecHarness.Prepare(new StepModel[] { step });
            Check("[PD5] 未标特性的必填端口仍报 [参数缺失]（特性不是「全局放开」）",
                compiled.Result?.Success == false
                    && compiled.Errors.Contains("参数缺失")
                    && compiled.Errors.Contains("Manual"),
                compiled.Compiled ? "竟然编译通过了" : compiled.Errors);
        }

        /// <summary>
        /// 运动类反向：轴运动的 Target / Velocity 默认 0，落盘就等于"拖进来就真走"。
        /// 本组断言是"运动控制必须人工确认"这条产品规则的守门人。
        /// </summary>
        private static void VerifyMotionStaysManual()
        {
            var tool = new ToolItemModel
            {
                Name = "轴运动",
                ModuleTypeName = typeof(Plugin.Motion.Steps.MotionMovePlugin).AssemblyQualifiedName!,
                IsContainer = false,
            };
            var step = (ActionStep)StepFactory.CreateFromTool(tool, "轴运动_0");

            Check("[PD6] 轴运动：Target / Velocity / Mode 不被自动落盘（默认 0 会真走）",
                !step.InputValues.ContainsKey("Target")
                    && !step.InputValues.ContainsKey("Velocity")
                    && !step.InputValues.ContainsKey("Mode"),
                string.Join(",", step.InputValues.Select(kv => kv.Key + "=" + kv.Value)));

            var compiled = ExecHarness.Prepare(new StepModel[] { step });
            Check("[PD7] 轴运动：未人工确认时编译仍报 [参数缺失]",
                compiled.Result?.Success == false
                    && compiled.Errors.Contains("参数缺失")
                    && compiled.Errors.Contains("Target"),
                compiled.Compiled ? "竟然编译通过了" : compiled.Errors);
        }
    }

    /// <summary>
    /// [PD3]/[PD4] 专用桩：一个端口标特性、一个不标，两个默认值都是 500。
    /// 走 FlowCanvasChecks 程序集，AssemblyQualifiedName 能被 Type.GetType 真解析。
    /// </summary>
    internal sealed class DefaultOnCreateStubPlugin : VisionPluginBase
    {
        [PortDefaultOnCreate]
        public InputPort<int> Auto { get; } = new("Auto", 500, "标了特性：创建时落盘");

        public InputPort<int> Manual { get; } = new("Manual", 500, "没标特性：必须人工确认");

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 断言桩不跑真算法
        }
    }
}
