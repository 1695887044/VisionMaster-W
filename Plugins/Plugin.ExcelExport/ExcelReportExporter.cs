using MiniExcelLibs;
using MiniExcelLibs.OpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Plugin.ExcelExport
{
    /// <summary>一次导出任务的输入参数（全部由插件按当前配置解析好后传入）。</summary>
    internal sealed class ExcelExportRequest
    {
        /// <summary>源 CSV 账本完整路径</summary>
        public string SourceCsvPath;
        /// <summary>输出 xlsx 完整路径</summary>
        public string OutputXlsxPath;
        /// <summary>要做成超链接的列名（通常是"图片路径"）；留空=纯表格导出</summary>
        public string LinkColumn;
        /// <summary>冻结首行</summary>
        public bool FreezeHeader;
        /// <summary>首行自动筛选</summary>
        public bool AutoFilter;
        /// <summary>只导出"图片列有有效路径"的行</summary>
        public bool OnlyRowsWithImage;
    }

    /// <summary>导出结果（成功与否 + 供界面/日志展示的统计）。</summary>
    internal sealed class ExcelExportResult
    {
        public bool Success;
        public string OutputPath;
        public int Rows;
        public int Links;
        public string Message;
    }

    /// <summary>
    /// CSV 账本 → 带图片超链接的 xlsx 报表。
    ///
    /// 一句话原理：MiniExcel 负责"写数据 + 冻结首行 + 自动筛选"，
    /// 它写完之后我们再开 zip 把"图片路径"列改造成可点击的超链接（MiniExcel 自己不支持超链接）。
    /// 报表里只放短文件名，点开才是磁盘上的高清原图——文件小、能筛选、能排序、还能看图。
    /// </summary>
    internal static class ExcelReportExporter
    {
        /// <summary>
        /// 占位符展开：{StepName} 本步骤名、{源步骤} 源账本步骤名，
        /// 以及 {yyyy-MM-dd HH-mm-ss} {yyyy-MM-dd} {yyyy-MM} {yyyy} {MM} {dd} 日期族。
        /// （与 CSV 记录插件的占位符规则保持一致，用户学一次就够）
        /// </summary>
        public static string ExpandTemplate(string template, DateTime time, string stepName, string sourceStepName)
        {
            if (string.IsNullOrEmpty(template)) return "";
            string s = template.Replace("{源步骤}", sourceStepName ?? "");
            s = s.Replace("{StepName}", stepName ?? "");
            // 长的日期 token 必须先替换，否则 {yyyy-MM-dd} 会被 {yyyy} 提前截胡
            s = s.Replace("{yyyy-MM-dd HH-mm-ss}", time.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
            s = s.Replace("{yyyy-MM-dd}", time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            s = s.Replace("{yyyy-MM}", time.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            s = s.Replace("{yyyy}", time.ToString("yyyy", CultureInfo.InvariantCulture));
            s = s.Replace("{MM}", time.ToString("MM", CultureInfo.InvariantCulture));
            s = s.Replace("{dd}", time.ToString("dd", CultureInfo.InvariantCulture));
            return s;
        }

        /// <summary>执行一次导出。不抛异常——所有失败都收敛成带说明的 ExcelExportResult。</summary>
        public static ExcelExportResult Export(ExcelExportRequest req)
        {
            var result = new ExcelExportResult();
            try
            {
                if (req == null) return Fail(result, "导出参数为空");
                if (string.IsNullOrWhiteSpace(req.SourceCsvPath)) return Fail(result, "源账本路径为空");
                if (string.IsNullOrWhiteSpace(req.OutputXlsxPath)) return Fail(result, "输出报表路径为空");
                if (!File.Exists(req.SourceCsvPath))
                    return Fail(result, $"源账本不存在：{req.SourceCsvPath}（请检查『源账本路径』模板与『源步骤名』）");

                // —— 1) 读账本：MiniExcel 读 CSV 必须显式给 excelType，否则它按 xlsx 解析直接抛 ——
                var rows = new List<IDictionary<string, object>>();
                var query = MiniExcel.Query(req.SourceCsvPath, useHeaderRow: true, excelType: ExcelType.CSV);
                foreach (var item in query)
                {
                    if (item is IDictionary<string, object> dict) rows.Add(dict);
                }

                if (rows.Count == 0)
                    return Fail(result, $"源账本没有数据行（只有表头？）：{req.SourceCsvPath}");

                // —— 2) 可选：只保留"图片列有有效路径"的行 ——
                bool hasLinkColumn = !string.IsNullOrWhiteSpace(req.LinkColumn)
                                     && rows[0].ContainsKey(req.LinkColumn);
                if (req.OnlyRowsWithImage && hasLinkColumn)
                {
                    rows = rows.Where(r => r.TryGetValue(req.LinkColumn, out var v)
                                           && TryAbsolutePath(v, out _)).ToList();
                    if (rows.Count == 0)
                        return Fail(result, $"『{req.LinkColumn}』列没有任何有效图片路径，没有可导出的行");
                }

                // —— 3) 写 xlsx（列序 = 字典键序 = CSV 表头序）——
                string outDir = Path.GetDirectoryName(req.OutputXlsxPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                MiniExcel.SaveAs(req.OutputXlsxPath, rows, overwriteFile: true,
                    configuration: new OpenXmlConfiguration
                    {
                        FreezeRowCount = req.FreezeHeader ? 1 : 0,
                        AutoFilter = req.AutoFilter
                    });

                // —— 4) 注入超链接（缺列/无路径时静默跳过，报表仍是可用纯表格）——
                int links = 0;
                if (hasLinkColumn)
                    links = XlsxHyperlinkInjector.Inject(req.OutputXlsxPath, req.LinkColumn);

                result.Success = true;
                result.OutputPath = req.OutputXlsxPath;
                result.Rows = rows.Count;
                result.Links = links;
                result.Message = links > 0
                    ? $"已导出 {rows.Count} 行，其中 {links} 行图片路径已变成可点击链接"
                    : (hasLinkColumn
                        ? $"已导出 {rows.Count} 行（『{req.LinkColumn}』列没有可用的绝对图片路径，未生成链接）"
                        : $"已导出 {rows.Count} 行（未找到列『{req.LinkColumn}』，导出为纯表格）");
                return result;
            }
            catch (IOException ex)
            {
                return Fail(result, $"写入报表失败：{ex.Message}（报表可能正被 Excel 打开，请先关闭）");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Fail(result, $"没有写入权限：{ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail(result, $"导出异常：{ex.GetType().Name} {ex.Message}");
            }
        }

        private static ExcelExportResult Fail(ExcelExportResult r, string msg)
        {
            r.Success = false;
            r.Message = msg;
            return r;
        }

        /// <summary>取"单元格值 → 绝对路径"。相对路径不算数（无法作为外部链接目标）。</summary>
        internal static bool TryAbsolutePath(object cellValue, out string path)
        {
            path = null;
            string s = cellValue as string ?? cellValue?.ToString();
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            if (!Path.IsPathRooted(s)) return false;
            path = s;
            return true;
        }
    }
}
