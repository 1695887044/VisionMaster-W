using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MiniExcelLibs;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 报表导出（<c>xlsx</c> / <c>csv</c>）。
    ///
    /// 两条纪律
    /// ---------
    /// ① <b>先写临时文件再覆盖移动</b>：MiniExcel 写到一半失败时，用户原来那份文件还在
    ///    （与 <see cref="DeviceVariableIoService"/> 的 xlsx 导出同一条）。
    /// ② <b>失败不抛</b>：返回 false + 中文原因，由调用方显示在报表底部状态行。
    ///    R180：`File.Move(overwrite)` 在受限环境会抛 `UnauthorizedAccessException`，
    ///    两种异常都要接住，否则会带崩断言宿主。
    ///
    /// 格式口径
    /// ---------
    /// · xlsx 两个工作表：`报表`（列头 + 数据）与 `说明`（标题/范围/来源/生成时间/操作者/备注）。
    /// · csv 只有列头 + 数据（UTF-8 带 BOM，Excel 直接打开不乱码）——带标题行会破坏再导入。
    /// </summary>
    public sealed class ScadaReportExporter : IScadaReportExporter
    {
        /// <summary>数据表名</summary>
        public const string DataSheetName = "报表";

        /// <summary>说明表名</summary>
        public const string NoteSheetName = "说明";

        private static readonly string[] SupportedFormats = { "xlsx", "csv" };

        public IReadOnlyList<string> Formats => SupportedFormats;

        public bool TryExport(
            ScadaReportResult result, string title, string format, string path, out string error)
        {
            error = string.Empty;

            if (result == null || result.Rows.Count == 0)
            {
                error = "报表还没有数据：先查询出结果再导出。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "没有选择保存位置。";
                return false;
            }

            var kind = (format ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                return kind switch
                {
                    "xlsx" => WriteXlsx(result, title, path, out error),
                    "csv" => WriteCsv(result, path, out error),
                    _ => UnknownFormat(kind, out error),
                };
            }
            catch (IOException ex)
            {
                error = "写入失败（文件可能被占用）：" + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = "写入失败（没有权限或文件只读）：" + ex.Message;
                return false;
            }
        }

        private static bool UnknownFormat(string kind, out string error)
        {
            error = $"不支持的导出格式「{kind}」：只支持 xlsx / csv。";
            return false;
        }

        private static bool WriteXlsx(
            ScadaReportResult result, string title, string path, out string error)
        {
            error = string.Empty;

            // 列头去重：Dictionary 的键是列名，重名列（两张表各有一列"温度"）会被后者覆盖
            var headers = UniqueHeaders(result.Columns);

            var rows = new List<Dictionary<string, object?>>(result.Rows.Count);
            foreach (var row in result.Rows)
            {
                var item = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < headers.Count; i++)
                    item[headers[i]] = i < row.Count ? row[i] : string.Empty;
                rows.Add(item);
            }

            var notes = new List<Dictionary<string, object?>>
            {
                Note("标题", string.IsNullOrWhiteSpace(title) ? "报表" : title),
                Note("来源", result.SourceName ?? string.Empty),
                Note("生成时间", result.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                Note("行数", result.RowCount.ToString()),
                Note("扫描行数", result.ScannedRows.ToString()),
                Note("备注", result.Message ?? string.Empty),
            };

            var sheets = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [DataSheetName] = rows,
                [NoteSheetName] = notes,
            };

            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp.xlsx";
            try
            {
                MiniExcel.SaveAs(temp, sheets);
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (IOException ex)
            {
                error = "写入失败（文件可能被占用）：" + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = "写入失败（没有权限或文件只读）：" + ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static Dictionary<string, object?> Note(string key, string value)
            => new(StringComparer.Ordinal) { ["项目"] = key, ["内容"] = value };

        private static IReadOnlyList<string> UniqueHeaders(IReadOnlyList<string> columns)
        {
            var result = new List<string>(columns.Count);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < columns.Count; i++)
            {
                var name = string.IsNullOrWhiteSpace(columns[i]) ? $"列{i + 1}" : columns[i];

                if (seen.TryGetValue(name, out var count))
                {
                    seen[name] = count + 1;
                    name = $"{name}({count + 1})";
                }
                else
                {
                    seen[name] = 1;
                }

                result.Add(name);
            }

            return result;
        }

        private static bool WriteCsv(ScadaReportResult result, string path, out string error)
        {
            error = string.Empty;

            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", result.Columns.Select(EscapeCsv)));

            foreach (var row in result.Rows)
                builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));

            // UTF-8 带 BOM：Excel 双击打开不乱码（无 BOM 的 UTF-8 会被按本地代码页解）
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp.csv";

            try
            {
                File.WriteAllText(temp, builder.ToString(), encoding);
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (IOException ex)
            {
                error = "写入失败（文件可能被占用）：" + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = "写入失败（没有权限或文件只读）：" + ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>CSV 字段转义：含逗号/引号/换行时用双引号包裹，内部引号翻倍。</summary>
        public static string EscapeCsv(string? value)
        {
            var text = value ?? string.Empty;
            if (text.Length == 0)
                return string.Empty;

            var needsQuote = text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuote)
                return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
