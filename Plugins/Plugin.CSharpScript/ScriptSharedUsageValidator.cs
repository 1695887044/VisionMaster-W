using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Plugin.CSharpScript
{
    /// <summary>脚本里对共享变量的一次字面量用法（<see cref="Alias"/> 已 Trim）。</summary>
    /// <param name="Alias">脚本里写的键（字符串字面量）</param>
    /// <param name="Method">GetGlobal / SetGlobal / TrySetGlobal</param>
    /// <param name="Line">1 起的行号</param>
    public readonly record struct ScriptSharedAliasUse(string Alias, string Method, int Line);

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
    ///
    /// 2026-10-30：把"抽取"与"校验声明表"拆开（<see cref="Extract"/>）——
    /// 配置期要用同一份扫描结果**自动补表**，两处口径必须完全一致，否则"校验通过"与"表里有什么"
    /// 会给出两种答案。校验错误文案与行号格式保持原样（回归断言 C32 反向钉住）。
    /// </summary>
    public static class ScriptSharedUsageValidator
    {
        private static readonly Regex CallPattern = new(
            @"Context\s*\.\s*(?<method>GetGlobal|SetGlobal|TrySetGlobal)\s*(?:<[^>\r\n]*>)?\s*\(\s*""(?<alias>[^""\r\n]*)""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>抽出脚本里所有字面量别名的用法（顺序 = 出现顺序；同一别名可能出现多次）。</summary>
        /// <remarks>
        /// **注释里的示例代码不算用法**（2026-10-30）：默认脚本模板把
        /// <c>Context.GetGlobal("imageIndex")</c> 写成注释示例，若把注释也当用法，
        /// 任何一个新建步骤点「校验脚本」都会要求先建一个 imageIndex 变量。
        /// 因此先按 C# 词法把 <c>//</c>、<c>/* */</c> 逐字符替换成空格（保留换行，行号不变），
        /// 字符串字面量原样保留（别名本身就在字符串里）。
        /// </remarks>
        public static IReadOnlyList<ScriptSharedAliasUse> Extract(string script)
        {
            if (string.IsNullOrEmpty(script)) return Array.Empty<ScriptSharedAliasUse>();

            var code = MaskComments(script);
            var uses = new List<ScriptSharedAliasUse>();
            int line = 1;
            int scanned = 0;
            foreach (Match match in CallPattern.Matches(code))
            {
                // 行号按"匹配起点之前的换行数 + 1"算；累进计数与逐次从头数等价（匹配天然有序）。
                for (int i = scanned; i < match.Index; i++)
                    if (code[i] == '\n') line++;
                scanned = match.Index;

                // 别名仍从原文取（掩码只动注释，字符串区域内容与原文一致）。
                uses.Add(new ScriptSharedAliasUse(
                    match.Groups["alias"].Value.Trim(),
                    match.Groups["method"].Value,
                    line));
            }
            return uses;
        }

        /// <summary>
        /// 把注释替换成等长空格（换行原样保留），跳过字符串字面量内部——
        /// 否则 <c>"http://x"</c> 里的 <c>//</c> 会被当成注释起点，把后面的代码一起吞掉。
        /// 只处理 C# 的普通串 / 逐字串（@""）/ 插值串（$""、$@""）；不做语法分析。
        /// </summary>
        private static string MaskComments(string script)
        {
            var buffer = script.ToCharArray();
            int i = 0;
            while (i < buffer.Length)
            {
                char c = buffer[i];

                // —— 字符串字面量：整段跳过（含转义）——
                if (c == '"' || (c == '@' && i + 1 < buffer.Length && buffer[i + 1] == '"')
                             || (c == '$' && i + 1 < buffer.Length && buffer[i + 1] == '"')
                             || (c == '$' && i + 2 < buffer.Length && buffer[i + 1] == '@' && buffer[i + 2] == '"'))
                {
                    bool verbatim = false;
                    if (buffer[i] == '$' && i + 1 < buffer.Length && buffer[i + 1] == '@') { verbatim = true; i += 2; }
                    else if (buffer[i] == '$') i++;
                    else if (buffer[i] == '@') { verbatim = true; i++; }

                    // buffer[i] 此时是开引号
                    i++;
                    while (i < buffer.Length)
                    {
                        if (verbatim)
                        {
                            if (buffer[i] == '"')
                            {
                                if (i + 1 < buffer.Length && buffer[i + 1] == '"') { i += 2; continue; }   // "" 转义
                                i++;
                                break;
                            }
                            i++;
                        }
                        else
                        {
                            if (buffer[i] == '\\') { i += 2; continue; }
                            if (buffer[i] == '"') { i++; break; }
                            if (buffer[i] == '\n') break;   // 未闭合：不越界吞全文
                            i++;
                        }
                    }
                    // 字符串内容一个字都不改；跳过只是为了不让里面的 // 被当成注释起点。
                    continue;
                }

                // —— 行注释 ——
                if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '/')
                {
                    while (i < buffer.Length && buffer[i] != '\n')
                    {
                        buffer[i] = ' ';
                        i++;
                    }
                    continue;
                }

                // —— 块注释 ——
                if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '*')
                {
                    buffer[i] = ' ';
                    buffer[i + 1] = ' ';
                    i += 2;
                    while (i < buffer.Length)
                    {
                        if (buffer[i] == '*' && i + 1 < buffer.Length && buffer[i + 1] == '/')
                        {
                            buffer[i] = ' ';
                            buffer[i + 1] = ' ';
                            i += 2;
                            break;
                        }
                        if (buffer[i] != '\n' && buffer[i] != '\r') buffer[i] = ' ';
                        i++;
                    }
                    continue;
                }

                i++;
            }
            return new string(buffer);
        }

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

            foreach (var use in Extract(script))
            {
                if (!byAlias.TryGetValue(use.Alias, out var def))
                {
                    return $"第 {use.Line} 行 第 1 列: 共享变量「{use.Alias}」未声明"
                        + $"（请在左侧「共享变量」表添加一行，别名 = {use.Alias}）";
                }

                if (use.Method == "GetGlobal") continue;

                if (def.IsSystem)
                    return $"第 {use.Line} 行 第 1 列: 「{use.Alias}」是系统变量（只读），不能调用 {use.Method}";
                if (!def.CanWrite)
                    return $"第 {use.Line} 行 第 1 列: 共享变量「{use.Alias}」声明为只读，不能调用 {use.Method}（把方向改成「读写」）";
            }

            return null;
        }
    }
}
