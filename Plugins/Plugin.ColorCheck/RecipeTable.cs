using System;
using System.Collections.Generic;
using System.Linq;
using Core.Halcon.Color;

namespace Plugin.ColorCheck
{
    /// <summary>一条配方：产品名 + 期望的颜色序列</summary>
    internal sealed class Recipe
    {
        internal Recipe(string name, string[] colors)
        {
            Name = name;
            Colors = colors;
        }

        internal string Name { get; }
        internal string[] Colors { get; }

        /// <summary>芯数：期望序列的长度就是这条配方要求几根</summary>
        internal int WireCount => Colors.Length;
    }

    /// <summary>
    /// 配方表的解析与匹配。
    ///
    /// 为什么配方用「一段多行文本」而不是一个表格控件
    /// ---------
    /// ① 文本随方案落盘最稳（`[StepConfig]` 只认简单类型，集合要另写序列化）；
    /// ② 现场可以直接从工艺文件粘贴过来；
    /// ③ 这一刀的重点是配方与投射本身，不该把工程量花在表格的增删/失焦提交上。
    /// 手打颜色词容易写错（词表只有黑/白/灰/紫/蓝/绿/黄/红/玫红/橙），
    /// 所以界面上另配了「把试算结果存成配方」按钮 —— 拿一张已知良品图点一下就能生成一条配方。
    ///
    /// 文本格式（一行一条）：
    ///     产品名 = 颜色1,颜色2,颜色3
    ///     # 井号开头是注释；空行忽略
    ///     不带等号的行按「整行就是颜色序列」处理，产品名自动取「N 芯」
    /// 期望序列里可以写 `*` 表示「这一位不检」。
    /// </summary>
    internal static class RecipeTable
    {
        /// <summary>通配符：这一位不参与比对（与内核的颜色词表同一处定义）</summary>
        internal const string AnyColor = ColorVocabulary.Any;

        /// <summary>
        /// 解析配方文本。解析失败（例如某一行只有产品名、没有颜色）时返回 null 并给出原因。
        /// </summary>
        internal static List<Recipe>? Parse(string? text, out string error)
        {
            error = string.Empty;
            var recipes = new List<Recipe>();

            if (string.IsNullOrWhiteSpace(text)) return recipes;

            var lines = text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                string name;
                string colorPart;
                int equals = line.IndexOf('=');
                if (equals >= 0)
                {
                    name = line.Substring(0, equals).Trim();
                    colorPart = line.Substring(equals + 1);
                }
                else
                {
                    name = string.Empty;
                    colorPart = line;
                }

                var colors = colorPart
                    .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .ToArray();

                if (colors.Length == 0)
                {
                    error = $"配方表第 {i + 1} 行「{line}」里没有颜色：格式是「产品名 = 颜色1,颜色2,...」";
                    return null;
                }

                if (name.Length == 0) name = $"{colors.Length} 芯";
                recipes.Add(new Recipe(name, colors));
            }

            return recipes;
        }

        /// <summary>
        /// 按实测根数挑配方：期望序列的长度就是这条配方要求几根。
        /// 多条同长度时取第一条并留一句提示 —— 不静默乱挑，也不因为撞车就整条流程失败。
        /// </summary>
        internal static Recipe? MatchByWireCount(List<Recipe> recipes, int wireCount, List<string> warnings)
        {
            var hits = recipes.Where(r => r.WireCount == wireCount).ToList();
            if (hits.Count == 0) return null;

            if (hits.Count > 1)
            {
                warnings.Add($"配方表里有 {hits.Count} 条都是 {wireCount} 芯（{string.Join("、", hits.Select(h => h.Name))}），"
                           + $"本次取第一条「{hits[0].Name}」；要明确指定请用「期望序列」输入端口");
            }

            return hits[0];
        }

        /// <summary>把一段颜色序列文本切成颜色词（用于「期望序列」输入端口）</summary>
        internal static string[] SplitColors(string? text)
            => string.IsNullOrWhiteSpace(text)
                ? Array.Empty<string>()
                : text!.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(c => c.Trim())
                       .Where(c => c.Length > 0)
                       .ToArray();

        /// <summary>
        /// 逐位比对。`*` 通配位跳过；长度不符直接判不合格（BadIndex 给 0，因为不是"第几位"的问题）。
        /// BadIndex 从 1 开始计数，0 表示没有不符的位。
        /// </summary>
        internal static bool Compare(string[] actual, string[] expected, out int badIndex)
        {
            badIndex = 0;
            if (actual.Length != expected.Length) return false;

            for (int i = 0; i < actual.Length; i++)
            {
                if (expected[i] == AnyColor) continue;
                if (expected[i] != actual[i]) { badIndex = i + 1; return false; }
            }

            return true;
        }
    }
}
