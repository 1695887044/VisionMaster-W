using System.IO;
using System.IO.Compression;
using System.Text;
using MiniExcelLibs;
using Plugin.ExcelExport;

namespace FlowCanvasChecks
{
    /// <summary>
    /// Excel 报表插件的行为断言（P0-B）。
    ///
    /// 覆盖"报表"最容易静默出错的面：
    ///  - 源账本 → xlsx 的列序/值/行数；
    ///  - 图片路径列真的变成外部超链接（不是只把文本缩短）；
    ///  - 仅导出有图行；
    ///  - 源文件缺失、目标被 Excel 占用时的明确失败；
    ///  - 仅手动模式不偷偷导出。
    ///
    /// 读回用 MiniExcel（与导出器同一个库），避免"自己写 XML 自己读"的同源假绿；
    /// 超链接关系文件用 ZipArchive 直接看，绕开库的抽象。
    /// </summary>
    internal static class ExcelExportChecks
    {
        public static void Run()
        {
            DataPipelineCheckSupport.Section("Excel报表：列映射 / 超链接 / 仅图行 / 失败契约");

            DataPipelineCheckSupport.SafeRun("基础导出与超链接", BasicExportAndHyperlink);
            DataPipelineCheckSupport.SafeRun("仅图行", OnlyRowsWithImage);
            DataPipelineCheckSupport.SafeRun("源账本缺失", MissingSourceFails);
            DataPipelineCheckSupport.SafeRun("目标被占用", LockedTargetFails);
            DataPipelineCheckSupport.SafeRun("仅手动模式", ManualPolicyDoesNothing);
            DataPipelineCheckSupport.SafeRun("缺少链接列", MissingLinkColumnStillExports);
        }

        // ------------------------------------------------------------------
        //  用例
        // ------------------------------------------------------------------

        private static void BasicExportAndHyperlink()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_basic");
            string csv = Path.Combine(dir, "ledger.csv");
            string img1 = Path.Combine(dir, "1.png");
            string img2 = Path.Combine(dir, "2.png");
            File.WriteAllBytes(img1, Array.Empty<byte>());
            File.WriteAllBytes(img2, Array.Empty<byte>());
            WriteCsv(csv, new[]
            {
                new[] { "序号", "结果", "图片路径" },
                new[] { "1", "OK", img1 },
                new[] { "2", "NG", img2 },
            });

            var plugin = NewPlugin(csv, dir, p =>
            {
                p.ExportPolicy = ExportPolicy.EachRun;
                p.LinkColumn = "图片路径";
                p.FreezeHeader = true;
                p.AutoFilter = true;
            });

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("Excel：导出成功", plugin.Success.Value is true, $"{plugin.Success.Value}");
            DataPipelineCheckSupport.Check("Excel：导出行数 = 2", plugin.ExportedRows.Value is 2, $"{plugin.ExportedRows.Value}");

            string report = plugin.ReportPath.Value as string;
            DataPipelineCheckSupport.Check("Excel：报表文件存在", !string.IsNullOrEmpty(report) && File.Exists(report), report ?? "(空)");
            if (string.IsNullOrEmpty(report) || !File.Exists(report)) return;

            var rows = MiniExcel.Query(report, useHeaderRow: true)
                .Cast<IDictionary<string, object>>()
                .ToList();
            DataPipelineCheckSupport.Check("Excel：数据行数 = 2", rows.Count == 2, $"{rows.Count}");
            DataPipelineCheckSupport.Check("Excel：列序与 CSV 表头一致",
                rows.Count == 2 && rows[0].ContainsKey("序号") && rows[0].ContainsKey("结果") && rows[0].ContainsKey("图片路径"),
                rows.Count == 2 ? string.Join(",", rows[0].Keys) : "(行数不足)");
            DataPipelineCheckSupport.Check("Excel：单元格值正确",
                rows.Count == 2 && Convert.ToString(rows[0]["序号"]) == "1" && Convert.ToString(rows[1]["结果"]) == "NG",
                rows.Count == 2 ? $"{rows[0]["序号"]}/{rows[1]["结果"]}" : "(行数不足)");

            string rels = ReadZipEntry(report, "xl/worksheets/_rels/sheet1.xml.rels");
            DataPipelineCheckSupport.Check("Excel：图片路径列注入外部超链接（两个目标都在关系文件里）",
                rels.Contains("1.png", StringComparison.Ordinal) && rels.Contains("2.png", StringComparison.Ordinal),
                rels.Length == 0 ? "(无关系文件)" : rels);

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void OnlyRowsWithImage()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_onlyimg");
            string csv = Path.Combine(dir, "ledger.csv");
            string img1 = Path.Combine(dir, "1.png");
            File.WriteAllBytes(img1, Array.Empty<byte>());
            WriteCsv(csv, new[]
            {
                new[] { "序号", "图片路径" },
                new[] { "1", img1 },
                new[] { "2", @"relative\2.png" },
            });

            var plugin = NewPlugin(csv, dir, p =>
            {
                p.ExportPolicy = ExportPolicy.EachRun;
                p.LinkColumn = "图片路径";
                p.OnlyRowsWithImage = true;
            });

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("Excel：仅图行模式只导出有绝对路径的行",
                plugin.Success.Value is true && plugin.ExportedRows.Value is 1,
                $"success={plugin.Success.Value}, rows={plugin.ExportedRows.Value}");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void MissingSourceFails()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_missing");
            string csv = Path.Combine(dir, "not_exists.csv");

            var plugin = NewPlugin(csv, dir, p =>
            {
                p.ExportPolicy = ExportPolicy.EachRun;
                p.BlockOnFailure = true;
            });

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("Excel：源账本不存在时步骤失败（勾阻断）",
                plugin.Success.Value is false, $"{plugin.Success.Value}");
            DataPipelineCheckSupport.Check("Excel：失败原因点名源账本不存在",
                (plugin.ErrorMessage.Value as string ?? "").Contains("源账本不存在", StringComparison.Ordinal),
                plugin.ErrorMessage.Value as string ?? "(空)");

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void LockedTargetFails()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_locked");
            string csv = Path.Combine(dir, "ledger.csv");
            WriteCsv(csv, new[]
            {
                new[] { "序号", "结果" },
                new[] { "1", "OK" },
            });

            var plugin = NewPlugin(csv, dir, p =>
            {
                p.ExportPolicy = ExportPolicy.EachRun;
                p.BlockOnFailure = true;
            });

            string report = plugin.OutputPathPreview;
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, "locked");

            using (new FileStream(report, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var log = new StubLog();
                plugin.Execute(DataPipelineCheckSupport.NewContext(log));

                DataPipelineCheckSupport.Check("Excel：目标被占用时步骤失败（勾阻断）",
                    plugin.Success.Value is false, $"{plugin.Success.Value}");
                string message = plugin.ErrorMessage.Value as string ?? "";
                DataPipelineCheckSupport.Check("Excel：失败原因指向写入/占用",
                    message.Contains("写入报表失败", StringComparison.Ordinal) ||
                    message.Contains("正被 Excel 打开", StringComparison.Ordinal),
                    message.Length == 0 ? "(空)" : message);
            }

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void ManualPolicyDoesNothing()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_manual");
            string csv = Path.Combine(dir, "ledger.csv");
            WriteCsv(csv, new[]
            {
                new[] { "序号", "结果" },
                new[] { "1", "OK" },
            });

            var plugin = NewPlugin(csv, dir, p =>
            {
                p.ExportPolicy = ExportPolicy.Manual;
                p.BlockOnFailure = true;
            });

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("Excel：仅手动模式流程执行不导出",
                string.IsNullOrEmpty(plugin.ReportPath.Value as string), plugin.ReportPath.Value as string ?? "(空)");
            DataPipelineCheckSupport.Check("Excel：仅手动模式不产生报表文件",
                !File.Exists(plugin.OutputPathPreview), plugin.OutputPathPreview);

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        private static void MissingLinkColumnStillExports()
        {
            string dir = DataPipelineCheckSupport.TempDir("xlsx_nolink");
            string csv = Path.Combine(dir, "ledger.csv");
            WriteCsv(csv, new[]
            {
                new[] { "序号", "结果" },
                new[] { "1", "OK" },
                new[] { "2", "NG" },
            });

            var plugin = NewPlugin(csv, dir, p =>
            {
                p.ExportPolicy = ExportPolicy.EachRun;
                p.LinkColumn = "图片路径";
            });

            var log = new StubLog();
            plugin.Execute(DataPipelineCheckSupport.NewContext(log));

            DataPipelineCheckSupport.Check("Excel：找不到链接列时仍导出为纯表格",
                plugin.Success.Value is true && plugin.ExportedRows.Value is 2,
                $"success={plugin.Success.Value}, rows={plugin.ExportedRows.Value}");
            string rels = plugin.ReportPath.Value is string path && File.Exists(path)
                ? ReadZipEntry(path, "xl/worksheets/_rels/sheet1.xml.rels")
                : "";
            DataPipelineCheckSupport.Check("Excel：纯表格模式不生成外部超链接关系",
                !rels.Contains("hyperlink", StringComparison.OrdinalIgnoreCase) &&
                !rels.Contains("TargetMode=\"External\"", StringComparison.OrdinalIgnoreCase),
                rels.Length == 0 ? "(无关系文件)" : rels);

            DataPipelineCheckSupport.DeleteDir(dir);
        }

        // ------------------------------------------------------------------
        //  辅助
        // ------------------------------------------------------------------

        private static ExcelExportPlugin NewPlugin(string csvPath, string outDir, Action<ExcelExportPlugin> configure)
        {
            var plugin = new ExcelExportPlugin();
            plugin.Initialize(new CheckStepData { StepName = "报表" });
            plugin.SourcePathTemplate = csvPath;
            plugin.OutputDirectoryTemplate = outDir;
            plugin.ReportFileNamePattern = "report.xlsx";
            plugin.ExportPolicy = ExportPolicy.EachRun;
            plugin.LinkColumn = "图片路径";
            configure?.Invoke(plugin);
            return plugin;
        }

        private static void WriteCsv(string path, string[][] rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                for (int i = 0; i < row.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    string cell = row[i] ?? "";
                    if (cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                        cell = "\"" + cell.Replace("\"", "\"\"") + "\"";
                    sb.Append(cell);
                }
                sb.Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string ReadZipEntry(string xlsxPath, string entryName)
        {
            try
            {
                using var zip = ZipFile.OpenRead(xlsxPath);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName == entryName);
                if (entry == null) return "";
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }
    }
}
