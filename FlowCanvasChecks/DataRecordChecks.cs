using System.IO;
using Core.Interfaces;
using HalconDotNet;
using Plugin.DataRecord;
using Plugin.ExcelExport;
using VisionMaster.Models;
using VisionMaster.Services;
// 裸写 ExecutionContext 会与 System.Threading.ExecutionContext 撞名（两个宿主都启用了隐式 using）
using ExecutionContext = VisionMaster.Services.ExecutionContext;

// 方案 B（2026-10-30）：`ColumnSource.Variable` 已从 UI 下线并标了 [Obsolete]，
// 但**成员与数值必须保留**（旧 .vms 的 "Source": 1 要能反序列化），运行期分支也保留。
// 下面的用例正是钉住"旧配置还能跑"，所以这里显式抑制 CS0618——不是违规，是刻意兼容路径。
#pragma warning disable CS0618

namespace FlowCanvasChecks
{
    /// <summary>
    /// CSV 记录插件的行为断言（P0-B）。
    ///
    /// 覆盖"账本"最容易静默出错的面：
    ///  - 表头/列序/CSV 转义（逗号、引号、换行）；
    ///  - 端口 / 运行期变量 / 内置字段三种取值来源；
    ///  - 目录与文件名模板、跨月/跨天滚动；
    ///  - 全局序号与边车持久化；
    ///  - 保留期清理只删自己模式匹配的文件；
    ///  - 仅NG存图（图与账本靠"图片路径"列关联）；
    ///  - 异步写盘失败可见（阻断/不阻断两条路）与可等待 flush；
    ///  - Excel 导出前的 flush 桥（未注册放行 / 卡住则导出失败）。
    ///
    /// 所有用例都走真 RunAlgorithm + 真后台写线程；用 RecordingHub.TryFlush 等队列排空，
    /// 不靠 Sleep 赌时序。
    /// </summary>
    internal static class DataRecordChecks
    {
        public static void Run()
        {
            DataPipelineCheckSupport.Section("CSV记录：转义 / 来源 / 模板 / 序号 / 清理 / 存图 / 写盘失败");

            DataPipelineCheckSupport.SafeRun("表头/行/转义", HeaderRowsAndEscaping);
            DataPipelineCheckSupport.SafeRun("运行期变量来源（旧配置兼容）", VariableSource);
            DataPipelineCheckSupport.SafeRun("变量来源覆盖全局/通讯与数组格式（N2，旧配置兼容）", VariableSourceCoversSharedRegistry);
            DataPipelineCheckSupport.SafeRun("缺名变量 WARN 有上限（N2）", MissingVariableWarnsBounded);
            DataPipelineCheckSupport.SafeRun("方案B：下拉不再提供「变量」档", SourceDropdownExcludesVariable);
            DataPipelineCheckSupport.SafeRun("目录/文件名模板", TemplateExpansion);
            DataPipelineCheckSupport.SafeRun("序号边车", SequenceSidecar);
            DataPipelineCheckSupport.SafeRun("保留期清理", KeepDaysCleanupOnlyTouchesOwnFiles);
            DataPipelineCheckSupport.SafeRun("仅NG存图", OnlyNgImage);
            DataPipelineCheckSupport.SafeRun("无列明确失败", NoColumnsFails);
            DataPipelineCheckSupport.SafeRun("异步写盘失败（阻断）", AsyncWriteFailureBlocks);
            DataPipelineCheckSupport.SafeRun("异步写盘失败（不阻断）与即时日志", AsyncWriteFailureDoesNotBlockButLogs);
            DataPipelineCheckSupport.SafeRun("TryFlush 正常路径", TryFlushSucceedsOnHealthyPath);
            DataPipelineCheckSupport.SafeRun("flush 桥未注册", FlushBridgeUnregisteredPasses);
            DataPipelineCheckSupport.SafeRun("导出前 flush 失败", ExcelExportFailsWhenFlushBridgeStuck);
        }

        // ------------------------------------------------------------------
        //  用例
        // ------------------------------------------------------------------

        /// <summary>
        /// 方案 B：`ColumnSource.Variable` 必须**从下拉数据源里消失**，但枚举成员与数值不变。
        ///
        /// 为什么要单独钉两条：
        /// ① 只隐藏 UI 不删成员 → 旧 `.vms` 的 `"Source": 1` 仍能反序列化（数据安全）；
        /// ② 只保留成员不隐藏 UI → 用户还能新建「变量」档，等于方案 B 没做。
        /// 两个方向都要断言，否则任何一边悄悄退回去都不会红。
        /// </summary>
        private static void SourceDropdownExcludesVariable()
        {
            var plugin = new DataRecordPlugin();
            var sources = plugin.ColumnSources.Cast<ColumnSource>().ToList();

            DataPipelineCheckSupport.Check("方案B：数据记录下拉只留「取值（绑定）」与「内置」",
                sources.Count == 2 && sources.Contains(ColumnSource.Port) && sources.Contains(ColumnSource.BuiltIn),
                string.Join(",", sources));

            // 枚举数值不许动：Variable 仍是 1、BuiltIn 仍是 2（旧 .vms 靠数值反序列化）
            DataPipelineCheckSupport.Check("方案B：枚举数值不变（Variable=1 / BuiltIn=2）",
                (int)ColumnSource.Variable == 1 && (int)ColumnSource.BuiltIn == 2,
                $"Variable={(int)ColumnSource.Variable} BuiltIn={(int)ColumnSource.BuiltIn}");

            // 旧配置（Source=1）反序列化后仍是 Variable 档，且名字保留
            var def = Newtonsoft.Json.JsonConvert.DeserializeObject<RecordColumnDef>(
                "{\"Name\":\"旧列\",\"Source\":1,\"VariableName\":\"Legacy\"}");
            DataPipelineCheckSupport.Check("方案B：旧 .vms 的 Source:1 仍反序列化成「变量」档",
                def != null && (int)def.Source == 1 && def.VariableName == "Legacy",
                def == null ? "null" : $"Source={(int)def.Source} Name={def.VariableName}");
        }

        private static void HeaderRowsAndEscaping()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_basic");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p =>
            {
                p.Columns.Add(Col("时间", ColumnSource.BuiltIn, BuiltInField.Timestamp));
                p.Columns.Add(Col("序号", ColumnSource.BuiltIn, BuiltInField.Sequence));
                p.Columns.Add(Col("结果", ColumnSource.Port));
                p.Columns.Add(Col("备注", ColumnSource.Port));
            });

            SetPort(plugin, "结果", "OK");
            SetPort(plugin, "备注", "a,b\"c\nd");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            SetPort(plugin, "结果", "NG");
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            bool flushed = RecordingHub.FlushForTests();
            DataPipelineCheckSupport.Check("CSV：后台队列在 5 秒内排空", flushed, flushed ? "" : "超时");

            string path = Path.Combine(dir, "ledger.csv");
            DataPipelineCheckSupport.Check("CSV：文件已生成", File.Exists(path), path);
            if (!File.Exists(path)) return;

            bool hasBom = DataPipelineCheckSupport.HasUtf8Bom(path);
            DataPipelineCheckSupport.Check("CSV：带 UTF-8 BOM（Excel 双击不乱码）",
                hasBom, hasBom ? "有 BOM" : "无 BOM");

            var rows = DataPipelineCheckSupport.ParseCsv(DataPipelineCheckSupport.ReadUtf8(path));
            DataPipelineCheckSupport.Check("CSV：表头列序 = 配置列序",
                rows.Count >= 1 && string.Join(",", rows[0]) == "时间,序号,结果,备注",
                rows.Count >= 1 ? string.Join(",", rows[0]) : "(空)");
            DataPipelineCheckSupport.Check("CSV：两件记录两行", rows.Count == 3, $"{Math.Max(0, rows.Count - 1)} 行");
            DataPipelineCheckSupport.Check("CSV：序号列从 1 单调递增",
                rows.Count == 3 && DataPipelineCheckSupport.Cell(rows, 1, 1) == "1" &&
                DataPipelineCheckSupport.Cell(rows, 2, 1) == "2",
                $"{DataPipelineCheckSupport.Cell(rows, 1, 1)},{DataPipelineCheckSupport.Cell(rows, 2, 1)}");
            DataPipelineCheckSupport.Check("CSV：逗号/引号/换行按 RFC 4180 转义后能原样读回",
                rows.Count == 3 && DataPipelineCheckSupport.Cell(rows, 1, 3) == "a,b\"c\nd",
                DataPipelineCheckSupport.Cell(rows, 1, 3).Replace("\n", "\\n"));
            DataPipelineCheckSupport.Check("CSV：时间戳列非空",
                rows.Count == 3 && DataPipelineCheckSupport.Cell(rows, 1, 0).Length > 0,
                DataPipelineCheckSupport.Cell(rows, 1, 0));

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void VariableSource()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_var");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p =>
            {
                p.Columns.Add(Col("值", ColumnSource.Variable, variable: "v"));
            });

            var log = new StubLog();
            var ctx = DataPipelineCheckSupport.NewContext(log);
            ctx.LocalVariables["v"] = 42;
            plugin.Execute(ctx);
            RecordingHub.FlushForTests();

            string path = Path.Combine(dir, "ledger.csv");
            var rows = File.Exists(path)
                ? DataPipelineCheckSupport.ParseCsv(DataPipelineCheckSupport.ReadUtf8(path))
                : new List<List<string>>();
            DataPipelineCheckSupport.Check("CSV：运行期变量来源取值正确",
                rows.Count == 2 && rows[1][0] == "42",
                rows.Count == 2 ? rows[1][0] : "(行数不足)");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        /// <summary>
        /// N2：来源=变量不再只认「本轮局部变量」——取不到时兜底到共享变量注册表
        /// （全局变量 / 通讯变量 / 系统变量共用一张表），并且数组不再落成 <c>System.Int32[]</c>。
        ///
        /// 为什么用**真 WorkspaceContext + 真 VariableRegistry** 而不是自己造桩：
        /// 生产链路是 <c>GlobalVariableWriter.TryRead → VariableRegistry.FindByName → IVariable.Value</c>，
        /// 桩掉这一段就等于把"注册表到底有没有把全局变量和通讯变量装进去"这件事排除在断言之外。
        /// </summary>
        private static void VariableSourceCoversSharedRegistry()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_var_n2");
            RecordingHub.ConfigureForTests(dir);

            var workspace = new WorkspaceContext();
            workspace.GlobalVariables.Clear();

            // ① 全局变量（变量管理里那批）
            workspace.GlobalVariables.Add(
                VariableFactory.CreateLocal("OKCount", typeof(int), "全局计数", 42));

            // ② 通讯变量（网络变量）：注册表只看 IVariable.Value（通信线程维护的镜像），
            //    没有地址/管理器时 Value 的 setter 不会写设备，正好当镜像值用。
            workspace.GlobalVariables.Add(new NetworkVariableModel
            {
                Name = "PLC1.Speed",
                ConnectionName = "PLC1",
                DataType = typeof(double),
                Value = 12.5
            });

            var plugin = NewPlugin(dir, p =>
            {
                p.Columns.Add(Col("全局值", ColumnSource.Variable, variable: "OKCount"));
                p.Columns.Add(Col("通讯值", ColumnSource.Variable, variable: "PLC1.Speed"));
                p.Columns.Add(Col("数组", ColumnSource.Variable, variable: "Arr"));
            });

            var log = new StubLog();
            var ctx = NewContextWithWorkspace(log, workspace);
            ctx.LocalVariables["Arr"] = new[] { 1, 2, 3 }; // 局部池放数组：验单元格格式化

            plugin.Execute(ctx);
            RecordingHub.FlushForTests();

            var rows = ReadLedger(dir);
            DataPipelineCheckSupport.Check("CSV N2：变量列取到全局变量（局部池没有 → 注册表兜底）",
                rows.Count == 2 && DataPipelineCheckSupport.Cell(rows, 1, 0) == "42",
                rows.Count == 2 ? DataPipelineCheckSupport.Cell(rows, 1, 0) : "(行数不足)");
            DataPipelineCheckSupport.Check("CSV N2：变量列取到通讯变量（网络变量镜像值）",
                rows.Count == 2 && DataPipelineCheckSupport.Cell(rows, 1, 1) == "12.5",
                rows.Count == 2 ? DataPipelineCheckSupport.Cell(rows, 1, 1) : "(行数不足)");
            DataPipelineCheckSupport.Check("CSV N2：数组列格式化成 [1,2,3]（不是 System.Int32[]）",
                rows.Count == 2 && DataPipelineCheckSupport.Cell(rows, 1, 2) == "[1,2,3]",
                rows.Count == 2 ? DataPipelineCheckSupport.Cell(rows, 1, 2) : "(行数不足)");
            DataPipelineCheckSupport.Check("CSV N2：三个变量都取到时不留缺名 WARN",
                log.Warns.All(w => !w.Contains("既不在本轮局部变量里", StringComparison.Ordinal)),
                log.Warns.Count == 0 ? "(无)" : string.Join(" | ", log.Warns));

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        /// <summary>
        /// N2：缺名变量必须留 WARN（不是静默写空），但同一名字最多 3 条——高频流程里
        /// "每轮都找不到"会把日志刷满，反而淹没真正的问题。
        /// </summary>
        private static void MissingVariableWarnsBounded()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_var_missing");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p =>
                p.Columns.Add(Col("缺名", ColumnSource.Variable, variable: "NoSuchVariableN2")));

            var log = new StubLog();
            var ctx = DataPipelineCheckSupport.NewContext(log);

            // 同一个插件实例跑 4 轮：提示计数挂在实例上（跨执行累计）
            plugin.Execute(ctx);
            plugin.Execute(ctx);
            plugin.Execute(ctx);
            plugin.Execute(ctx);
            RecordingHub.FlushForTests();

            int warns = log.Warns.Count(w => w.Contains("NoSuchVariableN2", StringComparison.Ordinal));
            DataPipelineCheckSupport.Check("CSV N2：缺名变量留 WARN（不是静默写空）",
                warns > 0, log.Warns.Count == 0 ? "(无 WARN)" : string.Join(" | ", log.Warns));
            DataPipelineCheckSupport.Check("CSV N2：缺名 WARN 有上限（4 轮只提示 3 条）",
                warns == 3, $"{warns} 条");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void TemplateExpansion()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_template");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p =>
            {
                p.DirectoryTemplate = Path.Combine(dir, "{yyyy-MM}");
                p.FileNamePattern = "{yyyy-MM-dd}.csv";
                p.Columns.Add(Col("结果", ColumnSource.Port));
            });
            SetPort(plugin, "结果", "OK");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            RecordingHub.FlushForTests();

            string expectedDir = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM"));
            string expectedFile = Path.Combine(expectedDir, DateTime.Now.ToString("yyyy-MM-dd") + ".csv");
            DataPipelineCheckSupport.Check("CSV：{yyyy-MM}/{yyyy-MM-dd} 模板按当天展开",
                File.Exists(expectedFile), expectedFile);
            DataPipelineCheckSupport.Check("CSV：模板目录下只有一本账",
                Directory.Exists(expectedDir) && Directory.GetFiles(expectedDir, "*.csv").Length == 1,
                Directory.Exists(expectedDir) ? $"{Directory.GetFiles(expectedDir, "*.csv").Length} 个" : "(目录不存在)");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void SequenceSidecar()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_seq");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p =>
            {
                p.Columns.Add(Col("序号", ColumnSource.BuiltIn, BuiltInField.Sequence));
            });

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            RecordingHub.FlushForTests();
            RecordingHub.PersistSequenceForTests();

            string sidecar = Path.Combine(dir, "DataRecord_seq.json");
            DataPipelineCheckSupport.Check("CSV：序号边车已落盘", File.Exists(sidecar), sidecar);
            DataPipelineCheckSupport.Check("CSV：边车记录当前序号 2",
                File.Exists(sidecar) && File.ReadAllText(sidecar).Trim() == "2",
                File.Exists(sidecar) ? File.ReadAllText(sidecar).Trim() : "(无文件)");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void KeepDaysCleanupOnlyTouchesOwnFiles()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_cleanup");
            RecordingHub.ConfigureForTests(dir);

            string oldFile = Path.Combine(dir, "old-2020-01-01.csv");
            string unrelated = Path.Combine(dir, "notes.csv");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(unrelated, "keep");

            var plugin = NewPlugin(dir, p =>
            {
                p.KeepDays = 1;
                p.Columns.Add(Col("结果", ColumnSource.Port));
            });
            SetPort(plugin, "结果", "OK");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            RecordingHub.FlushForTests();

            DataPipelineCheckSupport.Check("CSV：保留期清理删除过期日期文件", !File.Exists(oldFile), oldFile);
            DataPipelineCheckSupport.Check("CSV：保留期清理不碰文件名无日期的文件", File.Exists(unrelated), unrelated);

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void OnlyNgImage()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_image");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p =>
            {
                p.ImageSaveMode = ImageSaveMode.OnlyNG;
                p.ResultColumn = "结果";
                p.NgValue = "NG";
                p.ImageDirName = "img";
                p.ImageNameTemplate = "{序号}";
                p.Columns.Add(Col("结果", ColumnSource.Port));
                p.Columns.Add(Col("图片路径", ColumnSource.BuiltIn, BuiltInField.ImagePath));
            });

            HOperatorSet.GenImageConst(out HObject obj, "byte", 16, 16);
            var image = new HImage(obj);
            obj.Dispose();
            try
            {
                SetPort(plugin, "结果", "NG");
                SetPort(plugin, "图片", image);

                var log = new StubLog();
                plugin.Execute(DataPipelineCheckSupport.NewContext(log));
                RecordingHub.FlushForTests();

                string[] images = Directory.Exists(Path.Combine(dir, "img"))
                    ? Directory.GetFiles(Path.Combine(dir, "img"), "*.png")
                    : Array.Empty<string>();
                DataPipelineCheckSupport.Check("存图：仅NG 模式下 NG 件落一张图", images.Length == 1, $"{images.Length} 张");

                string csv = Path.Combine(dir, "ledger.csv");
                var rows = File.Exists(csv)
                    ? DataPipelineCheckSupport.ParseCsv(DataPipelineCheckSupport.ReadUtf8(csv))
                    : new List<List<string>>();
                DataPipelineCheckSupport.Check("存图：账本图片路径列指向刚落的图",
                    rows.Count == 2 && rows[1][1].EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(rows[1][1]),
                    rows.Count == 2 ? rows[1][1] : "(行数不足)");

                SetPort(plugin, "结果", "OK");
                plugin.Execute(DataPipelineCheckSupport.NewContext(log));
                RecordingHub.FlushForTests();

                images = Directory.Exists(Path.Combine(dir, "img"))
                    ? Directory.GetFiles(Path.Combine(dir, "img"), "*.png")
                    : Array.Empty<string>();
                DataPipelineCheckSupport.Check("存图：仅NG 模式下 OK 件不落图", images.Length == 1, $"{images.Length} 张");
            }
            finally
            {
                try { image.Dispose(); } catch { }
            }

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void NoColumnsFails()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_empty");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, _ => { });
            plugin.Columns.Clear();

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("CSV：没有启用列时明确失败", plugin.Success.Value is false, $"{plugin.Success.Value}");
            DataPipelineCheckSupport.Check("CSV：失败原因点名未配置列",
                (plugin.ErrorMessage.Value as string ?? "").Contains("未配置任何启用的列", StringComparison.Ordinal),
                plugin.ErrorMessage.Value as string ?? "(空)");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        /// <summary>
        /// 坏路径（父目录被同名文件占住）→ Directory.CreateDirectory 必失败。
        /// 这条失败发生在后台写线程，Enqueue 早已返回 Accepted：只有 flush + WriteErrorSeq 才拦得住。
        /// </summary>
        private static void AsyncWriteFailureBlocks()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_write_fail_block");
            RecordingHub.ConfigureForTests(dir);
            string blocker = Path.Combine(dir, "blocked");
            File.WriteAllText(blocker, "x"); // 占住路径：blocked\sub 建不出来

            var plugin = NewPlugin(dir, p =>
            {
                p.DirectoryTemplate = Path.Combine(blocker, "sub");
                p.Columns.Add(Col("结果", ColumnSource.Port));
                p.BlockOnFailure = true;
            });
            SetPort(plugin, "结果", "OK");

            var log = new StubLog();
            long seqBefore = RecordingHub.WriteErrorSeq;
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("CSV：异步写盘失败 + 阻断 → 步骤失败",
                plugin.Success.Value is false, $"{plugin.Success.Value}");
            string message = plugin.ErrorMessage.Value as string ?? "";
            DataPipelineCheckSupport.Check("CSV：失败原因点名数据写入失败与目标路径",
                message.Contains("数据写入失败", StringComparison.Ordinal) &&
                message.Contains("blocked", StringComparison.Ordinal),
                message.Length == 0 ? "(空)" : message);
            DataPipelineCheckSupport.Check("CSV：写盘失败计数递增（WriteErrorSeq）",
                RecordingHub.WriteErrorSeq > seqBefore, $"{seqBefore} → {RecordingHub.WriteErrorSeq}");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        /// <summary>
        /// 不阻断：步骤仍成功，但错误必须即时进应用日志（插件在本次执行开头把 LogSink 指向 context.Logger）。
        /// 另用临时 sink 直投一条坏记录，钉住 LogSink 本身（插件会把 sink 换成自己的 logger，故不混在一次执行里验）。
        /// </summary>
        private static void AsyncWriteFailureDoesNotBlockButLogs()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_write_fail_warn");
            RecordingHub.ConfigureForTests(dir);
            string blocker = Path.Combine(dir, "blocked");
            File.WriteAllText(blocker, "x");

            var plugin = NewPlugin(dir, p =>
            {
                p.DirectoryTemplate = Path.Combine(blocker, "sub");
                p.Columns.Add(Col("结果", ColumnSource.Port));
                p.BlockOnFailure = false;
            });
            SetPort(plugin, "结果", "OK");

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));
            bool flushed = RecordingHub.TryFlush(3000, out string flushError);

            DataPipelineCheckSupport.Check("CSV：异步写盘失败 + 不阻断 → 步骤仍成功",
                plugin.Success.Value is true, $"{plugin.Success.Value}");
            DataPipelineCheckSupport.Check("CSV：不阻断时队列仍能排空", flushed, flushError ?? "");
            DataPipelineCheckSupport.Check("CSV：不阻断时写盘错误即时进宿主日志（sink）",
                log.Errors.Any(e => e.Contains("[数据记录]", StringComparison.Ordinal) &&
                                    e.Contains("blocked", StringComparison.Ordinal)),
                log.Errors.Count == 0 ? "(无 ERROR)" : string.Join(" | ", log.Errors));

            var captured = new List<string>();
            var previousSink = RecordingHub.LogSink;
            RecordingHub.LogSink = (level, msg) => { lock (captured) captured.Add(level + ":" + msg); };
            try
            {
                var badTarget = new RecordingTarget
                {
                    OwnerName = "sink",
                    DirectoryTemplate = Path.Combine(blocker, "sub2"),
                    FileNamePattern = "sink.csv",
                    Header = new[] { "值" },
                    WriteHeader = true,
                    FlushEveryRow = true
                };
                RecordingHub.Enqueue(badTarget, new[] { "1" }, 0);
                bool sinkFlushed = RecordingHub.TryFlush(3000, out string sinkFlushError);

                DataPipelineCheckSupport.Check("CSV：LogSink 收到后台写盘 ERROR（临时 sink 捕获）",
                    sinkFlushed && captured.Any(c => c.StartsWith("ERROR:", StringComparison.Ordinal) &&
                                                     c.Contains("blocked", StringComparison.Ordinal)),
                    sinkFlushed ? (captured.Count == 0 ? "(无日志)" : string.Join(" | ", captured)) : sinkFlushError);
            }
            finally
            {
                RecordingHub.LogSink = previousSink;
            }

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void TryFlushSucceedsOnHealthyPath()
        {
            string dir = DataPipelineCheckSupport.TempDir("csv_flush_ok");
            RecordingHub.ConfigureForTests(dir);

            var plugin = NewPlugin(dir, p => p.Columns.Add(Col("结果", ColumnSource.Port)));
            SetPort(plugin, "结果", "OK");
            plugin.Execute(DataPipelineCheckSupport.NewContext(new StubLog()));

            bool flushed = RecordingHub.TryFlush(5000, out string error);
            DataPipelineCheckSupport.Check("CSV：TryFlush 正常路径返回 true 且无错误",
                flushed && error == null, error ?? "(无错误)");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void FlushBridgeUnregisteredPasses()
        {
            var previous = DataRecordFlushBridge.FlushHandler;
            DataRecordFlushBridge.FlushHandler = null;
            try
            {
                DataPipelineCheckSupport.Check("Excel：flush 桥未注册时直接放行（不阻塞单独使用）",
                    DataRecordFlushBridge.TryFlush(1), "(应为 true)");
            }
            finally
            {
                DataRecordFlushBridge.FlushHandler = previous;
            }
        }

        /// <summary>
        /// 桥卡住（队列排不空）时，Excel 导出必须失败并给出可读原因，不许静默读半截账本。
        /// </summary>
        private static void ExcelExportFailsWhenFlushBridgeStuck()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_flush_stuck");
            string csv = Path.Combine(dir, "ledger.csv");
            File.WriteAllText(csv, "序号,结果\r\n1,OK\r\n");
            string report = Path.Combine(dir, "report.xlsx");

            var previous = DataRecordFlushBridge.FlushHandler;
            DataRecordFlushBridge.FlushHandler = _ => false; // 模拟 CSV 队列排不空
            try
            {
                var plugin = new ExcelExportPlugin();
                plugin.Initialize(new CheckStepData { StepName = "报表" });
                plugin.SourcePathTemplate = csv;
                plugin.OutputDirectoryTemplate = dir;
                plugin.ReportFileNamePattern = "report.xlsx";
                plugin.ExportPolicy = ExportPolicy.EachRun;
                plugin.BlockOnFailure = true;

                plugin.Execute(DataPipelineCheckSupport.NewContext(new StubLog()));

                DataPipelineCheckSupport.Check("Excel：导出前 flush 失败 → 步骤失败",
                    plugin.Success.Value is false, $"{plugin.Success.Value}");
                string message = plugin.ErrorMessage.Value as string ?? "";
                DataPipelineCheckSupport.Check("Excel：失败原因点名等待队列排空超时",
                    message.Contains("排空超时", StringComparison.Ordinal),
                    message.Length == 0 ? "(空)" : message);
                DataPipelineCheckSupport.Check("Excel：flush 失败时不产出报表（不读半截账本）",
                    !File.Exists(report), report);
            }
            finally
            {
                DataRecordFlushBridge.FlushHandler = previous;
                DataPipelineCheckSupport.DeleteDir(dir);
            }
        }

        // ------------------------------------------------------------------
        //  辅助
        // ------------------------------------------------------------------

        private static DataRecordPlugin NewPlugin(string dir, Action<DataRecordPlugin> configure)
        {
            var plugin = new DataRecordPlugin();
            plugin.Initialize(new CheckStepData { StepName = "记录" });
            plugin.Columns.Clear();
            plugin.DirectoryTemplate = dir;
            plugin.FileNamePattern = "ledger.csv";
            plugin.WriteHeader = true;
            plugin.FlushEveryRow = true;
            plugin.KeepDays = 0;
            configure?.Invoke(plugin);
            return plugin;
        }

        /// <summary>
        /// 带"真工作区"的执行上下文：共享变量走生产同一条链路
        /// （<c>GlobalVariableWriter.TryRead → VariableRegistry.FindByName → IVariable.Value</c>）。
        /// </summary>
        private static IExecutionContext NewContextWithWorkspace(StubLog log, WorkspaceContext workspace)
            => new ExecutionContext(log, new FlowSession { FlowName = "数据链路断言" }, workspace, CancellationToken.None);

        /// <summary>读回本用例的账本（不存在时返回空表，让断言给出可读的失败而不是抛）。</summary>
        private static List<List<string>> ReadLedger(string dir)
        {
            string path = Path.Combine(dir, "ledger.csv");
            return File.Exists(path)
                ? DataPipelineCheckSupport.ParseCsv(DataPipelineCheckSupport.ReadUtf8(path))
                : new List<List<string>>();
        }

        private static RecordColumnDef Col(
            string name, ColumnSource source, BuiltInField builtIn = BuiltInField.Timestamp, string variable = "")
            => new RecordColumnDef { Name = name, Source = source, BuiltIn = builtIn, VariableName = variable };

        private static void SetPort(DataRecordPlugin plugin, string name, object value)
        {
            if (plugin.Inputs.TryGetValue(name, out var port))
                port.Value = value;
        }
    }
}
