using Plugin.HandEye.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Plugin.HandEye
{
    /// <summary>
    /// 成对数据的 CSV 序列化/反序列化：**只做文本 ↔ 行的转换，不碰文件**。
    ///
    /// 为什么不在这里读写文件
    /// ---------
    /// 数据的家是步骤配置里的 <see cref="HandEyeSample"/> 列表；CSV 是导入/导出的扩展功能，
    /// 由视图负责选文件、读字节、写字节（含编码/对话框/覆盖确认）。
    /// 把文件 IO 留在这里会让"求解"路径又变回依赖文件系统 —— 正是这一版要拆掉的东西。
    ///
    /// CSV 契约（12 列，无引号、无逗号转义；数值只允许 InvariantCulture 的小数点写法）
    /// ---------
    /// robotX,robotY,robotZ,robotRx,robotRy,robotRz,camX,camY,camZ,camRx,camRy,camRz
    /// · 位置单位 mm，角度单位**度**，姿态按 **ZYX 欧拉**（与 <see cref="HandEyeMath.EulerZyxDegToMat3"/> 一致）；
    /// · 首行可写表头（首列不是数字即视为表头，最多跳过前 3 行）；
    /// · 空行与 `#` 开头的行忽略；
    /// · 导出带表头，导回来会被自动跳过 —— 导出→导入往返不丢行。
    /// </summary>
    public static class HandEyeCsv
    {
        /// <summary>列数（固定 12）</summary>
        public const int ColumnCount = 12;

        /// <summary>表头行（导出用；导入时靠"首列不是数字"识别，不依赖这段文字）</summary>
        public const string Header =
            "robotX,robotY,robotZ,robotRx,robotRy,robotRz,camX,camY,camZ,camRx,camRy,camRz";

        /// <summary>
        /// 解析。**逐行给出行号与原始文本**：现场最常见的错是"某一行少一列"或"角度单位混用"，
        /// 只说"格式错误"会让人从头数到尾。
        /// </summary>
        public static bool TryParse(
            IEnumerable<string> lines,
            out List<HandEyeSample> rows,
            out string? error)
        {
            rows = new List<HandEyeSample>();
            error = null;

            int lineNo = 0;
            int skipped = 0;
            foreach (string raw in lines)
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] cells = line.Split(',');
                if (cells.Length != ColumnCount)
                {
                    // 表头（首列不是数字）跳过；其余按错误报
                    if (rows.Count == 0 && skipped < 3 && !LooksNumeric(cells[0]))
                    {
                        skipped++;
                        continue;
                    }

                    error = $"第 {lineNo} 行有 {cells.Length} 列，应为 {ColumnCount} 列：{line}";
                    return false;
                }

                var v = new double[ColumnCount];
                for (int c = 0; c < ColumnCount; c++)
                {
                    if (!double.TryParse(cells[c].Trim(), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out v[c]))
                    {
                        if (rows.Count == 0 && c == 0 && skipped < 3)
                            break;   // 表头

                        error = $"第 {lineNo} 行第 {c + 1} 列不是数字：「{cells[c].Trim()}」";
                        return false;
                    }

                    // "NaN"/"Infinity" 能被 TryParse 接受，但不是合法位姿：
                    // 放进去会在求解时抛 ArithmeticException 或落成误导性的"矩阵奇异"。
                    if (!double.IsFinite(v[c]))
                    {
                        error = $"第 {lineNo} 行第 {c + 1} 列不是有限数：「{cells[c].Trim()}」（NaN/Infinity 不能作为位姿）";
                        return false;
                    }
                }

                if (rows.Count == 0 && skipped < 3 && !LooksNumeric(cells[0]))
                {
                    skipped++;
                    continue;
                }

                var row = new HandEyeSample();
                row.SetRow(v);
                rows.Add(row);
            }

            if (rows.Count == 0)
            {
                error = "CSV 里没有有效数据行（应为 12 列，空行与 # 开头的行会被忽略）";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 序列化为 CSV 文本（含表头，`\r\n` 行尾）。
        /// 数值用 `R` 往返格式 —— 现场核对时少一位小数看起来像"导出丢了精度"，
        /// 而丢精度会让导出→导入之后的解与屏幕上看到的解不一致。
        /// </summary>
        public static string Format(IEnumerable<HandEyeSample> rows)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");

            foreach (var r in rows)
            {
                double[] v = r.ToRow();
                for (int i = 0; i < v.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(v[i].ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        private static bool LooksNumeric(string text)
            => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
