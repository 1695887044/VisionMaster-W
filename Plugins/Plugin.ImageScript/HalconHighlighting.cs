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

        // 动态接口变量定义缓存（变量集合不变则复用）
        private static string _varsKey;
        private static IHighlightingDefinition _defWithVars;

        /// <summary>
        /// 获取高亮定义。传入 variableNames 时返回"算子+接口变量"增强版
        /// （接口变量用紫色 Variable 色高亮，区别于算子棕黄）。
        /// </summary>
        public static IHighlightingDefinition GetDefinition(IEnumerable<string> variableNames = null)
        {
            EnsureRegistered();
            var baseDef = HighlightingManager.Instance.GetDefinition(DefinitionName);

            var vars = (variableNames ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            if (vars.Count == 0)
                return baseDef;

            string key = string.Join("|", vars);
            if (key == _varsKey && _defWithVars != null)
                return _defWithVars;

            string xshd = BuildXshd(vars);
            using var sr = new StringReader(xshd);
            using var reader = new XmlTextReader(sr);
            // 不注册到全局管理器，仅作为对象赋给编辑器（避免同名注册冲突）
            _defWithVars = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            _varsKey = key;
            return _defWithVars;
        }

        private static void EnsureRegistered()
        {
            if (_registered) return;
            lock (Lock)
            {
                if (_registered) return;

                string xshd = BuildXshd(null);
                using var sr = new StringReader(xshd);
                using var reader = new XmlTextReader(sr);
                // AvalonEdit 6.x：先用 HighlightingLoader 解析 xshd → 定义，再按名注册
                var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting(
                    DefinitionName, new[] { ".hdev", ".hdvp" }, definition);

                _registered = true;
            }
        }

        // 用 Keyword.cs 里的常量拼装一份 xshd 文本（vars=当前过程接口变量名，可空）
        private static string BuildXshd(List<string> vars)
        {
            var keywords = Split(Keyword.s_HalconString);          // 流程控制/内置关键字
            var operators = Split(Keyword.s_HalconProcedure);      // Halcon 算子/过程名

            var sb = new StringBuilder();
            sb.Append("<SyntaxDefinition name=\"").Append(DefinitionName)
              .Append("\" extensions=\".hdev\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");

            // 配色对齐 VS Light 主题：算子(函数色)/关键字/字符串/数字/注释 高对比区分
            sb.Append("<Color name=\"Comment\" foreground=\"#008000\" fontStyle=\"italic\" />");
            sb.Append("<Color name=\"Keyword\" foreground=\"#0000FF\" fontWeight=\"bold\" />");
            sb.Append("<Color name=\"Operator\" foreground=\"#795E26\" />");
            sb.Append("<Color name=\"String\" foreground=\"#A31515\" />");
            sb.Append("<Color name=\"Number\" foreground=\"#098658\" />");
            sb.Append("<Color name=\"Variable\" foreground=\"#7E0080\" />");

            sb.Append("<RuleSet ignoreCase=\"true\">");

            // 字符串（单引号 / 双引号）
            sb.Append("<Span color=\"String\" begin=\"'\" end=\"'\" />");
            sb.Append("<Span color=\"String\" begin=\"&quot;\" end=\"&quot;\" />");

            // 注释：Halcon 以行首 * 作为注释符
            sb.Append("<Rule color=\"Comment\">(?m)^[ \\t]*\\*[^\\n]*</Rule>");

            // 数字
            sb.Append("<Rule color=\"Number\">\\b[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?\\b</Rule>");

            // 接口变量优先（排在算子前，防止与算子同名时被算子规则吞掉）
            if (vars != null && vars.Count > 0)
                AppendKeywords(sb, "Variable", vars);

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
