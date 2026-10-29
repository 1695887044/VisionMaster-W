using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Services
{
    /// <summary>
    /// 汇川 EasyMonitor 导出的设备变量表 CSV → 导入侧的行模型。
    ///
    /// 为什么需要它：汇川导出的列（<c>名称 / 长度 / 数组计数 / 地址 / 采集周期 / 注释 / 变量组Id</c>）
    /// 和本仓的列（<c>变量名 / 数据类型 / 存储区 / 偏移量 / 元素个数 / …</c>）不是同一个 schema，
    /// 而且同一家软件不同版本的导出**列数都不一样**。这里只做"翻译"，所有校验仍然交给
    /// <see cref="DeviceVariableImportService.BuildPlan"/>——绝不在适配层另写一套规则，
    /// 否则会出现"读得进来、校验说不行"的两套口径。
    ///
    /// 三条按真实文件踩出来的口径：
    /// ① **列位置按表头取**：老工程 16 列（A..P，分组 Id 在倒数第 4 列），新工程 13 列（A..M，
    ///    分组 Id 是最后一列）。按下标硬编码，换个工程就静默错位（曾把「下限」当成变量组Id）。
    /// ② **「数组计数」标量有两种写法**：老导出留空、新导出写 <c>1</c>。只有 <c>&gt;1</c>
    ///    或 <c>[lo,hi]</c> 才算数组，否则会把每个布尔标量都判成"布尔数组"。
    /// ③ **编码是无 BOM 的 GB18030**：按 UTF-8 硬解会把整表中文读成 U+FFFD。
    /// </summary>
    /// <summary>把汇川 EasyMonitor 导出的 CSV 翻译成导入侧的行模型（公开是为了让断言宿主能直接钉住解析口径）。</summary>
    public static class EasyNetCsvReader
    {        /// <summary>显示分组前缀。与既有方案里导入出来的组名保持一致（汇川组3/4/5/…），不能改名。</summary>
        private const string DisplayGroupPrefix = "汇川组";

        public static bool IsCsv(string path)
            => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);

        /// <summary>汇川数据类型 → 本仓类型。认不出的一律回落原文，由 BuildPlan 报"无法识别"。</summary>
        private static readonly Dictionary<string, (Type Scalar, Type Array)> TypeMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Bool"] = (typeof(bool), typeof(bool[])),
                ["Int16"] = (typeof(short), typeof(short[])),
                ["Int32"] = (typeof(int), typeof(int[])),
                ["Float"] = (typeof(float), typeof(float[])),
                ["Double"] = (typeof(double), typeof(double[])),
                ["String"] = (typeof(string), typeof(string[])),
            };

        /// <summary>表头解析出来的列下标；没有表头时回落老工程的 16 列位置。</summary>
        private sealed class ColumnMap
        {
            public int Type = 2;
            public int Length = 3;
            public int Array = 4;
            public int Address = 5;
            public int Period = 6;
            public int Comment = 11;
            public int Group = -1;   // -1 = 表头里没有「变量组Id」列 → 按老口径取倒数第 4 列
        }

        public static List<DeviceVariableRawRow> ReadRows(string path, int? connectionCycleMs = null)
        {
            var lines = Decode(File.ReadAllBytes(path))
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var result = new List<DeviceVariableRawRow>();
            var map = new ColumnMap();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;   // 汇川在文件头写了 13 行 #列A:…

                var fields = SplitCsvLine(line);
                var name = Cell(fields, 0);
                if (name.Length == 0) continue;

                // 表头按第一列「名称」认。文件中间可能还嵌了一份重复表头，同样走这里。
                if (name == "名称")
                {
                    map = ResolveColumns(fields);
                    continue;
                }

                result.Add(new DeviceVariableRawRow
                {
                    RowNumber = i + 1,   // 文件里的物理行号，预览里的「第 N 行」直接对得上
                    Cells = BuildCells(fields, map, connectionCycleMs),
                });
            }

            return result;
        }

        private static Dictionary<string, string> BuildCells(List<string> fields, ColumnMap map, int? connectionCycleMs)
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            void Put(string column, string value)
                => cells[DeviceVariableIoText.NormalizeHeader(column)] = value;

            var name = Cell(fields, 0);
            var typeText = Cell(fields, map.Type);
            var lengthText = Cell(fields, map.Length);
            var arrayText = Cell(fields, map.Array);
            var addressText = Cell(fields, map.Address);
            var period = Cell(fields, map.Period);
            var comment = Cell(fields, map.Comment);
            var groupText = map.Group >= 0
                ? Cell(fields, map.Group)
                : (fields.Count >= 4 ? fields[^4].Trim() : string.Empty);

            Put(DeviceVariableColumns.Name, name);
            Put(DeviceVariableColumns.Description, comment);
            Put(DeviceVariableColumns.ScanGroup, MapScanGroup(period, connectionCycleMs));

            // 地址：汇川把存储区和偏移写在一列（"UB 2101060"），本仓要拆成两列。
            var address = Regex.Match(addressText, @"^([A-Za-z]+)\s*([0-9A-Fa-f]+)$");
            if (address.Success)
            {
                Put(DeviceVariableColumns.Area, MapArea(address.Groups[1].Value));
                Put(DeviceVariableColumns.Offset, address.Groups[2].Value);
            }
            else
            {
                // 拆不开就把原文塞进「偏移量」：BuildPlan 会报"存储区无法识别"，
                // 比在这里静默丢一行好（错误行仍然带着变量名，用户知道是哪一条）。
                Put(DeviceVariableColumns.Area, string.Empty);
                Put(DeviceVariableColumns.Offset, addressText);
            }

            // 元素个数：数组计数 >1 或 [lo,hi]；字符串标量取「长度」列（总字节数，与导入侧同语义）
            var (isArray, elementCount) = ParseArrayCount(arrayText);
            if (typeText.Equals("String", StringComparison.OrdinalIgnoreCase) && !isArray)
                elementCount = ParsePositiveInt(lengthText) ?? 20;
            Put(DeviceVariableColumns.ElementCount, elementCount.ToString(CultureInfo.InvariantCulture));

            Put(DeviceVariableColumns.DataType, MapType(typeText, isArray));
            Put(DeviceVariableColumns.DisplayGroup,
                groupText.Length == 0 ? string.Empty : DisplayGroupPrefix + groupText);

            return cells;
        }

        /// <summary>类型翻译：认得出就用本仓的显示名，认不出回落原文（让 BuildPlan 报清楚是哪个类型）。</summary>
        private static string MapType(string typeText, bool isArray)
        {
            if (!TypeMap.TryGetValue(typeText, out var types))
                return typeText;

            return GlobalVariableManagerViewModel.TypeDisplayName(isArray ? types.Array : types.Scalar);
        }

        private static (bool IsArray, int Count) ParseArrayCount(string text)
        {
            if (text.Length == 0) return (false, 1);

            var range = Regex.Match(text, @"^\[\s*(\d+)\s*,\s*(\d+)\s*\]$");
            if (range.Success)
            {
                int lo = int.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
                int hi = int.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
                return (true, Math.Max(1, hi - lo + 1));
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain))
                return (plain > 1, Math.Max(1, plain));

            return (false, 1);
        }

        private static int? ParsePositiveInt(string text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
                ? value
                : null;

        /// <summary>
        /// 采集周期 → 扫描组。
        ///
        /// 扫描组是**连接级**的具名节拍资源，汇川的采集周期是**逐变量**的周期，两者不是一回事。
        /// 只有当周期与连接读周期**不一致**时才把它写进「扫描组」列：那样 BuildPlan 会把它列成
        /// "未知扫描组 → 回落默认组"，用户才知道 CSV 里那个周期没有被采纳。
        /// 一致时留空（= 默认组，周期本来就取连接 ReadCycleMs）——否则每导一次都会弹一句
        /// 「扫描组『100ms』不存在」，正确但永远没用。
        /// </summary>
        private static string MapScanGroup(string period, int? connectionCycleMs)
        {
            if (period.Length == 0 || connectionCycleMs is not > 0) return period;

            int? ms = ParsePeriodMs(period);
            return ms.HasValue && ms.Value == connectionCycleMs.Value ? string.Empty : period;
        }

        private static int? ParsePeriodMs(string text)
        {
            var match = Regex.Match(text.Trim(), @"^(\d+)\s*(ms|s)?$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            int value = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return match.Groups[2].Value.Equals("s", StringComparison.OrdinalIgnoreCase) ? value * 1000 : value;
        }

        /// <summary>地址前缀 → 本仓存储区（枚举成员名）。认不出回落原文。</summary>
        private static string MapArea(string prefix) => prefix.ToUpperInvariant() switch
        {
            "U" or "UB" or "UW" => "U",
            "D" or "M" or "S" or "B" or "X" or "Y" or "R" or "W" => prefix.ToUpperInvariant(),
            _ => prefix.ToUpperInvariant(),
        };

        /// <summary>按表头文字定位列。找不到的列保持 <see cref="ColumnMap"/> 的默认值。</summary>
        private static ColumnMap ResolveColumns(List<string> header)
        {
            var map = new ColumnMap();
            for (int i = 0; i < header.Count; i++)
            {
                switch (header[i].Trim())
                {
                    case "数据类型": map.Type = i; break;
                    case "长度": map.Length = i; break;
                    case "数组计数": map.Array = i; break;
                    case "地址": map.Address = i; break;
                    case "采集周期": map.Period = i; break;
                    case "注释": map.Comment = i; break;
                    case "变量组Id": map.Group = i; break;
                }
            }
            return map;
        }

        private static string Cell(List<string> fields, int index)
            => index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;

        /// <summary>
        /// 无 BOM 的 GB18030 是汇川的默认导出编码：先按严格 UTF-8 试解（纯 ASCII / 真 UTF-8 走这条），
        /// 失败再回落 GB18030。
        /// </summary>
        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Gb18030.GetString(bytes);
            }
        }

        private static Encoding? _gb18030;

        /// <summary>GB18030 在 .NET 里不是内建编码，需要注册 CodePages provider（运行时自带该程序集）。</summary>
        private static Encoding Gb18030
        {
            get
            {
                if (_gb18030 != null) return _gb18030;
                try { return _gb18030 = Encoding.GetEncoding("GB18030"); }
                catch (ArgumentException) { /* 未注册 provider：注册后再取 */ }

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return _gb18030 = Encoding.GetEncoding("GB18030");
            }
        }

        /// <summary>带引号感知的 CSV 行拆分（数组计数列 "[0,19]" 自带逗号，注释里也可能有）。</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
