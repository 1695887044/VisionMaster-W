#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LogViewProbe
{
    /// <summary>
    /// 受控的"数值框 → ui:NumericBox"迁移器（2026-10-10 加）。
    ///
    /// 【为什么写工具而不是手工改】剩余 4 个插件视图共 37 处，块形状有三种（带规则的、
    /// 不带规则的、属性跨行的），手工改一处错一处；上次 BlobDetect 的"全字段变红"就是手工迁移的疏漏。
    ///
    /// 【为什么不是 PowerShell 正则】仓库纪律 R3 禁止用 PowerShell 往返编辑源文件（无 BOM UTF-8 会被按
    /// ANSI 读、按另一编码写回 → 静默损坏）。这里显式用 UTF-8 严格解码读、原样保留 BOM 写，
    /// 且**只改匹配到的块**，任何一处对不上就整篇不写、并打印出来。
    ///
    /// 【为什么要有允许清单】样式名叫 PluginNumericBox，但**不代表绑的就一定是数值** ——
    /// PoseTransform 那唯一一处绑的是 PreviewImagePath（图片路径）。只看样式名迁移会把路径框变成数值框。
    /// 所以：`--scan` 先列出每一处的绑定路径，人来判定哪些是数值，写进允许清单；`--apply` 只动清单里的。
    ///
    /// 用法：
    ///   LogViewProbe.exe --scan-numeric    列出所有 PluginNumericBox 用法（文件/行号/绑定路径/规则区间）
    ///   LogViewProbe.exe --apply-numeric   按允许清单迁移（清单：_LogViewProbe\numeric-migration-allowlist.txt）
    /// </summary>
    internal static class NumericBoxMigrator
    {
        private const string RepoRoot = @"D:\C#\VM\";
        private static readonly string AllowListPath = RepoRoot + @"_LogViewProbe\numeric-migration-allowlist.txt";

        private static readonly string[] Targets =
        {
            @"Plugins\Plugin.Matching\MatchingView.xaml",
            @"Plugins\Plugin.Calibration\CalibrationView.xaml",
            @"Plugins\Plugin.CaliperMeasure\CaliperMeasureView.xaml",
            @"Plugins\Plugin.PoseTransform\PoseTransformView.xaml",
            @"Plugins\Plugin.BlobDetect\BlobDetectView.xaml",
        };

        private sealed class Block
        {
            public string File;
            public int Start;
            public int End;
            public int Line;
            public string Path;
            public string Min;
            public string Max;
            public bool HasRule;
            public string Original;
            public string Replacement;
        }

        public static int Run(string mode)
        {
            bool apply = mode == "--apply-numeric";
            var allow = apply ? LoadAllowList() : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            int converted = 0, skipped = 0, unmatched = 0;

            foreach (var relative in Targets)
            {
                string full = Path.Combine(RepoRoot, relative);
                if (!File.Exists(full)) continue;

                // 严格 UTF-8 读（不猜 ANSI），并记住原文件有没有 BOM —— 写回时保持一致
                byte[] bytes = File.ReadAllBytes(full);
                bool hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string text = new UTF8Encoding(false, true).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));

                var blocks = Scan(text, relative);
                if (blocks.Count == 0) continue;

                Console.WriteLine($"---- {relative}（共 {blocks.Count} 处 PluginNumericBox）");

                var newText = new StringBuilder(text);
                foreach (var block in blocks.Reverse<Block>())
                {
                    string key = relative + "|" + block.Path;
                    bool inList = allow.TryGetValue(key, out bool integerOnly);
                    bool numeric = apply ? inList : true;

                    if (apply && !numeric)
                    {
                        skipped++;
                        Console.WriteLine($"   跳过  :{block.Line}  {block.Path}（不在允许清单里）");
                        continue;
                    }

                    if (apply && block.Replacement == null)
                    {
                        unmatched++;
                        Console.WriteLine($"   跳过  :{block.Line}  {block.Path}（块形状不认识，需手工处理）");
                        continue;
                    }

                    Console.WriteLine($"   {(apply ? "迁移" : "命中")}  :{block.Line}  {block.Path}"
                                      + (block.HasRule ? $"  区间=[{block.Min},{block.Max}]" : "  （无校验规则）")
                                      + (apply && integerOnly ? "  整数位=0" : string.Empty));

                    if (apply)
                    {
                        // 整数位：只插在**开头标签**的元素名后面（用 lookahead 避免命中 <ui:NumericBox.Value> —— 上一版
                        // 直接 Replace("<ui:NumericBox", …) 就把 <ui:NumericBox.Value> 改成了
                        // <ui:NumericBox DecimalPlaces="0".Value>，构建报 MC3000）
                        string replacement = block.Replacement;
                        if (integerOnly)
                            replacement = Regex.Replace(replacement, "<ui:NumericBox(?=[\\s>])",
                                "<ui:NumericBox DecimalPlaces=\"0\"");

                        newText.Remove(block.Start, block.End - block.Start);
                        newText.Insert(block.Start, replacement);
                        converted++;
                    }
                }

                if (apply)
                {
                    string result = newText.ToString();
                    // 需要时补 xmlns:ui（Matching / CaliperMeasure 没有这个前缀）
                    if (!result.Contains("xmlns:ui="))
                    {
                        int anchor = result.IndexOf("xmlns:local=", StringComparison.Ordinal);
                        int lineEnd = anchor >= 0 ? result.IndexOf('\n', anchor) : -1;
                        if (lineEnd > 0)
                        {
                            string indent = new string(' ', 4);
                            result = result.Insert(lineEnd + 1, indent + "xmlns:ui=\"clr-namespace:UI.CustomControl;assembly=UI\"\n");
                            Console.WriteLine("   + 补 xmlns:ui");
                        }
                    }

                    File.WriteAllText(full, result, new UTF8Encoding(hadBom));
                }
            }

            Console.WriteLine(apply
                ? $"==== 迁移完成：改写 {converted} 处，跳过 {skipped} 处（非数值），形状不认识 {unmatched} 处"
                : "==== 以上为扫描结果（未改动任何文件）");
            return unmatched == 0 ? 0 : 1;
        }

        private static Dictionary<string, bool> LoadAllowList()
        {
            var set = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(AllowListPath))
            {
                Console.WriteLine("### 找不到允许清单：" + AllowListPath);
                return set;
            }
            foreach (var line in File.ReadAllLines(AllowListPath, new UTF8Encoding(false, true)))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                var parts = trimmed.Split('|');
                if (parts.Length < 2) continue;
                // 第三段写 int 表示"整数参数"（生成 DecimalPlaces="0"）
                set[parts[0] + "|" + parts[1]] =
                    parts.Length > 2 && parts[2].Trim().Equals("int", StringComparison.OrdinalIgnoreCase);
            }
            return set;
        }

        /// <summary>扫描所有 &lt;TextBox …&gt;…&lt;/TextBox&gt; 块，解析出绑定路径与规则区间</summary>
        private static List<Block> Scan(string text, string relative)
        {
            var list = new List<Block>();
            int index = 0;

            while (true)
            {
                index = text.IndexOf("<TextBox", index, StringComparison.Ordinal);
                if (index < 0) break;

                int afterName = index + "<TextBox".Length;
                if (afterName >= text.Length || !(char.IsWhiteSpace(text[afterName]) || text[afterName] == '>'))
                {
                    index = afterName;
                    continue;
                }

                string startTag = ReadTag(text, index, out int tagEnd);   // index..tagEnd（含 '>'）
                if (startTag == null) { index = afterName; continue; }

                bool selfClosing = startTag.TrimEnd().EndsWith("/>");
                int blockEnd;
                string inner = string.Empty;

                if (selfClosing)
                {
                    blockEnd = tagEnd;
                }
                else
                {
                    int close = text.IndexOf("</TextBox>", tagEnd, StringComparison.Ordinal);
                    if (close < 0) { index = afterName; continue; }
                    blockEnd = close + "</TextBox>".Length;
                    inner = text.Substring(tagEnd, close - tagEnd);
                }

                var block = new Block
                {
                    File = relative,
                    Start = index,
                    End = blockEnd,
                    Line = CountLines(text, index),
                    Original = text.Substring(index, blockEnd - index),
                };

                // 只认套了 PluginNumericBox 样式的
                if (!startTag.Contains("PluginNumericBox"))
                {
                    index = blockEnd;
                    continue;
                }

                // 绑定路径：属性形式的 Text="{Binding X}" 或子元素形式的 <TextBox.Text><Binding Path="X" …/>
                var textAttr = Regex.Match(startTag, "Text=\"\\{Binding\\s+(?<path>[A-Za-z_][\\w.]*)");
                if (textAttr.Success)
                {
                    block.Path = textAttr.Groups["path"].Value;
                    block.Replacement = BuildFromTextAttribute(block, startTag);
                }
                else
                {
                    var binding = Regex.Match(inner, "<Binding\\s+Path=\"(?<path>[^\"]+)\"");
                    if (!binding.Success)
                    {
                        block.Path = "(未识别)";
                    }
                    else
                    {
                        block.Path = binding.Groups["path"].Value;
                        var rule = Regex.Match(inner, "NumberValidationRule[^>]*");
                        if (rule.Success)
                        {
                            block.HasRule = true;
                            block.Min = Regex.Match(rule.Value, "Min=\"([^\"]*)\"").Groups[1].Value;
                            block.Max = Regex.Match(rule.Value, "Max=\"([^\"]*)\"").Groups[1].Value;
                        }
                        block.Replacement = BuildFromBindingElement(block, startTag, inner);
                    }
                }

                list.Add(block);
                index = blockEnd;
            }

            return list;
        }

        /// <summary>属性形式：&lt;TextBox … Text="{Binding X}" …/&gt; → &lt;ui:NumericBox … Value="{Binding X}" …/&gt;</summary>
        private static string BuildFromTextAttribute(Block block, string startTag)
        {
            var replaced = Regex.Replace(startTag, "Text=\"\\{Binding\\s", "Value=\"{Binding ");
            replaced = Regex.Replace(replaced, "Text=\"(?=\\{Binding)", "Value=\"");
            replaced = replaced.Replace("<TextBox", "<ui:NumericBox");
            replaced = replaced.Replace("</TextBox>", "</ui:NumericBox>");
            return replaced;
        }

        /// <summary>子元素形式：&lt;TextBox.Text&gt;&lt;Binding …&gt;… 原样搬进 &lt;ui:NumericBox.Value&gt;，并补区间属性</summary>
        private static string BuildFromBindingElement(Block block, string startTag, string inner)
        {
            string indent = GetIndent(block.Original);

            // 开头标签：**原样保留属性排版**，只把元素名换掉，并在结尾 '>' 之前补上需要的新属性。
            // （2026-10-10 教训：上一版在这里做"去重复 Style + 截断结尾"的正则手术，
            //   结果把整行开头吃掉了 —— 生成的 XAML 丢了元素名，构建直接报 MC3015。
            //   凡是能"只替换元素名 + 在 '>' 前插入属性"做到的，就不要去裁剪/重建整行。）
            string head = startTag.Substring(0, startTag.Length - 1).Replace("<TextBox", "<ui:NumericBox");
            var extra = new StringBuilder();
            if (!startTag.Contains("Style="))
                extra.Append('\n').Append(indent).Append("    Style=\"{StaticResource PluginNumericBox}\"");
            if (block.HasRule && !string.IsNullOrEmpty(block.Min))
                extra.Append('\n').Append(indent).Append("    Minimum=\"").Append(block.Min).Append('"');
            if (block.HasRule && !string.IsNullOrEmpty(block.Max))
                extra.Append('\n').Append(indent).Append("    Maximum=\"").Append(block.Max).Append('"');

            // 取 <Binding …>…</Binding> 整段（规则原样保留：规则已被修成"字符串与数值都认"）
            int bStart = inner.IndexOf("<Binding", StringComparison.Ordinal);
            int bEnd = inner.IndexOf("</Binding>", StringComparison.Ordinal);
            if (bStart < 0 || bEnd < 0) return null;
            string binding = inner.Substring(bStart, bEnd + "</Binding>".Length - bStart);
            string bindingIndented = ReIndent(binding, indent + "        ");

            var sb = new StringBuilder();
            sb.Append(head).Append(extra).Append(">\n");
            sb.Append(indent).Append("    <ui:NumericBox.Value>\n");
            sb.Append(bindingIndented).Append('\n');
            sb.Append(indent).Append("    </ui:NumericBox.Value>\n");
            sb.Append(indent).Append("</ui:NumericBox>");
            return sb.ToString();
        }

        private static string ReIndent(string block, string targetIndent)
        {
            var lines = block.Replace("\r\n", "\n").Split('\n');
            int common = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(l => l.Trim().Length == 0 ? l : targetIndent + l.Substring(common)));
        }

        private static string GetIndent(string text)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, text.Length - 1)) + 1;
            int i = 0;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return text.Substring(0, i);
        }

        private static string ReadTag(string text, int start, out int end)
        {
            end = -1;
            bool inQuote = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') inQuote = !inQuote;
                else if (c == '>' && !inQuote) { end = i + 1; return text.Substring(start, i + 1 - start); }
            }
            return null;
        }

        private static int CountLines(string text, int position)
            => text.Take(position).Count(c => c == '\n') + 1;
    }
}
