using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// C# 脚本语法高亮（仿 HalconHighlighting 范式：运行时动态生成 xshd，不维护资源文件）。
    /// 相比 AvalonEdit 内置 "C#" 定义的增强：
    /// 1) Context API 方法名棕黄着色（与 ImageScript 算子色一致，凸显"脚本可调用 API"）；
    /// 2) 常见类型名青色（VS Light 风格，内置定义无类型着色）；
    /// 3) 接口变量（输入/输出变量表）动态并入紫色高亮；
    /// 4) 控制流关键字紫色，层次感强于纯蓝。
    /// </summary>
    public static class CSharpHighlighting
    {
        private const string DefinitionName = "CSharpScript";
        private static bool _registered;
        private static readonly object Lock = new object();

        // 动态接口变量定义缓存（变量集合不变则复用）
        private static string _varsKey;
        private static IHighlightingDefinition _defWithVars;

        // ScriptContext 门面 API（与 ScriptContext.cs 同步维护）：棕黄
        private static readonly string[] ContextApi =
        {
            "Context", "GetInput", "SetOutput", "GetVar", "SetVar", "Vars",
            "Info", "Warn", "Error", "Success", "ShowImage", "Fail",
        };

        // 脚本中常见的类型/静态类：青色（小写内置类型走关键字规则）
        private static readonly string[] KnownTypes =
        {
            "HImage", "HObject", "HRegion", "HXld", "HTuple", "HalconDotNet",
            "String", "Int32", "Int64", "Double", "Single", "Boolean", "Decimal", "Object",
            "Exception", "Console", "Math", "Convert", "DateTime", "TimeSpan",
            "List", "Dictionary", "HashSet", "Queue", "Stack",
            "IEnumerable", "IEnumerator", "KeyValuePair",
            "Task", "Action", "Func", "Tuple", "Guid", "Random",
            "StringBuilder", "Regex", "BitConverter", "Path", "File", "Directory", "Debug", "Stopwatch",
        };

        // 控制流关键字：紫色（VS 2019+ 主题风格）
        private const string ControlKeywords =
            "break case catch continue default do else finally for foreach goto " +
            "if lock return switch throw try when while yield";

        // 其余 C# 关键字（含内置类型）：蓝色
        private const string OtherKeywords =
            "abstract as async await base bool byte checked char class const decimal " +
            "delegate double enum event explicit extern false fixed float get implicit " +
            "in int interface internal is long namespace new null object operator out " +
            "override params private protected public readonly ref sbyte sealed set " +
            "short sizeof stackalloc static string struct this true typeof uint ulong " +
            "unchecked unsafe ushort using value var virtual void volatile where";

        /// <summary>
        /// 获取高亮定义。传入 variableNames 时返回"API+接口变量"增强版
        /// （接口变量用紫色 Variable 色高亮）。
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
                var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting(
                    DefinitionName, new[] { ".csx" }, definition);

                _registered = true;
            }
        }

        // 拼装 xshd 文本（vars=接口变量名列表，可空）
        private static string BuildXshd(List<string> vars)
        {
            var sb = new StringBuilder();
            sb.Append("<SyntaxDefinition name=\"").Append(DefinitionName)
              .Append("\" extensions=\".csx\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");

            // 配色对齐 VS Light 主题，与 ImageScript 视觉语言统一
            sb.Append("<Color name=\"Comment\" foreground=\"#008000\" fontStyle=\"italic\" />");
            sb.Append("<Color name=\"Keyword\" foreground=\"#0000FF\" fontWeight=\"bold\" />");
            sb.Append("<Color name=\"Control\" foreground=\"#871098\" />");
            sb.Append("<Color name=\"Type\" foreground=\"#2B91AF\" />");
            sb.Append("<Color name=\"Api\" foreground=\"#795E26\" />");
            sb.Append("<Color name=\"String\" foreground=\"#A31515\" />");
            sb.Append("<Color name=\"Number\" foreground=\"#098658\" />");
            sb.Append("<Color name=\"Variable\" foreground=\"#7E0080\" />");
            sb.Append("<Color name=\"Preprocessor\" foreground=\"#808080\" />");

            // C# 大小写敏感
            sb.Append("<RuleSet>");

            // 字符串：普通 / 逐字(@"") / 字符；转义序列规则防止 \" 提前收尾
            sb.Append("<Span color=\"String\" begin=\"&quot;\" end=\"&quot;\">");
            sb.Append("<Rule color=\"String\">\\\\.</Rule>");
            sb.Append("</Span>");
            sb.Append("<Span color=\"String\" multiline=\"true\" begin=\"@&quot;\" end=\"&quot;\">");
            sb.Append("<Rule color=\"String\">&quot;&quot;</Rule>");
            sb.Append("</Span>");
            sb.Append("<Span color=\"String\" begin=\"'\" end=\"'\">");
            sb.Append("<Rule color=\"String\">\\\\.</Rule>");
            sb.Append("</Span>");

            // 注释：XML 文档 ///  与普通 // 、块注释 /* */
            sb.Append("<Span color=\"Comment\" begin=\"//\" />");
            sb.Append("<Span color=\"Comment\" multiline=\"true\" begin=\"/*\" end=\"*/\" />");

            // 预处理指令（#if DEBUG 等）
            sb.Append("<Rule color=\"Preprocessor\">(?m)^[ \\t]*#[ \\t]*[^\\n]*</Rule>");

            // 数字：十六进制 / 十进制（含小数、科学计数、类型后缀）
            sb.Append("<Rule color=\"Number\">\\b0[xX][0-9a-fA-F]+[uUlL]*\\b</Rule>");
            sb.Append("<Rule color=\"Number\">\\b[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?[fFdDmMuUlL]*\\b</Rule>");

            // 接口变量优先（防止与 API/类型同名时被吞掉）
            if (vars != null && vars.Count > 0)
                AppendKeywords(sb, "Variable", vars);

            AppendKeywords(sb, "Api", ContextApi);
            AppendKeywords(sb, "Type", KnownTypes);
            AppendKeywords(sb, "Control", Split(ControlKeywords));
            AppendKeywords(sb, "Keyword", Split(OtherKeywords));

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

        private static IEnumerable<string> Split(string s) =>
            (s ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
