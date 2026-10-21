using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MiniExcelLibs;
using Prism.Mvvm;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.Services
{
    /// <summary>设备变量表（xlsx）的列名。导入/导出/说明页共用这一份，避免三处各写一串中文。</summary>
    public static class DeviceVariableColumns
    {
        public const string Name = "变量名";
        public const string Description = "描述";
        public const string DataType = "数据类型";
        public const string Area = "存储区";
        public const string Offset = "偏移量";
        public const string BitOffset = "位偏移";
        public const string Station = "站号";
        public const string DbNumber = "DB号";
        public const string ElementCount = "元素个数";
        public const string ElementByteLength = "每元素字节数";
        public const string AddressOffset = "地址偏移";
        public const string LegacyAddress = "40001写法";
        public const string ScanGroup = "扫描组";
        public const string DisplayGroup = "显示分组";
        public const string MergeAdjacentRead = "区域合并读";
        public const string EnableConversion = "启用换算";
        public const string Scale = "缩放系数";
        public const string EngineeringOffset = "工程偏移";
        public const string Unit = "工程单位";
        public const string InitialValue = "初始值";
        public const string AddressPreview = "地址预览";

        /// <summary>导入时必须有值的列（其它列可缺省）。</summary>
        public static readonly string[] Required = { Name, DataType, Area, Offset };

        /// <summary>导入表头别名：第一项是规范名，其余是现场常见写法。</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Aliases =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [Name] = new[] { Name, "变量名称", "名称" },
                [Description] = new[] { Description, "变量描述", "说明" },
                [DataType] = new[] { DataType, "类型" },
                [Area] = new[] { Area, "区域", "存储区域" },
                [Offset] = new[] { Offset, "偏移" },
                [BitOffset] = new[] { BitOffset, "位" },
                [Station] = new[] { Station, "从站号" },
                [DbNumber] = new[] { DbNumber, "DB块号", "DB 号" },
                [ElementCount] = new[] { ElementCount, "长度", "元素数" },
                [ElementByteLength] = new[] { ElementByteLength, "每元素字节", "元素字节数" },
                [AddressOffset] = new[] { AddressOffset, "地址增量" },
                [LegacyAddress] = new[] { LegacyAddress, "40001 写法", "5位地址" },
                [ScanGroup] = new[] { ScanGroup, "扫描组名" },
                [DisplayGroup] = new[] { DisplayGroup, "分组", "显示分组名" },
                [MergeAdjacentRead] = new[] { MergeAdjacentRead, "合并读" },
                [EnableConversion] = new[] { EnableConversion, "启用工程量换算" },
                [Scale] = new[] { Scale, "比例", "缩放" },
                [EngineeringOffset] = new[] { EngineeringOffset, "工程偏移量" },
                [Unit] = new[] { Unit, "工程单位名" },
                [InitialValue] = new[] { InitialValue, "初始值（选填）" },
                [AddressPreview] = new[] { AddressPreview, "地址预览列" },
            };
    }

    /// <summary>设备变量表的文本格式与解析（导入/导出共用一份，避免两套规则漂移）。</summary>
    public static class DeviceVariableIoText
    {
        public static string NormalizeHeader(string? header)
            => (header ?? string.Empty)
                .Replace('\uFEFF', ' ')
                .Replace('\u3000', ' ')
                .Trim()
                .Replace(" ", string.Empty)
                .ToLowerInvariant();

        public static string CellText(object? value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case string text:
                    return text.Trim();
                case bool flag:
                    return flag ? "是" : "否";
                case double d:
                    return d == Math.Truncate(d) && !double.IsInfinity(d)
                        ? d.ToString("0", CultureInfo.InvariantCulture)
                        : d.ToString("0.############", CultureInfo.InvariantCulture);
                case float f:
                    return f == Math.Truncate(f) && !float.IsInfinity(f)
                        ? f.ToString("0", CultureInfo.InvariantCulture)
                        : f.ToString("0.######", CultureInfo.InvariantCulture);
                case decimal m:
                    return m.ToString(CultureInfo.InvariantCulture);
                case int:
                case long:
                case short:
                case byte:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                case DateTime dt:
                    return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            }
        }

        public static string FormatBool(bool value) => value ? "是" : "否";

        public static string FormatValue(object? value, Type? type)
        {
            if (value == null) return string.Empty;
            if (type == typeof(bool)) return value is bool b ? FormatBool(b) : CellText(value);
            if (type == typeof(double))
            {
                // 旧数据 / 手改 JSON 可能给出与声明类型不符的 DefaultValue：
                // 导出不能因此抛异常（否则整个预览在 UI 线程炸掉），安全回退成空。
                try
                {
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return double.IsFinite(number)
                        ? number.ToString("0.############", CultureInfo.InvariantCulture)
                        : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
            if (type == typeof(float))
            {
                try
                {
                    var number = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    return float.IsFinite(number)
                        ? number.ToString("0.######", CultureInfo.InvariantCulture)
                        : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
            if (type == typeof(string)) return value as string ?? CellText(value);
            if (value is HalconDotNet.HObject) return string.Empty;
            return CellText(value);
        }

        /// <summary>可选布尔：空白返回 null（调用方决定默认值）；无法识别返回 false 且 error 非空。</summary>
        public static bool? ParseOptionalBool(string? text, out string? error)
        {
            error = null;
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0) return null;
            if (value is "是" or "true" or "True" or "TRUE" or "1" or "y" or "Y" or "yes" or "YES" or "on" or "ON")
                return true;
            if (value is "否" or "false" or "False" or "FALSE" or "0" or "n" or "N" or "no" or "NO" or "off" or "OFF")
                return false;
            error = $"无法识别布尔值「{value}」（可用：是 / 否）";
            return false;
        }

        /// <summary>
        /// 解析整数。**空白返回 true 且 value=0**（调用方必须按列语义自己决定默认值：
        /// 元素个数/DB 号默认 1、位偏移默认 -1、缩放系数默认 1，都不是 0）。
        /// </summary>
        public static bool TryParseInt(string? text, out int value, out string? error)
        {
            value = 0;
            error = null;
            var raw = (text ?? string.Empty).Trim();
            if (raw.Length == 0) return true;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
            error = $"「{raw}」不是有效整数";
            return false;
        }

        /// <summary>
        /// 解析数值。**空白返回 true 且 value=0**（调用方必须自己决定默认值：
        /// 缩放系数默认 1、工程偏移默认 0）。
        /// </summary>
        public static bool TryParseDouble(string? text, out double value, out string? error)
        {
            value = 0;
            error = null;
            var raw = (text ?? string.Empty).Trim();
            if (raw.Length == 0) return true;
            // NumberStyles.Float 不含千分位；与 R34 的"拒绝 1,000"同口径。
            // NaN / Infinity 也会被 double.TryParse 接受，但它们不是可用的工程量。
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                if (double.IsFinite(value)) return true;
                error = $"「{raw}」不是有限数（不允许 NaN / Infinity）";
                return false;
            }
            error = $"「{raw}」不是有效数值（不要使用千分位分隔符）";
            return false;
        }
    }

    /// <summary>一行原始单元格（已按表头归一成字符串）。RowNumber 是 Excel 里的行号（含表头）。</summary>
    public sealed class DeviceVariableRawRow
    {
        public int RowNumber { get; init; }
        public IReadOnlyDictionary<string, string> Cells { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Has(string column)
        {
            foreach (var alias in DeviceVariableColumns.Aliases.TryGetValue(column, out var list) ? list : new[] { column })
            {
                if (Cells.ContainsKey(DeviceVariableIoText.NormalizeHeader(alias))) return true;
            }
            return false;
        }

        public string Get(string column)
        {
            foreach (var alias in DeviceVariableColumns.Aliases.TryGetValue(column, out var list) ? list : new[] { column })
            {
                if (Cells.TryGetValue(DeviceVariableIoText.NormalizeHeader(alias), out var value))
                    return value ?? string.Empty;
            }
            return string.Empty;
        }
    }

    /// <summary>解析并校验通过的一条变量定义（尚未落地）。</summary>
    public sealed class DeviceVariableDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Type DataType { get; set; } = typeof(int);
        public string TypeName { get; set; } = string.Empty;
        public Enum Area { get; set; } = null!;
        public string Offset { get; set; } = "0";
        public int BitOffset { get; set; } = -1;
        public int Station { get; set; }
        public int DbNumber { get; set; } = 1;
        public int ElementCount { get; set; } = 1;
        public int ElementByteLength { get; set; }
        public int AddressOffset { get; set; }
        public bool UseLegacyAddress { get; set; }
        public string ScanGroup { get; set; } = string.Empty;
        public string ScanGroupDisplay { get; set; } = PollScheduler.DefaultGroupName;
        public string DisplayGroupName { get; set; } = "未分组";
        public bool MergeAdjacentRead { get; set; } = true;
        public bool EnableConversion { get; set; }
        public double Scale { get; set; } = 1;
        public double EngineeringOffset { get; set; }
        public string Unit { get; set; } = string.Empty;
        public object? InitialValue { get; set; }
        public DeviceAddressBase Address { get; set; } = null!;
    }

    public enum DeviceVariableImportStatus
    {
        New,
        Update,
        Unchanged,
        Error
    }

    /// <summary>导入预览的一行。IsSkipped 是界面选项（错误行被跳过），其余字段在计划构建后不再变。</summary>
    public sealed class DeviceVariableImportItem : BindableBase
    {
        public int RowNumber { get; init; }
        public string Name { get; init; } = string.Empty;
        public DeviceVariableImportStatus Status { get; init; }
        public DeviceVariableDefinition? Definition { get; init; }
        public NetworkVariableModel? Existing { get; init; }
        public string Note { get; init; } = string.Empty;

        private bool _isSkipped;
        public bool IsSkipped
        {
            get => _isSkipped;
            set => SetProperty(ref _isSkipped, value);
        }

        public string StatusText => Status switch
        {
            DeviceVariableImportStatus.New => "新增",
            DeviceVariableImportStatus.Update => "更新",
            DeviceVariableImportStatus.Unchanged => "无变化",
            _ => "错误"
        };

        public string TypeText => Definition?.TypeName ?? string.Empty;
        public string AddressText => Definition?.Address?.Address ?? string.Empty;
        public string ScanGroupText => Definition?.ScanGroupDisplay ?? string.Empty;
        public string DisplayGroupText => Definition?.DisplayGroupName ?? string.Empty;
    }

    /// <summary>导入计划：纯数据，不修改工作区。预览、计数与事务应用都以它为准。</summary>
    public sealed class DeviceVariableImportPlan
    {
        public List<DeviceVariableImportItem> Items { get; } = new();
        public List<string> NewDisplayGroups { get; } = new();
        public List<string> UnknownDisplayGroups { get; } = new();
        public List<string> UnknownScanGroups { get; } = new();
        public List<NetworkVariableModel> MissingVariables { get; } = new();

        /// <summary>本次导入所属的连接名（新建变量时由计划上下文补到变量上）。</summary>
        public string ConnectionName { get; set; } = string.Empty;

        public string? GlobalError { get; set; }

        public int NewCount => Items.Count(i => i.Status == DeviceVariableImportStatus.New);
        public int UpdateCount => Items.Count(i => i.Status == DeviceVariableImportStatus.Update);
        public int UnchangedCount => Items.Count(i => i.Status == DeviceVariableImportStatus.Unchanged);
        public int ErrorCount => Items.Count(i => i.Status == DeviceVariableImportStatus.Error);
        public int MissingCount => MissingVariables.Count;
        public bool HasErrors => ErrorCount > 0;
        public int ApplicableCount => NewCount + UpdateCount;
    }

    public sealed class DeviceVariableImportResult
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Deleted { get; set; }
        public int SkippedErrors { get; set; }
        public int Unchanged { get; set; }
    }

    /// <summary>导入事务失败：Message 已包含回滚结果（成功 / 未完全成功及原因），可直接上屏。</summary>
    public sealed class DeviceVariableImportException : Exception
    {
        public DeviceVariableImportException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public sealed class DeviceVariableIoOptions
    {
        public bool IncludeGuideSheet { get; init; } = true;
        public bool IncludeAddressPreview { get; init; } = true;
        public bool AutoCreateDisplayGroups { get; init; } = true;
    }

    /// <summary>
    /// 设备变量表导出（xlsx：数据页 + 说明页）。
    /// 只导出结构化字段，绝不导出/回读复合地址串——地址预览列只给人看，导入忽略。
    /// </summary>
    public static class DeviceVariableExportService
    {
        public static string BuildDefaultFileName(string connectionName, DateTime now)
        {
            var safe = string.Concat((connectionName ?? "device")
                .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            return $"{safe}_变量表_{now:yyyyMMdd_HHmm}.xlsx";
        }

        public static Dictionary<string, object?> BuildRow(
            NetworkVariableModel variable,
            IReadOnlyList<VariableGroupDescriptor> groups,
            bool includeAddressPreview = true)
        {
            var address = variable.AddressConfig;
            string area = string.Empty, offset = "0", bit = string.Empty, station = string.Empty,
                db = string.Empty, length = "1", elementBytes = string.Empty,
                addressOffset = "0", legacy = string.Empty;
            bool conversion = false;
            double scale = 1, engineeringOffset = 0;
            string unit = string.Empty;

            if (address is ModbusAddress modbus)
            {
                area = GlobalVariableManagerViewModel.AreaDisplayName(modbus.Area);
                offset = modbus.Offset ?? "0";
                bit = modbus.BitOffset >= 0 ? modbus.BitOffset.ToString(CultureInfo.InvariantCulture) : string.Empty;
                station = modbus.Station.ToString(CultureInfo.InvariantCulture);
                length = modbus.Length.ToString(CultureInfo.InvariantCulture);
                elementBytes = modbus.ElementByteLength > 0
                    ? modbus.ElementByteLength.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                addressOffset = modbus.AddressOffset.ToString(CultureInfo.InvariantCulture);
                legacy = DeviceVariableIoText.FormatBool(modbus.UseLegacyAddress);
                conversion = modbus.EnableConversion;
                scale = modbus.Scale ?? 1;
                engineeringOffset = modbus.EngineeringOffset;
                unit = modbus.Unit ?? string.Empty;
            }
            else if (address is S7Address s7)
            {
                area = GlobalVariableManagerViewModel.AreaDisplayName(s7.Area);
                offset = s7.Offset ?? "0";
                bit = s7.BitOffset >= 0 ? s7.BitOffset.ToString(CultureInfo.InvariantCulture) : string.Empty;
                db = s7.DbNumber.ToString(CultureInfo.InvariantCulture);
                length = s7.Length.ToString(CultureInfo.InvariantCulture);
                elementBytes = s7.ElementByteLength > 0
                    ? s7.ElementByteLength.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                addressOffset = s7.AddressOffset.ToString(CultureInfo.InvariantCulture);
                conversion = s7.EnableConversion;
                scale = s7.Scale ?? 1;
                engineeringOffset = s7.EngineeringOffset;
                unit = s7.Unit ?? string.Empty;
            }
            else if (address is McAddress mc)
            {
                // Mc / Fins 的存储区上屏走 AreaDisplayNames（枚举上的 [Display] 中文），
                // 与 GetAreaOptions 同一口径；导入侧 TryResolveArea 同时接受中文显示名与成员名，
                // 所以存量文件里写 "D"/"W" 的仍然能导进来。站号 / DB / 40001 三列不适用，留空。
                area = GlobalVariableManagerViewModel.AreaDisplayName(mc.Area);
                offset = mc.Offset ?? "0";
                bit = mc.BitOffset >= 0 ? mc.BitOffset.ToString(CultureInfo.InvariantCulture) : string.Empty;
                length = mc.Length.ToString(CultureInfo.InvariantCulture);
                elementBytes = mc.ElementByteLength > 0
                    ? mc.ElementByteLength.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                addressOffset = mc.AddressOffset.ToString(CultureInfo.InvariantCulture);
                conversion = mc.EnableConversion;
                scale = mc.Scale ?? 1;
                engineeringOffset = mc.EngineeringOffset;
                unit = mc.Unit ?? string.Empty;
            }
            else if (address is FinsAddress fins)
            {
                area = GlobalVariableManagerViewModel.AreaDisplayName(fins.Area);
                offset = fins.Offset ?? "0";
                bit = fins.BitOffset >= 0 ? fins.BitOffset.ToString(CultureInfo.InvariantCulture) : string.Empty;
                length = fins.Length.ToString(CultureInfo.InvariantCulture);
                elementBytes = fins.ElementByteLength > 0
                    ? fins.ElementByteLength.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                addressOffset = fins.AddressOffset.ToString(CultureInfo.InvariantCulture);
                conversion = fins.EnableConversion;
                scale = fins.Scale ?? 1;
                engineeringOffset = fins.EngineeringOffset;
                unit = fins.Unit ?? string.Empty;
            }

            var scanGroup = string.IsNullOrWhiteSpace(variable.ScanGroup)
                ? PollScheduler.DefaultGroupName
                : variable.ScanGroup;
            var displayGroup = groups.FirstOrDefault(g => g.Id == variable.GroupId)?.Name ?? "未分组";
            var initial = variable.DataType?.IsArray == true
                ? string.Empty
                : DeviceVariableIoText.FormatValue(variable.DefaultValue, variable.DataType);

            var row = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [DeviceVariableColumns.Name] = variable.Name ?? string.Empty,
                [DeviceVariableColumns.Description] = variable.Description ?? string.Empty,
                [DeviceVariableColumns.DataType] = GlobalVariableManagerViewModel.TypeDisplayName(variable.DataType),
                [DeviceVariableColumns.Area] = area,
                [DeviceVariableColumns.Offset] = offset,
                [DeviceVariableColumns.BitOffset] = bit,
                [DeviceVariableColumns.Station] = station,
                [DeviceVariableColumns.DbNumber] = db,
                [DeviceVariableColumns.ElementCount] = length,
                [DeviceVariableColumns.ElementByteLength] = elementBytes,
                [DeviceVariableColumns.AddressOffset] = addressOffset,
                [DeviceVariableColumns.LegacyAddress] = legacy,
                [DeviceVariableColumns.ScanGroup] = scanGroup,
                [DeviceVariableColumns.DisplayGroup] = displayGroup,
                [DeviceVariableColumns.MergeAdjacentRead] = DeviceVariableIoText.FormatBool(variable.MergeAdjacentRead),
                [DeviceVariableColumns.EnableConversion] = DeviceVariableIoText.FormatBool(conversion),
                [DeviceVariableColumns.Scale] = scale.ToString("0.############", CultureInfo.InvariantCulture),
                [DeviceVariableColumns.EngineeringOffset] = engineeringOffset.ToString("0.############", CultureInfo.InvariantCulture),
                [DeviceVariableColumns.Unit] = unit,
                [DeviceVariableColumns.InitialValue] = initial,
            };

            if (includeAddressPreview)
                row[DeviceVariableColumns.AddressPreview] = address?.Address ?? string.Empty;

            return row;
        }

        public static void Export(
            string path,
            CommunicationConfig connection,
            IEnumerable<NetworkVariableModel> variables,
            IReadOnlyList<VariableGroupDescriptor> groups,
            DeviceVariableIoOptions options)
        {
            var rows = variables.Select(v => BuildRow(v, groups, options.IncludeAddressPreview)).ToList();
            if (rows.Count == 0)
                rows.Add(BuildEmptyRow(options.IncludeAddressPreview));

            var sheets = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["变量表"] = rows
            };
            if (options.IncludeGuideSheet)
                sheets["说明"] = BuildGuideRows(connection, groups);

            WriteSheets(path, sheets);
        }

        public static void ExportTemplate(
            string path,
            CommunicationConfig connection,
            IReadOnlyList<VariableGroupDescriptor> groups)
        {
            var rows = new List<Dictionary<string, object?>> { BuildEmptyRow(includeAddressPreview: true) };
            var sheets = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["变量表"] = rows,
                ["说明"] = BuildGuideRows(connection, groups)
            };
            WriteSheets(path, sheets);
        }

        /// <summary>
        /// 先写同目录临时文件再覆盖移动：MiniExcel 写到一半失败时，用户原来那份文件还在。
        /// 旧写法"先 Delete 再 SaveAs"在磁盘满 / 文件被占用时会把原文件一起弄丢。
        /// </summary>
        private static void WriteSheets(string path, Dictionary<string, object> sheets)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp.xlsx";
            try
            {
                MiniExcel.SaveAs(temp, sheets);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static Dictionary<string, object?> BuildEmptyRow(bool includeAddressPreview)
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var key in DeviceVariableColumns.Aliases.Keys)
            {
                if (key == DeviceVariableColumns.AddressPreview && !includeAddressPreview) continue;
                row[key] = string.Empty;
            }
            return row;
        }

        private static List<Dictionary<string, object?>> BuildGuideRows(
            CommunicationConfig connection,
            IReadOnlyList<VariableGroupDescriptor> groups)
        {
            var scanGroups = new List<string> { PollScheduler.DefaultGroupName };
            scanGroups.AddRange(connection.ScanGroups?.Select(g => g.Name) ?? Enumerable.Empty<string>());
            var types = string.Join(" / ", GlobalVariableManagerViewModel.TypesFor(true).Select(t => t.DisplayName));
            var areas = string.Join(" / ", GlobalVariableManagerViewModel.GetAreaOptions(connection));
            var displayGroups = new List<string> { "未分组" };
            displayGroups.AddRange(groups.Select(g => g.Name));

            var rows = new List<(string Item, string Description)>
            {
                ("格式版本", "v1（2026-10-21）"),
                ("适用范围", "单个设备（连接）的网络变量；本地 / 系统变量不参与"),
                ("匹配规则", "按变量名匹配；不支持通过 Excel 改名；文件内重名报错"),
                ("必填列", string.Join(" / ", DeviceVariableColumns.Required)),
                ("数据类型", types),
                ("存储区", areas),
                ("扫描组", string.Join(" / ", scanGroups) + "；未知扫描组回落「默认组」，不会自动建组"),
                ("显示分组", string.Join(" / ", displayGroups) + "；未知显示分组按导入选项自动创建或回落「未分组」"),
                ("地址", "按 存储区 + 偏移量 + 位偏移 + 站号 + DB号 + 元素个数 + 地址偏移 结构化填写；40001写法仅 Modbus；S7 需填 DB号"),
                ("每元素字节数", "仅字符串数组：每个元素的固定字节数（>= 1）"),
                ("初始值", "仅标量；新增行留空 = 类型默认值；更新行留空 = 保持原初始值；数组忽略"),
                ("空白默认值", "DB号 1；元素个数 1；缩放系数 1；位偏移留空 = 不使用；站号 / 地址偏移 / 工程偏移 0"),
                ("列适用", "站号 / 40001写法仅 Modbus；DB号仅 S7；每元素字节数仅字符串数组；位偏移仅布尔变量；字符串标量的「元素个数」是总字节数"),
                ("删除", "默认不删除文件中没有的变量；勾选「删除文件中没有的变量」后才删除"),
                ("运行锁", "流程运行中不可导入"),
                ("地址预览", "只读列，导入时忽略"),
            };

            return rows.Select(r => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["项目"] = r.Item,
                ["说明"] = r.Description
            }).ToList();
        }
    }

    /// <summary>
    /// 设备变量表导入：读 xlsx → 构建差异计划（纯函数）→ 事务应用。
    ///
    /// 三条硬口径
    /// ---------
    /// ① **只按变量名匹配**：不支持通过 Excel 改名；同名 = 更新，文件内重名 = 错误。
    /// ② **默认不删**：文件中没有的变量默认保留；删除是显式选项。
    /// ③ **更新必须重注册轮询**：地址 / 扫描组 / 区域合并读 / 类型在注册时被抄成快照，
    ///    只改模型不 ReRegister 会出现"导入成功但还在读旧地址"。
    /// </summary>
    public static class DeviceVariableImportService
    {
        private const string DataSheetName = "变量表";

        public static List<DeviceVariableRawRow> ReadRows(string path)
        {
            var sheets = MiniExcel.GetSheetNames(path).ToList();
            // 明确要求「变量表」页：回落到第一张表时，若第一张是「说明」，
            // 会得到"缺少必需列"的误导错误（看起来像模板坏了）。
            var sheet = sheets.FirstOrDefault(n => string.Equals(n, DataSheetName, StringComparison.Ordinal));
            if (sheet == null)
                throw new InvalidOperationException("工作簿里找不到「变量表」工作表（请使用「下载模板」生成）。");

            var result = new List<DeviceVariableRawRow>();
            var index = 0;
            foreach (var raw in MiniExcel.Query(path, useHeaderRow: true, sheetName: sheet))
            {
                index++;
                if (raw is not IDictionary<string, object> dict) continue;

                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in dict)
                    cells[DeviceVariableIoText.NormalizeHeader(pair.Key)] = DeviceVariableIoText.CellText(pair.Value);

                // 空行跳过（模板里的空白行不算错误）
                if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                result.Add(new DeviceVariableRawRow { RowNumber = index + 1, Cells = cells });
            }
            return result;
        }

        public static DeviceVariableImportPlan BuildPlan(
            CommunicationConfig connection,
            IReadOnlyList<DeviceVariableRawRow> rows,
            IReadOnlyList<NetworkVariableModel> existingDeviceVariables,
            IReadOnlyList<IVariable> allVariables,
            IReadOnlyList<VariableGroupDescriptor> groups,
            IReadOnlyList<string> scanGroupNames,
            bool autoCreateDisplayGroups)
        {
            var plan = new DeviceVariableImportPlan
            {
                ConnectionName = connection.ConnectionName ?? string.Empty
            };
            if (rows.Count == 0)
            {
                plan.GlobalError = "文件没有数据行（模板里的空白行会被忽略）。";
                return plan;
            }

            foreach (var required in DeviceVariableColumns.Required)
            {
                if (!rows[0].Has(required))
                {
                    plan.GlobalError = $"缺少必需列「{required}」。请使用「下载模板」重新生成表头。";
                    return plan;
                }
            }

            // 键统一 Trim：导入侧的文件名会 Trim，既有变量名若带前后空格，
            // 不 Trim 就会"同一变量被判成新增 + 原变量被列为删除"。
            var existingByName = new Dictionary<string, NetworkVariableModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in existingDeviceVariables)
                if (!string.IsNullOrWhiteSpace(variable.Name))
                    existingByName[variable.Name.Trim()] = variable;

            var allByName = new Dictionary<string, IVariable>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in allVariables)
                if (!string.IsNullOrWhiteSpace(variable.Name))
                    allByName[variable.Name.Trim()] = variable;

            var groupsByName = new Dictionary<string, VariableGroupDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
                if (!string.IsNullOrWhiteSpace(group.Name))
                    groupsByName[group.Name.Trim()] = group;

            var canonicalScanGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in scanGroupNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                canonicalScanGroups[name] = string.Equals(name, PollScheduler.DefaultGroupName, StringComparison.Ordinal)
                    ? string.Empty
                    : name;
            }

            var seenNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var newDisplayGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknownDisplayGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknownScanGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var typeOptions = GlobalVariableManagerViewModel.TypesFor(true).Select(t => t.ActualType).ToHashSet();
            var typeNames = string.Join(" / ", GlobalVariableManagerViewModel.TypesFor(true).Select(t => t.DisplayName));
            var areaNames = string.Join(" / ", GlobalVariableManagerViewModel.GetAreaOptions(connection));
            var expectedAddressType = ExpectedAddressType(connection.Protocol);

            foreach (var row in rows)
            {
                var name = row.Get(DeviceVariableColumns.Name).Trim();

                void Fail(string message)
                {
                    plan.Items.Add(new DeviceVariableImportItem
                    {
                        RowNumber = row.RowNumber,
                        Name = name,
                        Status = DeviceVariableImportStatus.Error,
                        Note = message
                    });
                }

                if (name.Length == 0)
                {
                    Fail("变量名不能为空。");
                    continue;
                }

                if (!VisionMaster.Binding.VariableNameRules.TryValidateNewName(name, out var nameError))
                {
                    Fail(nameError);
                    continue;
                }

                if (seenNames.TryGetValue(name, out var firstRow))
                {
                    Fail($"文件内变量名重复（与第 {firstRow} 行）。");
                    continue;
                }
                seenNames[name] = row.RowNumber;

                existingByName.TryGetValue(name, out var existing);
                if (allByName.TryGetValue(name, out var other) && !ReferenceEquals(other, existing))
                {
                    var source = other is NetworkVariableModel network
                        ? $"设备「{network.ConnectionName}」"
                        : other.VariableType == VariableType.Local ? "本地变量" : "系统变量";
                    Fail($"变量名已被{source}使用（方案内变量名必须唯一）。");
                    continue;
                }

                var typeName = row.Get(DeviceVariableColumns.DataType).Trim();
                var dataType = GlobalVariableManagerViewModel.ResolveType(typeName);
                if (dataType == null)
                {
                    Fail($"数据类型「{typeName}」无法识别（可用：{typeNames}）。");
                    continue;
                }
                if (!typeOptions.Contains(dataType))
                {
                    Fail($"网络变量不支持数据类型「{typeName}」（可用：{typeNames}）。");
                    continue;
                }

                var areaName = row.Get(DeviceVariableColumns.Area).Trim();
                if (!GlobalVariableManagerViewModel.TryResolveArea(connection, areaName, out var area))
                {
                    Fail($"存储区「{areaName}」无法识别（可用：{areaNames}）。");
                    continue;
                }

                var offset = row.Get(DeviceVariableColumns.Offset).Trim();
                if (offset.Length == 0)
                {
                    Fail("偏移量不能为空。");
                    continue;
                }

                // 空白 = 不使用位访问（-1）。TryParseInt 对空白返回 true/0，
                // 不能把"没填"当成"位 0"——那会让 bool 变量悄悄改成按第 0 位读。
                var bitText = row.Get(DeviceVariableColumns.BitOffset).Trim();
                var bitOffset = -1;
                if (bitText.Length > 0)
                {
                    if (!DeviceVariableIoText.TryParseInt(bitText, out bitOffset, out var bitError))
                    {
                        Fail($"位偏移：{bitError}");
                        continue;
                    }
                    // 上限按协议：FINS 的字是 16 位（0~15），Modbus / S7 / MC 是字节内位（0~7）。
                    // 不能一律放行 15——地址对象的 BitOffset setter 会把 S7 的 15 静默钳成 7。
                    var maxBitOffset = connection.Protocol == CommunicationType.OmronFins ? 15 : 7;
                    if (bitOffset < 0 || bitOffset > maxBitOffset)
                    {
                        Fail($"位偏移必须在 0 ~ {maxBitOffset} 之间（留空 = 不使用位访问；" +
                             $"{GlobalVariableManagerViewModel.ProtocolDisplayName(connection.Protocol)} 上限 {maxBitOffset}）。");
                        continue;
                    }
                    if (dataType != typeof(bool))
                    {
                        Fail("位偏移仅布尔变量适用。");
                        continue;
                    }
                    if (connection.Protocol == CommunicationType.MitsubishiMc
                        || GlobalVariableManagerViewModel.IsCoilOrDiscreteArea(area))
                    {
                        Fail("位偏移对 MC / 线圈 / 离散输入不适用。");
                        continue;
                    }
                }

                // 站号 / DB号 / 40001写法都是**协议专属列**：空白忽略；
                // 非空但协议不适用时明确报错，而不是拿别的协议的列去范围校验。
                var stationText = row.Get(DeviceVariableColumns.Station).Trim();
                var station = 0;
                if (stationText.Length > 0)
                {
                    if (connection.Protocol is not (CommunicationType.ModbusTcp or CommunicationType.ModbusRtu))
                    {
                        Fail("站号仅 Modbus 适用。");
                        continue;
                    }
                    if (!DeviceVariableIoText.TryParseInt(stationText, out station, out var stationError)
                        || station < 0 || station > 255)
                    {
                        Fail(stationError ?? "站号必须在 0 ~ 255 之间。");
                        continue;
                    }
                }

                var dbText = row.Get(DeviceVariableColumns.DbNumber).Trim();
                var dbNumber = 1;
                if (dbText.Length > 0)
                {
                    if (connection.Protocol != CommunicationType.SiemensS7)
                    {
                        Fail("DB号仅 S7 适用。");
                        continue;
                    }
                    if (!DeviceVariableIoText.TryParseInt(dbText, out dbNumber, out var dbError))
                    {
                        Fail($"DB号：{dbError}");
                        continue;
                    }
                    if (dbNumber < 1 || dbNumber > 65535)
                    {
                        Fail("DB 号必须在 1 ~ 65535 之间。");
                        continue;
                    }
                }

                if (!DeviceVariableIoText.TryParseInt(row.Get(DeviceVariableColumns.ElementCount), out var elementCount, out var countError))
                {
                    Fail($"元素个数：{countError}");
                    continue;
                }
                // 空白 = 标量 1 个（字符串标量则是 1 字节）。
                if (row.Get(DeviceVariableColumns.ElementCount).Trim().Length == 0) elementCount = 1;
                if (elementCount < 1)
                {
                    Fail("元素个数必须 >= 1。");
                    continue;
                }

                if (!DeviceVariableIoText.TryParseInt(row.Get(DeviceVariableColumns.ElementByteLength), out var elementByteLength, out var bytesError)
                    || elementByteLength < 0)
                {
                    Fail(bytesError ?? "每元素字节数必须 >= 0。");
                    continue;
                }

                if (!DeviceVariableIoText.TryParseInt(row.Get(DeviceVariableColumns.AddressOffset), out var addressOffset, out var addressOffsetError))
                {
                    Fail($"地址偏移：{addressOffsetError}");
                    continue;
                }

                var legacy = DeviceVariableIoText.ParseOptionalBool(row.Get(DeviceVariableColumns.LegacyAddress), out var legacyError);
                if (legacyError != null)
                {
                    Fail($"40001写法：{legacyError}");
                    continue;
                }
                if (legacy == true
                    && connection.Protocol is not (CommunicationType.ModbusTcp or CommunicationType.ModbusRtu))
                {
                    Fail("40001写法仅 Modbus 适用。");
                    continue;
                }

                var merge = DeviceVariableIoText.ParseOptionalBool(row.Get(DeviceVariableColumns.MergeAdjacentRead), out var mergeError);
                if (mergeError != null)
                {
                    Fail($"区域合并读：{mergeError}");
                    continue;
                }

                var conversion = DeviceVariableIoText.ParseOptionalBool(row.Get(DeviceVariableColumns.EnableConversion), out var conversionError);
                if (conversionError != null)
                {
                    Fail($"启用换算：{conversionError}");
                    continue;
                }

                if (!DeviceVariableIoText.TryParseDouble(row.Get(DeviceVariableColumns.Scale), out var scale, out var scaleError))
                {
                    Fail($"缩放系数：{scaleError}");
                    continue;
                }
                // 空白 = 1（缩放系数是乘数，0 会把所有读数变成 0）。
                if (row.Get(DeviceVariableColumns.Scale).Trim().Length == 0) scale = 1;
                if (!DeviceVariableIoText.TryParseDouble(row.Get(DeviceVariableColumns.EngineeringOffset), out var engineeringOffset, out var engineeringError))
                {
                    Fail($"工程偏移：{engineeringError}");
                    continue;
                }

                var elementType = dataType.IsArray ? dataType.GetElementType() : null;
                bool isArray = dataType.IsArray && dataType != typeof(byte[]);
                bool isStringScalar = dataType == typeof(string);
                bool isStringArray = isArray && elementType == typeof(string);
                bool isBoolArray = isArray && elementType == typeof(bool);

                if (isArray)
                {
                    if (elementType == null)
                    {
                        Fail("数组元素类型无法确定。");
                        continue;
                    }
                    if (elementType == typeof(bool))
                    {
                        if (!GlobalVariableManagerViewModel.IsCoilOrDiscreteArea(area))
                        {
                            Fail("bool 数组仅支持 Modbus 线圈 / 离散输入区（S7 的位数组语义不唯一）。");
                            continue;
                        }
                    }
                    else if (elementType != typeof(string) && elementType != typeof(int)
                             && elementType != typeof(double) && elementType != typeof(float))
                    {
                        Fail($"网络数组不支持元素类型 {elementType.Name}（可用 int / double / float / string / bool）。");
                        continue;
                    }

                    if (isStringArray && elementByteLength < 1)
                    {
                        Fail("字符串数组必须填「每元素字节数」（>= 1）。");
                        continue;
                    }
                }

                if (row.Get(DeviceVariableColumns.ElementByteLength).Trim().Length > 0 && !isStringArray)
                {
                    Fail("每元素字节数仅字符串数组适用。");
                    continue;
                }

                if (!isArray && !isStringScalar && elementCount != 1)
                {
                    Fail("「元素个数」只对数组 / 字符串有效；标量请填 1。");
                    continue;
                }

                if (GlobalVariableManagerViewModel.IsCoilOrDiscreteArea(area)
                    && dataType != typeof(bool) && !isBoolArray)
                {
                    Fail("线圈 / 离散输入区仅支持布尔类型（或 bool 数组）。");
                    continue;
                }

                if ((isArray || isStringScalar) && conversion == true)
                {
                    Fail("数组与字符串变量暂不支持工程量换算，请关闭「启用换算」。");
                    continue;
                }

                object? initial = null;
                if (!isArray)
                {
                    var initialText = row.Get(DeviceVariableColumns.InitialValue).Trim();
                    if (initialText.Length == 0 && existing != null && existing.DataType == dataType)
                    {
                        // 更新行留空 = 保持原初始值（含 null）；新增行留空 = 类型默认值。
                        // 否则"导出→读回"会把 null 静默改成 0/false，判成一次更新。
                        initial = existing.DefaultValue;
                    }
                    else if (!GlobalVariableManagerViewModel.TryGetNewInitialValue(
                                 initialText, dataType, out initial, out var initialError))
                    {
                        Fail($"初始值无效：{initialError}");
                        continue;
                    }

                    if (initial is double doubleInitial && !double.IsFinite(doubleInitial))
                    {
                        Fail("初始值必须是有限数（不允许 NaN / Infinity）。");
                        continue;
                    }
                    if (initial is float floatInitial && !float.IsFinite(floatInitial))
                    {
                        Fail("初始值必须是有限数（不允许 NaN / Infinity）。");
                        continue;
                    }
                }

                if (!GlobalVariableManagerViewModel.TryBuildAddress(
                        connection.Protocol, area, offset, bitOffset, dataType,
                        out var address, out var addressError,
                        station: station,
                        enableConversion: conversion == true,
                        scale: scale,
                        engineeringOffset: engineeringOffset,
                        unit: row.Get(DeviceVariableColumns.Unit).Trim(),
                        elementCount: isArray || isStringScalar ? elementCount : 1,
                        elementByteLength: isStringArray ? elementByteLength : 0,
                        addressOffset: addressOffset,
                        useLegacyAddress: legacy == true,
                        dbNumber: dbNumber,
                        mcVendor: GlobalVariableManagerViewModel.McVendorOf(connection)))
                {
                    Fail(addressError);
                    continue;
                }

                var (valid, validationError) = address.Validate();
                if (!valid)
                {
                    Fail(validationError);
                    continue;
                }

                // 字符串标量的「元素个数」是总字节数，和数组一样受单次读取上限约束
                // （创建侧就是 isArray || isStringScalar，导入不能只拦数组）。
                if ((isArray || isStringScalar) && address.TryCreatePollAddress(out var poll) && poll != null
                    && poll.SpanUnits > poll.MaxSegmentUnits)
                {
                    var what = isStringScalar ? "字符串字节长度" : "数组跨度";
                    var hint = isStringScalar ? "字符串长度（元素个数列）" : "元素个数";
                    Fail($"{what} {poll.SpanUnits} 个地址单位，超过单次读取上限 {poll.MaxSegmentUnits}；请减小{hint}。");
                    continue;
                }

                var scanGroupText = row.Get(DeviceVariableColumns.ScanGroup).Trim();
                string storedScanGroup;
                string scanGroupDisplay;
                if (scanGroupText.Length == 0
                    || string.Equals(scanGroupText, PollScheduler.DefaultGroupName, StringComparison.Ordinal))
                {
                    storedScanGroup = string.Empty;
                    scanGroupDisplay = PollScheduler.DefaultGroupName;
                }
                else if (canonicalScanGroups.TryGetValue(scanGroupText, out var canonical))
                {
                    storedScanGroup = canonical;
                    scanGroupDisplay = canonical.Length == 0 ? PollScheduler.DefaultGroupName : canonical;
                }
                else
                {
                    storedScanGroup = string.Empty;
                    scanGroupDisplay = PollScheduler.DefaultGroupName;
                    unknownScanGroups.Add(scanGroupText);
                }

                var displayGroupText = row.Get(DeviceVariableColumns.DisplayGroup).Trim();
                string displayGroupName;
                bool newDisplayGroup = false;
                if (displayGroupText.Length == 0
                    || string.Equals(displayGroupText, "未分组", StringComparison.Ordinal))
                {
                    displayGroupName = "未分组";
                }
                else if (groupsByName.TryGetValue(displayGroupText, out var group))
                {
                    displayGroupName = group.Name;
                }
                else if (autoCreateDisplayGroups)
                {
                    // 复用分组编辑器的统一校验（长度 / 保留名 / 同名），不另写一份规则。
                    var groupError = VariableGroupEditorViewModel.ValidateGroupName(displayGroupText, groups, null);
                    if (groupError != null)
                    {
                        Fail(groupError);
                        continue;
                    }
                    displayGroupName = displayGroupText.Trim();
                    newDisplayGroup = true;
                }
                else
                {
                    displayGroupName = "未分组";
                    unknownDisplayGroups.Add(displayGroupText);
                }

                var definition = new DeviceVariableDefinition
                {
                    Name = name,
                    Description = row.Get(DeviceVariableColumns.Description).Trim(),
                    DataType = dataType,
                    TypeName = GlobalVariableManagerViewModel.TypeDisplayName(dataType),
                    Area = area,
                    Offset = offset,
                    BitOffset = bitOffset,
                    Station = station,
                    DbNumber = dbNumber,
                    ElementCount = elementCount,
                    ElementByteLength = elementByteLength,
                    AddressOffset = addressOffset,
                    UseLegacyAddress = legacy == true,
                    ScanGroup = storedScanGroup,
                    ScanGroupDisplay = scanGroupDisplay,
                    DisplayGroupName = displayGroupName,
                    MergeAdjacentRead = merge ?? true,
                    EnableConversion = conversion == true,
                    Scale = scale,
                    EngineeringOffset = engineeringOffset,
                    Unit = row.Get(DeviceVariableColumns.Unit).Trim(),
                    InitialValue = initial,
                    Address = address
                };

                // 只有第一次出现的新组名才提示"将新建"：大小写变体由 HashSet 去重，
                // 否则同一组会在多行重复说"将新建"。
                var isFirstNewGroup = newDisplayGroup && newDisplayGroups.Add(displayGroupName);

                var notes = new List<string>();
                if (isFirstNewGroup) notes.Add($"将新建显示分组「{displayGroupName}」");
                if (unknownScanGroups.Contains(scanGroupText)) notes.Add($"扫描组「{scanGroupText}」不存在 → 默认组");

                if (existing == null)
                {
                    if (notes.Count == 0) notes.Add("新变量");
                    plan.Items.Add(new DeviceVariableImportItem
                    {
                        RowNumber = row.RowNumber,
                        Name = name,
                        Status = DeviceVariableImportStatus.New,
                        Definition = definition,
                        Note = string.Join("；", notes)
                    });
                    continue;
                }

                // MC ↔ Fins 的 "D100" 等地址串可以完全相同：光比导出列会判"无变化"，
                // 变量却还挂着旧协议的地址对象。地址类型与当前连接协议不一致时必须重建。
                var protocolMismatch = expectedAddressType != null
                                       && existing.AddressConfig != null
                                       && existing.AddressConfig.GetType() != expectedAddressType;

                var existingRow = DeviceVariableExportService.BuildRow(existing, groups, includeAddressPreview: false);
                var temp = new NetworkVariableModel
                {
                    Name = definition.Name,
                    Description = definition.Description,
                    DataType = definition.DataType,
                    ConnectionName = connection.ConnectionName,
                    AddressConfig = definition.Address,
                    ScanGroup = definition.ScanGroup,
                    MergeAdjacentRead = definition.MergeAdjacentRead,
                    GroupId = Guid.Empty,
                    DefaultValue = definition.InitialValue
                };
                var newRow = DeviceVariableExportService.BuildRow(temp, groups, includeAddressPreview: false);
                newRow[DeviceVariableColumns.DisplayGroup] = definition.DisplayGroupName;

                var changed = DiffRows(existingRow, newRow);
                if (changed.Count == 0 && !protocolMismatch)
                {
                    plan.Items.Add(new DeviceVariableImportItem
                    {
                        RowNumber = row.RowNumber,
                        Name = name,
                        Status = DeviceVariableImportStatus.Unchanged,
                        Definition = definition,
                        Existing = existing,
                        Note = "—"
                    });
                }
                else
                {
                    if (protocolMismatch) notes.Add("地址类型与连接协议不一致，将按当前协议重建地址");
                    if (changed.Count > 0) notes.Add(BuildDiffNote(changed, existingRow, newRow));
                    var note = notes.Count > 0
                        ? string.Join("；", notes)
                        : "地址类型与连接协议不一致";
                    plan.Items.Add(new DeviceVariableImportItem
                    {
                        RowNumber = row.RowNumber,
                        Name = name,
                        Status = DeviceVariableImportStatus.Update,
                        Definition = definition,
                        Existing = existing,
                        Note = note
                    });
                }
            }

            foreach (var name in newDisplayGroups) plan.NewDisplayGroups.Add(name);
            foreach (var name in unknownDisplayGroups) plan.UnknownDisplayGroups.Add(name);
            foreach (var name in unknownScanGroups) plan.UnknownScanGroups.Add(name);

            foreach (var variable in existingDeviceVariables)
            {
                if (string.IsNullOrWhiteSpace(variable.Name)) continue;
                if (seenNames.ContainsKey(variable.Name.Trim())) continue;
                plan.MissingVariables.Add(variable);
            }

            return plan;
        }

        /// <summary>
        /// 事务应用：新增 → 更新（必要时 ReRegister）→ 删除缺失。任一步失败全量回滚。
        /// </summary>
        public static DeviceVariableImportResult Apply(
            DeviceVariableImportPlan plan,
            IWorkspaceManager workspace,
            Func<NetworkVariableModel, bool>? reRegister,
            bool deleteMissing,
            bool skipErrors)
        {
            if (plan.GlobalError != null)
                throw new InvalidOperationException(plan.GlobalError);
            if (string.IsNullOrWhiteSpace(plan.ConnectionName))
                throw new InvalidOperationException("导入计划缺少目标连接名。");
            if (plan.HasErrors && !skipErrors)
                throw new InvalidOperationException($"文件里有 {plan.ErrorCount} 行错误，未勾选「跳过错误行」。");

            var solution = workspace.CurrentSolution
                           ?? throw new InvalidOperationException("当前没有打开的方案，无法导入。");
            var variables = workspace.GlobalVariables;

            // 服务层也守运行锁：Apply 是 public，未来任何直接调用都不能绕过。
            if (workspace.CurrentFlow?.RunState == FlowRunState.Running)
                throw new InvalidOperationException("流程运行中禁止导入变量表。请先停止流程。");

            // 弹窗是模态的，但外部仍可能在这期间删变量/改方案：落地前轻量复核，
            // 避免把更新打在已移除的对象上、或新增一个已被占用的名字。
            // 撞名检查必须并上系统变量：注册表是 Global + System 两源合并的。
            var allVariables = variables.Concat(workspace.SystemVariables).ToList();
            foreach (var item in plan.Items)
            {
                if (item.Status is not (DeviceVariableImportStatus.New or DeviceVariableImportStatus.Update)) continue;
                if (item.Status == DeviceVariableImportStatus.Update)
                {
                    if (item.Existing == null || !variables.Contains(item.Existing))
                        throw new InvalidOperationException($"变量「{item.Name}」已不在当前方案中，请重新预览。");
                }
                else if (allVariables.Any(v => string.Equals(
                             (v.Name ?? string.Empty).Trim(), item.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"变量名「{item.Name}」已被占用，请重新预览。");
                }
            }

            var result = new DeviceVariableImportResult { Unchanged = plan.UnchangedCount };

            var createdGroups = new List<VariableGroupDescriptor>();
            var addedVariables = new List<NetworkVariableModel>();
            var updatedVariables = new List<(NetworkVariableModel Variable, VariableSnapshot Snapshot, bool NeedsReRegister)>();
            var deletedVariables = new List<NetworkVariableModel>();

            try
            {
                foreach (var name in plan.NewDisplayGroups)
                {
                    if (solution.VariableGroups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    var group = new VariableGroupDescriptor { Id = Guid.NewGuid(), Name = name };
                    solution.VariableGroups.Add(group);
                    createdGroups.Add(group);
                }

                foreach (var item in plan.Items)
                {
                    if (item.Status == DeviceVariableImportStatus.Error)
                    {
                        if (!skipErrors) throw new InvalidOperationException(item.Note);
                        result.SkippedErrors++;
                        continue;
                    }
                    if (item.Status == DeviceVariableImportStatus.Unchanged) continue;
                    if (item.Definition == null) continue;

                    var definition = item.Definition;
                    var groupId = ResolveDisplayGroupId(solution.VariableGroups, definition.DisplayGroupName);

                    if (item.Status == DeviceVariableImportStatus.New)
                    {
                        var variable = new NetworkVariableModel
                        {
                            Name = definition.Name,
                            DataType = definition.DataType,
                            Description = definition.Description,
                            ConnectionName = plan.ConnectionName,
                            AddressConfig = definition.Address,
                            ScanGroup = definition.ScanGroup,
                            MergeAdjacentRead = definition.MergeAdjacentRead,
                            GroupId = groupId,
                            DefaultValue = definition.InitialValue,
                            Value = definition.InitialValue
                        };
                        // 先进回滚清单再动集合：集合变更会触发桥接器注册，注册抛异常时
                        // 对象可能已经进了集合——后进清单就等于回滚漏掉它。
                        addedVariables.Add(variable);
                        variables.Add(variable);
                        result.Added++;
                    }
                    else if (item.Status == DeviceVariableImportStatus.Update && item.Existing != null)
                    {
                        var variable = item.Existing;
                        var snapshot = VariableSnapshot.Capture(variable);
                        var typeChanged = snapshot.DataType != definition.DataType;
                        var needsReRegister = typeChanged
                                              || AddressChanged(snapshot.AddressConfig, definition.Address)
                                              || !string.Equals(snapshot.ScanGroup ?? string.Empty, definition.ScanGroup ?? string.Empty, StringComparison.Ordinal)
                                              || snapshot.MergeAdjacentRead != definition.MergeAdjacentRead;

                        // 快照先入回滚清单：下面的赋值或 ReRegister 任一抛异常，
                        // 都必须能回到导入前（否则模型停在"改了一半"的状态）。
                        updatedVariables.Add((variable, snapshot, needsReRegister));

                        variable.Description = definition.Description;
                        variable.DataType = definition.DataType;
                        variable.AddressConfig = definition.Address;
                        variable.ScanGroup = definition.ScanGroup;
                        variable.MergeAdjacentRead = definition.MergeAdjacentRead;
                        variable.GroupId = groupId;

                        if (typeChanged)
                        {
                            // 类型变了，旧基准/旧镜像都不再是同一类型：数组清空基准，
                            // 标量用新初始值；只更新镜像，绝不经 Value setter 写设备。
                            var next = definition.DataType.IsArray ? null : definition.InitialValue;
                            variable.DefaultValue = next;
                            variable.UpdateMirrorValue(next);
                        }
                        else if (!definition.DataType.IsArray)
                        {
                            // 数组的「初始值」列按设计忽略（Excel 里是空的）：
                            // 不能把已有的基准数组清成 null——那是数组编辑/恢复基准的唯一来源。
                            variable.DefaultValue = definition.InitialValue;
                        }

                        if (needsReRegister)
                        {
                            if (reRegister == null)
                            {
                                throw new InvalidOperationException(
                                    $"变量「{variable.Name}」需要重注册轮询，但调用方未提供重注册委托。");
                            }
                            if (!reRegister(variable))
                            {
                                throw new InvalidOperationException(
                                    $"变量「{variable.Name}」重注册轮询失败（连接不存在或地址非法）。");
                            }
                        }
                        result.Updated++;
                    }
                }

                if (deleteMissing)
                {
                    foreach (var variable in plan.MissingVariables)
                    {
                        if (!variables.Contains(variable)) continue;
                        // 同上：先进回滚清单，再 Remove（Remove 的集合事件可能抛异常）。
                        deletedVariables.Add(variable);
                        if (variables.Remove(variable)) result.Deleted++;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                var rollbackErrors = Rollback(
                    variables, solution, createdGroups, addedVariables, updatedVariables, deletedVariables, reRegister);

                var message = rollbackErrors.Count == 0
                    ? $"{ex.Message}\n\n本次改动已回滚。"
                    : $"{ex.Message}\n\n回滚未完全成功：{string.Join("；", rollbackErrors)}";
                throw new DeviceVariableImportException(message, ex);
            }
        }

        private static List<string> Rollback(
            System.Collections.ObjectModel.ObservableCollection<IVariable> variables,
            SolutionModel solution,
            List<VariableGroupDescriptor> createdGroups,
            List<NetworkVariableModel> addedVariables,
            List<(NetworkVariableModel Variable, VariableSnapshot Snapshot, bool NeedsReRegister)> updatedVariables,
            List<NetworkVariableModel> deletedVariables,
            Func<NetworkVariableModel, bool>? reRegister)
        {
            var errors = new List<string>();

            foreach (var variable in addedVariables)
            {
                try { variables.Remove(variable); }
                catch (Exception ex) { errors.Add($"移除新增变量「{variable.Name}」失败：{ex.Message}"); }
            }

            foreach (var (variable, snapshot, needsReRegister) in updatedVariables)
            {
                try
                {
                    snapshot.Restore(variable);
                }
                catch (Exception ex)
                {
                    errors.Add($"恢复变量「{variable.Name}」失败：{ex.Message}");
                    continue;
                }

                if (!needsReRegister || reRegister == null) continue;
                try
                {
                    if (!reRegister(variable))
                        errors.Add($"变量「{variable.Name}」回滚重注册返回失败。");
                }
                catch (Exception ex)
                {
                    errors.Add($"变量「{variable.Name}」回滚重注册失败：{ex.Message}");
                }
            }

            foreach (var variable in deletedVariables)
            {
                try
                {
                    if (!variables.Contains(variable)) variables.Add(variable);
                }
                catch (Exception ex)
                {
                    errors.Add($"恢复已删变量「{variable.Name}」失败：{ex.Message}");
                }
            }

            foreach (var group in createdGroups)
            {
                try { solution.VariableGroups.Remove(group); }
                catch (Exception ex) { errors.Add($"移除自动建组「{group.Name}」失败：{ex.Message}"); }
            }

            return errors;
        }

        private static Guid ResolveDisplayGroupId(
            IReadOnlyList<VariableGroupDescriptor> groups, string displayGroupName)
        {
            if (string.IsNullOrWhiteSpace(displayGroupName)
                || string.Equals(displayGroupName, "未分组", StringComparison.Ordinal))
                return Guid.Empty;
            return groups.FirstOrDefault(g => string.Equals(g.Name, displayGroupName, StringComparison.OrdinalIgnoreCase))?.Id
                   ?? Guid.Empty;
        }

        private static Type? ExpectedAddressType(CommunicationType protocol) => protocol switch
        {
            CommunicationType.ModbusTcp or CommunicationType.ModbusRtu => typeof(ModbusAddress),
            CommunicationType.SiemensS7 => typeof(S7Address),
            CommunicationType.MitsubishiMc => typeof(McAddress),
            CommunicationType.OmronFins => typeof(FinsAddress),
            _ => null
        };

        private static bool AddressChanged(DeviceAddressBase? oldAddress, DeviceAddressBase newAddress)
        {
            if (oldAddress == null) return true;
            if (oldAddress.GetType() != newAddress.GetType()) return true;
            return oldAddress.DataType != newAddress.DataType
                   || !string.Equals(oldAddress.Address, newAddress.Address, StringComparison.Ordinal)
                   || oldAddress.Length != newAddress.Length
                   || oldAddress.BitOffset != newAddress.BitOffset
                   || oldAddress.ElementByteLength != newAddress.ElementByteLength
                   || oldAddress.AddressOffset != newAddress.AddressOffset
                   || oldAddress.EnableConversion != newAddress.EnableConversion
                   || oldAddress.Scale != newAddress.Scale
                   || oldAddress.EngineeringOffset != newAddress.EngineeringOffset
                   || !string.Equals(oldAddress.Unit ?? string.Empty, newAddress.Unit ?? string.Empty, StringComparison.Ordinal)
                   || (oldAddress is ModbusAddress oldModbus && newAddress is ModbusAddress newModbus
                       && (oldModbus.Station != newModbus.Station
                           || oldModbus.UseLegacyAddress != newModbus.UseLegacyAddress))
                   || (oldAddress is S7Address oldS7 && newAddress is S7Address newS7
                       && oldS7.DbNumber != newS7.DbNumber)
                   || (oldAddress is McAddress oldMc && newAddress is McAddress newMc
                       && oldMc.Vendor != newMc.Vendor);
        }

        private static List<string> DiffRows(
            Dictionary<string, object?> existingRow, Dictionary<string, object?> newRow)
        {
            var changed = new List<string>();
            foreach (var key in existingRow.Keys)
            {
                // 变量名是匹配键、不是可编辑字段（Excel 不支持改名）；地址预览是只读列。
                // 两者都不参与差异，否则大小写不同的同名行会被误判成"更新"。
                if (string.Equals(key, DeviceVariableColumns.AddressPreview, StringComparison.Ordinal)) continue;
                if (string.Equals(key, DeviceVariableColumns.Name, StringComparison.Ordinal)) continue;
                var oldText = DeviceVariableIoText.CellText(existingRow.TryGetValue(key, out var oldValue) ? oldValue : null);
                var newText = DeviceVariableIoText.CellText(newRow.TryGetValue(key, out var newValue) ? newValue : null);
                if (!string.Equals(oldText, newText, StringComparison.Ordinal))
                    changed.Add(key);
            }
            return changed;
        }

        private static string BuildDiffNote(
            IReadOnlyList<string> changed,
            Dictionary<string, object?> existingRow,
            Dictionary<string, object?> newRow)
        {
            string Text(Dictionary<string, object?> row, string key)
            {
                var text = DeviceVariableIoText.CellText(row.TryGetValue(key, out var value) ? value : null);
                return text.Length == 0 ? "（空）" : text;
            }

            var parts = changed.Take(2)
                .Select(key => $"{key} {Text(existingRow, key)} → {Text(newRow, key)}")
                .ToList();
            if (changed.Count > 2) parts.Add("…");
            return string.Join("；", parts);
        }

        /// <summary>更新前快照：只覆盖导入会改的字段，回滚时原样写回并重注册。</summary>
        private sealed class VariableSnapshot
        {
            public string Description { get; init; } = string.Empty;
            public Type DataType { get; init; } = typeof(int);
            public object? DefaultValue { get; init; }
            public object? Value { get; init; }
            public string ScanGroup { get; init; } = string.Empty;
            public bool MergeAdjacentRead { get; init; }
            public Guid GroupId { get; init; }
            public DeviceAddressBase? AddressConfig { get; init; }

            public static VariableSnapshot Capture(NetworkVariableModel variable) => new()
            {
                Description = variable.Description,
                DataType = variable.DataType,
                DefaultValue = variable.DefaultValue,
                Value = variable.Value,
                ScanGroup = variable.ScanGroup,
                MergeAdjacentRead = variable.MergeAdjacentRead,
                GroupId = variable.GroupId,
                AddressConfig = variable.AddressConfig
            };

            public void Restore(NetworkVariableModel variable)
            {
                variable.Description = Description;
                variable.DataType = DataType;
                variable.DefaultValue = DefaultValue;
                variable.ScanGroup = ScanGroup;
                variable.MergeAdjacentRead = MergeAdjacentRead;
                variable.GroupId = GroupId;
                variable.AddressConfig = AddressConfig;
                // 类型变更时成功路径会改镜像；回滚必须把镜像也还原（走 UpdateMirrorValue，不写设备）。
                variable.UpdateMirrorValue(Value);
            }
        }
    }
}
