using System.IO;
using HalconDotNet;
using Plugin.DataRecord;

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
    ///  - 仅NG存图（图与账本靠"图片路径"列关联）。
    ///
    /// 所有用例都走真 RunAlgorithm + 真后台写线程；用 RecordingHub.FlushForTests 等队列排空，
    /// 不靠 Sleep 赌时序。
    /// </summary>
    internal static class DataRecordChecks
    {
        public static void Run()
        {
            DataPipelineCheckSupport.Section("CSV记录：转义 / 来源 / 模板 / 序号 / 清理 / 存图");

            DataPipelineCheckSupport.SafeRun("表头/行/转义", HeaderRowsAndEscaping);
            DataPipelineCheckSupport.SafeRun("运行期变量来源", VariableSource);
            DataPipelineCheckSupport.SafeRun("目录/文件名模板", TemplateExpansion);
            DataPipelineCheckSupport.SafeRun("序号边车", SequenceSidecar);
            DataPipelineCheckSupport.SafeRun("保留期清理", KeepDaysCleanupOnlyTouchesOwnFiles);
            DataPipelineCheckSupport.SafeRun("仅NG存图", OnlyNgImage);
            DataPipelineCheckSupport.SafeRun("无列明确失败", NoColumnsFails);
        }

        // ------------------------------------------------------------------
        //  用例
        // ------------------------------------------------------------------

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
