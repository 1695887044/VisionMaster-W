using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// 「校验脚本」的共享变量用法扫描（不执行、不编译）：
    ///  · 声明表自身：空别名 / 重复别名；
    ///  · 脚本里的 <c>Context.GetGlobal("x")</c> / <c>SetGlobal("x", ...)</c> / <c>TrySetGlobal("x", ...)</c>：
    ///    未声明、只读声明被写、系统变量被写。
    ///
    /// 为什么用正则而不是 Roslyn 语义分析：这里判的是**字符串字面量**参数，
    /// 语法树能拿到 InvocationExpression，但要确认"这是 Context.GetGlobal 而不是别的同名方法"
    /// 仍需语义模型；正则足够，且与「校验脚本」按钮的轻量定位一致。
    /// 动态拼接的名字（<c>GetGlobal(name)</c>）扫不到——运行期访问器仍会拒绝未声明别名。
    /// </summary>
    public static class ScriptSharedUsageValidator
    {
        private static readonly Regex CallPattern = new(
            @"Context\s*\.\s*(?<method>GetGlobal|SetGlobal|TrySetGlobal)\s*(?:<[^>\r\n]*>)?\s*\(\s*""(?<alias>[^""\r\n]*)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>返回 null = 通过；否则为"第 N 行 第 1 列: …"格式的可上屏错误。</summary>
        public static string Validate(string script, IEnumerable<ScriptSharedVarDef> declarations)
        {
            var byAlias = new Dictionary<string, ScriptSharedVarDef>(StringComparer.Ordinal);
            foreach (var def in declarations ?? Enumerable.Empty<ScriptSharedVarDef>())
            {
                if (def == null) continue;

                var alias = def.EffectiveAlias;
                if (string.IsNullOrWhiteSpace(alias))
                    return "共享变量表里有一行既没有选变量也没有填别名：请选择变量，或删除该行";
                if (byAlias.ContainsKey(alias))
                    return $"共享变量别名「{alias}」重复：脚本里只能命中一个，请改成不同的别名";

                byAlias[alias] = def;
            }

            if (string.IsNullOrEmpty(script)) return null;

            foreach (Match match in CallPattern.Matches(script))
            {
                string method = match.Groups["method"].Value;
                string alias = match.Groups["alias"].Value.Trim();

                int line = 1;
                for (int i = 0; i < match.Index; i++)
                    if (script[i] == '\n') line++;

                if (!byAlias.TryGetValue(alias, out var def))
                {
                    return $"第 {line} 行 第 1 列: 共享变量「{alias}」未声明"
                        + $"（请在左侧「共享变量」表添加一行，别名 = {alias}）";
                }

                if (method == "GetGlobal") continue;

                if (def.IsSystem)
                    return $"第 {line} 行 第 1 列: 「{alias}」是系统变量（只读），不能调用 {method}";
                if (!def.CanWrite)
                    return $"第 {line} 行 第 1 列: 共享变量「{alias}」声明为只读，不能调用 {method}（把方向改成「读写」）";
            }

            return null;
        }
    }
}
