using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [E20] 分支匹配（Case）容器的断言：
    ///  · 语义：按顺序比对、命中第一条即执行该分支、都不命中走默认分支（不穿透）；
    ///  · 判据来源：局部变量别名 / 运行时变量名两条路；
    ///  · 类型：值按判据声明类型归一（数值 / 字符串等值类型），不匹配在编译期拦下；
    ///  · 硬错误：空判据 / 判据未声明 / 空匹配值 / 重复值 / 混装 / 不可达 / 无 Case 分支；
    ///  · 版本链：改判据（语义属性）必须递增 FlowModel.Version（否则跑的还是旧编译产物）。
    ///
    /// 编译成的是同一个 CompiledIfNode —— "首个为真即选中"由既有 If 机器保证，
    /// 本组只验 Case 特有的编译期合成（「判据 == 常量」）与校验，不重测 If 的执行语义。
    /// </summary>
    internal static class CaseBranchChecks
    {
        internal static void Run()
        {
            FirstMatchWinsAndFallback();
            StringJudgeAndValueType();
            CompileTimeGuards();
            JudgeVersionChain();
        }

        private static string Aqn<T>() => typeof(T).AssemblyQualifiedName!;

        /// <summary>
        /// 造一张"判据 = 运行时变量 loopN"的分支匹配图纸：
        /// LoopVarPlugin 先写入 loopN，容器按 [Case 1: 1] [Case 2: 2] [默认] 分档执行。
        /// </summary>
        private static (CaseStep caseStep, ExecRun run) BuildRuntimeJudgeFlow()
        {
            var producer = new ActionStep("V", "变量定义", Aqn<LoopVarPlugin>(), "写 loopN");

            var caseStep = new CaseStep("K", "分支匹配", "BuiltIn_Case", "按 loopN 分档");
            caseStep.JudgeExpression = "loopN";
            caseStep.RuntimeVariableRefs.Add(
                new LocalVariableItem { Name = "loopN", DataTypeName = "System.Double" }
            );

            caseStep.Children[0].Expression = "1";
            caseStep.Children[0].Steps.Add(new ActionStep("A", "计数A", Aqn<CountA>(), "Case 1 步"));

            caseStep.Children.Insert(
                1,
                new StepCollection { BranchType = BranchType.Case, StepName = "Case 2", Expression = "2" }
            );
            caseStep.Children[1].Steps.Add(new ActionStep("B", "计数B", Aqn<CountB>(), "Case 2 步"));

            // 兜底分支（构造器预建的 Else）放 Case 2 步
            caseStep.Children[2].Steps.Add(new ActionStep("C", "计数C", Aqn<CountC>(), "默认分支步"));

            var run = ExecHarness.Prepare(new StepModel[] { producer, caseStep });
            return (caseStep, run);
        }

        // ==================================================================
        //  [E20] ① 首中即选 + 默认分支兜底
        // ==================================================================
        private static void FirstMatchWinsAndFallback()
        {
            Section("[E20] 分支匹配：首中即选 / 默认分支兜底");

            CountA.Reset();
            CountB.Reset();
            CountC.Reset();
            LoopVarPlugin.Value = 2;

            var (caseStep, run) = BuildRuntimeJudgeFlow();
            Check("分支匹配图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled)
                return;

            run.Engine!.Run(run.NewContext(new StubLog()));
            Check("判据=2 → 命中 Case 2（Case 1 不命中，不穿透、不一起跑）",
                CountB.Runs == 1 && CountA.Runs == 0, $"A={CountA.Runs} B={CountB.Runs} C={CountC.Runs}");
            Check("命中 Case 分支后默认分支不执行", CountC.Runs == 0, $"C={CountC.Runs}");
            Check("★容器上报 Success（判断本身成功）", caseStep.State == StepState.Success, $"State={caseStep.State}");

            // 未命中任何匹配值 → 默认分支
            CountA.Reset();
            CountB.Reset();
            CountC.Reset();
            LoopVarPlugin.Value = 99;

            run.Engine!.Run(run.NewContext(new StubLog()));
            Check("★判据=99 未命中任何 Case → 执行默认分支",
                CountC.Runs == 1 && CountA.Runs == 0 && CountB.Runs == 0,
                $"A={CountA.Runs} B={CountB.Runs} C={CountC.Runs}");
        }

        // ==================================================================
        //  [E20] ② 字符串判据（值类型 + 字符串白名单；字符串匹配值不加引号）
        // ==================================================================
        private static void StringJudgeAndValueType()
        {
            Section("[E20] 分支匹配：字符串判据（值不加引号）");

            CountA.Reset();
            CountB.Reset();
            CountC.Reset();
            LoopVarPlugin.Value = "B";

            var caseStep = new CaseStep("K", "分支匹配", "BuiltIn_Case", "按型号分档");
            caseStep.JudgeExpression = "loopN";
            caseStep.RuntimeVariableRefs.Add(
                new LocalVariableItem { Name = "loopN", DataTypeName = "System.String" }
            );
            caseStep.Children[0].Expression = "A";
            caseStep.Children[0].Steps.Add(new ActionStep("A", "计数A", Aqn<CountA>(), "型号A"));
            caseStep.Children[1].Steps.Add(new ActionStep("C", "计数C", Aqn<CountC>(), "其它型号"));

            var run = ExecHarness.Prepare(
                new StepModel[]
                {
                    new ActionStep("V", "变量定义", Aqn<LoopVarPlugin>(), "写 loopN"),
                    caseStep,
                }
            );
            Check("字符串判据图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled)
                return;

            run.Engine!.Run(run.NewContext(new StubLog()));
            Check("判据=\"B\" 与匹配值 \"A\" 不等 → 不误命中（字符串按值比较）",
                CountA.Runs == 0 && CountC.Runs == 1, $"A={CountA.Runs} C={CountC.Runs}");

            // 同图纸改判据值为 A → 命中 Case 1
            CountA.Reset();
            CountC.Reset();
            LoopVarPlugin.Value = "A";

            run.Engine!.Run(run.NewContext(new StubLog()));
            Check("判据=\"A\" → 命中 Case 1", CountA.Runs == 1 && CountC.Runs == 0, $"A={CountA.Runs} C={CountC.Runs}");
        }

        // ==================================================================
        //  [E20] ③ 编译期硬错误（坏图纸必须在编译期说清楚，不许静默）
        // ==================================================================
        private static void CompileTimeGuards()
        {
            Section("[E20] 分支匹配：编译期硬错误");

            // 基准：判据 double 的合法图纸
            (CaseStep, List<CompilationError>) Build(Action<CaseStep> mutate, string judge = "loopN")
            {
                var caseStep = new CaseStep("K", "分支匹配", "BuiltIn_Case", "分档");
                caseStep.JudgeExpression = judge;
                caseStep.RuntimeVariableRefs.Add(
                    new LocalVariableItem { Name = "loopN", DataTypeName = "System.Double" }
                );
                caseStep.Children[0].Expression = "1";
                caseStep.Children[1].Steps.Add(new ActionStep("C", "计数C", Aqn<CountC>(), "默认步"));
                mutate?.Invoke(caseStep);

                var workspace = new WorkspaceContext();
                var result = new FlowCompiler(workspace).Compile(new StepModel[] { caseStep }, "坏图纸");
                return (caseStep, result.Errors);
            }

            bool HasError(List<CompilationError> errors, string fragment)
                => errors.Any(e => e.Message.Contains(fragment));

            var (_, e1) = Build(null, judge: "");
            Check("★判据为空 → 编译报错", HasError(e1, "判据表达式为空"), string.Join(" | ", e1.Select(e => e.Message)));
            Check("判据无效时不叠加“没有可用的 Case 分支”（病根只在判据，不把用户引向匹配值）",
                !HasError(e1, "没有可用的 Case 分支"), string.Join(" | ", e1.Select(e => e.Message)));

            var (_, e2) = Build(null, judge: "notDeclared");
            Check("★判据未声明 → 编译报错（不是 DynamicExpresso 的 Unknown identifier）",
                HasError(e2, "不是本容器已声明的变量"), string.Join(" | ", e2.Select(e => e.Message)));

            var (_, e3) = Build(s => s.Children[0].Expression = "");
            Check("★匹配值为空 → 编译报错", HasError(e3, "匹配值为空"), string.Join(" | ", e3.Select(e => e.Message)));

            var (_, e4) = Build(s => s.Children[0].Expression = "abc");
            Check("★匹配值不能转成判据类型 → 编译报错",
                HasError(e4, "无法转换为"), string.Join(" | ", e4.Select(e => e.Message)));

            var (_, e5) = Build(s =>
            {
                s.Children[0].Expression = "1";
                s.Children.Insert(
                    1,
                    new StepCollection { BranchType = BranchType.Case, StepName = "Case 2", Expression = "1" }
                );
            });
            Check("★重复匹配值 → 编译报错（第二条不可达）",
                HasError(e5, "重复"), string.Join(" | ", e5.Select(e => e.Message)));

            var (_, e6) = Build(s =>
                s.Children.Insert(
                    1,
                    new StepCollection { BranchType = BranchType.ElseIf, StepName = "ElseIf 分支", Expression = "true" }
                )
            );
            Check("★分支匹配容器里混用 ElseIf → 编译报错",
                HasError(e6, "不能混用"), string.Join(" | ", e6.Select(e => e.Message)));

            var (_, e7) = Build(s =>
                s.Children.Add(
                    new StepCollection { BranchType = BranchType.Case, StepName = "Case 越位", Expression = "9" }
                )
            );
            Check("★Case 分支排在默认分支之后 → 编译报错（不可达分支）",
                HasError(e7, "不可达分支"), string.Join(" | ", e7.Select(e => e.Message)));

            var (_, e8) = Build(s =>
            {
                s.Children.Clear();
                s.Children.Add(new StepCollection { BranchType = BranchType.Else, StepName = "默认" });
            });
            Check("★一条可用 Case 分支都没有 → 编译报错", HasError(e8, "没有可用的 Case 分支"), string.Join(" | ", e8.Select(e => e.Message)));

            // Case 分支塞进普通 If 容器（手改 .vms 场景）
            var ifStep = new ConditionStep("I", "条件判断", "BuiltIn_If", "普通If");
            ifStep.Children[0].Expression = "true";
            ifStep.Children.Insert(
                0,
                new StepCollection { BranchType = BranchType.Case, StepName = "伪 Case", Expression = "1" }
            );
            var ws = new WorkspaceContext();
            var ifResult = new FlowCompiler(ws).Compile(new StepModel[] { ifStep }, "伪Case图纸");
            Check("★普通 If 容器里出现 Case 分支 → 编译报错",
                ifResult.Errors.Any(e => e.Message.Contains("只能在分支匹配容器里使用")),
                string.Join(" | ", ifResult.Errors.Select(e => e.Message)));

            // 反向：普通 If 的既有行为不受影响（Case 分支类型不改变 If 的布尔表达式语义）
            var okIf = new ConditionStep("I", "条件判断", "BuiltIn_If", "正常If");
            okIf.Children[0].Expression = "1 == 1";
            var okResult = new FlowCompiler(new WorkspaceContext()).Compile(new StepModel[] { okIf }, "普通If图纸");
            Check("普通 If 图纸仍编译通过（未误伤既有语义）", okResult.Success,
                okResult.Success ? "" : string.Join(" | ", okResult.Errors.Select(e => e.Message)));
        }

        // ==================================================================
        //  [E20] ④ 判据是语义属性：改判据必须递增 Version（否则跑旧编译产物）
        // ==================================================================
        private static void JudgeVersionChain()
        {
            Section("[E20] 分支匹配：判据属性的版本链");

            var flow = new FlowModel { FlowName = "分支匹配版本链" };
            var caseStep = new CaseStep("K", "分支匹配", "BuiltIn_Case", "分档");
            flow.Steps.Add(caseStep);

            int before = flow.Version;
            caseStep.JudgeExpression = "loopN";
            Check("★改判据 → FlowModel.Version 递增（语义属性，不落 [RuntimeState] 排除名单）",
                flow.Version > before, $"before={before} after={flow.Version}");

            int afterJudge = flow.Version;
            caseStep.State = StepState.Running;
            Check("运行状态变更不递增 Version（纯运行期，回写不被误判成语义修改）",
                flow.Version == afterJudge, $"before={afterJudge} after={flow.Version}");
        }
    }
}
