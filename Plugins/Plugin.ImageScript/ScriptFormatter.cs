using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Plugin.ImageScript
{
    /// <summary>
    /// Halcon 脚本格式化（保守规则，只动空白不动语义）：
    /// ① 调用统一为 "op (a, b, c)"（HDevelop 风格：算子后一空格、逗号后一空格）
    /// ② 行尾空白清理；注释行(*开头)内容原样保留
    /// 不做的事：改 =/:=、调整参数顺序、换行策略——格式化绝不改变语义。
    /// </summary>
    public static class ScriptFormatter
    {
        private static readonly Regex CallLine = new Regex(
            @"^(\s*)([a-zA-Z_][a-zA-Z0-9_]*)\s*\((.*)\)\s*$",
            RegexOptions.Compiled);

        public static string Format(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;

            string[] lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i];
                string trimmed = t.Trim();

                if (trimmed.Length == 0)
                {
                    // 空行原样
                }
                else if (trimmed.StartsWith("*", StringComparison.Ordinal))
                {
                    t = t.TrimEnd(); // 注释只去行尾空白
                }
                else
                {
                    var m = CallLine.Match(t);
                    if (m.Success)
                    {
                        string indent = m.Groups[1].Value;
                        string name = m.Groups[2].Value;
                        string args = JoinArgs(SplitArgs(m.Groups[3].Value));
                        t = $"{indent}{name} ({args})";
                    }
                    else
                    {
                        t = t.TrimEnd();
                    }
                }

                if (i > 0) sb.Append("\r\n");
                sb.Append(t);
            }
            return sb.ToString();
        }

        /// <summary>按顶层逗号切分实参（括号内/引号内的逗号不分）。</summary>
        private static List<string> SplitArgs(string argText)
        {
            var list = new List<string>();
            int depth = 0;
            bool inQuote = false;
            var cur = new StringBuilder();

            foreach (char c in argText)
            {
                if (c == '\'') inQuote = !inQuote;

                if (!inQuote)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth = Math.Max(0, depth - 1);
                    else if (c == ',' && depth == 0)
                    {
                        list.Add(cur.ToString().Trim());
                        cur.Clear();
                        continue;
                    }
                }
                cur.Append(c);
            }
            list.Add(cur.ToString().Trim());
            return list;
        }

        private static string JoinArgs(List<string> args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (a.Length == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(a);
            }
            return sb.ToString();
        }
    }
}
