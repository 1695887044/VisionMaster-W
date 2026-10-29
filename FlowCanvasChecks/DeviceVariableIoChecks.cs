using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 单设备变量表导入/导出（xlsx）的断言。
    ///
    /// 覆盖四类只有真跑才看得出来的事：
    /// ① **导出 → 读回 → 计划** 必须判定"无变化"（列名/格式/默认值任何一处漂移都会把它变红）；
    /// ② 差异计划：文件内重名、与本地变量撞名、未知类型、未知扫描组回落、未知显示分组自动建/回落；
    /// ③ 事务应用：更新改地址/扫描组必须触发 ReRegister；删除缺失默认关闭；任一步失败全量回滚；
    /// ④ S7 的 DB 号是结构化列，必须导出再导入原样保留（复合地址串不参与回读）。
    /// </summary>
    internal static class DeviceVariableIoChecks
    {
        public static void Run()
        {
            Section("[IO] 设备变量表导入/导出（xlsx / 差异计划 / 事务回滚）");
            RunExportRowContract();
            RunArrayConversionColumnRoundTrip();
            RunXlsxRoundTripIsUnchanged();
            RunPlanValidation();
            RunAutoCreateAndFallback();
            RunDefaultValueSemantics();
            RunExplicitInitialUpdate();
            RunWhitespaceName();
            RunStringScalarSpan();
            RunApplyNewVariable();
            RunApplyUpdateReRegisters();
            RunArrayUpdateKeepsBaseline();
            RunSkipErrors();
            RunCorruptDefault();
            RunScalarToArray();
            RunTypeChangeRollback();
            RunProtocolSwap();
            RunDeleteMissing();
            RunRollback();
            RunS7DbNumberRoundTrip();
            RunMcRoundTrip();
            RunFinsRoundTrip();
            RunAreaOptionContract();
            RunBitOffsetRange();
            RunEasyNetCsvImport();
        }

        // ==================================================================
        //  ① 导出列：结构化地址 + 中文显示名 + 分组/扫描组默认文案
        // ==================================================================

        private static void RunExportRowContract()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var group = new VariableGroupDescriptor { Name = "监控分组" };
            solution.VariableGroups.Add(group);
            var nv = AddVariable(ws, "Speed", "100", group.Id);

            var row = DeviceVariableExportService.BuildRow(nv, solution.VariableGroups.ToList());

            Check("导出：变量名", Cell(row, DeviceVariableColumns.Name) == "Speed", Cell(row, DeviceVariableColumns.Name));
            Check("导出：类型中文显示名", Cell(row, DeviceVariableColumns.DataType) == "整数 (int)", Cell(row, DeviceVariableColumns.DataType));
            Check("导出：显示分组 = 分组名", Cell(row, DeviceVariableColumns.DisplayGroup) == "监控分组", Cell(row, DeviceVariableColumns.DisplayGroup));
            Check("导出：空扫描组显示「默认组」", Cell(row, DeviceVariableColumns.ScanGroup) == PollScheduler.DefaultGroupName, Cell(row, DeviceVariableColumns.ScanGroup));
            Check("导出：地址预览 = 结构化地址串", Cell(row, DeviceVariableColumns.AddressPreview) == nv.AddressConfig!.Address, Cell(row, DeviceVariableColumns.AddressPreview));
            Check("导出：Modbus 站号列有值", Cell(row, DeviceVariableColumns.Station) == "0", Cell(row, DeviceVariableColumns.Station));
            Check("导出：Modbus 的 DB 号列留空", Cell(row, DeviceVariableColumns.DbNumber) == "", Cell(row, DeviceVariableColumns.DbNumber));
            Check("导出：40001 写法列 = 否", Cell(row, DeviceVariableColumns.LegacyAddress) == "否", Cell(row, DeviceVariableColumns.LegacyAddress));
        }

        // ==================================================================
        //  ①b 数组逐元素换算列：JSON 落列 + xlsx 往返不漂移
        // ==================================================================

        private static void RunArrayConversionColumnRoundTrip()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var arrayNv = new NetworkVariableModel
            {
                Name = "Ctrl",
                DataType = typeof(int[]),
                Description = "数组换算",
                ConnectionName = "PLC-1",
                AddressConfig = BuildModbusArrayAddress("200", 2),
                ElementConversions = new ElementConversion?[]
                {
                    new() { Scale = 0.1, Offset = 5, DecimalPlaces = 1, Unit = "℃" },
                    null,
                }
            };
            ws.GlobalVariables.Add(arrayNv);
            var groups = solution.VariableGroups.ToList();

            var row = DeviceVariableExportService.BuildRow(arrayNv, groups);
            string json = Cell(row, DeviceVariableColumns.ElementConversions);
            Check("[IO] 导出：数组逐元素换算以 JSON 落列（含 null 槽位）",
                json.Contains("\"Scale\":0.1", StringComparison.Ordinal)
                && json.Contains("\"Unit\":\"℃\"", StringComparison.Ordinal)
                && json.Contains("null", StringComparison.Ordinal),
                json);

            var path = TempXlsx();
            try
            {
                DeviceVariableExportService.Export(
                    path, connection, new[] { arrayNv }, groups,
                    new DeviceVariableIoOptions { IncludeGuideSheet = false, IncludeAddressPreview = false });

                var rows = DeviceVariableImportService.ReadRows(path);
                Check("[IO] 往返：xlsx 读回 1 行（数组）", rows.Count == 1, $"实际 {rows.Count} 行");
                if (rows.Count != 1) return;

                var plan = BuildPlan(connection, ws, rows, new[] { arrayNv }, groups);
                Check("[IO] 往返：数组逐元素换算 JSON 列读回后计划判无变化",
                    plan.UnchangedCount == 1 && plan.NewCount == 0 && plan.UpdateCount == 0 && plan.ErrorCount == 0,
                    $"新增 {plan.NewCount} / 更新 {plan.UpdateCount} / 无变化 {plan.UnchangedCount} / 错误 {plan.ErrorCount} / {plan.Items.FirstOrDefault()?.Note}");

                Check("[IO] 往返：计划带回的逐元素换算与导出前一致（长度/系数/单位/null 槽位）",
                    plan.Items[0].Definition?.ElementConversions is { Length: 2 }
                    && plan.Items[0].Definition.ElementConversions[0]?.Scale == 0.1
                    && plan.Items[0].Definition.ElementConversions[0]?.Unit == "℃"
                    && plan.Items[0].Definition.ElementConversions[1] == null,
                    "计划未带回逐元素换算");

                // 旧模板缺「逐元素换算」列：更新时保持原档案（列缺失 ≠ 显式清空）
                var rowWithoutConversions = Row(2,
                    (DeviceVariableColumns.Name, "Ctrl"),
                    (DeviceVariableColumns.Description, "数组换算"),
                    (DeviceVariableColumns.DataType, GlobalVariableManagerViewModel.TypeDisplayName(typeof(int[]))),
                    (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayName(ModbusArea.HoldingRegisters)),
                    (DeviceVariableColumns.Offset, "200"),
                    (DeviceVariableColumns.ElementCount, "2"));
                var planWithout = BuildPlan(
                    connection, ws, new[] { rowWithoutConversions }, new[] { arrayNv }, groups);
                Check("[IO] 旧模板缺「逐元素换算」列：计划判无变化（保持原档案）",
                    planWithout.UnchangedCount == 1 && planWithout.UpdateCount == 0,
                    $"新增 {planWithout.NewCount} / 更新 {planWithout.UpdateCount} / 无变化 {planWithout.UnchangedCount} / {planWithout.Items.FirstOrDefault()?.Note}");
                Check("[IO] 旧模板缺列：定义标记 HasElementConversionsColumn=false（应用时保留原档案）",
                    planWithout.Items[0].Definition?.HasElementConversionsColumn == false,
                    "定义未标记列缺失");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        // ==================================================================
        //  ② xlsx 往返：导出 → 读回 → 计划必须"无变化"
        // ==================================================================

        private static void RunXlsxRoundTripIsUnchanged()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var group = new VariableGroupDescriptor { Name = "监控分组" };
            solution.VariableGroups.Add(group);
            var nv = AddVariable(ws, "Speed", "100", group.Id);
            var groups = solution.VariableGroups.ToList();

            var path = TempXlsx();
            try
            {
                DeviceVariableExportService.Export(
                    path, connection, new[] { nv }, groups,
                    new DeviceVariableIoOptions { IncludeGuideSheet = true, IncludeAddressPreview = true });

                var rows = DeviceVariableImportService.ReadRows(path);
                Check("xlsx：读回 1 个数据行", rows.Count == 1, $"实际 {rows.Count} 行");
                if (rows.Count != 1) return;

                Check("xlsx：表头映射「变量名」", rows[0].Get(DeviceVariableColumns.Name) == "Speed", rows[0].Get(DeviceVariableColumns.Name));
                Check("xlsx：表头映射「偏移量」", rows[0].Get(DeviceVariableColumns.Offset) == "100", rows[0].Get(DeviceVariableColumns.Offset));
                Check("xlsx：表头映射「显示分组」", rows[0].Get(DeviceVariableColumns.DisplayGroup) == "监控分组", rows[0].Get(DeviceVariableColumns.DisplayGroup));

                var plan = BuildPlan(connection, ws, rows, new[] { nv }, groups);
                Check("往返：导出再导入 = 无变化（列名/格式/默认值无漂移）",
                    plan.UnchangedCount == 1 && plan.NewCount == 0 && plan.UpdateCount == 0 && plan.ErrorCount == 0,
                    $"新增 {plan.NewCount} / 更新 {plan.UpdateCount} / 无变化 {plan.UnchangedCount} / 错误 {plan.ErrorCount}");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        // ==================================================================
        //  ③ 计划校验：重名 / 撞名 / 未知类型
        // ==================================================================

        private static void RunPlanValidation()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);

            var rows = new[]
            {
                ModbusRow(2, "New1", "整数 (int)", "10"),
                ModbusRow(3, "New1", "整数 (int)", "11"),
                ModbusRow(4, "BadType", "不存在的类型", "12"),
            };
            var plan = BuildPlan(connection, ws, rows, Array.Empty<NetworkVariableModel>(), solution.VariableGroups.ToList());

            Check("计划：第一行是新增", plan.Items.Count == 3 && plan.Items[0].Status == DeviceVariableImportStatus.New,
                $"实际 {plan.Items.Count} 行 / 首行 {plan.Items.FirstOrDefault()?.StatusText}");
            Check("计划：文件内重名报错", plan.Items[1].Status == DeviceVariableImportStatus.Error && plan.Items[1].Note.Contains("重复"),
                plan.Items[1].Note);
            Check("计划：未知类型报错", plan.Items[2].Status == DeviceVariableImportStatus.Error && plan.Items[2].Note.Contains("无法识别"),
                plan.Items[2].Note);
            Check("计划：错误行计入 HasErrors", plan.HasErrors && plan.ErrorCount == 2, $"错误 {plan.ErrorCount}");

            ws.GlobalVariables.Add(VariableFactory.CreateLocal("Conflict", typeof(int), "本地冲突", 0));
            var conflictPlan = BuildPlan(
                connection, ws,
                new[] { ModbusRow(2, "Conflict", "整数 (int)", "10") },
                Array.Empty<NetworkVariableModel>(),
                solution.VariableGroups.ToList());
            Check("计划：与本地变量撞名报错（本地变量无设备，仍全方案唯一）",
                conflictPlan.Items[0].Status == DeviceVariableImportStatus.Error && conflictPlan.Items[0].Note.Contains("本地变量"),
                conflictPlan.Items[0].Note);
        }

        // ==================================================================
        //  ④ 显示分组自动建 / 未知扫描组回落
        // ==================================================================

        private static void RunAutoCreateAndFallback()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var rows = new[] { ModbusRow(2, "Temp", "小数 (double)", "20", "新分组", "未知组") };

            var plan = BuildPlan(connection, ws, rows, Array.Empty<NetworkVariableModel>(), solution.VariableGroups.ToList(), autoCreate: true);
            Check("计划：未知显示分组进入「将新建」清单", plan.NewDisplayGroups.Contains("新分组"),
                string.Join(" / ", plan.NewDisplayGroups));
            Check("计划：未知扫描组回落默认组并列出",
                plan.UnknownScanGroups.Contains("未知组") && plan.Items[0].Definition?.ScanGroupDisplay == PollScheduler.DefaultGroupName,
                plan.Items[0].Note);

            var noCreate = BuildPlan(connection, ws, rows, Array.Empty<NetworkVariableModel>(), solution.VariableGroups.ToList(), autoCreate: false);
            Check("计划：关闭自动建组后回落「未分组」并列出",
                noCreate.Items[0].Definition?.DisplayGroupName == VariableGroupEditorViewModel.UngroupedName
                && noCreate.UnknownDisplayGroups.Contains("新分组"),
                noCreate.Items[0].Note);
        }

        // ==================================================================
        //  ⑤ 应用新增：连接名 / 分组 Id / 扫描组落库
        // ==================================================================

        private static void RunApplyNewVariable()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var rows = new[] { ModbusRow(2, "Temp", "小数 (double)", "20", "新分组", "未知组") };
            var plan = BuildPlan(connection, ws, rows, Array.Empty<NetworkVariableModel>(), solution.VariableGroups.ToList());

            var result = DeviceVariableImportService.Apply(plan, ws, null, deleteMissing: false, skipErrors: false);
            var added = ws.GlobalVariables.OfType<NetworkVariableModel>().FirstOrDefault(v => v.Name == "Temp");
            var group = solution.VariableGroups.FirstOrDefault(g => g.Name == "新分组");

            Check("应用：新增 1 个", result.Added == 1, $"新增 {result.Added}");
            Check("应用：变量已落库且连接名正确", added != null && added.ConnectionName == "PLC-1",
                added == null ? "未找到 Temp" : added.ConnectionName ?? "null");
            Check("应用：自动建组并挂到新变量", group != null && added?.GroupId == group!.Id,
                $"group={group?.Id} var={added?.GroupId}");
            Check("应用：未知扫描组落库为空（默认组）", added?.ScanGroup == string.Empty, added?.ScanGroup ?? "null");
        }

        // ==================================================================
        //  ⑥ 应用更新：地址 / 扫描组变化必须 ReRegister
        // ==================================================================

        private static void RunApplyUpdateReRegisters()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nv = AddVariable(ws, "Speed", "100");
            var rows = new[] { ModbusRow(2, "Speed", "整数 (int)", "200", "", "Fast") };
            var plan = BuildPlan(connection, ws, rows, new[] { nv }, solution.VariableGroups.ToList());

            Check("更新：计划识别为更新", plan.UpdateCount == 1 && plan.ErrorCount == 0,
                $"更新 {plan.UpdateCount} / 错误 {plan.ErrorCount}");

            var reRegistered = new List<string>();
            var result = DeviceVariableImportService.Apply(
                plan, ws,
                v => { reRegistered.Add(v.Name); return true; },
                false, false);

            Check("更新：应用 1 个", result.Updated == 1, $"更新 {result.Updated}");
            Check("更新：地址已改到 200", nv.AddressConfig!.Address.Contains("200"), nv.AddressConfig!.Address);
            Check("更新：扫描组已改到 Fast", nv.ScanGroup == "Fast", nv.ScanGroup);
            Check("更新：触发 ReRegister（否则轮询仍读旧快照）",
                reRegistered.Count == 1 && reRegistered[0] == "Speed",
                string.Join(",", reRegistered));
        }

        // ==================================================================
        //  ⑥b 数组更新：Excel 空初始值不许清空基准数组
        // ==================================================================

        private static void RunArrayUpdateKeepsBaseline()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var baseline = new[] { 1, 2, 3 };
            var nv = new NetworkVariableModel
            {
                Name = "Arr",
                DataType = typeof(int[]),
                Description = "arr",
                ConnectionName = "PLC-1",
                AddressConfig = BuildModbusArrayAddress("10", 3),
                DefaultValue = baseline,
                Value = new[] { 4, 5, 6 }
            };
            ws.GlobalVariables.Add(nv);

            var raw = Row(2,
                (DeviceVariableColumns.Name, "Arr"),
                (DeviceVariableColumns.DataType, "整数数组 (int[])"),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                (DeviceVariableColumns.Offset, "10"),
                (DeviceVariableColumns.ElementCount, "3"),
                (DeviceVariableColumns.Description, "arr2"));
            var plan = BuildPlan(connection, ws, new[] { raw }, new[] { nv }, solution.VariableGroups.ToList());
            Check("数组更新：计划识别为更新", plan.UpdateCount == 1,
                $"更新 {plan.UpdateCount} / 错误 {plan.ErrorCount} / 首行 {plan.Items.FirstOrDefault()?.Note}");

            DeviceVariableImportService.Apply(plan, ws, null, false, false);
            Check("数组更新：Excel 空初始值不清空已有基准数组",
                nv.DefaultValue is int[] current && current.SequenceEqual(baseline),
                nv.DefaultValue == null ? "null" : string.Join(",", (int[])nv.DefaultValue));
        }

        // ==================================================================
        //  ⑦ 删除缺失：默认关闭，显式勾选才删
        // ==================================================================

        private static void RunDeleteMissing()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var a = AddVariable(ws, "A", "10");
            var b = AddVariable(ws, "B", "20");
            var rows = new[] { ModbusRow(2, "A", "整数 (int)", "10") };
            var plan = BuildPlan(connection, ws, rows, new[] { a, b }, solution.VariableGroups.ToList());

            Check("删除缺失：B 被列为「文件中没有」",
                plan.MissingVariables.Count == 1 && ReferenceEquals(plan.MissingVariables[0], b),
                $"缺失 {plan.MissingVariables.Count}");

            var keep = DeviceVariableImportService.Apply(plan, ws, null, deleteMissing: false, skipErrors: false);
            Check("删除缺失：默认不删，B 仍在", keep.Deleted == 0 && ws.GlobalVariables.Contains(b), $"删除 {keep.Deleted}");

            var remove = DeviceVariableImportService.Apply(plan, ws, null, deleteMissing: true, skipErrors: false);
            Check("删除缺失：勾选后 B 被移除", remove.Deleted == 1 && !ws.GlobalVariables.Contains(b), $"删除 {remove.Deleted}");
        }

        // ==================================================================
        //  ⑧ 事务回滚：新增 / 更新 / 自动建组任一步失败都回到导入前
        // ==================================================================

        private static void RunRollback()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nv = AddVariable(ws, "Speed", "100");
            var originalAddress = nv.AddressConfig!.Address;
            var originalDescription = nv.Description;

            var rows = new[]
            {
                ModbusRow(2, "NewVar", "整数 (int)", "300", "新分组"),
                ModbusRow(3, "Speed", "整数 (int)", "200"),
            };
            var plan = BuildPlan(connection, ws, rows, new[] { nv }, solution.VariableGroups.ToList());
            Check("回滚：计划含 1 新增 + 1 更新",
                plan.NewCount == 1 && plan.UpdateCount == 1,
                $"新增 {plan.NewCount} / 更新 {plan.UpdateCount}");

            var threw = false;
            string? failureMessage = null;
            try
            {
                DeviceVariableImportService.Apply(
                    plan, ws,
                    _ => throw new InvalidOperationException("模拟注册失败"),
                    deleteMissing: false,
                    skipErrors: false);
            }
            catch (Exception ex)
            {
                threw = true;
                failureMessage = ex.Message;
            }

            Check("回滚：失败向上抛出（调用方要能提示）", threw, failureMessage ?? "未抛出");
            Check("回滚：回滚未完全成功时如实上报（不再固定声称已回滚）",
                failureMessage?.Contains("回滚未完全成功", StringComparison.Ordinal) == true,
                failureMessage ?? "");
            Check("回滚：更新变量地址还原", nv.AddressConfig!.Address == originalAddress,
                $"{originalAddress} → {nv.AddressConfig!.Address}");
            Check("回滚：更新变量描述还原", nv.Description == originalDescription, nv.Description);
            Check("回滚：新增变量已移除",
                ws.GlobalVariables.OfType<NetworkVariableModel>().All(v => v.Name != "NewVar"),
                "剩余：" + string.Join(",", ws.GlobalVariables.OfType<NetworkVariableModel>().Select(v => v.Name)));
            Check("回滚：自动创建的分组已移除",
                solution.VariableGroups.All(g => g.Name != "新分组"),
                "剩余：" + string.Join(",", solution.VariableGroups.Select(g => g.Name)));
        }

        // ==================================================================
        //  ⑨ S7 DB 号：结构化列往返
        // ==================================================================

        private static void RunS7DbNumberRoundTrip()
        {
            var connection = new CommunicationConfig
            {
                ConnectionName = "S7-1",
                Protocol = CommunicationType.SiemensS7,
                ReadCycleMs = 1000
            };
            var ws = CreateWorkspace(out var solution);

            var address = BuildS7Address("10", 7);
            var nv = new NetworkVariableModel
            {
                Name = "S7Speed",
                DataType = typeof(int),
                Description = string.Empty,
                ConnectionName = "S7-1",
                AddressConfig = address,
                DefaultValue = 0,
                Value = 0
            };
            ws.GlobalVariables.Add(nv);

            var row = DeviceVariableExportService.BuildRow(nv, solution.VariableGroups.ToList());
            Check("S7：导出 DB 号 = 7", Cell(row, DeviceVariableColumns.DbNumber) == "7", Cell(row, DeviceVariableColumns.DbNumber));

            var raw = Row(2,
                (DeviceVariableColumns.Name, "S7Speed"),
                (DeviceVariableColumns.DataType, "整数 (int)"),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[S7Area.DB]),
                (DeviceVariableColumns.Offset, "10"),
                (DeviceVariableColumns.DbNumber, "7"));
            var plan = BuildPlan(connection, ws, new[] { raw }, new[] { nv }, solution.VariableGroups.ToList());
            Check("S7：导入 DB 号 7 后判定无变化", plan.UnchangedCount == 1 && plan.ErrorCount == 0,
                $"无变化 {plan.UnchangedCount} / 错误 {plan.ErrorCount} / 首行 {plan.Items.FirstOrDefault()?.Note}");
        }

        // ==================================================================
        //  ⑩ 三菱 MC：存储区/偏移同样必须结构化往返
        // ==================================================================

        private static void RunMcRoundTrip()
        {
            var connection = new CommunicationConfig
            {
                ConnectionName = "MC-1",
                Protocol = CommunicationType.MitsubishiMc,
                ReadCycleMs = 1000
            };
            var ws = CreateWorkspace(out var solution);

            var nv = new NetworkVariableModel
            {
                Name = "McSpeed",
                DataType = typeof(int),
                Description = string.Empty,
                ConnectionName = "MC-1",
                AddressConfig = BuildMcAddress("100"),
                DefaultValue = 0,
                Value = 0
            };
            ws.GlobalVariables.Add(nv);

            var row = DeviceVariableExportService.BuildRow(nv, solution.VariableGroups.ToList());
            // 存储区上屏走枚举上的 [Display]（R40）：导出列也跟着走中文显示名。
            // 导入侧仍接受成员名 "D"（存量文件兼容），下面那行 raw 用的就是 "D"。
            Check("Mc：导出存储区 = D 数据寄存器",
                Cell(row, DeviceVariableColumns.Area) == GlobalVariableManagerViewModel.AreaDisplayNames[McArea.D],
                Cell(row, DeviceVariableColumns.Area));
            Check("Mc：导出偏移 = 100", Cell(row, DeviceVariableColumns.Offset) == "100",
                Cell(row, DeviceVariableColumns.Offset));

            var raw = Row(2,
                (DeviceVariableColumns.Name, "McSpeed"),
                (DeviceVariableColumns.DataType, "整数 (int)"),
                (DeviceVariableColumns.Area, "D"), // 存量文件里的成员名写法，必须仍然认
                (DeviceVariableColumns.Offset, "100"));
            var plan = BuildPlan(connection, ws, new[] { raw }, new[] { nv }, solution.VariableGroups.ToList());
            Check("Mc：导出再导入 = 无变化", plan.UnchangedCount == 1 && plan.ErrorCount == 0,
                $"无变化 {plan.UnchangedCount} / 错误 {plan.ErrorCount} / 首行 {plan.Items.FirstOrDefault()?.Note}");
        }

        // ==================================================================
        //  ⑪ 欧姆龙 Fins：存储区/偏移同样必须结构化往返
        // ==================================================================

        private static void RunFinsRoundTrip()
        {
            var connection = new CommunicationConfig
            {
                ConnectionName = "FINS-1",
                Protocol = CommunicationType.OmronFins,
                ReadCycleMs = 1000
            };
            var ws = CreateWorkspace(out var solution);

            var nv = new NetworkVariableModel
            {
                Name = "FinsSpeed",
                DataType = typeof(int),
                Description = string.Empty,
                ConnectionName = "FINS-1",
                AddressConfig = BuildFinsAddress("100"),
                DefaultValue = 0,
                Value = 0
            };
            ws.GlobalVariables.Add(nv);

            var row = DeviceVariableExportService.BuildRow(nv, solution.VariableGroups.ToList());
            Check("Fins：导出存储区 = D DM 区",
                Cell(row, DeviceVariableColumns.Area) == GlobalVariableManagerViewModel.AreaDisplayNames[FinsArea.D],
                Cell(row, DeviceVariableColumns.Area));
            Check("Fins：导出偏移 = 100", Cell(row, DeviceVariableColumns.Offset) == "100",
                Cell(row, DeviceVariableColumns.Offset));

            var raw = Row(2,
                (DeviceVariableColumns.Name, "FinsSpeed"),
                (DeviceVariableColumns.DataType, "整数 (int)"),
                (DeviceVariableColumns.Area, "D"), // 存量文件里的成员名写法，必须仍然认
                (DeviceVariableColumns.Offset, "100"));
            var plan = BuildPlan(connection, ws, new[] { raw }, new[] { nv }, solution.VariableGroups.ToList());
            Check("Fins：导出再导入 = 无变化", plan.UnchangedCount == 1 && plan.ErrorCount == 0,
                $"无变化 {plan.UnchangedCount} / 错误 {plan.ErrorCount} / 首行 {plan.Items.FirstOrDefault()?.Note}");
        }

        // ==================================================================
        //  ⑫ 存储区候选：中文显示名 + MC 按厂商收窄
        // ==================================================================

        /// <summary>
        /// 存储区候选的两个契约：
        /// ① 上屏走中文显示名（R40）——枚举上早已标好 [Display]，不能再用 ToString；
        /// ② MC 的候选按厂商收窄——同一份 McArea 枚举，松下只认 R/X/Y/L/D，
        ///    基恩士的 F/S/V 需要 FM/SM·SD 前缀，放出去就是"建的时候全绿、轮询才报错"。
        /// </summary>
        private static void RunAreaOptionContract()
        {
            var mc = new CommunicationConfig(CommunicationType.MitsubishiMc) { ConnectionName = "MC" };
            var keyence = new CommunicationConfig(CommunicationType.MitsubishiMc) { ConnectionName = "MC-K" };
            ((MitsubishiMcConfig)keyence.Config).Vendor = McVendor.Keyence;
            var panasonic = new CommunicationConfig(CommunicationType.MitsubishiMc) { ConnectionName = "MC-P" };
            ((MitsubishiMcConfig)panasonic.Config).Vendor = McVendor.Panasonic;
            var fins = new CommunicationConfig(CommunicationType.OmronFins) { ConnectionName = "FINS" };

            var mcOptions = GlobalVariableManagerViewModel.GetAreaOptions(mc);
            var keyenceOptions = GlobalVariableManagerViewModel.GetAreaOptions(keyence);
            var panasonicOptions = GlobalVariableManagerViewModel.GetAreaOptions(panasonic);
            var finsOptions = GlobalVariableManagerViewModel.GetAreaOptions(fins);

            Check("存储区：MC（三菱）候选 12 个且走中文显示名",
                mcOptions.Count == 12 && mcOptions.Contains("D 数据寄存器") && mcOptions.Contains("ZR 扩展文件寄存器"),
                string.Join(" / ", mcOptions));

            Check("存储区：基恩士收窄掉 F/S/V（解析器只认 FM/SM·SD/无 V）",
                keyenceOptions.Count == 9 && !keyenceOptions.Contains("F 报警器")
                && !keyenceOptions.Contains("S 步进继电器") && !keyenceOptions.Contains("V 边缘继电器")
                && keyenceOptions.Contains("B 链接继电器"),
                string.Join(" / ", keyenceOptions));

            Check("存储区：松下只留 D/R/X/Y/L（解析器不支持其余 7 个）",
                panasonicOptions.Count == 5
                && panasonicOptions.SequenceEqual(new[] { "D 数据寄存器", "R 文件寄存器", "X 输入继电器", "Y 输出继电器", "L 锁存继电器" }),
                string.Join(" / ", panasonicOptions));

            Check("存储区：FINS 候选 5 个且走中文显示名",
                finsOptions.Count == 5 && finsOptions[0] == "D DM 区",
                string.Join(" / ", finsOptions));

            Check("存储区：默认区跟随协议（MC/FINS 都是 D 区的中文名）",
                GlobalVariableManagerViewModel.GetDefaultAreaName(mc) == "D 数据寄存器"
                && GlobalVariableManagerViewModel.GetDefaultAreaName(fins) == "D DM 区",
                $"{GlobalVariableManagerViewModel.GetDefaultAreaName(mc)} / {GlobalVariableManagerViewModel.GetDefaultAreaName(fins)}");

            bool byDisplay = GlobalVariableManagerViewModel.TryResolveArea(mc, "D 数据寄存器", out var a1) && a1 is McArea.D;
            bool byMember = GlobalVariableManagerViewModel.TryResolveArea(mc, "D", out var a2) && a2 is McArea.D;
            bool numericRejected = !GlobalVariableManagerViewModel.TryResolveArea(mc, "7", out _);
            bool vendorRejected = !GlobalVariableManagerViewModel.TryResolveArea(panasonic, "M 内部继电器", out _);
            Check("存储区：显示名与成员名都能反查；数字串与厂商不支持区被拒",
                byDisplay && byMember && numericRejected && vendorRejected,
                $"显示名={byDisplay} 成员名={byMember} 数字被拒={numericRejected} 厂商不支持被拒={vendorRejected}");

            // 弹窗内切换「所属连接」：候选与默认区必须跟着新协议重算。
            // 旧实现把候选在构造时钉死，切换后仍列旧协议的区——同名的 "D" 会静默建错区，
            // 不同名则"预览报先选存储区、候选里却没有可选项"直接走进死路。
            var draft = new NetworkVariableDraft(
                new[] { "整数 (int)" },
                new[] { "MC", "FINS" },
                new[] { PollScheduler.DefaultGroupName },
                Array.Empty<string>(),
                _ => string.Empty,
                new NetworkVariableDraftContext
                {
                    AreaOptionsFor = name => name == "FINS" ? new[] { "D DM 区" } : new[] { "D 数据寄存器" },
                    DefaultAreaFor = name => name == "FINS" ? "D DM 区" : "D 数据寄存器",
                    ScanGroupsFor = _ => new[] { PollScheduler.DefaultGroupName },
                    // 协议回调：切换连接时「地址参数」块要跟着换（MC 无专属字段 / FINS 位偏移 0-15）
                    ProtocolFor = name => name == "FINS" ? CommunicationType.OmronFins : CommunicationType.MitsubishiMc,
                    TypeResolver = GlobalVariableManagerViewModel.ResolveType,
                })
            {
                ConnectionName = "MC",
                AreaName = "D 数据寄存器",
            };
            var before = draft.AreaOptions.ToList();
            draft.ConnectionName = "FINS";
            Check("存储区：弹窗内切换连接后候选与默认区跟着新协议走",
                before.Count == 1 && before[0] == "D 数据寄存器"
                && draft.AreaOptions.Single() == "D DM 区" && draft.AreaName == "D DM 区",
                $"before={string.Join("/", before)} after={string.Join("/", draft.AreaOptions)} area={draft.AreaName}");
        }

        // ==================================================================
        //  ⑬ 初始值语义：更新行留空 = 保持原值；新增行留空 = 类型默认值
        // ==================================================================

        private static void RunDefaultValueSemantics()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nullDefault = AddVariable(ws, "NullDefault", "10");
            nullDefault.Description = string.Empty;
            nullDefault.DefaultValue = null;
            var keepDefault = AddVariable(ws, "KeepDefault", "11");
            keepDefault.Description = string.Empty;
            keepDefault.DefaultValue = 42;

            var rows = new[]
            {
                ModbusRow(2, "NullDefault", "整数 (int)", "10"),
                ModbusRow(3, "KeepDefault", "整数 (int)", "11"),
                Row(4,
                    (DeviceVariableColumns.Name, "ExplicitDefault"),
                    (DeviceVariableColumns.DataType, "整数 (int)"),
                    (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                    (DeviceVariableColumns.Offset, "12"),
                    (DeviceVariableColumns.InitialValue, "7")),
            };
            var plan = BuildPlan(connection, ws, rows, new[] { nullDefault, keepDefault }, solution.VariableGroups.ToList());

            Check("初始值：既有 null 默认值 + 空白 = 无变化",
                plan.Items[0].Status == DeviceVariableImportStatus.Unchanged,
                $"{plan.Items[0].StatusText} / {plan.Items[0].Note}");
            Check("初始值：既有非空默认值 + 空白 = 无变化",
                plan.Items[1].Status == DeviceVariableImportStatus.Unchanged,
                $"{plan.Items[1].StatusText} / {plan.Items[1].Note}");
            Check("初始值：新增行显式值按 7 落计划",
                plan.Items[2].Status == DeviceVariableImportStatus.New
                && Convert.ToInt32(plan.Items[2].Definition!.InitialValue) == 7,
                plan.Items[2].Definition?.InitialValue?.ToString() ?? "null");
        }

        // ==================================================================
        //  ⑬ 变量名前后空格：Trim 后匹配，不误判新增/删除
        // ==================================================================

        private static void RunExplicitInitialUpdate()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nv = AddVariable(ws, "Explicit", "13");
            nv.DefaultValue = 42;
            nv.Description = string.Empty;

            var row = Row(2,
                (DeviceVariableColumns.Name, "Explicit"),
                (DeviceVariableColumns.DataType, "整数 (int)"),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                (DeviceVariableColumns.Offset, "13"),
                (DeviceVariableColumns.InitialValue, "7"));
            var plan = BuildPlan(connection, ws, new[] { row }, new[] { nv }, solution.VariableGroups.ToList());
            Check("初始值：既有 42 + 文件显式 7 → 更新", plan.UpdateCount == 1,
                $"{plan.Items[0].StatusText} / {plan.Items[0].Note}");

            DeviceVariableImportService.Apply(plan, ws, null, false, false);
            Check("初始值：应用后默认值 = 7", nv.DefaultValue is int value && value == 7,
                nv.DefaultValue?.ToString() ?? "null");
        }

        private static void RunWhitespaceName()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nv = AddVariable(ws, " Speed ", "10");
            nv.Description = string.Empty;

            var plan = BuildPlan(
                connection, ws,
                new[] { ModbusRow(2, "Speed", "整数 (int)", "10") },
                new[] { nv },
                solution.VariableGroups.ToList());

            Check("前后空格名：Trim 后匹配为无变化",
                plan.UnchangedCount == 1,
                $"{plan.Items[0].StatusText} / {plan.Items[0].Note}");
            Check("前后空格名：原变量不被列为删除", plan.MissingCount == 0,
                $"缺失 {plan.MissingCount}");
        }

        // ==================================================================
        //  ⑭ 字符串标量跨度：和数组一样受单次读取上限约束
        // ==================================================================

        private static void RunStringScalarSpan()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var row = Row(2,
                (DeviceVariableColumns.Name, "LongText"),
                (DeviceVariableColumns.DataType, "文本 (string)"),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                (DeviceVariableColumns.Offset, "0"),
                (DeviceVariableColumns.ElementCount, "300"));

            var plan = BuildPlan(connection, ws, new[] { row }, Array.Empty<NetworkVariableModel>(), solution.VariableGroups.ToList());
            Check("字符串标量：超过单次读取上限报错",
                plan.Items[0].Status == DeviceVariableImportStatus.Error
                && plan.Items[0].Note.Contains("超过单次读取上限", StringComparison.Ordinal),
                plan.Items[0].Note);
        }

        // ==================================================================
        //  ⑮ 跳过错误行：只应用合法行
        // ==================================================================

        private static void RunSkipErrors()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var rows = new[]
            {
                ModbusRow(2, "GoodNew", "整数 (int)", "10"),
                ModbusRow(3, "BadNew", "不存在的类型", "11"),
            };
            var plan = BuildPlan(connection, ws, rows, Array.Empty<NetworkVariableModel>(), solution.VariableGroups.ToList());
            Check("跳过错误：计划 1 新增 + 1 错误",
                plan.NewCount == 1 && plan.ErrorCount == 1,
                $"新增 {plan.NewCount} / 错误 {plan.ErrorCount}");

            var result = DeviceVariableImportService.Apply(plan, ws, null, deleteMissing: false, skipErrors: true);
            Check("跳过错误：只应用合法行（新增 1 / 跳过 1）",
                result.Added == 1 && result.SkippedErrors == 1,
                $"新增 {result.Added} / 跳过 {result.SkippedErrors}");
            Check("跳过错误：错误行未落库",
                ws.GlobalVariables.OfType<NetworkVariableModel>().All(v => v.Name != "BadNew"),
                string.Join(",", ws.GlobalVariables.OfType<NetworkVariableModel>().Select(v => v.Name)));
        }

        // ==================================================================
        //  ⑯ 损坏默认值：导出/预览不抛，往返判无变化
        // ==================================================================

        private static void RunCorruptDefault()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nv = AddVariable(ws, "Corrupt", "10");
            nv.DataType = typeof(double);
            nv.DefaultValue = "abc";
            nv.Value = "abc";
            nv.Description = string.Empty;

            var plan = BuildPlan(
                connection, ws,
                new[] { ModbusRow(2, "Corrupt", "小数 (double)", "10") },
                new[] { nv },
                solution.VariableGroups.ToList());

            Check("损坏默认值：导出/预览不抛且判无变化",
                plan.UnchangedCount == 1,
                $"{plan.Items[0].StatusText} / {plan.Items[0].Note}");
        }

        // ==================================================================
        //  ⑰ 类型变更：scalar→array 清基准/镜像并重注册
        // ==================================================================

        private static void RunScalarToArray()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var nv = AddVariable(ws, "ToArray", "10");
            nv.DefaultValue = 7;
            nv.Description = string.Empty;

            var row = Row(2,
                (DeviceVariableColumns.Name, "ToArray"),
                (DeviceVariableColumns.DataType, "整数数组 (int[])"),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                (DeviceVariableColumns.Offset, "10"),
                (DeviceVariableColumns.ElementCount, "3"));
            var plan = BuildPlan(connection, ws, new[] { row }, new[] { nv }, solution.VariableGroups.ToList());
            Check("类型变更：scalar→array 判更新", plan.UpdateCount == 1,
                $"{plan.Items[0].StatusText} / {plan.Items[0].Note}");

            var reRegistered = new List<string>();
            DeviceVariableImportService.Apply(
                plan, ws,
                v => { reRegistered.Add(v.Name); return true; },
                false, false);

            Check("类型变更：DataType 变为 int[]", nv.DataType == typeof(int[]), nv.DataType.Name);
            Check("类型变更：基准/镜像清空", nv.DefaultValue == null && nv.Value == null,
                $"default={nv.DefaultValue ?? "null"} value={nv.Value ?? "null"}");
            Check("类型变更：地址长度 3", nv.AddressConfig?.Length == 3,
                nv.AddressConfig?.Length.ToString() ?? "null");
            Check("类型变更：触发重注册", reRegistered.Contains("ToArray"),
                string.Join(",", reRegistered));
        }

        // ==================================================================
        //  ⑰b 类型变更失败回滚：类型/基准/镜像全部还原
        // ==================================================================

        private static void RunTypeChangeRollback()
        {
            var connection = CreateConnection();
            var ws = CreateWorkspace(out var solution);
            var changed = AddVariable(ws, "TypeChange", "10");
            changed.DefaultValue = 7;
            changed.Value = 7;
            changed.Description = string.Empty;
            var second = AddVariable(ws, "Second", "20");
            second.Description = string.Empty;

            var rows = new[]
            {
                Row(2,
                    (DeviceVariableColumns.Name, "TypeChange"),
                    (DeviceVariableColumns.DataType, "整数数组 (int[])"),
                    (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                    (DeviceVariableColumns.Offset, "10"),
                    (DeviceVariableColumns.ElementCount, "3")),
                Row(3,
                    (DeviceVariableColumns.Name, "Second"),
                    (DeviceVariableColumns.DataType, "整数 (int)"),
                    (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                    (DeviceVariableColumns.Offset, "21")),
            };
            var plan = BuildPlan(connection, ws, rows, new[] { changed, second }, solution.VariableGroups.ToList());
            Check("类型变更回滚：计划 2 更新", plan.UpdateCount == 2,
                $"更新 {plan.UpdateCount} / 错误 {plan.ErrorCount}");

            var calls = 0;
            var threw = false;
            try
            {
                DeviceVariableImportService.Apply(
                    plan, ws,
                    _ => { calls++; return calls == 1; },
                    deleteMissing: false,
                    skipErrors: false);
            }
            catch (Exception)
            {
                threw = true;
            }

            Check("类型变更回滚：第二项重注册失败", threw, threw ? "已抛出" : "未抛出");
            Check("类型变更回滚：类型/基准/镜像全部还原",
                changed.DataType == typeof(int)
                && Convert.ToInt32(changed.DefaultValue) == 7
                && Convert.ToInt32(changed.Value) == 7,
                $"type={changed.DataType.Name} default={changed.DefaultValue} value={changed.Value}");
        }

        // ==================================================================
        //  ⑱ 协议切换：MC 地址挂 Fins 连接必须重建地址并重注册
        // ==================================================================

        private static void RunProtocolSwap()
        {
            var connection = new CommunicationConfig
            {
                ConnectionName = "FINS-1",
                Protocol = CommunicationType.OmronFins,
                ReadCycleMs = 1000
            };
            var ws = CreateWorkspace(out var solution);
            var nv = new NetworkVariableModel
            {
                Name = "ProtoSwap",
                DataType = typeof(int),
                Description = string.Empty,
                ConnectionName = "FINS-1",
                AddressConfig = BuildMcAddress("100"),
                DefaultValue = 0,
                Value = 0
            };
            ws.GlobalVariables.Add(nv);

            var row = Row(2,
                (DeviceVariableColumns.Name, "ProtoSwap"),
                (DeviceVariableColumns.DataType, "整数 (int)"),
                (DeviceVariableColumns.Area, "D"),
                (DeviceVariableColumns.Offset, "100"));
            var plan = BuildPlan(connection, ws, new[] { row }, new[] { nv }, solution.VariableGroups.ToList());
            Check("协议切换：MC 地址挂 Fins 连接判更新", plan.UpdateCount == 1,
                $"{plan.Items[0].StatusText} / {plan.Items[0].Note}");

            var reRegistered = new List<string>();
            DeviceVariableImportService.Apply(
                plan, ws,
                v => { reRegistered.Add(v.Name); return true; },
                false, false);

            Check("协议切换：地址重建为 FinsAddress", nv.AddressConfig is FinsAddress,
                nv.AddressConfig?.GetType().Name ?? "null");
            Check("协议切换：触发重注册", reRegistered.Contains("ProtoSwap"),
                string.Join(",", reRegistered));
        }

        // ==================================================================
        //  ⑳ 位偏移上限按协议：FINS 0~15，其余 0~7
        // ==================================================================

        private static void RunBitOffsetRange()
        {
            var finsConnection = new CommunicationConfig
            {
                ConnectionName = "FINS-1",
                Protocol = CommunicationType.OmronFins,
                ReadCycleMs = 1000
            };
            var finsWs = CreateWorkspace(out var finsSolution);
            var finsAddress = BuildFinsBitAddress("100", 15);
            var finsVar = new NetworkVariableModel
            {
                Name = "FinsBit15",
                DataType = typeof(bool),
                Description = string.Empty,
                ConnectionName = "FINS-1",
                AddressConfig = finsAddress,
                DefaultValue = false,
                Value = false
            };
            finsWs.GlobalVariables.Add(finsVar);

            var finsRow = Row(2,
                (DeviceVariableColumns.Name, "FinsBit15"),
                (DeviceVariableColumns.DataType, "布尔 (bool)"),
                (DeviceVariableColumns.Area, "D"),
                (DeviceVariableColumns.Offset, "100"),
                (DeviceVariableColumns.BitOffset, "15"));
            var finsPlan = BuildPlan(
                finsConnection, finsWs, new[] { finsRow }, new[] { finsVar }, finsSolution.VariableGroups.ToList());
            Check("FINS 位偏移 15：放行并判无变化",
                finsPlan.UnchangedCount == 1,
                $"{finsPlan.Items[0].StatusText} / {finsPlan.Items[0].Note}");

            var s7Connection = new CommunicationConfig
            {
                ConnectionName = "S7-1",
                Protocol = CommunicationType.SiemensS7,
                ReadCycleMs = 1000
            };
            var s7Ws = CreateWorkspace(out var s7Solution);
            var s7Row = Row(3,
                (DeviceVariableColumns.Name, "S7Bit15"),
                (DeviceVariableColumns.DataType, "布尔 (bool)"),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[S7Area.M]),
                (DeviceVariableColumns.Offset, "0"),
                (DeviceVariableColumns.BitOffset, "15"));
            var s7Plan = BuildPlan(
                s7Connection, s7Ws, new[] { s7Row }, Array.Empty<NetworkVariableModel>(), s7Solution.VariableGroups.ToList());
            Check("S7 位偏移 15：明确报错（上限 7，不静默钳成 7）",
                s7Plan.Items[0].Status == DeviceVariableImportStatus.Error
                && s7Plan.Items[0].Note.Contains("0 ~ 7", StringComparison.Ordinal),
                s7Plan.Items[0].Note);
        }

        // ==================================================================
        //  工具
        // ==================================================================

        // ==================================================================
        //  ⑨ 汇川 EasyNet CSV 直接导入
        // ==================================================================
        //
        // 【背景】汇川 EasyMonitor 导出的 CSV 与本仓 xlsx 不是同一个 schema，而且不同版本导出的
        // 列数都不一样（16 列 A..P / 13 列 A..M）。适配器只做"翻译"，校验仍归 BuildPlan。
        // 三条按真实文件踩出来的口径：按表头取列、数组计数>1 才算数组、无 BOM 的 GB18030。

        private static void RunEasyNetCsvImport()
        {
            Section("[IO] 汇川 EasyNet CSV 直接导入（表头取列 / 数组计数 / GB18030）");

            // —— 13 列新导出：标量的数组计数写 1，变量组Id 是最后一列 ——
            var newStyle = string.Join("\r\n", new[]
            {
                "#变量导出文件（汇川）",
                "#列A:名称",
                "名称,连接,数据类型,长度,数组计数,地址,采集周期,采集模式,上限,下限,初始值,注释,变量组Id",
                "\"Alarm1[0]\",,\"Bool\",\"1\",\"1\",\"UB 2101060\",\"100ms\",\"1\",,,,\"打标超时\",1",
                "\"Alarm1[1]\",,\"Bool\",\"1\",\"1\",\"UB 2101061\",\"100ms\",\"1\",,,,\"\",1",
                "\"Speed\",,\"Int32\",\"4\",\"1\",\"UW 21011B0\",\"200ms\",\"1\",,,,\"\",1",
                "\"Barcode\",,\"String\",\"50\",\"1\",\"UW 2101200\",\"100ms\",\"1\",,,,\"\",1",
                "\"Arr\",,\"Int16\",\"2\",\"[0,19]\",\"UW 2101300\",\"100ms\",\"1\",,,,\"\",1",
            });

            var newPath = TempCsv(newStyle);
            var connection = CreateEasyNetConnection();
            var rows = DeviceVariableImportService.ReadRows(newPath, connection.ReadCycleMs);

            Check("CSV：注释行与表头行不算数据行（5 条数据）", rows.Count == 5, $"count={rows.Count}");

            var alarm0 = rows[0];
            Check("CSV：名称 → 变量名", alarm0.Get(DeviceVariableColumns.Name) == "Alarm1[0]",
                alarm0.Get(DeviceVariableColumns.Name));
            Check("CSV：Bool → 布尔 (bool)（用本仓显示名，不是汇川原文）",
                alarm0.Get(DeviceVariableColumns.DataType) == "布尔 (bool)",
                alarm0.Get(DeviceVariableColumns.DataType));
            Check("CSV：地址「UB 2101060」拆成 存储区=U / 偏移量=2101060",
                alarm0.Get(DeviceVariableColumns.Area) == "U"
                && alarm0.Get(DeviceVariableColumns.Offset) == "2101060",
                $"{alarm0.Get(DeviceVariableColumns.Area)} / {alarm0.Get(DeviceVariableColumns.Offset)}");
            Check("★CSV：GB18030 无 BOM 也能解出中文描述（按 UTF-8 硬解会变成 U+FFFD）",
                alarm0.Get(DeviceVariableColumns.Description) == "打标超时",
                alarm0.Get(DeviceVariableColumns.Description));
            Check("★CSV：数组计数写 1 的标量不能被当成数组",
                alarm0.Get(DeviceVariableColumns.ElementCount) == "1"
                && !alarm0.Get(DeviceVariableColumns.DataType).Contains("[]"),
                $"{alarm0.Get(DeviceVariableColumns.ElementCount)} / {alarm0.Get(DeviceVariableColumns.DataType)}");
            Check("CSV：变量组Id → 显示分组「汇川组1」",
                alarm0.Get(DeviceVariableColumns.DisplayGroup) == "汇川组1",
                alarm0.Get(DeviceVariableColumns.DisplayGroup));
            Check("★CSV：采集周期与连接读周期一致时不写扫描组（否则每次都弹「未知扫描组」）",
                alarm0.Get(DeviceVariableColumns.ScanGroup) == "",
                alarm0.Get(DeviceVariableColumns.ScanGroup));
            Check("CSV：采集周期与连接读周期不一致时保留原值，交给 BuildPlan 报「回落默认组」",
                rows[2].Get(DeviceVariableColumns.ScanGroup) == "200ms",
                rows[2].Get(DeviceVariableColumns.ScanGroup));

            Check("CSV：字符串标量的元素个数 = 「长度」列（总字节数）",
                rows[3].Get(DeviceVariableColumns.ElementCount) == "50",
                rows[3].Get(DeviceVariableColumns.ElementCount));
            Check("CSV：数组计数 [0,19] → 20 个元素 + 数组类型",
                rows[4].Get(DeviceVariableColumns.ElementCount) == "20"
                && rows[4].Get(DeviceVariableColumns.DataType) == "短整数数组 (short[])",
                $"{rows[4].Get(DeviceVariableColumns.ElementCount)} / {rows[4].Get(DeviceVariableColumns.DataType)}");

            var plan = DeviceVariableImportService.BuildPlan(
                connection, rows, Array.Empty<NetworkVariableModel>(), Array.Empty<IVariable>(),
                Array.Empty<VariableGroupDescriptor>(), new[] { PollScheduler.DefaultGroupName },
                autoCreateDisplayGroups: true);

            Check("★CSV → 导入计划：5 条全部新增、0 错误（下游校验一条都没改）",
                plan.GlobalError == null && plan.NewCount == 5 && plan.ErrorCount == 0,
                $"global={plan.GlobalError} new={plan.NewCount} err={plan.ErrorCount}");

            Check("CSV → 导入计划：自动建显示分组「汇川组1」",
                plan.NewDisplayGroups.Contains("汇川组1"),
                string.Join("、", plan.NewDisplayGroups));

            Check("CSV → 导入计划：「200ms」被列成未知扫描组（回落默认组，用户看得见）",
                plan.UnknownScanGroups.Contains("200ms"),
                string.Join("、", plan.UnknownScanGroups));

            // —— 16 列老导出：分组 Id 在倒数第 4 列，后面还跟着索引变量/操作记录/父节点 ——
            var oldStyle = string.Join("\r\n", new[]
            {
                "名称,连接,数据类型,长度,数组计数,地址,采集周期,采集模式,上限,下限,初始值,注释,变量组Id,索引变量,操作记录,父节点",
                "\"Old1\",,\"Int16\",\"2\",\"1\",\"UW 100\",\"100ms\",\"1\",,,,\"老格式\",3,\"\",\"\",\"\"",
            });

            var oldPath = TempCsv(oldStyle);
            var oldRows = DeviceVariableImportService.ReadRows(oldPath, 100);
            Check("★CSV：16 列老导出的分组 Id 取第 13 列（不是「下限」，也不是「父节点」）",
                oldRows.Count == 1 && oldRows[0].Get(DeviceVariableColumns.DisplayGroup) == "汇川组3",
                oldRows.Count == 1 ? oldRows[0].Get(DeviceVariableColumns.DisplayGroup) : "读不到行");

            // —— 非 CSV 后缀仍走 xlsx：这里只钉"分流条件"，xlsx 本身由上面 20 条往返断言守 ——
            Check("CSV：只按 .csv 后缀分流（大写后缀同样认）",
                EasyNetCsvReader.IsCsv("a.CSV") && !EasyNetCsvReader.IsCsv("a.xlsx") && !EasyNetCsvReader.IsCsv("a.csvx"),
                "");

            TryDelete(newPath);
            TryDelete(oldPath);
        }

        private static CommunicationConfig CreateEasyNetConnection()
            => new("PLC-1", new EasyNetConfig
            {
                // 手工构造 Config 必须显式赋 Type（默认 0 = ModbusTcp，工厂会取错实现）
                Type = CommunicationType.EasyNet,
                IpAddress = "127.0.0.1",
                Port = 12939,
            })
            {
                ReadCycleMs = 100,
            };

        /// <summary>写一个 GB18030（无 BOM）的临时 CSV——汇川导出的真实编码。</summary>
        private static string TempCsv(string text)
        {
            try { return TempCsvCore(text); }
            catch (ArgumentException)
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                return TempCsvCore(text);
            }
        }

        private static string TempCsvCore(string text)
        {
            var path = Path.Combine(Path.GetTempPath(), "vm_io_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllBytes(path, System.Text.Encoding.GetEncoding("GB18030").GetBytes(text));
            return path;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* 临时文件，删不掉也不影响断言 */ }
        }

        private static CommunicationConfig CreateConnection()
        {
            var connection = new CommunicationConfig
            {
                ConnectionName = "PLC-1",
                Protocol = CommunicationType.ModbusTcp,
                ReadCycleMs = 1000
            };
            connection.ScanGroups.Add(new ScanGroupConfig("Fast", 200));
            return connection;
        }

        private static WorkspaceContext CreateWorkspace(out SolutionModel solution)
        {
            var ws = new WorkspaceContext();
            solution = new SolutionModel();
            ws.SwitchSolution(solution);
            return ws;
        }

        private static NetworkVariableModel AddVariable(
            WorkspaceContext ws, string name, string offset, Guid groupId = default)
        {
            var nv = new NetworkVariableModel
            {
                Name = name,
                DataType = typeof(int),
                Description = name + " 描述",
                ConnectionName = "PLC-1",
                AddressConfig = BuildModbusAddress(offset),
                ScanGroup = string.Empty,
                GroupId = groupId,
                DefaultValue = 0,
                Value = 0
            };
            ws.GlobalVariables.Add(nv);
            return nv;
        }

        private static DeviceAddressBase BuildModbusAddress(string offset)
        {
            var ok = GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.ModbusTcp, ModbusArea.HoldingRegisters, offset, -1, typeof(int),
                out var address, out var error);
            Check("构造 Modbus 测试地址", ok, error);
            return address;
        }

        private static DeviceAddressBase BuildS7Address(string offset, int dbNumber)
        {
            var ok = GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.SiemensS7, S7Area.DB, offset, -1, typeof(int),
                out var address, out var error, dbNumber: dbNumber);
            Check("构造 S7 测试地址", ok, error);
            return address;
        }

        private static DeviceAddressBase BuildMcAddress(string offset)
        {
            var ok = GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.MitsubishiMc, McArea.D, offset, -1, typeof(int),
                out var address, out var error);
            Check("构造 MC 测试地址", ok, error);
            return address;
        }

        private static DeviceAddressBase BuildFinsAddress(string offset)
        {
            var ok = GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.OmronFins, FinsArea.D, offset, -1, typeof(int),
                out var address, out var error);
            Check("构造 Fins 测试地址", ok, error);
            return address;
        }

        private static DeviceAddressBase BuildFinsBitAddress(string offset, int bitOffset)
        {
            var ok = GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.OmronFins, FinsArea.D, offset, bitOffset, typeof(bool),
                out var address, out var error);
            Check("构造 FINS 位地址", ok, error);
            return address;
        }

        private static DeviceAddressBase BuildModbusArrayAddress(string offset, int count)
        {
            var ok = GlobalVariableManagerViewModel.TryBuildAddress(
                CommunicationType.ModbusTcp, ModbusArea.HoldingRegisters, offset, -1, typeof(int[]),
                out var address, out var error, elementCount: count);
            Check("构造 Modbus 数组测试地址", ok, error);
            return address;
        }

        private static IReadOnlyList<string> ScanGroups(CommunicationConfig connection)
            => new[] { PollScheduler.DefaultGroupName }
                .Concat(connection.ScanGroups.Select(g => g.Name))
                .ToList();

        private static DeviceVariableImportPlan BuildPlan(
            CommunicationConfig connection,
            WorkspaceContext ws,
            IReadOnlyList<DeviceVariableRawRow> rows,
            IReadOnlyList<NetworkVariableModel> existing,
            IReadOnlyList<VariableGroupDescriptor> groups,
            bool autoCreate = true)
            => DeviceVariableImportService.BuildPlan(
                connection,
                rows,
                existing,
                ws.GlobalVariables.ToList(),
                groups,
                ScanGroups(connection),
                autoCreate);

        private static DeviceVariableRawRow ModbusRow(
            int rowNumber, string name, string type, string offset,
            string displayGroup = "", string scanGroup = "")
            => Row(rowNumber,
                (DeviceVariableColumns.Name, name),
                (DeviceVariableColumns.DataType, type),
                (DeviceVariableColumns.Area, GlobalVariableManagerViewModel.AreaDisplayNames[ModbusArea.HoldingRegisters]),
                (DeviceVariableColumns.Offset, offset),
                (DeviceVariableColumns.DisplayGroup, displayGroup),
                (DeviceVariableColumns.ScanGroup, scanGroup));

        private static DeviceVariableRawRow Row(int rowNumber, params (string Key, string Value)[] cells)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in cells)
                dict[DeviceVariableIoText.NormalizeHeader(key)] = value;
            return new DeviceVariableRawRow { RowNumber = rowNumber, Cells = dict };
        }

        private static string Cell(Dictionary<string, object?> row, string key)
            => DeviceVariableIoText.CellText(row.TryGetValue(key, out var value) ? value : null);

        private static string TempXlsx()
            => Path.Combine(Path.GetTempPath(), "vm_io_" + Guid.NewGuid().ToString("N") + ".xlsx");
    }
}
