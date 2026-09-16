using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Plugin.ExcelExport
{
    /// <summary>
    /// 往 MiniExcel 生成的 xlsx 里注入 OOXML 外部超链接（MiniExcel 1.46.0 自身完全不支持超链接）。
    ///
    /// 为什么是"改写 zip"而不是"换个库"：MiniExcel 写数据/冻结首行/自动筛选都很利落，
    /// 唯独链接能力缺失；而 xlsx 本质就是一个 zip + 若干 XML，缺的那点东西自己补最省事
    /// ——不必为了超链接引入一整个 OpenXML SDK。
    ///
    /// 需要改三处（缺一处 Excel 就报"需要修复"）：
    /// 1) xl/worksheets/sheet1.xml —— 目标列单元格挂链接样式、值改成短显示名，末尾追加 &lt;hyperlinks&gt; 段；
    /// 2) xl/worksheets/_rels/sheet1.xml.rels —— 追加 External 关系（Id → file:/// 目标）；
    /// 3) xl/styles.xml —— 追加"蓝色下划线"字体 + 对应的 cellXfs 样式项。
    /// </summary>
    internal static class XlsxHyperlinkInjector
    {
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PKG = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>超链接字体色（Excel 默认的"超链接蓝"）。</summary>
        private const string LinkColorRgb = "FF0563C1";

        /// <summary>
        /// 把 <paramref name="columnName"/> 列里的"绝对路径"文本变成可点击的外部超链接，
        /// 单元格显示名缩短为文件名（完整路径仍藏在链接目标里）。
        /// </summary>
        /// <param name="xlsxPath">MiniExcel 已写好的 xlsx</param>
        /// <param name="columnName">表头列名（如"图片路径"）</param>
        /// <returns>成功注入的链接数；0 表示没找到该列或没有可用路径</returns>
        public static int Inject(string xlsxPath, string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName)) return 0;

            var doc = ReadZipEntries(xlsxPath);
            string sheetName = doc.Keys.FirstOrDefault(k => k.StartsWith("xl/worksheets/sheet") && k.EndsWith(".xml"));
            if (sheetName == null) return 0;

            XDocument sheet = XDocument.Parse(doc[sheetName]);
            XElement data = sheet.Root.Element(S + "sheetData");
            if (data == null) return 0;

            // —— 1) 表头定位列字母 + 探出该列数据格用的样式索引（后面克隆它，保证链接格与普通格观感一致）——
            XElement head = data.Elements().FirstOrDefault();
            if (head == null) return 0;
            string colLetter = null;
            foreach (XElement c in head.Elements(S + "c"))
            {
                if ((string)c.Element(S + "v") == columnName)
                {
                    colLetter = CellRef((string)c.Attribute("r"));
                    break;
                }
            }
            if (colLetter == null) return 0;

            int normalStyle = 2; // MiniExcel 默认表头 s="1"、数据格 s="2"
            foreach (XElement c in data.Elements().Skip(1).Elements(S + "c"))
            {
                if (CellRef((string)c.Attribute("r")) == colLetter && int.TryParse((string)c.Attribute("s"), out int s))
                {
                    normalStyle = s;
                    break;
                }
            }

            // —— 2) 样式：加"蓝色下划线"字体 + 克隆数据格样式的新 cellXfs 项 ——
            int linkStyle = normalStyle;
            if (doc.TryGetValue("xl/styles.xml", out string stylesXml))
            {
                XDocument st = XDocument.Parse(stylesXml);
                XElement fonts = st.Root.Element(S + "fonts");
                XElement xfs = st.Root.Element(S + "cellXfs");
                if (fonts != null && xfs != null)
                {
                    // CT_Font 子元素顺序是 schema 固定的：u 必须排在 sz/color/name 之前，否则 Excel 报错
                    var linkFont = new XElement(S + "font",
                        new XElement(S + "u", new XAttribute("val", "single")),
                        new XElement(S + "vertAlign", new XAttribute("val", "baseline")),
                        new XElement(S + "sz", new XAttribute("val", "11")),
                        new XElement(S + "color", new XAttribute("rgb", LinkColorRgb)),
                        new XElement(S + "name", new XAttribute("val", "Calibri")),
                        new XElement(S + "family", new XAttribute("val", "2")));
                    fonts.Add(linkFont);
                    fonts.SetAttributeValue("count", fonts.Elements(S + "font").Count());
                    int fontId = fonts.Elements(S + "font").Count() - 1;

                    // 克隆"数据格样式"而非末位样式：MiniExcel 默认 cellXfs 末位是时间格式（numFmtId=21）
                    XElement template = xfs.Elements(S + "xf").ElementAtOrDefault(normalStyle);
                    linkStyle = AppendXf(xfs, template, fontId);
                    doc["xl/styles.xml"] = st.ToString(SaveOptions.DisableFormatting);
                }
            }

            // —— 3) 逐行改单元格 + 收集关系 ——
            var rels = new List<(string Id, string Target)>();
            int rowIndex = 1;
            foreach (XElement row in data.Elements().Skip(1))
            {
                rowIndex++;
                XElement cell = row.Elements(S + "c").FirstOrDefault(c => CellRef((string)c.Attribute("r")) == colLetter);
                if (cell == null) continue;
                XElement v = cell.Element(S + "v");
                string target = v?.Value;
                if (string.IsNullOrWhiteSpace(target)) continue;          // 空路径行：保持原样（不挂样式不建关系）
                if (!TryExternalUri(target, out string uri)) continue;    // 相对路径/非法路径：跳过

                string id = "rIdHL" + rowIndex;
                cell.SetAttributeValue("s", linkStyle);
                v.Value = Path.GetFileName(target);                      // 显示名：只留文件名（带扩展名，一眼看出是 png 还是 jpg），报表才紧凑
                rels.Add((id, uri));

                var hl = new XElement(S + "hyperlink",
                    new XAttribute("ref", colLetter + rowIndex),
                    new XAttribute(R + "id", id));                        // R+"id" 是"属性名" → 输出 r:id
                HyperlinksContainer(sheet.Root).Add(hl);
            }
            if (rels.Count == 0) return 0;

            // ★ 必须把改过的 sheet 写回条目表：漏这步会退化成"关系有了、单元格没变"
            doc[sheetName] = sheet.ToString(SaveOptions.DisableFormatting);

            // —— 4) sheet 的关系文件追加 External 关系 ——
            // Type 必须是纯 URI 字符串：写成 XName 会被序列化成 Clark 记法 {http://...}hyperlink，Excel 直接报"需要修复"
            string relPath = "xl/worksheets/_rels/" + Path.GetFileName(sheetName) + ".rels";
            XDocument rd = doc.TryGetValue(relPath, out string relXml) && !string.IsNullOrWhiteSpace(relXml)
                ? XDocument.Parse(relXml)
                : new XDocument(new XElement(PKG + "Relationships"));
            foreach ((string id, string target) in rels)
            {
                rd.Root.Add(new XElement(PKG + "Relationship",
                    new XAttribute("Id", id),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                    new XAttribute("Target", target),
                    new XAttribute("TargetMode", "External")));
            }
            doc[relPath] = rd.Root.ToString(SaveOptions.DisableFormatting);

            WriteZipEntries(xlsxPath, doc);
            return rels.Count;
        }

        /// <summary>克隆一份等价样式、换字体、追加到 cellXfs，返回新样式索引。</summary>
        private static int AppendXf(XElement xfs, XElement template, int fontId)
        {
            var xf = template == null
                ? new XElement(S + "xf", new XAttribute("fontId", fontId))
                : new XElement(template);
            xf.SetAttributeValue("fontId", fontId);
            xf.SetAttributeValue("applyFont", "1");
            xfs.Add(xf);
            xfs.SetAttributeValue("count", xfs.Elements(S + "xf").Count());
            return xfs.Elements(S + "xf").Count() - 1;
        }

        /// <summary>取（或按 schema 顺序新建）&lt;hyperlinks&gt; 容器。</summary>
        private static XElement HyperlinksContainer(XElement root)
        {
            XElement c = root.Element(S + "hyperlinks");
            if (c != null) return c;

            c = new XElement(S + "hyperlinks");
            // sheet 子元素顺序固定：hyperlinks 在 autoFilter/mergeCells 之后，在 printOptions/pageMargins/drawing 之前
            XElement after = root.Elements().FirstOrDefault(e =>
            {
                string n = e.Name.LocalName;
                return n == "printOptions" || n == "pageMargins" || n == "pageSetup"
                    || n == "headerFooter" || n == "rowBreaks" || n == "colBreaks" || n == "drawing";
            });
            if (after != null) after.AddBeforeSelf(c);
            else root.Add(c);
            return c;
        }

        /// <summary>绝对路径 → file:/// URI（空格、中文、逗号都会被正确转义）。</summary>
        private static bool TryExternalUri(string path, out string uri)
        {
            uri = null;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            try
            {
                uri = new Uri(path, UriKind.Absolute).AbsoluteUri;
                return true;
            }
            catch (UriFormatException)
            {
                return false; // 相对路径（如 img\1.png）无法作为 External 目标
            }
        }

        /// <summary>取单元格引用的列字母部分：E2 → E。</summary>
        private static string CellRef(string r) => new string((r ?? "").Where(char.IsLetter).ToArray());

        /// <summary>把整个 zip 读进内存（xlsx 报表通常几 MB，改写场景下内存换简单最划算）。</summary>
        private static Dictionary<string, string> ReadZipEntries(string path)
        {
            var map = new Dictionary<string, string>();
            using var z = ZipFile.Open(path, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry e in z.Entries)
            {
                using var r = new StreamReader(e.Open(), Encoding.UTF8);
                map[e.FullName] = r.ReadToEnd();
            }
            return map;
        }

        /// <summary>先写临时包再替换：任何一步失败都不会留下半个损坏的报表。</summary>
        private static void WriteZipEntries(string path, Dictionary<string, string> entries)
        {
            string tmp = path + ".tmp";
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                using (var z = ZipFile.Open(tmp, ZipArchiveMode.Create))
                {
                    foreach (var kv in entries)
                    {
                        ZipArchiveEntry e = z.CreateEntry(kv.Key, CompressionLevel.Optimal);
                        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                        w.Write(kv.Value);
                    }
                }
                File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }
    }
}
