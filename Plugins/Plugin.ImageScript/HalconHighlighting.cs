using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 把外部插件的 Halcon 关键字/算子常量（Keyword.cs）转换为 AvalonEdit 的语法高亮定义。
    /// 运行时动态生成一份 xshd（XML），注册到全局 HighlightingManager，编辑器直接取用，
    /// 避免维护独立资源文件、并保证关键字与 Keyword.cs 单一来源同步。
    /// </summary>
    public static class HalconHighlighting
    {
        private const string DefinitionName = "Halcon";
        private static bool _registered;
        private static readonly object Lock = new object();

        /// <summary>获取 Halcon 高亮定义（首次调用时构建并注册）。</summary>
        public static IHighlightingDefinition GetDefinition()
        {
            EnsureRegistered();
            return HighlightingManager.Instance.GetDefinition(DefinitionName);
        }

        private static void EnsureRegistered()
        {
            if (_registered) return;
            lock (Lock)
            {
                if (_registered) return;

                string xshd = BuildXshd();
                using var sr = new StringReader(xshd);
                using var reader = new XmlTextReader(sr);
                // AvalonEdit 6.x：先用 HighlightingLoader 解析 xshd → 定义，再按名注册
                var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting(
                    DefinitionName, new[] { ".hdev", ".hdvp" }, definition);

                _registered = true;
            }
        }

        // 用 Keyword.cs 里的常量拼装一份 xshd 文本
        private static string BuildXshd()
        {
            var keywords = Split(Keyword.s_HalconString);          // 流程控制/内置关键字
            var operators = Split(Keyword.s_HalconProcedure);      // Halcon 算子/过程名

            var sb = new StringBuilder();
            sb.Append("<SyntaxDefinition name=\"").Append(DefinitionName)
              .Append("\" extensions=\".hdev\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");

            sb.Append("<Color name=\"Comment\" foreground=\"#008000\" />");
            sb.Append("<Color name=\"Keyword\" foreground=\"#0000FF\" fontWeight=\"bold\" />");
            sb.Append("<Color name=\"Operator\" foreground=\"#000096\" />");
            sb.Append("<Color name=\"String\" foreground=\"#A31515\" />");
            sb.Append("<Color name=\"Number\" foreground=\"#FF6532\" />");

            sb.Append("<RuleSet ignoreCase=\"true\">");

            // 字符串（单引号 / 双引号）
            sb.Append("<Span color=\"String\" begin=\"'\" end=\"'\" />");
            sb.Append("<Span color=\"String\" begin=\"&quot;\" end=\"&quot;\" />");

            // 注释：Halcon 以行首 * 作为注释符
            sb.Append("<Rule color=\"Comment\">(?m)^[ \\t]*\\*[^\\n]*</Rule>");

            // 数字
            sb.Append("<Rule color=\"Number\">\\b[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?\\b</Rule>");

            AppendKeywords(sb, "Operator", operators);
            AppendKeywords(sb, "Keyword", keywords);

            sb.Append("</RuleSet>");
            sb.Append("</SyntaxDefinition>");
            return sb.ToString();
        }

        private static void AppendKeywords(StringBuilder sb, string color, IEnumerable<string> words)
        {
            var list = words.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().ToList();
            if (list.Count == 0) return;

            sb.Append("<Keywords color=\"").Append(color).Append("\">");
            foreach (var w in list)
                sb.Append("<Word>").Append(Escape(w)).Append("</Word>");
            sb.Append("</Keywords>");
        }

        private static List<string> Split(string s) =>
            (s ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
