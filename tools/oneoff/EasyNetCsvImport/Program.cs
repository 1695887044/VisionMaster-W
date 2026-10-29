using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MiniExcelLibs;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;

namespace EasyNetCsvImport;

/// <summary>
/// 一次性工具（R166）：汇川导出的设备变量表 CSV → 本仓 xlsx（变量管理「按连接导入」用）。
///
/// 生成后立即用导入侧真实现（DeviceVariableImportService.ReadRows + BuildPlan）全量校验，
/// 0 错误才退出 0 —— 校验绝不自己另写一套规则，否则会出现"转换器说行、导入说不行"。
///
/// 用法（在仓库根目录）：
///   dotnet run --project tools\oneoff\EasyNetCsvImport -- [csv] [xlsx] [扫描组]
///   csv     默认 C:\Users\16958\Desktop\12.csv
///   xlsx    默认 artifacts\render\&lt;今天&gt;\Plc_变量表.xlsx
///   扫描组  默认空 = 全部落默认组；填了则要求连接里已建同名组（未知组导入时回落默认组）
///
/// 映射口径（与用户确认过的方案一致）：
///   Bool→布尔(bool)  Int16→短整数(short)  Int32→整数(int)  Float→单精度(float)  String→文本(string)
///   地址前缀 UB/UW/U→U（十六进制位地址原样透传）；D/M/S/B/X/Y/R/W→同名
///   E 列 [lo,hi] → 数组（元素个数 = hi-lo+1，类型同步变数组）；字符串标量 → 元素个数 = 长度列（总字节数）
///   描述 ← 注释列；显示分组 ← 变量组Id（汇川组&lt;Id&gt;，导入时自动建组）；重名 → 加 _2/_3 后缀保点不丢
///
/// 列位置**按表头取，不按下标猜**：汇川不同版本导出的列数不一样——
/// 老工程 16 列（A..P，分组 Id 在倒数第 4 列），新工程 13 列（A..M，分组 Id 就是最后一列）。
/// 早期实现按 `f[^4]` 取分组，喂 13 列文件时取到的是「下限」列，
/// 结果是描述与显示分组**静默丢失**（转换器不报错、导入侧也全绿）。
/// </summary>
internal static class Program
{
    /// <summary>表头解析出来的列下标；没有表头时回落老工程的 16 列位置（分组 Id 取倒数第 4 列）。</summary>
    private sealed class ColumnMap
    {
        public int Type = 2;
        public int Length = 3;
        public int Array = 4;
        public int Address = 5;
        public int Comment = 11;
        public int Group = -1;   // -1 = 表头里没有「变量组Id」列 → 按老口径取倒数第 4 列
    }

    private static readonly Dictionary<string, (string Scalar, string Array)> TypeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Bool"] = ("布尔 (bool)", "布尔数组 (bool[])"),
            ["Int16"] = ("短整数 (short)", "短整数数组 (short[])"),
            ["Int32"] = ("整数 (int)", "整数数组 (int[])"),
            ["Float"] = ("单精度 (float)", "单精度数组 (float[])"),
            ["Double"] = ("小数 (double)", "小数数组 (double[])"),
            ["String"] = ("文本 (string)", "文本数组 (string[])"),
        };

    private static readonly List<string> Errors = new();
    private static readonly List<string> Skipped = new();
    private static readonly List<string> Renames = new();

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string csv = args.Length > 0 ? args[0] : @"C:\Users\16958\Desktop\12.csv";
        string xlsx = args.Length > 1 ? args[1]
            : Path.GetFullPath(Path.Combine("artifacts", "render",
                DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), "Plc_变量表.xlsx"));
        string scanGroup = args.Length > 2 ? args[2].Trim() : string.Empty;
        const string connectionName = "Plc";

        if (!File.Exists(csv))
        {
            Console.WriteLine($"[FAIL] CSV 不存在：{csv}");
            return 1;
        }

        var rows = ConvertCsv(csv, scanGroup);

        Console.WriteLine($"CSV 数据行 {rows.Count + Skipped.Count}：转换 {rows.Count}，跳过 {Skipped.Count}，重名改名 {Renames.Count}");
        foreach (var line in Renames) Console.WriteLine("  [改名] " + line);
        foreach (var line in Skipped) Console.WriteLine("  [跳过] " + line);
        foreach (var line in Errors.Take(20)) Console.WriteLine("  [错误] " + line);
        if (Errors.Count > 0)
        {
            Console.WriteLine($"[FAIL] 转换期错误 {Errors.Count} 条，未生成文件");
            return 1;
        }
        if (rows.Count == 0)
        {
            Console.WriteLine("[FAIL] 没有可转换的行");
            return 1;
        }

        WriteXlsx(xlsx, rows);
        Console.WriteLine($"[OK] 已生成 {xlsx}");

        return Validate(xlsx, connectionName, rows.Count);
    }

    // ------------------------------------------------------------------
    //  CSV → 行字典
    // ------------------------------------------------------------------

    private static List<Dictionary<string, object?>> ConvertCsv(string csv, string scanGroup)
    {
        var result = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = ReadCsvLines(csv);
        var columns = new ColumnMap();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var f = SplitCsvLine(line);
            string name = f.Count > 0 ? f[0].Trim() : string.Empty;
            if (name.Length == 0) continue;

            // 表头：按第一列「名称」认，就地换成真实列位（文件中间嵌的那份重复表头也走这里，
            // 不会再被当成"类型未知的行"混进 [跳过] 列表）
            if (name == "名称")
            {
                columns = ResolveColumns(f);
                continue;
            }

            string typeText = Cell(f, columns.Type);
            if (!TypeMap.TryGetValue(typeText, out var types))
            {
                Skipped.Add($"{name}: 类型「{typeText}」未知，整行跳过");
                continue;
            }

            string lengthText = Cell(f, columns.Length);
            string arrayText = Cell(f, columns.Array);
            string addressText = Cell(f, columns.Address);

            // 注释只取「注释」这一列；分组 Id 按表头给的下标（无表头才回落老口径"倒数第 4 列"）
            string groupText = columns.Group >= 0
                ? Cell(f, columns.Group)
                : (f.Count >= 4 ? f[^4].Trim() : string.Empty);
            string comment = Cell(f, columns.Comment);

            var match = Regex.Match(addressText, @"^([A-Za-z]+)\s*([0-9A-Fa-f]+)$");
            if (!match.Success)
            {
                Errors.Add($"{name}: 地址「{addressText}」无法解析（应为 前缀+数字）");
                continue;
            }
            var area = MapArea(match.Groups[1].Value);
            if (area == null)
            {
                Errors.Add($"{name}: 地址前缀「{match.Groups[1].Value}」不是本仓支持的存储区");
                continue;
            }
            string offset = match.Groups[2].Value;

            // 数组计数有两种写法：老导出对标量**留空**、数组写 "[lo,hi]"；
            // 新导出（13 列）对每个标量都写 "1"。所以只有 >1 或 [lo,hi] 才算数组——
            // 早期按"非空即数组"判，喂新导出会把 304 个布尔标量全判成"布尔数组"直接报错。
            bool isArray = false;
            int elementCount = 1;
            if (arrayText.Length > 0)
            {
                var m = Regex.Match(arrayText, @"^\[\s*(\d+)\s*,\s*(\d+)\s*\]$");
                if (m.Success)
                {
                    elementCount = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                                   - int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + 1;
                    isArray = true;
                }
                else if (int.TryParse(arrayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain))
                {
                    if (plain < 1)
                    {
                        Errors.Add($"{name}: 数组计数 {plain} 无效");
                        continue;
                    }
                    elementCount = plain;
                    isArray = plain > 1;
                }
                else
                {
                    Errors.Add($"{name}: 数组计数「{arrayText}」无法解析");
                    continue;
                }

                if (isArray && elementCount < 1)
                {
                    Errors.Add($"{name}: 数组元素个数 {elementCount} 无效");
                    continue;
                }
                if (isArray && typeText.Equals("Bool", StringComparison.OrdinalIgnoreCase))
                {
                    Errors.Add($"{name}: 布尔数组仅 Modbus 线圈/离散输入区支持，EasyNet 不支持");
                    continue;
                }
            }

            // 字符串标量：本列长度 = 总字节数（与导入侧 string 标量的元素个数同语义）
            if (typeText.Equals("String", StringComparison.OrdinalIgnoreCase))
            {
                if (isArray)
                {
                    Errors.Add($"{name}: 字符串数组的每元素字节数在汇川表里无独立列，语义不明，请人工确认后手工建");
                    continue;
                }
                elementCount = int.TryParse(lengthText, out int bytes) && bytes > 0 ? bytes : 20;
            }

            string dataType = isArray ? types.Array : types.Scalar;

            string finalName = name;
            int suffix = 2;
            while (!seen.Add(finalName))
                finalName = $"{name}_{suffix++}";
            if (!string.Equals(finalName, name, StringComparison.Ordinal))
                Renames.Add($"{name} → {finalName}（{area}{offset}）");

            result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [DeviceVariableColumns.Name] = finalName,
                [DeviceVariableColumns.Description] = comment,
                [DeviceVariableColumns.DataType] = dataType,
                [DeviceVariableColumns.Area] = area,
                [DeviceVariableColumns.Offset] = offset,
                [DeviceVariableColumns.BitOffset] = string.Empty,
                [DeviceVariableColumns.Station] = string.Empty,
                [DeviceVariableColumns.DbNumber] = string.Empty,
                [DeviceVariableColumns.ElementCount] = elementCount.ToString(CultureInfo.InvariantCulture),
                [DeviceVariableColumns.ElementByteLength] = string.Empty,
                [DeviceVariableColumns.AddressOffset] = string.Empty,
                [DeviceVariableColumns.LegacyAddress] = string.Empty,
                [DeviceVariableColumns.ScanGroup] = scanGroup,
                [DeviceVariableColumns.DisplayGroup] = groupText.Length == 0 ? string.Empty : "汇川组" + groupText,
                [DeviceVariableColumns.MergeAdjacentRead] = string.Empty,
                [DeviceVariableColumns.EnableConversion] = string.Empty,
                [DeviceVariableColumns.Scale] = string.Empty,
                [DeviceVariableColumns.EngineeringOffset] = string.Empty,
                [DeviceVariableColumns.Unit] = string.Empty,
                [DeviceVariableColumns.InitialValue] = string.Empty,
            });
        }

        return result;
    }

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
                case "注释": map.Comment = i; break;
                case "变量组Id": map.Group = i; break;
            }
        }
        return map;
    }

    private static string Cell(List<string> f, int index)
        => index >= 0 && index < f.Count ? f[index].Trim() : string.Empty;

    /// <summary>
    /// 读 CSV 行。汇川导出是**无 BOM 的 GB18030**：先按严格 UTF-8 试解（纯 ASCII / 真 UTF-8 走这条），
    /// 失败回落 GB18030。按 UTF-8 硬解 GBK 会把整表中文读成 U+FFFD/错字（实测踩过：
    /// xlsx 里描述列全是 FFFD，只有 exe 里写死的中文常量是好的）。
    /// </summary>
    private static string[] ReadCsvLines(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return SplitLines(new UTF8Encoding(false).GetString(bytes[3..]));
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return SplitLines(Encoding.Unicode.GetString(bytes));

        try
        {
            return SplitLines(new UTF8Encoding(false, true).GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return SplitLines(Gb18030.GetString(bytes));
        }
        catch (ArgumentException)
        {
            return SplitLines(Gb18030.GetString(bytes));
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
            catch { /* 未注册 provider 时走注册再取 */ }
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return _gb18030 = Encoding.GetEncoding("GB18030");
        }
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>地址前缀 → 本仓存储区（枚举成员名，TryResolveArea 认成员名也认显示名）。</summary>
    private static string? MapArea(string prefix) => prefix.ToUpperInvariant() switch
    {
        "U" or "UB" or "UW" => "U",
        "D" or "M" or "S" or "B" or "X" or "Y" or "R" or "W" => prefix.ToUpperInvariant(),
        _ => null,
    };

    /// <summary>带引号感知的 CSV 行拆分（汇川导出的数组计数列 "[0,19]" 自带逗号）。</summary>
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

    // ------------------------------------------------------------------
    //  写 xlsx + 导入侧真校验
    // ------------------------------------------------------------------

    private static void WriteXlsx(string path, List<Dictionary<string, object?>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 先写临时文件再覆盖移动：写一半失败不会弄丢用户已有的文件（与宿主导出同一口径）
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp.xlsx";
        try
        {
            var sheets = new Dictionary<string, object>(StringComparer.Ordinal) { ["变量表"] = rows };
            MiniExcel.SaveAs(temp, sheets);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* 已不存在 */ }
        }
    }

    private static int Validate(string xlsx, string connectionName, int expectedRows)
    {
        var connection = new CommunicationConfig(connectionName, new EasyNetConfig
        {
            // 手工构造 Config 必须显式赋 Type（默认 0 = ModbusTcp，工厂会取错实现）
            Type = CommunicationType.EasyNet,
            IpAddress = "127.0.0.1",
            Port = 12939,
        })
        {
            ReadCycleMs = 100
        };

        var rows = DeviceVariableImportService.ReadRows(xlsx);
        var plan = DeviceVariableImportService.BuildPlan(
            connection,
            rows,
            Array.Empty<NetworkVariableModel>(),
            Array.Empty<IVariable>(),
            Array.Empty<VariableGroupDescriptor>(),
            new[] { PollScheduler.DefaultGroupName },
            autoCreateDisplayGroups: true);

        Console.WriteLine($"导入侧校验：读回 {rows.Count} 行（期望 {expectedRows}），" +
                          $"新增 {plan.NewCount} / 更新 {plan.UpdateCount} / 错误 {plan.ErrorCount}");
        if (plan.NewDisplayGroups.Count > 0)
            Console.WriteLine("将自动创建显示分组：" + string.Join("、", plan.NewDisplayGroups));
        if (plan.UnknownScanGroups.Count > 0)
            Console.WriteLine("[警告] 未知扫描组（会回落默认组）：" + string.Join("、", plan.UnknownScanGroups));
        foreach (var e in plan.Items.Where(i => i.Status == DeviceVariableImportStatus.Error).Take(20))
            Console.WriteLine($"  [校验错误] 第{e.RowNumber}行 {e.Name}: {e.Note}");
        if (plan.GlobalError != null)
            Console.WriteLine("  [全局] " + plan.GlobalError);

        bool ok = plan.GlobalError == null
                  && plan.ErrorCount == 0
                  && rows.Count == expectedRows
                  && plan.NewCount == expectedRows;
        Console.WriteLine(ok ? "[OK] 导入侧校验全绿" : "[FAIL] 导入侧校验未通过");
        return ok ? 0 : 1;
    }
}
