using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Core.Interfaces;
using Plugin.Util.Models;
using VisionMaster;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Plugins.Util;
using VisionMaster.Services;

namespace VarScopeAudit
{
    /// <summary>
    /// 审计探针：批 1（重名安全）+ 批 2（同设备内唯一）验收。
    /// 直接引用产品工程，用真实现跑；只读产品代码，不改产品行为。
    /// </summary>
    internal static class Program
    {
        private static int _fail;
        private static int _pass;

        private static void Check(string id, bool ok, string detail)
        {
            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + " :: " + detail);
            if (ok) _pass++;
            else _fail++;
        }

        private static void Guard(string group, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check(group + ".X 未预期异常", false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static NetworkVariableModel Net(string name, string conn, object mirror)
        {
            var v = new NetworkVariableModel
            {
                Name = name,
                ConnectionName = conn,
                DataType = mirror is Array ? mirror.GetType() : typeof(int),
            };
            // 只写镜像，不走 Value setter（后者会 WriteToDevice）
            v.UpdateMirrorValue(mirror);
            return v;
        }

        private static NetworkVariableModel Net(string name, string conn, Type dataType, object mirror)
        {
            var v = new NetworkVariableModel
            {
                Name = name,
                ConnectionName = conn,
                DataType = dataType,
            };
            v.UpdateMirrorValue(mirror);
            return v;
        }

        private static DeviceVariableRawRow Row(string name, int rowNumber = 2)
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            void Put(string column, string value) => cells[DeviceVariableIoText.NormalizeHeader(column)] = value;
            Put(DeviceVariableColumns.Name, name);
            // 用 int：bool 在 Modbus 保持寄存器区会被"存储区不支持该类型"先拦掉，
            // 那是另一条校验，会把本组想验证的"名字冲突"盖住。
            Put(DeviceVariableColumns.DataType, "整数 (int)");
            Put(DeviceVariableColumns.Area, "HoldingRegisters");
            Put(DeviceVariableColumns.Offset, "0");
            return new DeviceVariableRawRow { RowNumber = rowNumber, Cells = cells };
        }

        private static DeviceVariableImportPlan Plan(
            CommunicationConfig connection,
            string name,
            IEnumerable<NetworkVariableModel> existingOnDevice,
            IEnumerable<IVariable> allVariables)
            => DeviceVariableImportService.BuildPlan(
                connection,
                new[] { Row(name) },
                existingOnDevice.ToList(),
                allVariables.ToList(),
                Array.Empty<VariableGroupDescriptor>(),
                Array.Empty<string>(),
                autoCreateDisplayGroups: true);

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== VarScopeAudit: 批1「重名安全」+ 批2「同设备内唯一」验收 ===");

            Guard("C1", C1_RegistryCore);
            Guard("C2", C2_LoadPath);
            Guard("C3", C3_WriterPaths);
            Guard("C4", C4_ImportScope);
            Guard("C5", C5_ScadaTemplate);
            Guard("C6", C6_RenameScope);
            Guard("C7", C7_PollingUnchanged);
            Guard("C8", C8_NameRulesUnchanged);
            Guard("C9", C9_FuzzConsistency);
            Guard("C10", C10_ScopeTruthTable);
            Guard("C11", C11_ImportBruteForce);
            Guard("C12", C12_ImportApplyEndToEnd);
            Guard("C13", C13_FindBlocker);
            Guard("C14", C14_CandidateSnapshot);
            Guard("C15", C15_AssignmentCandidates);
            Guard("C16", C16_AssignmentRuntimeWrite);
            Guard("C17", C17_RecordAndUploadRead);
            Guard("C18", C18_Serialization);
            Guard("C19", C19_CaptureId);
            Guard("C20", C20_BindingDialogCandidates);
            Guard("C21", C21_BindingDialogDisplay);
            Guard("C22", C22_AssignmentRowDisplay);
            Guard("C23", C23_LastProblemsScope);
            Guard("C24", C24_RecordUploadTarget);
            Guard("C25", C25_ScadaQualified);
            Guard("C26", C26_TouchedViewsParse);

            Console.WriteLine("=== RESULT: " + (_fail == 0 ? "ALL_OK" : _fail + " FAIL") + " ===");
            // 供 run_gates.ps1 的 Get-Counts 提取（两个捕获组：pass / fail）
            Console.WriteLine($"ASSERT: {_pass} PASS / {_fail} FAIL");
            return _fail == 0 ? 0 : 1;
        }

        // ================= C1 注册表核心语义（批 1） =================
        private static void C1_RegistryCore()
        {
            var a = Net("Alarm[90]", "L1", 111);
            var b = Net("Alarm[90]", "L2", 222);
            var solo = Net("Solo", "L1", 7);
            var reg = new VariableRegistry(new ObservableCollection<IVariable> { a, b, solo });

            Check("C1.1 唯一名 FindByName 命中", ReferenceEquals(reg.FindByName("Solo"), solo),
                "hit=" + (reg.FindByName("Solo")?.Name ?? "null"));
            Check("C1.2 重名 FindByName 返回 null", reg.FindByName("Alarm[90]") == null,
                "hit=" + (reg.FindByName("Alarm[90]")?.Name ?? "null"));

            var matches = reg.FindAllByName("Alarm[90]");
            Check("C1.3 重名 FindAllByName 返回全部候选",
                matches.Count == 2 && matches.Contains(a) && matches.Contains(b),
                "count=" + matches.Count);

            Check("C1.4a 唯一名大小写不敏感", ReferenceEquals(reg.FindByName("SOLO"), solo), "");
            Check("C1.4b 重名大小写不敏感（仍为 null）", reg.FindByName("alarm[90]") == null, "");
            Check("C1.4c FindAllByName 大小写不敏感（返回 2）",
                reg.FindAllByName("ALARM[90]").Count == 2, "");

            Check("C1.5a FindById 精确命中 L1", ReferenceEquals(reg.FindById(a.VariableId), a), "");
            Check("C1.5b FindById 精确命中 L2", ReferenceEquals(reg.FindById(b.VariableId), b), "");
            Check("C1.6 Resolve(空Id, 重名) 返回 null", reg.Resolve(Guid.Empty, "Alarm[90]") == null, "");
            Check("C1.7 Resolve(带Id, 重名) 仍按 Id 命中", ReferenceEquals(reg.Resolve(b.VariableId, "Alarm[90]"), b), "");

            Check("C1.8a 无命中 FindByName 返回 null", reg.FindByName("NoSuchVar") == null, "");
            Check("C1.8b 无命中 FindAllByName 返回空", reg.FindAllByName("NoSuchVar").Count == 0, "");
            Check("C1.8c 空名字 FindByName 返回 null", reg.FindByName(null) == null && reg.FindByName("") == null, "");
            Check("C1.8d 空名字 FindAllByName 返回空", reg.FindAllByName(null).Count == 0 && reg.FindAllByName("").Count == 0, "");
            Check("C1.9 Count 仍按 Id 统计", reg.Count == 3, "Count=" + reg.Count);
        }

        // ================= C2 加载路径（批 1） =================
        private static void C2_LoadPath()
        {
            var coll = new ObservableCollection<IVariable>();
            var reg = new VariableRegistry(coll);

            var a = Net("Alarm[90]", "L1", 1);
            var b = Net("Alarm[90]", "L2", 2);

            Exception error = null;
            try
            {
                coll.Clear();
                coll.Add(a);
                coll.Add(b);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Check("C2.1 Clear+Add 重名不抛异常", error == null,
                error == null ? "无异常" : error.GetType().Name + ": " + error.Message);
            Check("C2.2 加载后按名解析为 null（不再静默取首个）",
                reg.FindByName("Alarm[90]") == null, "");
            Check("C2.3 跨设备重名不进诊断（合法状态，不弹假警报）",
                reg.LastProblems.Count == 0, "problems=" + reg.LastProblems.Count);

            var beforeCount = reg.FindAllByName("Alarm[90]").Count;
            var beforeFind = reg.FindByName("Alarm[90]") == null;
            reg.Rebuild();
            Check("C2.4 增量路径与 Rebuild 路径结果一致",
                beforeCount == reg.FindAllByName("Alarm[90]").Count && beforeFind == (reg.FindByName("Alarm[90]") == null),
                $"before(count={beforeCount}) after(count={reg.FindAllByName("Alarm[90]").Count})");

            coll.Remove(b);
            Check("C2.5 移除重名之一后恢复唯一命中",
                ReferenceEquals(reg.FindByName("Alarm[90]"), a),
                "hit=" + (reg.FindByName("Alarm[90]")?.ConnectionName ?? "null"));
        }

        // ================= C3 按名/按 Id 读写（批 1） =================
        private static void C3_WriterPaths()
        {
            var a = Net("Alarm[90]", "L1", 111);
            var b = Net("Alarm[90]", "L2", 222);
            var solo = VariableFactory.CreateLocal("Solo", typeof(int), "唯一名", 7);

            var ws = new WorkspaceContext();
            ws.GlobalVariables.Clear();
            ws.GlobalVariables.Add(a);
            ws.GlobalVariables.Add(b);
            ws.GlobalVariables.Add(solo);

            var writer = new GlobalVariableWriter(ws);

            var okAmbiguous = writer.TryRead(Guid.Empty, "Alarm[90]", out var ambiguousValue, out var ambiguousError);
            Check("C3.1 TryRead(空Id, 重名) 失败且给出歧义原因",
                !okAmbiguous && ambiguousValue == null
                && (ambiguousError ?? string.Empty).Contains("重复")
                && (ambiguousError ?? string.Empty).Contains("L1")
                && (ambiguousError ?? string.Empty).Contains("L2"),
                "ok=" + okAmbiguous + " err=" + (ambiguousError ?? "null"));

            var okById = writer.TryRead(b.VariableId, "Alarm[90]", out var valueB, out var errorB);
            Check("C3.2 TryRead(带Id) 命中 L2", okById && Equals(valueB, 222),
                "ok=" + okById + " value=" + (valueB ?? "null"));

            var okWriteAmbiguous = writer.TryWrite(Guid.Empty, "Alarm[90]", 999, out var writeError);
            Check("C3.3 TryWrite(空Id, 重名) 失败且两台都未被写",
                !okWriteAmbiguous && Equals(a.Value, 111) && Equals(b.Value, 222)
                && (writeError ?? string.Empty).Contains("重复"),
                "ok=" + okWriteAmbiguous + " L1=" + (a.Value ?? "null") + " L2=" + (b.Value ?? "null"));

            var okSoloRead = writer.TryRead(Guid.Empty, "Solo", out var soloValue, out var soloError);
            Check("C3.4 TryRead(空Id, 唯一名) 成功", okSoloRead && Equals(soloValue, 7),
                "ok=" + okSoloRead + " value=" + (soloValue ?? "null"));

            var okSoloWrite = writer.TryWrite(Guid.Empty, "Solo", 8, out var soloWriteError);
            Check("C3.5 TryWrite(空Id, 唯一名) 成功", okSoloWrite && Equals(solo.Value, 8),
                "ok=" + soloWriteError);

            var okMissing = writer.TryRead(Guid.Empty, "NoSuchVar", out _, out var missingError);
            Check("C3.6 不存在时仍报'找不到'（歧义分支不误吞）",
                !okMissing && (missingError ?? string.Empty).Contains("找不到"),
                "err=" + (missingError ?? "null"));

            var okNamedWrite = writer.TryWrite("Alarm[90]", 1, out var namedWriteError);
            Check("C3.7 TryWrite(名字重载) 重名时失败并给出歧义",
                !okNamedWrite && (namedWriteError ?? string.Empty).Contains("重复"),
                "err=" + (namedWriteError ?? "null"));
        }

        // ================= C4 导入口径（批 2：同设备内唯一） =================
        private static void C4_ImportScope()
        {
            var connL1 = new CommunicationConfig { ConnectionName = "L1", Protocol = CommunicationType.ModbusTcp };
            var connL2 = new CommunicationConfig { ConnectionName = "L2", Protocol = CommunicationType.ModbusTcp };

            var onL1 = Net("Alarm[90]", "L1", true);
            var local = VariableFactory.CreateLocal("Alarm[90]", typeof(bool), "同名本地变量", false);

            // ① 跨设备同名 → 放行（批 2 的核心变化）
            var crossDevice = Plan(connL2, "Alarm[90]", Array.Empty<NetworkVariableModel>(), new IVariable[] { onL1 });
            var crossItem = crossDevice.Items.FirstOrDefault();
            Check("C4.1 跨设备同名 → 允许新增（不再判错误行）",
                crossDevice.GlobalError == null
                && crossItem != null
                && crossItem.Status == DeviceVariableImportStatus.New,
                "status=" + (crossItem?.Status.ToString() ?? "null") + " note=" + (crossItem?.Note ?? "null"));

            // ② 与本地变量同名 → 仍拦（本地变量没有设备，按名无法区分）
            var localClash = Plan(connL2, "Alarm[90]", Array.Empty<NetworkVariableModel>(), new IVariable[] { local });
            var localItem = localClash.Items.FirstOrDefault();
            Check("C4.2 与本地变量同名 → 仍判错误行",
                localItem != null
                && localItem.Status == DeviceVariableImportStatus.Error
                && (localItem.Note ?? string.Empty).Contains("本地变量")
                && (localItem.Note ?? string.Empty).Contains("同一设备内变量名必须唯一"),
                "status=" + (localItem?.Status.ToString() ?? "null") + " note=" + (localItem?.Note ?? "null"));

            // ③ 同设备同名（既有变量在同一连接）→ 走"更新"，不是冲突
            var sameDevice = Net("Alarm[90]", "L2", false);
            var update = Plan(connL2, "Alarm[90]", new[] { sameDevice }, new IVariable[] { sameDevice });
            var updateItem = update.Items.FirstOrDefault();
            Check("C4.3 同设备同名 → 走更新（不误判冲突）",
                updateItem != null
                && updateItem.Status != DeviceVariableImportStatus.Error
                && updateItem.Existing != null,
                "status=" + (updateItem?.Status.ToString() ?? "null"));

            // ④ 文件内重名 → 仍拦
            var twoRows = DeviceVariableImportService.BuildPlan(
                connL2,
                new[] { Row("Alarm[90]", 2), Row("Alarm[90]", 3) },
                Array.Empty<NetworkVariableModel>(),
                Array.Empty<IVariable>(),
                Array.Empty<VariableGroupDescriptor>(),
                Array.Empty<string>(),
                autoCreateDisplayGroups: true);
            var second = twoRows.Items.LastOrDefault();
            Check("C4.4 文件内重名 → 仍判错误行",
                second != null
                && second.Status == DeviceVariableImportStatus.Error
                && (second.Note ?? string.Empty).Contains("文件内变量名重复"),
                "status=" + (second?.Status.ToString() ?? "null") + " note=" + (second?.Note ?? "null"));
        }

        // ================= C5 SCADA 模板（批 1） =================
        private static void C5_ScadaTemplate()
        {
            var a = Net("Alarm[90]", "L1", 111);
            var b = Net("Alarm[90]", "L2", 222);
            var root1 = VariableFactory.CreateLocal("Samples", typeof(int[]), "数组根 1", new[] { 1, 2, 3 });
            var root2 = VariableFactory.CreateLocal("Samples", typeof(int[]), "数组根 2", new[] { 4, 5, 6 });
            var soloArray = VariableFactory.CreateLocal("Solo", typeof(int[]), "唯一数组", new[] { 9, 8 });

            var reg = new VariableRegistry(new ObservableCollection<IVariable> { a, b, root1, root2, soloArray });
            var resolver = new ScadaTemplateVariableResolver(reg);

            var okAmbiguous = resolver.TryResolve("Alarm[90]", out _, out var ambiguousError);
            Check("C5.1 重名 → 失败且给出歧义（不再静默取首个）",
                !okAmbiguous && (ambiguousError ?? string.Empty).Contains("重复"),
                "err=" + (ambiguousError ?? "null"));

            var okRootAmbiguous = resolver.TryResolve("Samples[1]", out _, out var rootError);
            Check("C5.2 数组根重名 → 报歧义（不再是'数组根不存在'）",
                !okRootAmbiguous
                && (rootError ?? string.Empty).Contains("重复")
                && !(rootError ?? string.Empty).Contains("数组根「Samples」不存在"),
                "err=" + (rootError ?? "null"));

            var okSolo = resolver.TryResolve("Solo[1]", out var soloVar, out var soloError);
            Check("C5.3 唯一数组 → 正常取元素",
                okSolo && soloVar.ArrayIndex == 1 && soloVar.VariableId == soloArray.VariableId,
                "ok=" + okSolo + " index=" + (okSolo ? soloVar.ArrayIndex : -1));

            var okMissing = resolver.TryResolve("NoSuchVar", out _, out var missingError);
            Check("C5.4 不存在 → 仍报'找不到变量'",
                !okMissing && (missingError ?? string.Empty).Contains("找不到变量"),
                "err=" + (missingError ?? "null"));
        }

        // ================= C6 改名口径（批 2） =================
        private static void C6_RenameScope()
        {
            var a = Net("Alarm[90]", "L1", 111);   // L1
            var b = Net("Alarm[91]", "L2", 222);   // L2
            var c = Net("Alarm[92]", "L3", 333);   // L3
            var d = Net("Alarm[93]", "L2", 444);   // L2（与 b 同设备）
            var local = VariableFactory.CreateLocal("LocalTag", typeof(int), "本地", 0);
            var reg = new VariableRegistry(new ObservableCollection<IVariable> { a, b, c, d, local });

            var okFree = reg.TryRename(c, "FreeTag", out var freeError);
            Check("C6.1 改成未占用的名字成功", okFree, "ok=" + okFree + " err=" + (freeError ?? "null"));

            var okSameDevice = reg.TryRename(d, "Alarm[91]", out var sameDeviceError);
            Check("C6.2 改成**同一设备**已用的名字 → 拒",
                !okSameDevice
                && (sameDeviceError ?? string.Empty).Contains("已存在同名变量")
                && (sameDeviceError ?? string.Empty).Contains("设备「L2」"),
                "ok=" + okSameDevice + " err=" + (sameDeviceError ?? "null"));

            var okCrossDevice = reg.TryRename(c, "Alarm[91]", out var crossDeviceError);
            Check("C6.3 改成**另一台设备**已用的名字 → 允许（批 2 的核心变化）",
                okCrossDevice,
                "ok=" + okCrossDevice + " err=" + (crossDeviceError ?? "null"));

            Check("C6.4 跨设备同名后按名解析变为歧义、按 Id 各自命中",
                reg.FindByName("Alarm[91]") == null
                && reg.FindAllByName("Alarm[91]").Count == 2
                && ReferenceEquals(reg.FindById(b.VariableId), b)
                && ReferenceEquals(reg.FindById(c.VariableId), c),
                "count=" + reg.FindAllByName("Alarm[91]").Count);

            var okLocalTarget = reg.TryRename(local, "Alarm[90]", out var localTargetError);
            Check("C6.5 本地变量改成网络变量已用的名字 → 拒",
                !okLocalTarget && (localTargetError ?? string.Empty).Contains("已存在同名变量"),
                "ok=" + okLocalTarget + " err=" + (localTargetError ?? "null"));

            var okToLocal = reg.TryRename(d, "LocalTag", out var toLocalError);
            Check("C6.6 网络变量改成本地变量已用的名字 → 拒（提示点名本地变量）",
                !okToLocal && (toLocalError ?? string.Empty).Contains("本地变量"),
                "ok=" + okToLocal + " err=" + (toLocalError ?? "null"));

            var okIdempotent = reg.TryRename(d, "Alarm[93]", out var idemError);
            Check("C6.7 改成自己当前的名字 → 幂等成功", okIdempotent,
                "ok=" + okIdempotent + " err=" + (idemError ?? "null"));

            var okAnother = reg.TryRename(c, "Alarm[90]", out var anotherError);
            Check("C6.8 L3 的变量改成 L1 已用的名字 → 允许，按名解析 2 个候选",
                okAnother && reg.FindAllByName("Alarm[90]").Count == 2,
                "ok=" + okAnother + " count=" + reg.FindAllByName("Alarm[90]").Count
                + " err=" + (anotherError ?? "null"));
        }

        // ================= C7 轮询层（未改） =================
        private static void C7_PollingUnchanged()
        {
            var manager = new AdvancedCommunicationManager();
            Exception error = null;
            try
            {
                manager.RegisterVariable(new CommunicationVariable { ConnectionName = "L1", VariableName = "Alarm[90]", Address = "40001" });
                manager.RegisterVariable(new CommunicationVariable { ConnectionName = "L2", VariableName = "Alarm[90]", Address = "40001" });
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Check("C7.1 轮询层仍接受跨连接同名", error == null,
                error == null ? "两次注册均无异常" : error.GetType().Name + ": " + error.Message);
        }

        // ================= C8 名字规则（未改） =================
        private static void C8_NameRulesUnchanged()
        {
            var okPlain = VariableNameRules.TryValidateNewName("Alarm[90]", out _);
            var okSlash = VariableNameRules.TryValidateNewName("L2/Alarm[90]", out _);
            var okDot = VariableNameRules.TryValidateNewName("L2.Alarm[90]", out _);
            Check("C8.1 名字规则仍允许 [ ] / .", okPlain && okSlash && okDot,
                $"plain={okPlain} slash={okSlash} dot={okDot}");

            var okReserved = VariableNameRules.TryValidateNewName("System.Foo", out var reservedError);
            Check("C8.2 System. 前缀仍被拒",
                !okReserved && (reservedError ?? string.Empty).Contains("保留前缀"),
                "err=" + (reservedError ?? "null"));
        }

        // ================= C9 注册表暴力一致性（批 1） =================
        private static void C9_FuzzConsistency()
        {
            var names = new[] { "Alarm[90]", "alarm[90]", "Alarm[91]", "Speed", "speed", "Temp" };
            var random = new Random(20261029);
            var collection = new ObservableCollection<IVariable>();

            for (var i = 0; i < 200; i++)
                collection.Add(VariableFactory.CreateLocal(names[random.Next(names.Length)], typeof(int), "fuzz-" + i, i));

            var reg = new VariableRegistry(collection);
            var distinctNames = collection.Select(v => v.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var mismatch = new List<string>();
            foreach (var name in distinctNames)
            {
                var single = reg.FindByName(name);
                var all = reg.FindAllByName(name);
                if ((single != null) != (all.Count == 1))
                    mismatch.Add($"{name}: single={(single == null ? "null" : "hit")} count={all.Count}");
            }
            Check("C9.1 每个名字：FindByName 非空 ⟺ 候选唯一",
                mismatch.Count == 0,
                mismatch.Count == 0 ? $"names={distinctNames.Length} 全部一致" : string.Join(" | ", mismatch));

            var countMismatch = new List<string>();
            foreach (var name in distinctNames)
            {
                var expected = collection.Count(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
                var actual = reg.FindAllByName(name).Count;
                if (expected != actual)
                    countMismatch.Add($"{name}: expected={expected} actual={actual}");
            }
            Check("C9.2 FindAllByName 数量 == 集合实际条数",
                countMismatch.Count == 0,
                countMismatch.Count == 0 ? "全部一致" : string.Join(" | ", countMismatch));

            Check("C9.3 200 个变量全部可按 Id 精确命中",
                collection.All(v => ReferenceEquals(reg.FindById(v.VariableId), v)), "");
            Check("C9.4 Count == 变量数", reg.Count == collection.Count,
                $"Count={reg.Count} expected={collection.Count}");

            var before = distinctNames.ToDictionary(
                n => n,
                n => (Single: reg.FindByName(n)?.VariableId, Count: reg.FindAllByName(n).Count),
                StringComparer.OrdinalIgnoreCase);
            reg.Rebuild();
            var afterMismatch = new List<string>();
            foreach (var name in distinctNames)
            {
                var b = before[name];
                var a = (Single: reg.FindByName(name)?.VariableId, Count: reg.FindAllByName(name).Count);
                if (b.Single != a.Single || b.Count != a.Count)
                    afterMismatch.Add($"{name}: before=({b.Single},{b.Count}) after=({a.Single},{a.Count})");
            }
            Check("C9.5 Rebuild 后逐名结果与增量路径完全一致",
                afterMismatch.Count == 0,
                afterMismatch.Count == 0 ? "全部一致" : string.Join(" | ", afterMismatch));

            var removeFailures = new List<string>();
            foreach (var name in distinctNames)
            {
                var group = reg.FindAllByName(name).ToList();
                if (group.Count < 2) continue;

                var keeper = group[0];
                for (var i = 1; i < group.Count; i++)
                    collection.Remove(group[i]);

                if (!ReferenceEquals(reg.FindByName(name), keeper))
                    removeFailures.Add($"{name}: 移除后未恢复唯一命中");
            }
            Check("C9.6 移除重名组多余项后全部恢复唯一命中",
                removeFailures.Count == 0,
                removeFailures.Count == 0 ? "全部恢复" : string.Join(" | ", removeFailures));
            Check("C9.7 移除后 Count 与集合一致", reg.Count == collection.Count,
                $"Count={reg.Count} expected={collection.Count}");
        }

        // ================= C10 作用域真值表（批 2） =================
        private static void C10_ScopeTruthTable()
        {
            var pool = new IVariable[]
            {
                Net("T", "L1", 1),                                        // 0 net L1
                Net("T", "L2", 2),                                        // 1 net L2
                Net("T", "", 3),                                          // 2 net 无连接
                VariableFactory.CreateLocal("T", typeof(int), "本地A", 4), // 3 local A
                VariableFactory.CreateLocal("T", typeof(int), "本地B", 5), // 4 local B
                new SystemVariableModel(Guid.NewGuid(), "T", typeof(int), "系统变量", null, "Motion"), // 5 system
            };

            // 硬编码期望（同名前提下）：不同连接的网络变量互不冲突；任何本地/系统变量与任何变量冲突
            var expected = new[,]
            {
                //  L1     L2     null   localA localB sys
                {  true,  false, false, true,  true,  true }, // L1
                {  false, true,  false, true,  true,  true }, // L2
                {  false, false, true,  true,  true,  true }, // null 连接
                {  true,  true,  true,  true,  true,  true }, // localA
                {  true,  true,  true,  true,  true,  true }, // localB
                {  true,  true,  true,  true,  true,  true }, // system
            };

            var failures = new List<string>();
            for (var i = 0; i < pool.Length; i++)
            for (var j = 0; j < pool.Length; j++)
            {
                var actual = VariableNameScope.Conflicts(pool[i], pool[j]);
                if (actual != expected[i, j])
                    failures.Add($"[{Describe(pool[i])} vs {Describe(pool[j])}] expected={expected[i, j]} actual={actual}");
            }

            Check("C10.1 两两作用域真值表（6×6）", failures.Count == 0,
                failures.Count == 0 ? "36 组全部符合" : string.Join(" | ", failures.Take(5)));

            // 目标作用域重载：本地目标（null/空）与任何变量都冲突；对方是本地/系统变量时也冲突
            var targetFailures = new List<string>();
            foreach (var scope in new[] { null, "", "L1", "L2" })
            foreach (var other in pool)
            {
                var actual = VariableNameScope.Conflicts(scope, other);

                bool want;
                if (string.IsNullOrEmpty(scope))
                    want = true;                       // 目标是本地变量 → 全方案唯一
                else if (other is NetworkVariableModel n)
                    want = string.Equals((n.ConnectionName ?? "").Trim(), scope!.Trim(), StringComparison.Ordinal);
                else
                    want = true;                       // 对方是本地/系统变量 → 与任何作用域都冲突

                if (actual != want)
                    targetFailures.Add($"[scope={scope ?? "null"} vs {Describe(other)}] expected={want} actual={actual}");
            }
            Check("C10.2 目标作用域重载真值表（4×6）", targetFailures.Count == 0,
                targetFailures.Count == 0 ? "24 组全部符合" : string.Join(" | ", targetFailures.Take(5)));

            // 对称性
            var asymmetric = new List<string>();
            for (var i = 0; i < pool.Length; i++)
            for (var j = 0; j < pool.Length; j++)
                if (VariableNameScope.Conflicts(pool[i], pool[j]) != VariableNameScope.Conflicts(pool[j], pool[i]))
                    asymmetric.Add($"{Describe(pool[i])}/{Describe(pool[j])}");
            Check("C10.3 冲突判定对称", asymmetric.Count == 0,
                asymmetric.Count == 0 ? "36 组对称" : string.Join(" | ", asymmetric));
        }

        private static string Describe(IVariable v)
            => v is NetworkVariableModel n ? $"net:{n.ConnectionName}" : v.VariableType.ToString();

        // ================= C11 导入判定暴力（批 2） =================
        private static void C11_ImportBruteForce()
        {
            var devices = new[] { "L1", "L2", "L3", "L4" };
            var names = new[] { "Alarm[90]", "Alarm[91]", "Speed", "Temp", "Ready", "Fault" };
            var random = new Random(20261029);

            // 造一个既有变量池：每台设备随机拥有部分名字（同设备内不重复）
            var all = new List<IVariable>();
            var byDevice = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var device in devices)
            {
                var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                {
                    if (random.Next(100) < 45)
                    {
                        all.Add(Net(name, device, 0));
                        owned.Add(name);
                    }
                }
                byDevice[device] = owned;
            }

            // 再放两个本地变量，名字故意与点位重名
            var localA = VariableFactory.CreateLocal("Speed", typeof(int), "本地 Speed", 0);
            var localB = VariableFactory.CreateLocal("Fault", typeof(int), "本地 Fault", 0);
            all.Add(localA);
            all.Add(localB);

            var cases = 0;
            var mismatches = new List<string>();
            foreach (var device in devices)
            {
                var conn = new CommunicationConfig { ConnectionName = device, Protocol = CommunicationType.ModbusTcp };

                // 真实调用方（DeviceVariableImportViewModel.DeviceVariables）传的是"本连接下的既有变量"，
                // 这里必须照做：不传的话，本设备自己的同名变量会被当成"别人的"而误判成冲突。
                var existingOnDevice = all.OfType<NetworkVariableModel>()
                    .Where(v => string.Equals(v.ConnectionName, device, StringComparison.Ordinal))
                    .ToList();

                foreach (var name in names)
                {
                    cases++;
                    var plan = Plan(conn, name, existingOnDevice, all);
                    var item = plan.Items.FirstOrDefault();
                    var actual = item?.Status ?? DeviceVariableImportStatus.Error;

                    var localClash = name.Equals("Speed", StringComparison.OrdinalIgnoreCase)
                                     || name.Equals("Fault", StringComparison.OrdinalIgnoreCase);
                    var owned = byDevice[device].Contains(name);

                    var acceptable = localClash
                        ? new[] { DeviceVariableImportStatus.Error }
                        : owned
                            ? new[] { DeviceVariableImportStatus.Update, DeviceVariableImportStatus.Unchanged }
                            : new[] { DeviceVariableImportStatus.New };

                    if (!acceptable.Contains(actual))
                        mismatches.Add($"{device}/{name}: expected={string.Join("/", acceptable)} actual={actual} note={item?.Note}");
                }
            }

            Check("C11.1 4 设备 × 6 名字 = 24 组导入判定全部符合预期",
                mismatches.Count == 0 && cases == 24,
                mismatches.Count == 0 ? $"cases={cases} 全部符合" : string.Join(" | ", mismatches.Take(5)));
        }

        // ================= C12 导入落地端到端（批 2） =================
        private static void C12_ImportApplyEndToEnd()
        {
            var ws = new WorkspaceContext();
            // Apply 会读写 CurrentSolution（显示分组表），没有方案会直接抛"当前没有打开的方案"
            ws.SwitchSolution(new SolutionModel());
            ws.GlobalVariables.Clear();

            var l1 = Net("Alarm[90]", "L1", 1);
            ws.GlobalVariables.Add(l1);

            var conn = new CommunicationConfig { ConnectionName = "L2", Protocol = CommunicationType.ModbusTcp };
            var plan = Plan(conn, "Alarm[90]", Array.Empty<NetworkVariableModel>(), ws.GlobalVariables.ToList());
            var item = plan.Items.FirstOrDefault();
            Check("C12.1 第二台设备同名导入计划为新增",
                item != null && item.Status == DeviceVariableImportStatus.New,
                "status=" + (item?.Status.ToString() ?? "null"));

            DeviceVariableImportResult result = null;
            Exception error = null;
            try
            {
                result = DeviceVariableImportService.Apply(plan, ws, _ => true, deleteMissing: false, skipErrors: false);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Check("C12.2 应用成功且新增 1 条", error == null && result != null && result.Added == 1,
                error == null ? $"added={result?.Added}" : error.GetType().Name + ": " + error.Message);

            var l2 = ws.GlobalVariables.OfType<NetworkVariableModel>()
                .FirstOrDefault(v => v.ConnectionName == "L2" && v.Name == "Alarm[90]");

            Check("C12.3 两台设备的同名变量共存（各自独立 Id）",
                l2 != null && l2.VariableId != l1.VariableId,
                "L2Id=" + (l2?.VariableId.ToString() ?? "null"));

            Check("C12.4 落地后按名解析为歧义、按 Id 各自命中",
                ws.VariableRegistry.FindByName("Alarm[90]") == null
                && ws.VariableRegistry.FindAllByName("Alarm[90]").Count == 2
                && ReferenceEquals(ws.VariableRegistry.FindById(l1.VariableId), l1)
                && ReferenceEquals(ws.VariableRegistry.FindById(l2!.VariableId), l2),
                "count=" + ws.VariableRegistry.FindAllByName("Alarm[90]").Count);

            // 再导一次同设备同名 → 应走更新而不是新增
            var plan2 = Plan(conn, "Alarm[90]",
                ws.GlobalVariables.OfType<NetworkVariableModel>().Where(v => v.ConnectionName == "L2").ToList(),
                ws.GlobalVariables.ToList());
            var item2 = plan2.Items.FirstOrDefault();
            Check("C12.5 同设备再次导入 → 更新（不新增、不报冲突）",
                item2 != null
                && item2.Status != DeviceVariableImportStatus.Error
                && item2.Existing != null,
                "status=" + (item2?.Status.ToString() ?? "null"));
        }
        // ================= C13 新建路径的撞名判定（FindBlocker） =================
        private static void C13_FindBlocker()
        {
            var netL1 = Net("T", "L1", 1);
            var netL2 = Net("T", "L2", 2);
            var localT = VariableFactory.CreateLocal("T", typeof(int), "本地 T", 3);
            var localOther = VariableFactory.CreateLocal("Other", typeof(int), "本地 Other", 4);
            var pool = new IVariable[] { netL1, netL2, localT, localOther };

            Check("C13.1 目标在 L1 + 名字 T → 命中 L1 的那个",
                ReferenceEquals(VariableNameScope.FindBlocker(pool, "T", "L1"), netL1),
                Describe(VariableNameScope.FindBlocker(pool, "T", "L1")!));

            Check("C13.2 目标在 L2 + 名字 T → 命中 L2 的那个（不是 L1）",
                ReferenceEquals(VariableNameScope.FindBlocker(pool, "T", "L2"), netL2),
                Describe(VariableNameScope.FindBlocker(pool, "T", "L2")!));

            Check("C13.3 目标在 L3 + 名字 T → 命中本地变量（本地无设备，与任何设备都冲突）",
                ReferenceEquals(VariableNameScope.FindBlocker(pool, "T", "L3"), localT),
                Describe(VariableNameScope.FindBlocker(pool, "T", "L3")!));

            Check("C13.4 目标在 L1 + 名字 Other → 命中本地 Other",
                ReferenceEquals(VariableNameScope.FindBlocker(pool, "Other", "L1"), localOther),
                Describe(VariableNameScope.FindBlocker(pool, "Other", "L1")!));

            Check("C13.5 目标在 L1 + 未占用的名字 → 允许（null）",
                VariableNameScope.FindBlocker(pool, "BrandNew", "L1") == null, "null");

            Check("C13.6 名字大小写不敏感（t vs T）",
                VariableNameScope.FindBlocker(pool, "t", "L1") != null, "");

            Check("C13.7 空名字/空集合 → 允许（null）",
                VariableNameScope.FindBlocker(pool, "", "L1") == null
                && VariableNameScope.FindBlocker(pool, null, "L1") == null
                && VariableNameScope.FindBlocker(null, "T", "L1") == null, "");

            // 只有别的设备有同名时，新建应当放行（批 2 的核心）
            var onlyL2 = new IVariable[] { netL2 };
            Check("C13.8 只有 L2 有同名 → 在 L1 新建放行（null）",
                VariableNameScope.FindBlocker(onlyL2, "T", "L1") == null, "null");

            // 目标本身是本地变量（scope=null）→ 与任何同名变量冲突
            Check("C13.9 新建本地变量 + 已有同名网络变量 → 命中",
                ReferenceEquals(VariableNameScope.FindBlocker(onlyL2, "T", null), netL2),
                Describe(VariableNameScope.FindBlocker(onlyL2, "T", null)!));
        }

        // ================= C14 候选快照（批 3） =================
        private static void C14_CandidateSnapshot()
        {
            var ws = new WorkspaceContext();
            ws.SwitchSolution(new SolutionModel());
            ws.GlobalVariables.Clear();

            var a = Net("Alarm[90]", "L1", 1);
            var b = Net("Alarm[90]", "L2", 2);
            var local = VariableFactory.CreateLocal("Solo", typeof(int), "本地", 3);
            ws.GlobalVariables.Add(a);
            ws.GlobalVariables.Add(b);
            ws.GlobalVariables.Add(local);

            var options = VisionMaster.Helpers.PluginVariableOptions.Build(ws, Guid.Empty);
            var dup = options.Where(o => o.Name == "Alarm[90]").ToList();

            Check("C14.1 两台设备的同名点位都在快照里", dup.Count == 2, "count=" + dup.Count);
            Check("C14.2 候选带设备名与各自 Id",
                dup.Any(o => o.Device == "L1" && o.VariableId == a.VariableId)
                && dup.Any(o => o.Device == "L2" && o.VariableId == b.VariableId),
                string.Join(" | ", dup.Select(o => o.Device + ":" + o.VariableId)));
            Check("C14.3 Caption 含设备（下拉可分辨）",
                dup.All(o => o.Caption.Contains("· " + o.Device)),
                string.Join(" | ", dup.Select(o => o.Caption)));

            var solo = options.FirstOrDefault(o => o.Name == "Solo");
            Check("C14.4 本地变量 Caption 不带设备段",
                solo != null && !solo.Caption.Contains("· "), solo?.Caption ?? "null");

            // 高负载：200 台设备 × 同一名字 → 200 条候选（一条都不能吞）
            var ws2 = new WorkspaceContext();
            ws2.SwitchSolution(new SolutionModel());
            ws2.GlobalVariables.Clear();
            for (var i = 0; i < 200; i++)
                ws2.GlobalVariables.Add(Net("Tag", "D" + i, i));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var big = VisionMaster.Helpers.PluginVariableOptions.Build(ws2, Guid.Empty);
            sw.Stop();

            Check("C14.5 高负载：200 台同名 → 200 条候选（无吞噬）",
                big.Count(o => o.Name == "Tag") == 200,
                "count=" + big.Count(o => o.Name == "Tag") + " elapsed=" + sw.ElapsedMilliseconds + "ms");
        }

        // ================= C15 变量赋值候选与解析（批 3） =================
        private static void C15_AssignmentCandidates()
        {
            var a = Net("Alarm[90]", "L1", 1);
            var b = Net("Alarm[90]", "L2", 2);
            var options = new List<VariableOption>
            {
                new() { VariableId = a.VariableId, Name = "Alarm[90]", Scope = "Global", Device = "L1", TypeName = "Int32" },
                new() { VariableId = b.VariableId, Name = "Alarm[90]", Scope = "Global", Device = "L2", TypeName = "Int32" },
                new() { VariableId = Guid.Empty, Name = "Rt", Scope = "Runtime", TypeName = "Int32" },
            };

            var plugin = new VariableAssignmentPlugin();
            plugin.SetConfigContext(new PluginConfigContext { Variables = options });

            var row = new AssignmentRow { Scope = VariableAssignmentPlugin.ScopeGlobal, VariableName = "Alarm[90]" };
            var cands = plugin.BuildCandidates(row);
            Check("C15.1 重名候选两条都在（不再按名字去重）",
                cands.Count(c => c.Name == "Alarm[90]") == 2,
                "count=" + cands.Count(c => c.Name == "Alarm[90]"));
            Check("C15.2 候选带 Id 与设备，Caption 可分辨",
                cands.Any(c => c.VariableId == b.VariableId && c.Caption.Contains("L2")),
                string.Join(" | ", cands.Select(c => c.Caption)));

            row.VariableId = b.VariableId;
            var hit = plugin.FindVariable(row);
            Check("C15.3 FindVariable(行) 按 Id 命中 L2",
                hit != null && hit.VariableId == b.VariableId && hit.Device == "L2",
                hit == null ? "null" : hit.Device);

            row.VariableName = "Alarm[90]_RENAMED";
            var hitById = plugin.FindVariable(row);
            Check("C15.4 名字失效但 Id 有效 → 仍解析到同一变量（改名不断链）",
                hitById != null && hitById.VariableId == b.VariableId,
                hitById == null ? "null" : hitById.Name);

            var stale = new AssignmentRow { Scope = VariableAssignmentPlugin.ScopeGlobal, VariableName = "Gone" };
            var staleCands = plugin.BuildCandidates(stale);
            Check("C15.5 名字查不到 → 补一条「已失效」只读项",
                staleCands.Count > 0 && staleCands[0].IsInvalid,
                staleCands.Count == 0 ? "empty" : staleCands[0].Caption);

            var rt = new AssignmentRow { Scope = VariableAssignmentPlugin.ScopeRuntime, VariableName = "NotDeclared" };
            var rtCands = plugin.BuildCandidates(rt);
            Check("C15.6 运行时作用域不补失效项", rtCands.All(c => !c.IsInvalid), "count=" + rtCands.Count);
        }

        // ================= C16 变量赋值运行期写入（批 3） =================
        private static void C16_AssignmentRuntimeWrite()
        {
            var method = typeof(VariableAssignmentPlugin).GetMethod(
                "TryAssign", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method == null || method.GetParameters().Length != 7)
            {
                Check("C16.0 反射定位 TryAssign(7 参)", false,
                    "签名变了：探针需同步（Id 是否仍作为第 3 个参数）");
                return;
            }
            Check("C16.0 反射定位 TryAssign(7 参)", true, method.ToString());

            var plugin = new VariableAssignmentPlugin();
            var b = Net("Alarm[90]", "L2", 2);

            // ① 带 Id：Id 与名字原样交给 SharedVariables（运行期由它 Id 优先解析）
            var ctx = new FakeContext();
            var args = new object[] { ctx, "Alarm[90]", b.VariableId, "Global", false, 999, null };
            var ok = (bool)method.Invoke(plugin, args);
            Check("C16.1 带 Id 写：Id 与名字原样传给 SharedVariables.TryWrite",
                ok && ctx.Shared.WriteCount == 1 && ctx.Shared.LastId == b.VariableId
                && ctx.Shared.LastName == "Alarm[90]" && Equals(ctx.Shared.LastValue, 999),
                $"ok={ok} id={ctx.Shared.LastId} name={ctx.Shared.LastName} value={ctx.Shared.LastValue}");

            // ② 旧配置（无 Id）：仍按名字写
            var ctx2 = new FakeContext();
            var args2 = new object[] { ctx2, "Solo", Guid.Empty, "Global", false, 7, null };
            var ok2 = (bool)method.Invoke(plugin, args2);
            Check("C16.2 旧配置（无 Id）仍按名字写",
                ok2 && ctx2.Shared.LastId == Guid.Empty && ctx2.Shared.LastName == "Solo",
                $"ok={ok2} id={ctx2.Shared.LastId} name={ctx2.Shared.LastName}");

            // ③ 空名字 + 空 Id：明确拒绝且不调用写入
            var ctx3 = new FakeContext();
            var args3 = new object[] { ctx3, "", Guid.Empty, "Global", false, 1, null };
            var ok3 = (bool)method.Invoke(plugin, args3);
            Check("C16.3 空名字 + 空 Id → 拒绝且不调用写入",
                !ok3 && ctx3.Shared.WriteCount == 0 && ((string)args3[6] ?? "").Contains("不能为空"),
                $"ok={ok3} err={args3[6]}");

            // ④ 运行时作用域：不碰 SharedVariables
            var ctx4 = new FakeContext();
            var args4 = new object[] { ctx4, "RtLocal", Guid.Empty, "Runtime", true, 5, null };
            var ok4 = (bool)method.Invoke(plugin, args4);
            Check("C16.4 运行时作用域走本地变量池、不碰共享变量",
                ok4 && ctx4.Shared.WriteCount == 0 && ctx4.LocalVariables.ContainsKey("RtLocal"),
                $"ok={ok4} writes={ctx4.Shared.WriteCount}");
        }

        // ================= C17 数据记录 / 结果上报读取（批 3） =================
        private static void C17_RecordAndUploadRead()
        {
            var a = Net("Alarm[90]", "L1", 111);
            var b = Net("Alarm[90]", "L2", 222);

            var ws = new WorkspaceContext();
            ws.SwitchSolution(new SolutionModel());
            ws.GlobalVariables.Clear();
            ws.GlobalVariables.Add(a);
            ws.GlobalVariables.Add(b);

            var ctx = new VisionMaster.Services.ExecutionContext(null)
            {
                Workspace = ws,
            };

            var dr = typeof(Plugin.DataRecord.DataRecordPlugin).GetMethod(
                "TryResolveVariable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var ur = typeof(Plugin.ResultUpload.ResultUploadPlugin).GetMethod(
                "TryResolveVariable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (dr == null || ur == null || dr.GetParameters().Length != 4 || ur.GetParameters().Length != 4)
            {
                Check("C17.0 反射定位两个 TryResolveVariable(4 参)", false, "签名变了：探针需同步");
                return;
            }
            Check("C17.0 反射定位两个 TryResolveVariable(4 参)", true, "");

            var drPlugin = new Plugin.DataRecord.DataRecordPlugin();
            var args = new object[] { ctx, b.VariableId, "Alarm[90]", null };
            var ok = (bool)dr.Invoke(drPlugin, args);
            Check("C17.1 数据记录：带 Id 读到 L2 的值", ok && Equals(args[3], 222),
                $"ok={ok} value={args[3]}");

            var args2 = new object[] { ctx, Guid.Empty, "Alarm[90]", null };
            var ok2 = (bool)dr.Invoke(drPlugin, args2);
            Check("C17.2 数据记录：无 Id + 重名 → 失败（明确报歧义，不取第一个）",
                !ok2 && args2[3] == null, $"ok={ok2} value={args2[3]}");

            var args3 = new object[] { ctx, b.VariableId, "", null };
            var ok3 = (bool)dr.Invoke(drPlugin, args3);
            Check("C17.3 数据记录：只有 Id（名字为空）也能读到 L2", ok3 && Equals(args3[3], 222),
                $"ok={ok3} value={args3[3]}");

            var urPlugin = new Plugin.ResultUpload.ResultUploadPlugin();
            var uargs = new object[] { ctx, b.VariableId, "Alarm[90]", null };
            var uok = (bool)ur.Invoke(urPlugin, uargs);
            Check("C17.4 结果上报：带 Id 读到 L2 的值", uok && Equals(uargs[3], 222),
                $"ok={uok} value={uargs[3]}");

            ctx.LocalVariables["Alarm[90]"] = 555;
            var largs = new object[] { ctx, b.VariableId, "Alarm[90]", null };
            var lok = (bool)dr.Invoke(drPlugin, largs);
            Check("C17.5 局部变量优先于共享变量（既定语义不变）", lok && Equals(largs[3], 555),
                $"ok={lok} value={largs[3]}");
        }

        // ================= C18 配置模型 JSON 往返（批 3） =================
        private static void C18_Serialization()
        {
            var id = Guid.NewGuid();

            var row = new AssignmentRow { VariableName = "Alarm[90]", VariableId = id, Scope = "Global" };
            var rowBack = Newtonsoft.Json.JsonConvert.DeserializeObject<AssignmentRow>(
                Newtonsoft.Json.JsonConvert.SerializeObject(row));
            Check("C18.1 AssignmentRow.VariableId 序列化往返", rowBack.VariableId == id,
                rowBack.VariableId.ToString());

            var oldRow = Newtonsoft.Json.JsonConvert.DeserializeObject<AssignmentRow>(
                "{\"VariableName\":\"Old\",\"Scope\":\"Global\"}");
            Check("C18.2 旧 JSON（无 VariableId）→ Guid.Empty，不抛异常",
                oldRow != null && oldRow.VariableId == Guid.Empty, oldRow?.VariableId.ToString());

            var col = new Plugin.DataRecord.RecordColumnDef
            {
                Name = "c",
                Source = Plugin.DataRecord.ColumnSource.Variable,
                VariableName = "Alarm[90]",
                VariableId = id,
            };
            var colBack = Newtonsoft.Json.JsonConvert.DeserializeObject<Plugin.DataRecord.RecordColumnDef>(
                Newtonsoft.Json.JsonConvert.SerializeObject(col));
            Check("C18.3 RecordColumnDef.VariableId 往返", colBack.VariableId == id, colBack.VariableId.ToString());

            var fld = new Plugin.ResultUpload.UploadFieldDef
            {
                Name = "f",
                Source = Plugin.ResultUpload.FieldSource.Variable,
                VariableName = "Alarm[90]",
                VariableId = id,
            };
            var fldBack = Newtonsoft.Json.JsonConvert.DeserializeObject<Plugin.ResultUpload.UploadFieldDef>(
                Newtonsoft.Json.JsonConvert.SerializeObject(fld));
            Check("C18.4 UploadFieldDef.VariableId 往返", fldBack.VariableId == id, fldBack.VariableId.ToString());
        }

        // ================= C19 目标 Id 捕获：边界 + 暴力（批 3） =================
        private static void C19_CaptureId()
        {
            var a = Net("A", "L1", 1);
            var b = Net("A", "L2", 2);
            var opts = new List<VariableOption>
            {
                new() { VariableId = a.VariableId, Name = "A", Scope = "Global", Device = "L1" },
                new() { VariableId = b.VariableId, Name = "A", Scope = "Global", Device = "L2" },
                new() { VariableId = Guid.NewGuid(), Name = "U", Scope = "Global" },
                new() { VariableId = Guid.Empty, Name = "R", Scope = "Runtime" },
            };

            Check("C19.1 null 快照 → 保留原 Id（不动配置）",
                VariableOptionResolver.CaptureId(null, b.VariableId, "A", "Global") == b.VariableId, "");
            Check("C19.2 已有 Id 且可解析 → 保留（改名后仍有效）",
                VariableOptionResolver.CaptureId(opts, b.VariableId, "Whatever", "Global") == b.VariableId, "");
            Check("C19.3 无 Id + 唯一名字 → 捕获",
                VariableOptionResolver.CaptureId(opts, Guid.Empty, "U", "Global") == opts[2].VariableId, "");
            Check("C19.4 无 Id + 重名 → 留空（不替用户选）",
                VariableOptionResolver.CaptureId(opts, Guid.Empty, "A", "Global") == Guid.Empty, "");
            Check("C19.5 空名字 + 空 Id → 空",
                VariableOptionResolver.CaptureId(opts, Guid.Empty, "", "Global") == Guid.Empty, "");
            Check("C19.6 作用域不匹配 → 空（运行时变量不参与全局捕获）",
                VariableOptionResolver.CaptureId(opts, Guid.Empty, "R", "Global") == Guid.Empty, "");
            Check("C19.7 Id 已失效（变量被删）→ 回退按名解析，重名留空",
                VariableOptionResolver.CaptureId(opts, Guid.NewGuid(), "A", "Global") == Guid.Empty, "");
            Check("C19.8 名字前后空白被 Trim",
                VariableOptionResolver.CaptureId(opts, Guid.Empty, "  U  ", "Global") == opts[2].VariableId, "");
            Check("C19.9 名字大小写不敏感",
                VariableOptionResolver.CaptureId(opts, Guid.Empty, "u", "Global") == opts[2].VariableId, "");

            var longName = new string('X', 4000);
            var longOpt = new List<VariableOption>
            {
                new() { VariableId = Guid.NewGuid(), Name = longName, Scope = "Global" },
            };
            Check("C19.10 超长名字（4000 字符）仍能唯一捕获",
                VariableOptionResolver.CaptureId(longOpt, Guid.Empty, longName, "Global") == longOpt[0].VariableId, "");

            // 高负载 + 反复执行：5000 候选 × 500 行 × 20 轮
            var big = new List<VariableOption>(5000);
            for (var i = 0; i < 5000; i++)
                big.Add(new VariableOption
                {
                    VariableId = Guid.NewGuid(),
                    Name = "V" + (i % 4000),
                    Scope = "Global",
                    Device = "D" + (i % 7),
                });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wrongDup = 0;
            var wrongUnique = 0;
            for (var round = 0; round < 20; round++)
            {
                // 重复名（V0..V499 各出现两次）→ 必须留空
                for (var row = 0; row < 500; row++)
                {
                    if (VariableOptionResolver.CaptureId(big, Guid.Empty, "V" + row, "Global") != Guid.Empty)
                        wrongDup++;
                }

                // 唯一名（V1000..V1499 各出现一次）→ 必须捕获到那一条
                for (var row = 1000; row < 1500; row++)
                {
                    var expected = big.First(o => o.Name == "V" + row).VariableId;
                    if (VariableOptionResolver.CaptureId(big, Guid.Empty, "V" + row, "Global") != expected)
                        wrongUnique++;
                }
            }
            sw.Stop();

            Check("C19.11 暴力：5000 候选 × 1000 行 × 20 轮 = 20000 次捕获全部正确",
                wrongDup == 0 && wrongUnique == 0,
                $"wrongDup={wrongDup} wrongUnique={wrongUnique} elapsed={sw.ElapsedMilliseconds}ms");

            // 确认期性能守门：真实量级（2000 行 × 2000 候选，每次确认都全量跑一遍）
            var perfOptions = new List<VariableOption>(2000);
            for (var i = 0; i < 2000; i++)
                perfOptions.Add(new VariableOption { VariableId = Guid.NewGuid(), Name = "P" + i, Scope = "Global" });

            var perf = System.Diagnostics.Stopwatch.StartNew();
            var captured = 0;
            for (var row = 0; row < 2000; row++)
            {
                if (VariableOptionResolver.CaptureId(perfOptions, Guid.Empty, "P" + row, "Global") != Guid.Empty)
                    captured++;
            }
            perf.Stop();

            Check("C19.12 确认期性能：2000 行 × 2000 候选 < 2000ms 且全部捕获",
                captured == 2000 && perf.ElapsedMilliseconds < 2000,
                $"captured={captured} elapsed={perf.ElapsedMilliseconds}ms");
        }

        // ================= C20 绑定弹窗候选树（两套设备的同名变量） =================
        private static void C20_BindingDialogCandidates()
        {
            var a = Net("A", "A_PLC", 1);
            var b = Net("A", "B_PLC", 2);

            var tree = VisionMaster.Helpers.FlowQueryHelper.GetAvailableVariablesTree(
                new IVariable[] { a, b },
                Array.Empty<StepModel>(),
                null);

            var globalNode = tree.FirstOrDefault(n => n.ModuleGroup == "Global");
            var ports = globalNode?.OutputDefinitions?.Where(p => p.Name == "A").ToList()
                        ?? new List<PortDefinition>();

            Check("C20.1 绑定弹窗候选树里两台设备的 A 都在", ports.Count == 2, "count=" + ports.Count);
            Check("C20.2 两条候选带各自 VariableId（绑定按身份落地）",
                ports.Count == 2 && ports.Select(p => p.VariableId).Distinct().Count() == 2,
                string.Join(" | ", ports.Select(p => p.VariableId.ToString())));
            Check("C20.3 候选描述里带设备名（肉眼可分辨）",
                ports.Any(p => (p.Description ?? string.Empty).Contains("A_PLC"))
                && ports.Any(p => (p.Description ?? string.Empty).Contains("B_PLC")),
                string.Join(" | ", ports.Select(p => p.Description)));
        }

        // ================= C21 绑定弹窗：显示串带设备 + 已绑定按 Id 判定 =================
        private static void C21_BindingDialogDisplay()
        {
            var a = Net("A", "A_PLC", 1);
            var b = Net("A", "B_PLC", 2);
            var local = VariableFactory.CreateLocal("X", typeof(int), "本地", 3);

            var tree = VisionMaster.Helpers.FlowQueryHelper.GetAvailableVariablesTree(
                new IVariable[] { a, b, local }, Array.Empty<StepModel>(), null);
            var node = tree.First(n => n.ModuleGroup == "Global");
            var portA = node.OutputDefinitions.First(p => p.Name == "A" && p.Device == "A_PLC");
            var portB = node.OutputDefinitions.First(p => p.Name == "A" && p.Device == "B_PLC");
            var portX = node.OutputDefinitions.First(p => p.Name == "X");

            var displayA = VisionMaster.ViewModels.VariableBindingViewModel.BuildVariableDisplayAddress(node.Name, portA, -1);
            var displayB = VisionMaster.ViewModels.VariableBindingViewModel.BuildVariableDisplayAddress(node.Name, portB, -1);
            var displayX = VisionMaster.ViewModels.VariableBindingViewModel.BuildVariableDisplayAddress(node.Name, portX, -1);
            var displayArr = VisionMaster.ViewModels.VariableBindingViewModel.BuildVariableDisplayAddress(node.Name, portB, 2);

            Check("C21.1 候选端口带设备名",
                portA.Device == "A_PLC" && portB.Device == "B_PLC",
                portA.Device + " / " + portB.Device);
            Check("C21.2 两台设备的 A 显示串不同（界面能分辨）",
                !string.Equals(displayA, displayB, StringComparison.Ordinal),
                displayA + "  !=  " + displayB);
            Check("C21.3 本地变量显示串格式不变（无设备段）",
                displayX == "全局变量 (Global).X", displayX);
            Check("C21.4 数组下标仍在最后",
                displayArr == "全局变量 (Global).B_PLC.A[2]", displayArr);

            // 改名级联：设备段必须保留（这正是"设备插在变量名之前"的原因）
            var ws = new WorkspaceContext();
            var solution = new SolutionModel();
            var flow = new FlowModel { FlowName = "C21" };
            solution.Flows.Add(flow);
            ws.SwitchSolution(solution);
            ws.SwitchFlow(flow);
            ws.GlobalVariables.Clear();
            ws.GlobalVariables.Add(b);

            var step = new ActionStep("", "S", "NoSuchPlugin", "S_0");
            step.LinkedSources["In"] = new LinkReference(
                LinkKind.GlobalVariable, Guid.Empty, "A", displayB)
            {
                TargetVariableId = b.VariableId,
            };
            flow.Steps.Add(step);

            var renamed = ws.VariableRegistry.TryRename(b, "A_NEW", out var renameError);
            var after = step.LinkedSources["In"].DisplayAddress;
            Check("C21.5 改名级联保留设备段（只换名字那一段）",
                renamed && after == "全局变量 (Global).B_PLC.A_NEW",
                $"renamed={renamed} after={after} err={renameError}");

            // 「已绑定」判定：Id 优先、名字兜底
            var isBoundMethod = typeof(VisionMaster.Views.CandidateBoundVisibilityConverter)
                .GetMethod("IsBound",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            if (isBoundMethod == null || isBoundMethod.GetParameters().Length != 5)
            {
                Check("C21.6 反射定位 IsBound(5 参)", false, "签名变了：探针需同步");
                return;
            }
            Check("C21.6 反射定位 IsBound(5 参)", true, "");

            bool IsBound(Guid boundId, Guid candidateId, string linked = "", string nodeName = "", string portName = "")
                => (bool)isBoundMethod.Invoke(null, new object[] { boundId, candidateId, linked, nodeName, portName });

            Check("C21.7 两边都有 Id：只有选中的那台亮「已绑定」",
                IsBound(b.VariableId, b.VariableId, displayB, node.Name, "A")
                && !IsBound(b.VariableId, a.VariableId, displayA, node.Name, "A"),
                $"bound={b.VariableId}");
            Check("C21.8 旧连线（无 Id）仍按名字串兜底",
                IsBound(Guid.Empty, a.VariableId, "全局变量 (Global).A", "全局变量 (Global)", "A"),
                "");
            Check("C21.9 未绑定时不亮",
                !IsBound(Guid.Empty, a.VariableId, string.Empty, "全局变量 (Global)", "A"), "");
        }

        // ================= C22 变量赋值行内显示（设备上屏） =================
        private static void C22_AssignmentRowDisplay()
        {
            var a = Net("Alarm1[48]", "A_PLC", 1);
            var b = Net("Alarm1[48]", "B_PLC", 2);
            var options = new List<VariableOption>
            {
                new() { VariableId = a.VariableId, Name = "Alarm1[48]", Scope = "Global", Device = "A_PLC", TypeName = "Boolean" },
                new() { VariableId = b.VariableId, Name = "Alarm1[48]", Scope = "Global", Device = "B_PLC", TypeName = "Boolean" },
                new() { VariableId = Guid.NewGuid(), Name = "Local1", Scope = "Global", TypeName = "Int32" },
            };

            var plugin = new VariableAssignmentPlugin();
            plugin.SetConfigContext(new PluginConfigContext { Variables = options });

            var row = new AssignmentRow
            {
                Scope = VariableAssignmentPlugin.ScopeGlobal,
                VariableName = "Alarm1[48]",
                VariableId = b.VariableId,
            };

            var candB = plugin.BuildCandidates(row).First(c => c.VariableId == b.VariableId);
            Check("C22.1 候选行 DisplayName 带设备", candB.DisplayName == "Alarm1[48] · B_PLC", candB.DisplayName);

            var candLocal = plugin
                .BuildCandidates(new AssignmentRow { Scope = VariableAssignmentPlugin.ScopeGlobal, VariableName = "Local1" })
                .First(c => c.Name == "Local1");
            Check("C22.2 本地变量候选不带设备段", candLocal.DisplayName == "Local1", candLocal.DisplayName);

            var vm = new AssignmentRowVm(plugin, row);
            Check("C22.3 行内/右侧标题 DisplayName 带设备（按 Id 取）",
                vm.DisplayName == "Alarm1[48] · B_PLC", vm.DisplayName);

            row.VariableName = "Alarm1[48]_NEW";
            Check("C22.4 名字变了仍按 Id 显示同一台设备",
                vm.DisplayName == "Alarm1[48]_NEW · B_PLC", vm.DisplayName);

            var hint = plugin.BuildHint(row);
            Check("C22.5 提示文案点名设备", (hint.Text ?? string.Empty).Contains("设备 B_PLC"), hint.Text);

            var localHint = plugin.BuildHint(new AssignmentRow
            {
                Scope = VariableAssignmentPlugin.ScopeGlobal,
                VariableName = "Local1",
            });
            Check("C22.6 本地变量提示不带设备段",
                !(localHint.Text ?? string.Empty).Contains("设备 "), localHint.Text);
        }

        // ================= C23 重名诊断（LastProblems 按作用域） =================
        private static void C23_LastProblemsScope()
        {
            // ① 跨设备同名：合法 → 不报（不弹假警报）
            var reg1 = new VariableRegistry(new ObservableCollection<IVariable>
            {
                Net("A", "L1", 1),
                Net("A", "L2", 2),
            });
            Check("C23.1 跨设备同名 → 不进诊断", reg1.LastProblems.Count == 0,
                string.Join(" | ", reg1.LastProblems));

            // ② 同设备同名：报并点名设备
            var reg2 = new VariableRegistry(new ObservableCollection<IVariable>
            {
                Net("A", "L1", 1),
                Net("A", "L1", 2),
            });
            Check("C23.2 同设备同名 → 诊断点名设备",
                reg2.LastProblems.Count == 1 && reg2.LastProblems[0].Contains("设备「L1」"),
                string.Join(" | ", reg2.LastProblems));

            // ③ 本地变量与网络变量同名：报（本地无设备，按名无法区分）
            var reg3 = new VariableRegistry(new ObservableCollection<IVariable>
            {
                VariableFactory.CreateLocal("A", typeof(int), "本地", 1),
                Net("A", "L1", 2),
            });
            Check("C23.3 本地变量与网络变量同名 → 诊断",
                reg3.LastProblems.Count == 1 && reg3.LastProblems[0].Contains("本地变量"),
                string.Join(" | ", reg3.LastProblems));

            // ④ 增量加载路径（方案加载就是 Clear + Add）同样能发现
            var coll4 = new ObservableCollection<IVariable>();
            var reg4 = new VariableRegistry(coll4);
            coll4.Clear();
            coll4.Add(Net("A", "L1", 1));
            coll4.Add(Net("A", "L1", 2));
            Check("C23.4 增量加载路径同样能发现同设备重名",
                reg4.LastProblems.Count == 1,
                string.Join(" | ", reg4.LastProblems));

            // ⑤ 改掉一个 → 诊断消失（惰性计算，永远反映当前状态）
            reg4.TryRename(coll4[1], "B", out _);
            Check("C23.5 改名后诊断消失",
                reg4.LastProblems.Count == 0, string.Join(" | ", reg4.LastProblems));
        }

        // ================= C24 数据记录 / 结果上报：文本解析目标（批 4） =================
        private static void C24_RecordUploadTarget()
        {
            var a = Net("Alarm1[48]", "A_PLC", 1);
            var b = Net("Alarm1[48]", "B_PLC", 2);
            var opts = new List<VariableOption>
            {
                new() { VariableId = a.VariableId, Name = "Alarm1[48]", Scope = "Global", Device = "A_PLC" },
                new() { VariableId = b.VariableId, Name = "Alarm1[48]", Scope = "Global", Device = "B_PLC" },
                new() { VariableId = Guid.NewGuid(), Name = "Local1", Scope = "Global" },
            };

            Guid Id(string text, Guid currentId, bool dirty, out string bare)
            {
                var t = VariableOptionResolver.ResolveTarget(opts, currentId, text, dirty, "Global");
                bare = t.Name;
                return t.VariableId;
            }

            // ① 未改文本 + Id 可解析 → 保留（变量改名不断链）
            var id1 = Id("Alarm1[48]", b.VariableId, false, out var bare1);
            Check("C24.1 文本未改 + Id 可解析 → 保留（改名不断链）",
                id1 == b.VariableId && bare1 == "Alarm1[48]", bare1);

            // ② 带设备后缀 → 按（名字, 设备）解析并归一裸名（批 4 的核心：重名可选）
            var id2 = Id("Alarm1[48] · B_PLC", Guid.Empty, true, out var bare2);
            Check("C24.2 「名字 · 设备」→ 解析到 B_PLC 并归一裸名",
                id2 == b.VariableId && bare2 == "Alarm1[48]", bare2);

            // ③ 设备不存在 → Id 空、名字原样
            var id3 = Id("Alarm1[48] · C_PLC", Guid.Empty, true, out var bare3);
            Check("C24.3 设备不存在 → Id 空且名字不丢",
                id3 == Guid.Empty && bare3 == "Alarm1[48] · C_PLC", bare3);

            // ④ 裸名重名（没写设备）→ 不猜
            var id4 = Id("Alarm1[48]", Guid.Empty, true, out var bare4);
            Check("C24.4 裸名重名（无后缀）→ Id 空不猜",
                id4 == Guid.Empty && bare4 == "Alarm1[48]", bare4);

            // ⑤ 唯一名 → 捕获
            var id5 = Id("Local1", Guid.Empty, true, out var bare5);
            Check("C24.5 唯一名 → 捕获", id5 == opts[2].VariableId && bare5 == "Local1", bare5);

            // ⑥ 变量名本身含「 · 」：精确名优先于后缀解析
            var tricky = Net("X · Y", "L9", 9);
            var opts2 = new List<VariableOption>
            {
                new() { VariableId = tricky.VariableId, Name = "X · Y", Scope = "Global", Device = "L9" },
            };
            var id6 = VariableOptionResolver.ResolveTarget(opts2, Guid.Empty, "X · Y", true, "Global");
            Check("C24.6 变量名本身含分隔符 → 精确名优先", id6.VariableId == tricky.VariableId, id6.VariableId.ToString());

            // ⑦ 数据记录插件的候选串带设备后缀（两台都可见）
            var plugin = new Plugin.DataRecord.DataRecordPlugin();
            plugin.SetConfigContext(new PluginConfigContext { Variables = opts });
            var cands = plugin.VariableNameCandidates;
            Check("C24.7 数据记录候选串带设备后缀（两台都可见）",
                cands.Contains("Alarm1[48] · A_PLC") && cands.Contains("Alarm1[48] · B_PLC"),
                string.Join(" | ", cands));

            // ⑧ 模型：改名字 → 标脏；MarkTextClean → 复位；同值写回不标脏
            var col = new Plugin.DataRecord.RecordColumnDef
            {
                VariableName = "Alarm1[48]",
                VariableId = b.VariableId,
            };
            Check("C24.8 名字变化 → 标脏", col.IsTextDirty, "");
            col.MarkTextClean();
            col.VariableName = "Alarm1[48]";
            Check("C24.9 同值写回不标脏", !col.IsTextDirty, "");
            col.VariableName = "Local1";
            Check("C24.10 再改名字 → 标脏", col.IsTextDirty, "");

            // ⑪ JSON 往返仍保留 Id（声明顺序：名字在前、Id 在后；IsTextDirty 不落盘）
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(col);
            var colBack = Newtonsoft.Json.JsonConvert.DeserializeObject<Plugin.DataRecord.RecordColumnDef>(json);
            Check("C24.11 JSON 往返保留 Id 与名字（脏标记不落盘）",
                colBack.VariableId == col.VariableId && colBack.VariableName == "Local1",
                colBack.VariableId + " / " + colBack.VariableName);
            Check("C24.11b IsTextDirty 不落盘（[JsonIgnore] 生效）",
                !json.Contains("IsTextDirty") && !json.Contains("_textDirty"), json);
            colBack.MarkTextClean();   // 插件加载路径的复位动作
            Check("C24.11c 加载复位后 clean", !colBack.IsTextDirty, "");

            // ⑫ 结果上报字段同款
            var fld = new Plugin.ResultUpload.UploadFieldDef { VariableName = "A", VariableId = a.VariableId };
            Check("C24.12 结果上报字段：改名字 → 标脏", fld.IsTextDirty, "");

            // ⑬ 结果上报插件的候选串同口径
            var urPlugin = new Plugin.ResultUpload.ResultUploadPlugin();
            urPlugin.SetConfigContext(new PluginConfigContext { Variables = opts });
            var urCands = urPlugin.VariableNameOptions;
            Check("C24.13 结果上报候选串带设备后缀",
                urCands.Contains("Alarm1[48] · A_PLC") && urCands.Contains("Alarm1[48] · B_PLC"),
                string.Join(" | ", urCands));
        }

        // ================= C25 SCADA 模板：设备限定写法（批 4） =================
        private static void C25_ScadaQualified()
        {
            var a = Net("A", "A_PLC", 111);
            var b = Net("A", "B_PLC", 222);
            // 限定写法针对**网络变量**（本地变量没有设备），数组镜像给 int[]
            var arrA = Net("Samples", "A_PLC", typeof(int[]), new[] { 1, 2, 3 });
            var arrB = Net("Samples", "B_PLC", typeof(int[]), new[] { 4, 5, 6 });

            var reg = new VariableRegistry(new ObservableCollection<IVariable> { a, b, arrA, arrB });
            var resolver = new ScadaTemplateVariableResolver(reg);

            // ① 设备/名字 → 指定那一台
            var okA = resolver.TryResolve("A_PLC/A", out var varA, out var errA);
            Check("C25.1 A_PLC/A → 命中 A_PLC 的 A",
                okA && varA.VariableId == a.VariableId, "err=" + (errA ?? "null"));

            var okB = resolver.TryResolve("B_PLC/A", out var varB, out var errB);
            Check("C25.2 B_PLC/A → 命中 B_PLC 的 A",
                okB && varB.VariableId == b.VariableId, "err=" + (errB ?? "null"));

            // ② 设备/数组[下标]
            var okArr = resolver.TryResolve("B_PLC/Samples[1]", out var varArr, out var errArr);
            Check("C25.3 B_PLC/Samples[1] → 数组 B 的元素 1",
                okArr && varArr.VariableId == arrB.VariableId && varArr.ArrayIndex == 1,
                "err=" + (errArr ?? "null"));

            // ③ 设备不存在
            var okNoDev = resolver.TryResolve("C_PLC/A", out _, out var errNoDev);
            Check("C25.4 设备不存在 → 明确指引",
                !okNoDev && (errNoDev ?? string.Empty).Contains("C_PLC"),
                "err=" + (errNoDev ?? "null"));

            // ④ 设备在、变量不在
            var okNoVar = resolver.TryResolve("A_PLC/NoSuch", out _, out var errNoVar);
            Check("C25.5 设备上没有该变量 → 明确指引",
                !okNoVar && (errNoVar ?? string.Empty).Contains("A_PLC") && (errNoVar ?? string.Empty).Contains("NoSuch"),
                "err=" + (errNoVar ?? "null"));

            // ⑤ 不写设备的重名 → 仍是歧义（批 1 行为不回退）
            var okAmbiguous = resolver.TryResolve("A", out _, out var errAmbiguous);
            Check("C25.6 不写设备的重名 → 仍报歧义",
                !okAmbiguous && (errAmbiguous ?? string.Empty).Contains("重复"),
                "err=" + (errAmbiguous ?? "null"));

            // ⑥ 精确名优先：真的建了一个名字叫 A_PLC/A 的变量
            var literal = Net("A_PLC/A", "L9", 9);
            var reg2 = new VariableRegistry(new ObservableCollection<IVariable> { a, b, literal });
            var resolver2 = new ScadaTemplateVariableResolver(reg2);
            var okLiteral = resolver2.TryResolve("A_PLC/A", out var varLiteral, out var errLiteral);
            Check("C25.7 精确名优先：名字就叫 A_PLC/A 的变量仍按精确名命中",
                okLiteral && varLiteral.VariableId == literal.VariableId,
                "err=" + (errLiteral ?? "null"));

            // ⑧ 同设备重名（手工编辑）→ 报数量
            var dup = Net("A", "A_PLC", 333);
            var reg3 = new VariableRegistry(new ObservableCollection<IVariable> { a, dup });
            var resolver3 = new ScadaTemplateVariableResolver(reg3);
            var okDup = resolver3.TryResolve("A_PLC/A", out _, out var errDup);
            Check("C25.8 同设备重名 → 报同名数量",
                !okDup && (errDup ?? string.Empty).Contains("2 个同名变量"),
                "err=" + (errDup ?? "null"));
        }

        // ================= 探针夹具：可记录的共享变量入口 =================
        private sealed class RecordingSharedAccess : ISharedVariableAccess
        {
            public int WriteCount { get; private set; }
            public Guid LastId { get; private set; }
            public string LastName { get; private set; }
            public object LastValue { get; private set; }

            public bool TryRead(Guid variableId, string fallbackName, out object value, out string error)
            {
                value = null;
                error = null;
                return false;
            }

            public bool TryWrite(Guid variableId, string fallbackName, object value, out string error)
            {
                WriteCount++;
                LastId = variableId;
                LastName = fallbackName;
                LastValue = value;
                error = null;
                return true;
            }
        }

        // ================= C26 被改过的视图都能 XAML 解析（批 4 的教训） =================
        private static void C26_TouchedViewsParse()
        {
            // 本宿主刻意不 new Application（E-13）：每个视图在自己的 STA 线程上构造，
            // XAML 解析错误（TargetType 不匹配 / 缺资源 / 缺转换器）当场判红——
            // ResultUpload 的 TextBox→ComboBox 换错了样式，闸门四轮全绿都没抓到，
            // 直到用户在真机上打开参数面板才炸（XamlParseException）。
            void OnSta(Action action)
            {
                Exception? error = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();
                if (error != null)
                    throw error;
            }

            OnSta(() =>
            {
                var assignment = new VisionMaster.Plugins.Util.VariableAssignmentView();
                Check("C26.1 VariableAssignmentView XAML 解析", assignment != null, "");
            });

            OnSta(() =>
            {
                var record = new Plugin.DataRecord.DataRecordView();
                Check("C26.2 DataRecordView XAML 解析", record != null, "");
            });

            OnSta(() =>
            {
                var upload = new Plugin.ResultUpload.ResultUploadView();
                Check("C26.3 ResultUploadView XAML 解析（TargetType 不匹配当场红）", upload != null, "");
            });

        }

        private sealed class FakeContext : IExecutionContext
        {
            public RecordingSharedAccess Shared { get; } = new();

            public ILogService Logger => null;
            public System.Threading.CancellationToken CancellationToken => System.Threading.CancellationToken.None;
            public FlowControlState CurrentFlowState { get; set; }
            public Guid? CurrentNodeId { get; set; }
            public string CurrentFlowName => string.Empty;
            public DateTime ExecutionStartTime { get; } = DateTime.Now;
            public IGlobalVariableWriter GlobalVariables => null;
            public ISharedVariableAccess SharedVariables => Shared;
            public ICameraProvider Cameras => null;
            public IMotionProvider Motions => null;
            public IFlowInvoker FlowInvoker => null;
            public IDictionary<string, object> LocalVariables { get; } = new Dictionary<string, object>();
        }
    }
}
