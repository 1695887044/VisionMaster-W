using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Plugin.HandEye.Models;

namespace Plugin.HandEye
{
    /// <summary>
    /// 从 Excel / WPS 复制的一片单元格 → 成对数据表。
    ///
    /// 为什么放在插件程序集而不是视图里
    /// ---------
    /// 剪贴板文本的处理规则（制表符分列、换行分行、先整体校验再写、超宽丢弃）是**数据规则**，
    /// 不是界面规则。放在这里，断言宿主能直接测；留在视图里就只能靠人去点。
    /// 视图那边只负责：读剪贴板、判断是不是"一片"、决定从哪一格开始、把错误/丢弃数报给用户。
    ///
    /// 三条硬规则
    /// ---------
    ///  ① **先整体校验、再写**：有一个格子不是数字就一个字都不写。写一半的代价比报错大得多 ——
    ///     半张表被改过，用户看不出哪几行是新的，而数据指纹已经变了、上次的结论已经作废。
    ///  ② **空单元格保持原值**，不当作 0。Excel 里"这一列我没量"很常见，当 0 写进去会变成
    ///     "有效但错误的样本"，比缺一行更难查。
    ///  ③ **超出 12 列的部分丢弃并报数**，不环绕到下一行续写 —— 环绕会让整片数据错位一格，
    ///     而错位后的数据看起来仍然"像"一组合法样本。
    /// </summary>
    public static class HandEyePaste
    {
        /// <summary>粘贴结果，供界面决定要不要提示。</summary>
        public readonly struct Outcome
        {
            public Outcome(int rowsWritten, int cellsWritten, int dropped)
            {
                RowsWritten = rowsWritten;
                CellsWritten = cellsWritten;
                Dropped = dropped;
            }

            /// <summary>被写入的行数（含为容纳粘贴而新加的空行）。</summary>
            public int RowsWritten { get; }

            /// <summary>被写入的格子数（空的格子不算）。</summary>
            public int CellsWritten { get; }

            /// <summary>因为超出 <see cref="HandEyeSample.ColumnCount"/> 列而丢弃的格子数。</summary>
            public int Dropped { get; }
        }

        /// <summary>
        /// 判断剪贴板文本是不是"一片"。单格纯数字不接管 —— 交给单元格编辑器，
        /// 保留它自己的校验与撤销（那里的错误提示是逐格的，比整片失败更精确）。
        /// </summary>
        public static bool IsBlock(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string trimmed = text.TrimEnd('\r', '\n');
            return trimmed.Contains('\t') || trimmed.Contains('\n') || trimmed.Contains('\r');
        }

        /// <summary>
        /// 把一片文本写到 <paramref name="plugin"/> 的成对数据表上。
        /// </summary>
        /// <param name="startRow">起点行（0 基）。不足时自动加行；为 <c>Count</c> 时接在表尾。</param>
        /// <param name="startColumn">起点列（0 基，0~11）。</param>
        /// <param name="outcome">成功时的计数。</param>
        /// <param name="error">失败原因（带行列号与原文）。</param>
        /// <returns>成功写入返回 true；校验失败返回 false 且**一个格子都没写**。</returns>
        public static bool TryApply(
            HandEyePlugin plugin,
            string? text,
            int startRow,
            int startColumn,
            out Outcome outcome,
            out string? error)
        {
            outcome = default;
            error = null;

            if (plugin is null) { error = "插件未初始化。"; return false; }

            if (!TryParseRows(text, out List<string[]> rows, out error)) return false;

            if (startRow < 0) startRow = 0;
            if (startColumn < 0 || startColumn >= HandEyeSample.ColumnCount) startColumn = 0;

            int dropped = 0;
            int rowCount = 0;
            int cellCount = 0;

            for (int r = 0; r < rows.Count; r++)
            {
                int targetRow = startRow + r;
                while (targetRow >= plugin.Samples.Count) plugin.Samples.Add(new HandEyeSample());

                HandEyeSample row = plugin.Samples[targetRow];
                double[] cells = row.ToRow();
                bool touched = false;

                for (int c = 0; c < rows[r].Length; c++)
                {
                    int targetColumn = startColumn + c;
                    if (targetColumn >= HandEyeSample.ColumnCount) { dropped++; continue; }

                    string raw = rows[r][c].Trim();
                    if (raw.Length == 0) continue;   // 空 = 保持原值

                    cells[targetColumn] = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                    touched = true;
                    cellCount++;
                }

                // SetRow 一次性赋值并统一发通知：走 12 个 setter 会推 12 次"预览作废"，
                // 每次都要重算一遍数据指纹。
                if (touched)
                {
                    row.SetRow(cells);
                    rowCount++;
                }
            }

            outcome = new Outcome(rowCount, cellCount, dropped);
            return true;
        }

        /// <summary>
        /// 文本 → 行/格。表头与空行跳过（Excel 复制时末尾常多一个空行），
        /// 除数字外的任何内容都算错 —— **整片拒绝**，不是逐格跳过。
        /// </summary>
        public static bool TryParseRows(string? text, out List<string[]> rows, out string? error)
        {
            rows = new List<string[]>();
            error = null;

            if (string.IsNullOrEmpty(text)) { error = "剪贴板里没有文本。"; return false; }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;

                string[] cells = line.Split('\t');
                for (int c = 0; c < cells.Length; c++)
                {
                    string raw = cells[c].Trim();
                    if (raw.Length == 0) continue;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        error = $"第 {i + 1} 行第 {c + 1} 列不是数字：「{raw}」。\n这次粘贴一个字都没写进去。";
                        rows.Clear();
                        return false;
                    }
                }

                rows.Add(cells);
            }

            if (rows.Count == 0) { error = "剪贴板里没有可用的数据行。"; return false; }
            return true;
        }

        /// <summary>把错误/丢弃数拼成一句人话（视图直接弹这一句）。</summary>
        public static string DescribeOutcome(Outcome outcome)
        {
            var text = new StringBuilder();
            text.Append($"粘贴完成：写入 {outcome.RowsWritten} 行 {outcome.CellsWritten} 个数值");

            if (outcome.Dropped > 0)
            {
                text.Append($"；右侧多出来的 {outcome.Dropped} 个格子没有写入（本表固定 ")
                    .Append(HandEyeSample.ColumnCount)
                    .Append(" 列）");
            }

            return text.Append('。').ToString();
        }
    }
}
