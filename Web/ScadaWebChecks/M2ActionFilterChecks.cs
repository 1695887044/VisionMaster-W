using System;
using System.Collections.Generic;
using VisionMaster.Scada;

namespace ScadaWebChecks
{
    /// <summary>
    /// M2 动作白名单过滤器（<see cref="VisionMaster.ScadaWeb.ScadaWebActionFilter"/>）的断言。
    ///
    /// 为什么这些断言必须存在
    /// ---------
    /// 过滤器是"Web 命令"与"桌面文档"之间唯一的隔离带，而它的两类错误都是<b>静默</b>的：
    ///   · 漏过滤 → Web 一条点击能改桌面配置 / 切走所有人的画面（现场表现为"没人动它自己跳页"）；
    ///   · 漏复制 → 允许的动作到了执行侧变成空参数（表现为"点了没反应"或"写错变量"）。
    /// 两者都编译期全绿、运行期不报错，只能靠逐字段比对钉住。
    ///
    /// 覆盖（M2-AF1 ~ M2-AF11）：
    ///   AF1  只保留允许的动作，且顺序与原钩子一致
    ///   AF2  Navigate 即使在 allowed 里也被剔除（镜像模式）
    ///   AF3  未允许的动作被剔除，skipped 逐条记账
    ///   AF4  允许的动作参数逐字段原样（日志文本 / 写变量 / 运动参数）
    ///   AF5  原钩子与 Actions 集合未被修改（条数、顺序、引用、确认开关、Navigate 仍在）
    ///   AF6  副本字段：RequireConfirmation=false、Event/ConfirmationMessage 复制、动作是深拷贝
    ///   AF7  副本可被 IScadaActionDispatcher.Dispatch 直接执行
    ///   AF8  source=null → 返回 null、skipped 为空
    ///   AF9  空白名单 → 副本非空但零动作（分发器空操作），全部动作进 skipped
    ///   AF10 流程动作在允许时 FlowId/FlowName 原样保留
    ///   AF11 坏数据里的 null 动作不抛、不记账，后面的正常动作照常保留
    ///
    /// 接线（在 Program.Main 的 Finish() 之前一行）：
    ///   <c>M2ActionFilterChecks.Run(); _failed += M2ActionFilterChecks.Failures;</c>
    /// </summary>
    public static class M2ActionFilterChecks
    {
        /// <summary>本段失败的条数（Run 开始时清零）。Program 用它把结果计入退出码</summary>
        public static int Failures { get; private set; }

        /// <summary>本段通过的条数（Program 用它并入总 PASS 数；只影响计数，不改变任何断言）</summary>
        public static int Pass => _total - Failures;

        /// <summary>本段执行的断言总条数</summary>
        public static int Total => _total;

        private static int _total;

        public static void Run()
        {
            Failures = 0;
            _total = 0;

            Console.WriteLine();
            Console.WriteLine("--- M2ActionFilterChecks：动作白名单 / 镜像模式 ---");

            var pageId = Guid.NewGuid();
            var variableId = Guid.NewGuid();
            var axisId = Guid.NewGuid();

            var source = new ScadaEventHook
            {
                Event = ScadaEventType.Pressed,
                RequireConfirmation = true,
                ConfirmationMessage = "确认启动？",
            };

            var logAction = new ScadaAction { Type = ScadaActionType.Log, Text = "按下启动" };
            var navigateAction = new ScadaAction
            {
                Type = ScadaActionType.Navigate,
                TargetPageId = pageId,
                TargetPageName = "运行页",
            };
            var navigateBackAction = new ScadaAction { Type = ScadaActionType.NavigateBack };
            var writeAction = new ScadaAction
            {
                Type = ScadaActionType.WriteVariable,
                VariableId = variableId,
                VariableName = "启动",
                Value = "1",
                OnlyIfPreviousSucceeded = true,
            };
            var motionAction = new ScadaAction
            {
                Type = ScadaActionType.MotionMoveAbsolute,
                AxisId = axisId,
                AxisName = "X",
                TargetMm = 12.5,
                VelocityMmPerS = 30,
            };
            var flowAction = new ScadaAction
            {
                Type = ScadaActionType.StartFlow,
                FlowId = "f-1",
                FlowName = "上料",
            };

            source.Actions.Add(logAction);
            source.Actions.Add(navigateAction);
            source.Actions.Add(navigateBackAction);
            source.Actions.Add(writeAction);
            source.Actions.Add(motionAction);
            source.Actions.Add(flowAction);

            var originalCollection = source.Actions;

            // allowed 里<b>故意</b>带上 Navigate / NavigateBack：要证明"永远剔除"不是靠调用方自律。
            var allowed = new HashSet<ScadaActionType>
            {
                ScadaActionType.Log,
                ScadaActionType.WriteVariable,
                ScadaActionType.MotionMoveAbsolute,
                ScadaActionType.Navigate,
                ScadaActionType.NavigateBack,
            };

            var copy = VisionMaster.ScadaWeb.ScadaWebActionFilter.BuildAllowedCopy(source, allowed, out var skipped);
            var copyActions = copy?.Actions;

            var copyTypes = Types(copyActions);

            Check("M2-AF1",
                copy != null
                && copyActions != null && copyActions.Count == 3
                && copyTypes.Count == 3
                && copyTypes[0] == ScadaActionType.Log
                && copyTypes[1] == ScadaActionType.WriteVariable
                && copyTypes[2] == ScadaActionType.MotionMoveAbsolute,
                copy == null
                    ? "BuildAllowedCopy 返回 null（源非空时不应发生）"
                    : $"actions={copyActions?.Count} types={Join(copyTypes)}（期望 记录日志,写变量,轴绝对定位）");

            Check("M2-AF2",
                skipped.Contains(ScadaActionType.Navigate)
                && !copyTypes.Contains(ScadaActionType.Navigate),
                $"skipped={Join(skipped)} copyTypes={Join(copyTypes)}（Navigate 即使在 allowed 里也必须被剔除）");

            Check("M2-AF2b",
                skipped.Contains(ScadaActionType.NavigateBack)
                && !copyTypes.Contains(ScadaActionType.NavigateBack),
                $"skipped={Join(skipped)} copyTypes={Join(copyTypes)}（NavigateBack 即使在 allowed 里也必须被剔除）");

            Check("M2-AF3",
                skipped.Contains(ScadaActionType.StartFlow)
                && !copyTypes.Contains(ScadaActionType.StartFlow)
                && skipped.Count == 3,
                $"skipped={Join(skipped)}（期望 切换画面,返回上一页,启动流程；未允许的 StartFlow 被剔除）");

            var copyLog = At(copyActions, 0);
            var copyWrite = At(copyActions, 1);
            var copyMotion = At(copyActions, 2);

            Check("M2-AF4",
                copyLog != null && copyLog.Text == logAction.Text
                && copyWrite != null
                && copyWrite.VariableId == variableId
                && copyWrite.VariableName == "启动"
                && copyWrite.Value == "1"
                && copyWrite.OnlyIfPreviousSucceeded
                && copyMotion != null
                && copyMotion.AxisId == axisId
                && copyMotion.AxisName == "X"
                && copyMotion.TargetMm == 12.5
                && copyMotion.VelocityMmPerS == 30,
                $"log.Text={Show(copyLog?.Text)} write={Show(copyWrite?.VariableName)}:={Show(copyWrite?.Value)}"
                + $" onlyIfPrev={copyWrite?.OnlyIfPreviousSucceeded}"
                + $" axis={Show(copyMotion?.AxisName)} target={Show(copyMotion?.TargetMm)} speed={Show(copyMotion?.VelocityMmPerS)}");

            // 原钩子必须"一个字节都没动"：条数、顺序、对象引用、确认开关、Navigate 都还在。
            var sourceUnchanged = source.Actions.Count == 6
                && ReferenceEquals(source.Actions, originalCollection)
                && ReferenceEquals(source.Actions[0], logAction)
                && ReferenceEquals(source.Actions[1], navigateAction)
                && ReferenceEquals(source.Actions[2], navigateBackAction)
                && ReferenceEquals(source.Actions[3], writeAction)
                && ReferenceEquals(source.Actions[4], motionAction)
                && ReferenceEquals(source.Actions[5], flowAction)
                && source.RequireConfirmation
                && source.ConfirmationMessage == "确认启动？"
                && source.Event == ScadaEventType.Pressed;

            Check("M2-AF5", sourceUnchanged,
                $"count={source.Actions.Count} sameCollection={ReferenceEquals(source.Actions, originalCollection)}"
                + $" requireConfirmation={source.RequireConfirmation} event={source.Event}"
                + $" navigateStillThere={ReferenceEquals(source.Actions.Count > 1 ? source.Actions[1] : null, navigateAction)}");

            var deepCopied = copy != null
                && copyActions != null
                && copyActions.Count == 3
                && !ReferenceEquals(copyActions[0], logAction)
                && !ReferenceEquals(copyActions[1], writeAction)
                && !ReferenceEquals(copyActions[2], motionAction);

            Check("M2-AF6",
                copy != null
                && !ReferenceEquals(copy, source)
                && !copy.RequireConfirmation
                && copy.Event == source.Event
                && copy.ConfirmationMessage == source.ConfirmationMessage
                && deepCopied,
                copy == null
                    ? "副本为 null"
                    : $"requireConfirmation={copy.RequireConfirmation}（期望 false）"
                      + $" event={copy.Event} message={Show(copy.ConfirmationMessage)}"
                      + $" deepCopy={deepCopied}");

            // 副本要能被分发器直接消费：不是"看起来像钩子"，而是真的走一遍接口。
            var dispatcher = new RecordingDispatcher();
            dispatcher.Dispatch(copy, "启动按钮", "web-operator");

            Check("M2-AF7",
                dispatcher.CallCount == 1
                && dispatcher.ActionCount == 3
                && dispatcher.Types.Count == 3
                && dispatcher.Types[0] == ScadaActionType.Log
                && dispatcher.Types[1] == ScadaActionType.WriteVariable
                && dispatcher.Types[2] == ScadaActionType.MotionMoveAbsolute
                && dispatcher.LastSource == "启动按钮"
                && dispatcher.LastOperator == "web-operator",
                $"calls={dispatcher.CallCount} actions={dispatcher.ActionCount} types={Join(dispatcher.Types)}"
                + $" source={Show(dispatcher.LastSource)} operator={Show(dispatcher.LastOperator)}");

            var nullCopy = VisionMaster.ScadaWeb.ScadaWebActionFilter.BuildAllowedCopy(null, allowed, out var nullSkipped);
            Check("M2-AF8", nullCopy == null && nullSkipped.Count == 0,
                $"copy={(nullCopy == null ? "<null>" : "非 null")} skipped={nullSkipped.Count}（源为 null 时应原样返回 null）");

            var emptyAllowed = VisionMaster.ScadaWeb.ScadaWebActionFilter.BuildAllowedCopy(
                source, new HashSet<ScadaActionType>(), out var emptySkipped);

            Check("M2-AF9",
                emptyAllowed != null
                && emptyAllowed.Actions.Count == 0
                && emptySkipped.Count == 6,
                emptyAllowed == null
                    ? "副本为 null（空白名单也应返回可执行的空副本）"
                    : $"actions={emptyAllowed.Actions.Count} skipped={emptySkipped.Count}（期望 0 / 6）");

            var flowOnly = VisionMaster.ScadaWeb.ScadaWebActionFilter.BuildAllowedCopy(
                source,
                new HashSet<ScadaActionType> { ScadaActionType.StartFlow },
                out var flowSkipped);

            var copiedFlow = At(flowOnly?.Actions, 0);

            Check("M2-AF10",
                flowOnly != null
                && flowOnly.Actions.Count == 1
                && copiedFlow != null
                && copiedFlow.Type == ScadaActionType.StartFlow
                && copiedFlow.FlowId == flowAction.FlowId
                && copiedFlow.FlowName == flowAction.FlowName
                && flowSkipped.Count == 5,
                $"actions={flowOnly?.Actions.Count} skipped={flowSkipped.Count}"
                + $" flowId={Show(copiedFlow?.FlowId)} flowName={Show(copiedFlow?.FlowName)}");

            // 手工改坏的 .vms：集合里可能有 null。过滤器不许抛，也不许把后面的正常动作一起丢掉。
            var broken = new ScadaEventHook { Event = ScadaEventType.Loaded };
            broken.Actions.Add(null!);
            broken.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "坏数据之后的正常动作" });

            ScadaEventHook? brokenCopy = null;
            var brokenSkipped = new List<ScadaActionType>();
            var brokenThrew = string.Empty;

            try
            {
                brokenCopy = VisionMaster.ScadaWeb.ScadaWebActionFilter.BuildAllowedCopy(
                    broken,
                    new HashSet<ScadaActionType> { ScadaActionType.Log },
                    out brokenSkipped);
            }
            catch (Exception ex)
            {
                brokenThrew = $"{ex.GetType().Name}: {ex.Message}";
            }

            Check("M2-AF11",
                brokenThrew.Length == 0
                && brokenCopy != null
                && brokenCopy.Actions.Count == 1
                && brokenCopy.Actions[0].Text == "坏数据之后的正常动作"
                && brokenSkipped.Count == 0,
                brokenThrew.Length > 0
                    ? $"抛异常：{brokenThrew}"
                    : $"actions={brokenCopy?.Actions.Count} skipped={brokenSkipped.Count}（null 项应静默跳过）");

            Console.WriteLine($"RESULT: {_total - Failures} PASS / {Failures} FAIL（M2ActionFilterChecks）");
        }

        // ---------------- 辅助 ----------------

        private static List<ScadaActionType> Types(System.Collections.ObjectModel.ObservableCollection<ScadaAction>? actions)
        {
            var result = new List<ScadaActionType>();
            if (actions == null)
                return result;

            foreach (var action in actions)
            {
                if (action != null)
                    result.Add(action.Type);
            }

            return result;
        }

        private static ScadaAction? At(System.Collections.ObjectModel.ObservableCollection<ScadaAction>? actions, int index)
            => actions != null && index >= 0 && index < actions.Count ? actions[index] : null;

        private static string Join(IReadOnlyList<ScadaActionType>? types)
            => types == null || types.Count == 0 ? "<空>" : string.Join(",", types);

        private static string Show(object? value) => value?.ToString() ?? "<null>";

        private static void Check(string id, bool ok, string detail)
        {
            _total++;
            if (!ok) Failures++;

            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail);
        }

        /// <summary>
        /// 记录型分发器：只证明"副本能被接口消费、顺序与参数没走样"，不执行任何真实动作
        /// （断言工程不碰日志/变量/流程/运动卡）。
        /// </summary>
        private sealed class RecordingDispatcher : IScadaActionDispatcher
        {
            public int CallCount { get; private set; }

            public int ActionCount { get; private set; }

            public string? LastSource { get; private set; }

            public string? LastOperator { get; private set; }

            public List<ScadaActionType> Types { get; } = new();

            public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
            {
                CallCount++;
                LastSource = sourceName;
                LastOperator = operatorName;
                ActionCount = hook?.Actions.Count ?? 0;

                if (hook == null)
                    return;

                foreach (var action in hook.Actions)
                {
                    if (action != null)
                        Types.Add(action.Type);
                }
            }
        }
    }
}
