using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Core.Interfaces.Database;

namespace Core.Database
{
    /// <summary>
    /// 命名语句的保存期/执行期校验（唯一一处；宿主页与插件都不许各写一套）。
    ///
    /// 为什么不做完整 SQL 解析：v1 只需要挡住"明显越界"的写法——
    /// 多语句、ATTACH/DETACH、写 PRAGMA、无 WHERE 的整表更新、参数与占位符对不上。
    /// 规则**宁可保守**（宁可拒，不可漏），并且先把注释与字符串字面量剥掉再判，
    /// 否则 <c>SELECT '-- 注释'</c> 这类正常 SQL 会被误杀。
    /// </summary>
    public static class StatementValidator
    {
        private static readonly Regex PlaceholderRegex =
            new(@"@([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        private static readonly Regex WhereRegex =
            new(@"\bWHERE\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WriteKeywordRegex =
            new(@"\b(INSERT|UPDATE|DELETE|REPLACE|CREATE|DROP|ALTER|VACUUM|REINDEX)\b",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// MySQL 下整条拒绝的语句（出现在任何位置都拒）：
        /// 文件读写、会话改写、存储过程、预处理——v1 只做参数化的单条 DML。
        /// </summary>
        private static readonly Regex MySqlForbiddenRegex = new(
            @"\b(LOAD\s+DATA|LOAD\s+XML|INTO\s+OUTFILE|INTO\s+DUMPFILE|PREPARE|HANDLER)\b"
            + @"|^\s*(SET|USE|CALL)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>锁定读（SELECT ... FOR UPDATE / LOCK IN SHARE MODE）会拿写锁，查询步骤里一律拒。</summary>
        private static readonly Regex MySqlLockingReadRegex = new(
            @"\b(FOR\s+UPDATE|LOCK\s+IN\s+SHARE\s+MODE)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PragmaNameRegex =
            new(@"^\s*PRAGMA\s+(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)?([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IdentifierRegex =
            new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        /// <summary>只读 PRAGMA 白名单（带参数的形式一律拒，避免 table_info(x) 之外的写形态）。</summary>
        private static readonly HashSet<string> ReadOnlyPragmas = new(StringComparer.OrdinalIgnoreCase)
        {
            "table_info", "table_xinfo", "table_list", "index_list", "index_info",
            "foreign_key_list", "database_list", "compile_options"
        };

        /// <summary>校验一条语句（保存前必须过；执行前服务会再过一次）。</summary>
        public static bool Validate(NamedStatement statement, DatabaseProfile profile, out string? error)
        {
            error = null;
            if (statement == null) { error = "语句为空"; return false; }

            if (string.IsNullOrWhiteSpace(statement.Name))
            {
                error = "语句名不能为空";
                return false;
            }

            if (statement.TimeoutSeconds is < 0 or > 3600)
            {
                error = "超时必须在 0~3600 秒之间（0 = 用数据库默认值）";
                return false;
            }

            if (statement.MaxRows is < 0 or > 1_000_000)
            {
                error = "最大返回行数必须在 0~1000000 之间（0 = 用数据库默认值）";
                return false;
            }

            if (statement.MaxAffectedRows < 0)
            {
                error = "最大影响行数不能为负数（0 = 不限制）";
                return false;
            }

            if (profile != null && profile.OpenMode == DbOpenMode.ReadOnly && statement.Kind != DbStatementKind.Query)
            {
                error = "只读数据库只允许保存『查询』语句";
                return false;
            }

            if (!ValidateSql(statement.Sql, statement.Kind, profile?.Provider ?? DbProviderKind.Sqlite, out error))
                return false;

            return ValidateParameters(statement, out error);
        }

        /// <summary>只校验 SQL 文本（宿主页编辑时即时提示用）。</summary>
        public static bool ValidateSql(string sql, DbStatementKind kind, DbProviderKind provider, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sql))
            {
                error = "SQL 不能为空";
                return false;
            }

            var stripped = StripCommentsAndStrings(sql);
            var text = stripped.Trim();
            if (text.Length == 0)
            {
                error = "SQL 只有注释";
                return false;
            }

            // 单条语句：去掉末尾一个分号后，不许再有内容
            var semi = text.IndexOf(';');
            if (semi >= 0)
            {
                var tail = text[(semi + 1)..].Trim();
                if (tail.Length > 0)
                {
                    error = "只允许单条 SQL 语句（分号后还有内容）";
                    return false;
                }
                text = text[..semi].TrimEnd();
            }

            if (Regex.IsMatch(text, @"\b(ATTACH|DETACH)\b", RegexOptions.IgnoreCase))
            {
                error = "禁止 ATTACH / DETACH（只允许单库）";
                return false;
            }

            if (provider == DbProviderKind.MySql && MySqlForbiddenRegex.IsMatch(text))
            {
                error = "MySQL 下禁止这些语句：LOAD DATA / OUTFILE / DUMPFILE / SET / USE / CALL / PREPARE / HANDLER（v1 只做参数化的单条 DML）";
                return false;
            }

            var firstWord = FirstWord(text);
            if (firstWord.Length == 0)
            {
                error = "无法识别 SQL 语句类型";
                return false;
            }

            switch (kind)
            {
                case DbStatementKind.Query:
                    return ValidateQuery(text, firstWord, provider, out error);

                case DbStatementKind.Execute:
                    if (firstWord is not ("UPDATE" or "DELETE"))
                    {
                        error = "『执行』只允许 UPDATE / DELETE；INSERT 请用『写一行』类型";
                        return false;
                    }
                    if (!WhereRegex.IsMatch(text))
                    {
                        error = "UPDATE / DELETE 必须带 WHERE（v1 不做整表操作）";
                        return false;
                    }
                    return true;

                case DbStatementKind.WriteRow:
                    // MySQL 的 REPLACE INTO 是"删了再插"的 upsert，归到写一行
                    if (firstWord != "INSERT" && !(provider == DbProviderKind.MySql && firstWord == "REPLACE"))
                    {
                        error = provider == DbProviderKind.MySql
                            ? "『写一行』只允许 INSERT / REPLACE（含 ON DUPLICATE KEY UPDATE 的 UPSERT）"
                            : "『写一行』只允许 INSERT（含 ON CONFLICT 的 UPSERT）";
                        return false;
                    }
                    return true;

                default:
                    error = "未知的语句类型";
                    return false;
            }
        }

        private static bool ValidateQuery(string text, string firstWord, DbProviderKind provider, out string? error)
        {
            error = null;

            if (firstWord == "PRAGMA")
            {
                if (provider != DbProviderKind.Sqlite)
                {
                    error = "PRAGMA 是 SQLite 专有语法，当前数据库不是 SQLite";
                    return false;
                }

                if (text.Contains('='))
                {
                    error = "只读 PRAGMA 不允许赋值（禁止写 PRAGMA）";
                    return false;
                }

                var m = PragmaNameRegex.Match(text);
                if (!m.Success || !ReadOnlyPragmas.Contains(m.Groups[1].Value))
                {
                    error = "该 PRAGMA 不在只读白名单里（允许：table_info / table_xinfo / table_list / index_list / index_info / foreign_key_list / database_list / compile_options）";
                    return false;
                }
                return true;
            }

            if (firstWord is not ("SELECT" or "WITH"))
            {
                error = "『查询』只允许 SELECT / WITH / 只读 PRAGMA";
                return false;
            }

            // 锁定读先判：FOR UPDATE 里含 UPDATE 关键字，放后面会被"写关键字"那条抢先，
            // 报错文案就变成了"不允许出现写关键字"，用户看不出真正原因。
            if (provider == DbProviderKind.MySql && MySqlLockingReadRegex.IsMatch(text))
            {
                error = "查询步骤不允许锁定读（FOR UPDATE / LOCK IN SHARE MODE）";
                return false;
            }

            // WITH 后面可能接写语句（WITH ... INSERT/UPDATE/DELETE），v1 一律拒
            if (WriteKeywordRegex.IsMatch(text))
            {
                error = "『查询』语句里不允许出现写关键字（INSERT/UPDATE/DELETE/DDL）";
                return false;
            }

            return true;
        }

        /// <summary>参数表与 SQL 占位符必须双向一致；参数名必须是合法标识符且不重复。</summary>
        public static bool ValidateParameters(NamedStatement statement, out string? error)
        {
            error = null;
            var defs = statement.Parameters ?? new List<DbStatementParameter>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in defs)
            {
                if (p == null) { error = "参数表里有空行"; return false; }

                var name = (p.Name ?? string.Empty).Trim();
                if (name.Length == 0) { error = "参数名不能为空"; return false; }
                if (name.StartsWith('@')) { error = $"参数名不要带 '@'：{name}"; return false; }
                if (!IdentifierRegex.IsMatch(name))
                {
                    error = $"参数名只能是字母/数字/下划线，且不能以数字开头：{name}";
                    return false;
                }
                if (!seen.Add(name))
                {
                    error = $"参数名重复：{name}";
                    return false;
                }
                if (!DbTypeNames.IsKnown(p.DbType))
                {
                    error = $"参数 {name} 的类型未知：{p.DbType}";
                    return false;
                }
            }

            var placeholders = ExtractPlaceholders(StripCommentsAndStrings(statement.Sql ?? string.Empty));
            var missingDefs = placeholders.Where(n => !seen.Contains(n)).ToList();
            if (missingDefs.Count > 0)
            {
                error = "SQL 里的占位符没有对应参数：" + string.Join("、", missingDefs.Select(n => "@" + n));
                return false;
            }

            var unused = seen.Where(n => !placeholders.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unused.Count > 0)
            {
                error = "参数表里有 SQL 没用到的参数：" + string.Join("、", unused);
                return false;
            }

            return true;
        }

        /// <summary>提取 @name 占位符（去重、保持出现顺序）。调用方必须先剥注释/字符串。</summary>
        public static IReadOnlyList<string> ExtractPlaceholders(string strippedSql)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in PlaceholderRegex.Matches(strippedSql ?? string.Empty))
            {
                var name = m.Groups[1].Value;
                if (seen.Add(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// 把注释与字符串字面量替换成空格（保留换行与长度感），
        /// 让关键字/占位符扫描不被 <c>'-- 不是注释'</c> 这类内容误伤。
        /// </summary>
        public static string StripCommentsAndStrings(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return string.Empty;

            var sb = new StringBuilder(sql.Length);
            for (var i = 0; i < sql.Length; i++)
            {
                var c = sql[i];

                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }

                // MySQL 的 # 行注释（SQLite 不认这个字符，剥掉也不会误伤合法语句）
                if (c == '#')
                {
                    while (i < sql.Length && sql[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }

                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                    i++; // 跳过 '/'
                    sb.Append(' ');
                    continue;
                }

                if (c is '\'' or '"')
                {
                    var quote = c;
                    sb.Append(' ');
                    i++;
                    while (i < sql.Length)
                    {
                        if (sql[i] == quote)
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == quote) { i += 2; continue; }
                            break;
                        }
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string FirstWord(string text)
        {
            var i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            var start = i;
            while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '_')) i++;
            return text[start..i].ToUpperInvariant();
        }
    }
}
