using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.CodeReader
{
    /// <summary>码所属的 HALCON 家族。两个家族的模型构造、结果寻址方式完全不同（见各 Engine 的注释）</summary>
    public enum CodeFamily
    {
        /// <summary>一维条码：create_bar_code_model + find_bar_code</summary>
        BarCode1D,

        /// <summary>二维码：create_data_code_2d_model + find_data_code_2d</summary>
        DataCode2D,
    }

    /// <summary>
    /// 一种码制。<see cref="HalconName"/> 是喂给 HALCON 的**原始字符串**，拼错会被 HALCON 直接报错。
    /// </summary>
    public sealed class CodeSymbology
    {
        public required string DisplayName { get; init; }

        public required CodeFamily Family { get; init; }

        /// <summary>
        /// HALCON 侧的原始名：
        ///   一维 —— find_bar_code 的 CodeType 参数（'auto' 表示让 HALCON 自动试）
        ///   二维 —— create_data_code_2d_model 的模型类型（**一个模型只能是一种码制**）
        ///
        /// 存盘存的也是这个值（而不是 DisplayName）：它是稳定身份，改中文标签不会让存量图纸失效。
        /// 拼错或填了表里没有的值时**报明确错误**，不静默退回默认码制 ——
        /// 静默退回会让现场以为在用 A 码制、实际按 B 在找，表现只是"读不到码"，无从排查。
        /// </summary>
        public required string HalconName { get; init; }
    }

    /// <summary>
    /// 码制表。一维与二维码放在同一张表里 —— 对现场来说"读码"是一个动作，
    /// 不该先判断"我这是条码还是二维码"再挑节点（选错节点只会拿到看不懂的报错）。
    /// </summary>
    public static class CodeSymbologyTable
    {
        public static IReadOnlyList<CodeSymbology> All { get; } = new List<CodeSymbology>
        {
            // ---- 一维：CodeType 传给 find_bar_code ----
            // 'auto' 放在首位是想让它容易被看到，但它不是默认值：
            // auto 会在图里逐个码制试，节拍明显更慢，且码制混杂时可能给出意外的匹配。
            new() { DisplayName = "一维·自动识别码制（较慢）", Family = CodeFamily.BarCode1D, HalconName = "auto" },
            new() { DisplayName = "Code 128", Family = CodeFamily.BarCode1D, HalconName = "Code 128" },
            new() { DisplayName = "Code 39", Family = CodeFamily.BarCode1D, HalconName = "Code 39" },
            new() { DisplayName = "Code 93", Family = CodeFamily.BarCode1D, HalconName = "Code 93" },
            new() { DisplayName = "GS1-128", Family = CodeFamily.BarCode1D, HalconName = "GS1-128" },
            new() { DisplayName = "EAN-13", Family = CodeFamily.BarCode1D, HalconName = "EAN-13" },
            new() { DisplayName = "EAN-8", Family = CodeFamily.BarCode1D, HalconName = "EAN-8" },
            new() { DisplayName = "UPC-A", Family = CodeFamily.BarCode1D, HalconName = "UPC-A" },
            new() { DisplayName = "UPC-E", Family = CodeFamily.BarCode1D, HalconName = "UPC-E" },
            new() { DisplayName = "2/5 Interleaved（交插二五码）", Family = CodeFamily.BarCode1D, HalconName = "2/5 Interleaved" },
            new() { DisplayName = "Codabar", Family = CodeFamily.BarCode1D, HalconName = "Codabar" },
            new() { DisplayName = "Code 32（意大利医药码）", Family = CodeFamily.BarCode1D, HalconName = "Code 32" },
            new() { DisplayName = "MSI", Family = CodeFamily.BarCode1D, HalconName = "MSI" },
            new() { DisplayName = "PharmaCode", Family = CodeFamily.BarCode1D, HalconName = "PharmaCode" },

            // ---- 二维：模型类型传给 create_data_code_2d_model ----
            // 工业追溯里 Data Matrix 最常见（激光 DPM 打码基本都是它），所以它是默认值。
            // GS1 系列不是独立的模型类型：GS1 DataMatrix / GS1 QR 用的是同一个模型，
            // 只是数据内容遵循 GS1 规则，不需要单独列条目。
            new() { DisplayName = "Data Matrix ECC 200", Family = CodeFamily.DataCode2D, HalconName = "Data Matrix ECC 200" },
            new() { DisplayName = "QR Code", Family = CodeFamily.DataCode2D, HalconName = "QR Code" },
            new() { DisplayName = "Micro QR Code", Family = CodeFamily.DataCode2D, HalconName = "Micro QR Code" },
            new() { DisplayName = "PDF417", Family = CodeFamily.DataCode2D, HalconName = "PDF417" },
            new() { DisplayName = "Aztec Code", Family = CodeFamily.DataCode2D, HalconName = "Aztec Code" },
            new() { DisplayName = "DotCode", Family = CodeFamily.DataCode2D, HalconName = "DotCode" },
        };

        /// <summary>默认码制：Data Matrix ECC 200（工业追溯最常见）</summary>
        public static CodeSymbology Default => All.First(s => s.HalconName == "Data Matrix ECC 200");

        /// <summary>
        /// 按存盘值（HalconName）查表。查不到返回 false，由调用方报明确错误 —— 不静默退回默认。
        /// </summary>
        public static bool TryGet(string? halconName, out CodeSymbology symbology)
        {
            symbology = All.FirstOrDefault(s =>
                string.Equals(s.HalconName, halconName, StringComparison.Ordinal))!;
            return symbology != null;
        }

        /// <summary>给错误提示用：把可选值列出来，现场看一眼就知道该填什么</summary>
        public static string DescribeValidNames() =>
            string.Join(" / ", All.Select(s => s.HalconName));
    }
}
