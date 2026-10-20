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
    /// 3) 控制流关键字紫色，层次感强于纯蓝。
    ///
    /// 刻意**不**给"接口变量名"着色：它们只是端口名/字典键，不是 C# 局部变量。
    /// 旧实现把裸名染成变量色，等于告诉用户 `index` 能直接写——而那必然 CS0103，
    /// 正是"怎么写都不对"的视觉暗示。脚本里请走 Context.GetInput&lt;T&gt;("名")。
    /// </summary>
    public static class CSharpHighlighting
    {
        private const string DefinitionName = "CSharpScript";
        private static bool _registered;
        private static readonly object Lock = new object();

        // ScriptContext 门面 API（与 ScriptContext.cs 同步维护）：棕黄
        private static readonly string[] ContextApi =
        {
            "Context", "GetInput", "SetOutput", "GetGlobal", "SetGlobal", "TrySetGlobal",
            "GetVar", "SetVar", "Vars",
            "Info", "Warn", "Error", "Success", "ShowImage", "Fail",
        };

        // 脚本中常见的类型/静态类：青色（小写内置类型走关键字规则）
        private static readonly string[] KnownTypes =
        {
            "HImage", "HObject", "HRegion", "HXld", "HXLDCont", "HTuple", "HalconDotNet",
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
        /// 获取高亮定义（API / 类型 / 关键字着色）。
        /// 接口变量名不参与着色——见类注释。
        /// </summary>
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
                var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting(
                    DefinitionName, new[] { ".csx" }, definition);

                _registered = true;
            }
        }

        // 拼装 xshd 文本
        private static string BuildXshd()
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
            sb.Append("<Color name=\"Preprocessor\" foreground=\"#808080\" />");

            // C# 大小写敏感
            sb.Append("<RuleSet>");

            // 字符串：普通 / 逐字(@"") / 字符
            // 转义序列用"嵌套 Span"吃掉（begin="\\" end="."），使 \" 不会提前触发外层 end；
            // 这是 AvalonEdit 内置 CSharp-Mode.xshd 的既有写法。
            // 注意：xshd schema 规定 Span 的子元素只能是 Begin/End/RuleSet，
            // 直接把 Rule 放在 Span 下会抛 HighlightingDefinitionInvalidException。
            sb.Append("<Span color=\"String\" begin=\"&quot;\" end=\"&quot;\">");
            sb.Append("<RuleSet><Span color=\"String\" begin=\"\\\\\" end=\".\" /></RuleSet>");
            sb.Append("</Span>");
            sb.Append("<Span color=\"String\" multiline=\"true\" begin=\"@&quot;\" end=\"&quot;\">");
            sb.Append("<RuleSet><Span color=\"String\" begin=\"&quot;&quot;\" end=\"\" /></RuleSet>");
            sb.Append("</Span>");
            sb.Append("<Span color=\"String\" begin=\"'\" end=\"'\">");
            sb.Append("<RuleSet><Span color=\"String\" begin=\"\\\\\" end=\".\" /></RuleSet>");
            sb.Append("</Span>");

            // 注释：XML 文档 ///  与普通 // 、块注释 /* */
            // 块注释的 * 必须转义（/\* 、\*/），裸写 "*/" 是非法正则：'*' 前面没有可限定的内容
            sb.Append("<Span color=\"Comment\" begin=\"//\" />");
            sb.Append("<Span color=\"Comment\" multiline=\"true\" begin=\"/\\*\" end=\"\\*/\">");
            sb.Append("</Span>");

            // 预处理指令（#if DEBUG 等）
            // 注意：# 必须转义为 \#。
            // 原因：AvalonEdit 对 <Rule>文本</Rule> 这种"元素文本"写法会强制附加
            // XshdRegexType.IgnorePatternWhitespace（x 模式），该模式下 # 起"行注释直到行尾"，
            // 未转义的 # 会把后面的 [ \t]*[^\n]* 全部注释掉，规则退化成 (?m)^[ \t]*，
            // 每行行首都能零长度匹配 → 高亮引擎抛 "A highlighting rule matched 0 characters"，
            // 正文绘制被反复中断，表现为"只有行号、内容空白、无法编辑"。
            // （x 模式是上游有意为之，用于支持在规则文本里写 # 注释，如内置 CSharp-Mode.xshd。）
            sb.Append("<Rule color=\"Preprocessor\">(?m)^[ \\t]*\\#[ \\t]*[^\\n]*</Rule>");

            // 数字：十六进制 / 十进制（含小数、科学计数、类型后缀）
            sb.Append("<Rule color=\"Number\">\\b0[xX][0-9a-fA-F]+[uUlL]*\\b</Rule>");
            sb.Append("<Rule color=\"Number\">\\b[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?[fFdDmMuUlL]*\\b</Rule>");

            // 接口变量不参与关键字着色：它们不是 C# 标识符（见类注释）
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
