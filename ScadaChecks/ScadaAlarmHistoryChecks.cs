using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [AH] 报警历史跟着方案走：方案目录 <c>Alarms\AlarmHistory.json</c> 的存/取、
        /// 历史行只读、历史面板合并“上次记录 + 本轮记录”。
        /// </summary>
        private static void ScadaAlarmHistoryChecks()
        {
            Section("[AH] 报警历史：方案目录存储 + 跨次加载");

            var root = Path.Combine(Path.GetTempPath(), "ScadaAlarmHistory-" + Guid.NewGuid().ToString("N")[..8]);

            try
            {
                var solutionPath = Path.Combine(root, "Line1", "Line1.vms");
                var expectedDir = Path.Combine(root, "Line1", "Alarms");
                var solution = new SolutionModel { SolutionFilePath = solutionPath };

                Check("AH1 报警历史目录 = 方案文件所在目录\\Alarms（不是软件目录）",
                    string.Equals(ScadaAlarmHistoryStore.ResolveDirectory(solution), expectedDir, StringComparison.OrdinalIgnoreCase),
                    ScadaAlarmHistoryStore.ResolveDirectory(solution));

                var unsavedDir = ScadaAlarmHistoryStore.ResolveDirectory(new SolutionModel());
                Check("AH2 未保存方案回落到软件目录\\Alarms（至少不丢数据）",
                    string.Equals(unsavedDir, Path.Combine(AppContext.BaseDirectory, "Alarms"), StringComparison.OrdinalIgnoreCase),
                    unsavedDir);

                var store = new ScadaAlarmHistoryStore(expectedDir);
                var activated = DateTime.UtcNow.AddMinutes(-5);
                var definition = new ScadaAlarmDefinition
                {
                    AlarmId = Guid.NewGuid(),
                    Name = "1#电机温度高",
                    VariableName = "Temp01",
                    Kind = ScadaAlarmKind.High,
                    Severity = ScadaAlarmSeverity.Warning,
                    Threshold = 80,
                    AlarmGroup = "一号线",
                };

                var live = new ScadaAlarmRecord(definition, activated, "92.5");
                live.State = ScadaAlarmState.Acknowledged;
                live.AcknowledgedAtUtc = activated.AddSeconds(5);

                Check("AH3 首次写入方案历史成功", store.Save(new[] { live }),
                    store.FilePath);

                var loaded = store.Load();
                Check("AH4 重新读取：记录身份/名称/变量/状态/确认时间都还在，且标记为历史行",
                    loaded.Count == 1
                    && loaded[0].RecordId == live.RecordId
                    && loaded[0].AlarmId == definition.AlarmId
                    && loaded[0].Name == "1#电机温度高"
                    && loaded[0].VariableName == "Temp01"
                    && loaded[0].State == ScadaAlarmState.Acknowledged
                    && loaded[0].AcknowledgedAtUtc == live.AcknowledgedAtUtc
                    && loaded[0].IsHistorical,
                    loaded.Count == 0 ? "空表" : $"{loaded[0].Name} / {loaded[0].State} / historical={loaded[0].IsHistorical}");

                Check("AH5 历史行不许点“确认”（上一轮引擎已不在，按 AlarmId 找不回去）",
                    !new ScadaAlarmHistoryRow(loaded[0]).CanAcknowledge,
                    new ScadaAlarmHistoryRow(loaded[0]).CanAcknowledge.ToString());

                // 历史面板：prior + 本轮（本轮为空）应显示上次那条
                var engine = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource());
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher, TimeSpan.FromMilliseconds(200));
                var context = new ScadaRuntimeContext(engine, beat, priorAlarms: loaded);
                var vm = new ScadaAlarmHistoryViewModel(Dispatcher.CurrentDispatcher);
                vm.Attach(context);

                Check("AH6 历史面板 Attach 后能看到上次加载的报警（不是只有本轮内存流水）",
                    vm.Rows.Count == 1 && vm.Rows[0].Record.RecordId == live.RecordId,
                    $"{vm.Rows.Count} 行");

                vm.Attach(null);
                Check("AH7 摘掉运行上下文后历史面板清空（历史已落到方案目录，不靠面板常驻）",
                    vm.Rows.Count == 0, $"{vm.Rows.Count} 行");

                // 写快照必须把 prior 与本轮合并：只写本轮会在第一次报警时把上次记录整批覆盖
                var later = new ScadaAlarmRecord(definition, activated.AddMinutes(1), "95.1");
                var merged = ScadaAlarmHistoryStore.Merge(loaded, new[] { later });
                Check("AH9 写回快照合并“上次 + 本轮”，两条都在（不会第一次报警就覆盖上次历史）",
                    merged.Count == 2
                    && merged.Any(r => r.RecordId == live.RecordId)
                    && merged.Any(r => r.RecordId == later.RecordId),
                    $"{merged.Count} 条");

                var sameSnapshot = ScadaAlarmRecordSnapshot.FromRecord(loaded[0]);
                sameSnapshot.State = ScadaAlarmState.Normal;
                var sameIdUpdated = sameSnapshot.ToRecord();
                var mergedSame = ScadaAlarmHistoryStore.Merge(loaded, new[] { sameIdUpdated });
                Check("AH10 同一 RecordId 出现两次时保留本轮最新状态（不被旧快照顶回去）",
                    mergedSame.Count == 1 && mergedSame[0].State == ScadaAlarmState.Normal,
                    $"{mergedSame.Count} 条 / {mergedSame.FirstOrDefault()?.State}");

                // 另存为：Image / Alarms 一起搬
                var oldRoot = Path.Combine(root, "Old");
                var newRoot = Path.Combine(root, "New");
                Directory.CreateDirectory(Path.Combine(oldRoot, "Image"));
                Directory.CreateDirectory(Path.Combine(oldRoot, "Alarms"));
                File.WriteAllText(Path.Combine(oldRoot, "Image", "logo.png"), "image");
                File.WriteAllText(Path.Combine(oldRoot, "Alarms", "AlarmHistory.json"), "alarm");

                var failures = SolutionAssetFolders.CopyAlongside(
                    Path.Combine(oldRoot, "Old.vms"),
                    Path.Combine(newRoot, "New.vms"));

                Check("AH8 另存为把 Image / Alarms 一起带到新方案目录",
                    failures.Length == 0
                    && File.Exists(Path.Combine(newRoot, "Image", "logo.png"))
                    && File.Exists(Path.Combine(newRoot, "Alarms", "AlarmHistory.json")),
                    failures.Length == 0 ? "已复制" : string.Join("；", failures));
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); }
                catch { /* 临时目录删不掉不影响断言结论 */ }
            }
        }
    }
}
