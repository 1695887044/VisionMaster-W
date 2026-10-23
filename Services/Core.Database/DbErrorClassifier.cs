using System;
using Core.Interfaces.Database;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace Core.Database
{
    /// <summary>
    /// 数据库异常 → <see cref="DbErrorKind"/> 的分类（SQLite + MySQL/MariaDB）。
    ///
    /// 分类只用于"上屏文案 + 日志归因"；v1 **不做自动重试**（架构硬闸 6：
    /// 不自动重试非幂等语句）。将来要加重试，也只能对 Transient/Deadlock 且语句显式声明幂等时加。
    /// </summary>
    public static class DbErrorClassifier
    {
        public static DbErrorKind Classify(Exception? ex)
        {
            switch (ex)
            {
                case OperationCanceledException:
                    return DbErrorKind.Timeout;

                case SqliteException sqlite:
                    return ClassifySqlite(sqlite);

                case MySqlException mysql:
                    return ClassifyMySqlNumber(mysql.Number);

                case UnauthorizedAccessException:
                case System.Security.SecurityException:
                    return DbErrorKind.Permission;

                case System.IO.FileNotFoundException:
                case System.IO.DirectoryNotFoundException:
                    return DbErrorKind.NotFound;

                case TimeoutException:
                    return DbErrorKind.Timeout;

                default:
                    return DbErrorKind.Unknown;
            }
        }

        /// <summary>MySQL/MariaDB 错误号分类（公开给断言直接按号码验证）。</summary>
        public static DbErrorKind ClassifyMySqlNumber(int number) => number switch
        {
            1040 => DbErrorKind.Transient,    // too many connections
            1044 or 1045 => DbErrorKind.Permission, // access denied
            1049 => DbErrorKind.NotFound,     // unknown database
            1053 => DbErrorKind.Transient,    // server shutdown in progress
            1054 => DbErrorKind.NotFound,     // unknown column
            1062 => DbErrorKind.Constraint,   // duplicate entry
            1064 => DbErrorKind.Syntax,       // syntax error
            1142 or 1143 => DbErrorKind.Permission, // command denied
            1146 => DbErrorKind.NotFound,     // table doesn't exist
            1205 => DbErrorKind.Deadlock,     // lock wait timeout
            1213 => DbErrorKind.Deadlock,     // deadlock found
            1264 or 1406 => DbErrorKind.Constraint, // out of range / data too long
            1317 => DbErrorKind.Timeout,      // query interrupted
            2002 or 2003 => DbErrorKind.NotFound,   // can't connect
            2013 => DbErrorKind.Transient,    // lost connection during query
            3024 => DbErrorKind.Timeout,      // query execution timeout
            _ => DbErrorKind.Unknown
        };

        private static DbErrorKind ClassifySqlite(SqliteException ex)
        {
            // 消息级细化：SQLITE_ERROR(1) 里最常见的是"表/列不存在"，它比"语法错误"更准确。
            var message = ex.Message ?? string.Empty;
            if (message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
                || message.Contains("no such function", StringComparison.OrdinalIgnoreCase))
                return DbErrorKind.NotFound;

            return ex.SqliteErrorCode switch
            {
                5 or 6 => DbErrorKind.Transient,   // BUSY / LOCKED
                8 => DbErrorKind.Permission,       // READONLY
                9 => DbErrorKind.Timeout,          // INTERRUPT（命令取消/超时）
                14 => DbErrorKind.NotFound,        // CANTOPEN
                19 => DbErrorKind.Constraint,      // CONSTRAINT
                23 => DbErrorKind.Permission,      // AUTH
                26 => DbErrorKind.NotFound,        // NOTADB
                1 or 2 => DbErrorKind.Syntax,
                _ => DbErrorKind.Unknown
            };
        }

        /// <summary>可直接上屏的错误描述：错误类别 + 数据库错误码 + 下一步建议。</summary>
        public static string Describe(Exception ex, DbErrorKind kind)
        {
            var suffix = ex switch
            {
                SqliteException sqlite => $"（SQLite 错误 {sqlite.SqliteErrorCode}/{sqlite.SqliteExtendedErrorCode}）",
                MySqlException mysql => $"（MySQL 错误 {mysql.Number}）",
                _ => string.Empty
            };

            var hint = kind switch
            {
                DbErrorKind.Transient => "；数据库正忙或连接中断，稍后重试或缩短事务",
                DbErrorKind.Deadlock => "；发生死锁/锁等待超时，请重试",
                DbErrorKind.Timeout => "；已超过命令超时，可提高超时或优化语句",
                DbErrorKind.Constraint => "；违反唯一/非空/外键约束，请检查数据",
                DbErrorKind.Syntax => "；SQL 语法错误，请到硬件管理 → 数据库页修正语句",
                DbErrorKind.Permission => "；当前连接没有该权限，或数据库以只读方式打开",
                DbErrorKind.NotFound => "；找不到数据库/表/列，或服务器不可达，请检查连接目标与表结构",
                DbErrorKind.Disabled => "；该数据库或语句已被禁用",
                _ => string.Empty
            };

            var message = ex is SqliteException or MySqlException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            return $"{DbErrorKindNames.Text(kind)}：{message}{suffix}{hint}";
        }
    }
}
