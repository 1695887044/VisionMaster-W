using System;
using System.IO;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [MH] 方案级监控：报警/变量事件引擎不再依赖 SCADA 运行窗口；关窗后监控仍在，
        /// 重新打开方案能从方案目录恢复历史。
        /// </summary>
        private static void ScadaMonitoringChecks()
        {
            Section("[MH] 方案级监控：报警/变量事件不依赖 SCADA 窗口");

            var root = Path.Combine(Path.GetTempPath(), "ScadaMonitoring-" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                var solutionPath = Path.Combine(root, "Line1", "Line1.vms");
                Directory.CreateDirectory(Path.GetDirectoryName(solutionPath)!);

                var document = new ScadaDocument();
                document.Alarms.Add(new ScadaAlarmDefinition
                {
                    AlarmId = Guid.NewGuid(),
                    Name = "温度高",
                    VariableName = "Temp",
                    Kind = ScadaAlarmKind.High,
                    Severity = ScadaAlarmSeverity.Warning,
                    Threshold = 80,
                });

                var workspace = new WorkspaceContext();
                var solution = new SolutionModel { SolutionFilePath = solutionPath };
                solution.Scada = document;
                workspace.SwitchSolution(solution);

                var values = new FakeValueSource();
                values.ByName["Temp"] = new FakeValueHandle
                {
                    Name = "Temp",
                    DataType = typeof(double),
                    Value = 90.0,
                };

                var triggerHandle = new FakeValueHandle
                {
                    VariableId = Guid.NewGuid(),
                    Name = "Trigger",
                    DataType = typeof(bool),
                    Value = false,
                };
                values.ByName["Trigger"] = triggerHandle;

                var variableEvent = document.GetOrAddVariableEvent(triggerHandle.VariableId, "Trigger");
                var triggerHook = variableEvent.GetOrAddEventHook(ScadaEventType.ValueBecameTrue);
                triggerHook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "变量触发" });

                var actionDispatcher = new RecordingActionDispatcher();
                var host = new ScadaMonitoringHost(
                    new LoggerStub(),
                    workspace,
                    values,
                    null,
                    DefaultScadaAccessPolicy.Instance,
                    null,
                    null,
                    null,
                    actionDispatcher,
                    new AppSettingsService());

                host.EnsureStarted(document);
                Check("MH1 方案加载后监控启动：报警引擎已挂载、上下文可用",
                    host.IsActive && host.Context != null && host.Context.Alarms.SlotCount == 1,
                    $"active={host.IsActive} / slots={host.Context?.Alarms.SlotCount}");

                Check("MH2 不需要打开 SCADA 运行窗口，报警也能立即初判（90 > 80）",
                    host.Context?.Alarms.ActiveAlarms.Count == 1,
                    $"{host.Context?.Alarms.ActiveAlarms.Count} 条");

                triggerHandle.Raise(true);
                PumpDispatcher(400);
                Check("MH2b 变量事件也不依赖窗口：值变为真 → 动作被派发（source=变量名）",
                    actionDispatcher.Calls == 1 && actionDispatcher.LastSource == "Trigger",
                    $"{actionDispatcher.Calls} 次 / {actionDispatcher.LastSource ?? "null"}");

                Check("MH3 方案级监控默认无人值守：执行前确认不允许弹模态框",
                    !ScadaConfirmationService.InteractiveAllowed,
                    ScadaConfirmationService.InteractiveAllowed.ToString());

                var alarmDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "Alarms");
                Check("MH4 报警历史写到方案目录 Alarms（不是软件目录）",
                    Directory.Exists(alarmDir)
                    && Directory.GetFiles(alarmDir, "AlarmHistory-*.csv").Length == 1,
                    alarmDir);

                host.Stop();
                Check("MH5 停止监控后上下文清空、引擎不再挂载",
                    !host.IsActive && host.Context == null,
                    $"active={host.IsActive} / context={(host.Context == null ? "null" : "not null")}");

                // 重新加载同一方案：历史快照应能读回
                var host2 = new ScadaMonitoringHost(
                    new LoggerStub(),
                    workspace,
                    values,
                    null,
                    DefaultScadaAccessPolicy.Instance,
                    null,
                    null,
                    null,
                    actionDispatcher,
                    new AppSettingsService());
                host2.EnsureStarted(document);
                Check("MH6 重新加载方案后能从方案目录恢复上次报警",
                    host2.Context?.PriorAlarms.Count == 1
                    && host2.Context.PriorAlarms[0].Name == "温度高",
                    $"{host2.Context?.PriorAlarms.Count} 条");
                host2.Stop();

                var repoRoot = FindRepoRoot();
                var shellXaml = repoRoot == null ? string.Empty : File.ReadAllText(Path.Combine(repoRoot, @"VisionMaster\Shell.xaml"));
                var editorXaml = repoRoot == null ? string.Empty : File.ReadAllText(Path.Combine(repoRoot, @"VisionMaster\Views\ScadaEditorView.xaml"));
                var flowXaml = repoRoot == null ? string.Empty : File.ReadAllText(Path.Combine(repoRoot, @"VisionMaster\Views\FlowListView.xaml"));

                Check("MH7 报警配置入口在 SCADA 编辑器工具栏；旧「文件」菜单入口已删除",
                    editorXaml.Contains("OpenAlarmConfigCommand")
                    && !shellXaml.Contains("SolutionAction.AlarmConfig"),
                    editorXaml.Length == 0 ? "没读到 XAML" : "工具栏有 / 文件菜单无");

                Check("MH8 变量事件入口在流程栏；旧「系统」菜单入口已删除",
                    flowXaml.Contains("FlowAction.VariableEvents")
                    && !shellXaml.Contains("SystemAction.VariableEvents"),
                    flowXaml.Length == 0 ? "没读到 XAML" : "流程栏有 / 系统菜单无");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { /* 临时目录删不掉不影响断言结论 */ }
            }
        }

        private sealed class RecordingActionDispatcher : IScadaActionDispatcher
        {
            public int Calls { get; private set; }

            public ScadaEventHook? LastHook { get; private set; }

            public string? LastSource { get; private set; }

            public void Dispatch(ScadaEventHook? hook, string? sourceName, string? operatorName = null)
            {
                Calls++;
                LastHook = hook;
                LastSource = sourceName;
            }
        }
    }
}
