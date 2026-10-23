using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Core.Database;
using Core.Interfaces;
using Core.Interfaces.Database;
using Microsoft.Data.Sqlite;
using Plugin.Database;
using VisionMaster.Models;
using VisionMaster.Services;

namespace DatabaseChecks
{
    /// <summary>
    /// 数据库三层断言宿主（服务层 / 插件层 / 静态契约）。
    ///
    /// 服务层用**真 SQLite 临时文件**：参数化、事务回滚、截断、影响行数上限这些
    /// 只有真跑才证明得了；插件层用假 IDatabaseAccess，只验端口/映射/失败语义；
    /// 静态层扫源码与产物，钉住"Provider 只在宿主"这条红线。
    /// </summary>
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static int Main()
        {
            var root = FindRepoRoot();
            // 受限环境里既不能新建目录、也不能写 %TEMP%，artifacts\ 下 SQLite 也打不开（错误 14）。
            // 落到**本进程自己的输出目录**（构建产物目录，必然可写），跑完删文件。
            var tempDir = AppContext.BaseDirectory;

            var stem = "dbchecks-" + Guid.NewGuid().ToString("N");
            var jsonPath = Path.Combine(tempDir, stem + "-databases.json");
            var dbPath = Path.Combine(tempDir, stem + "-prod.db");
            var mysqlJsonPath = Path.Combine(tempDir, stem + "-mysql.json");

            Console.WriteLine("===== DatabaseChecks =====");
            Console.WriteLine("repo: " + (root ?? "(not found)"));

            try
            {
                ServiceChecks(jsonPath, dbPath);
                MySqlProviderChecks(mysqlJsonPath);
                PluginChecks();
                StaticChecks(root);
            }
            catch (Exception ex)
            {
                Check("断言宿主未抛异常", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                DatabaseService.ClearPools();
                foreach (var file in new[] { jsonPath, dbPath, jsonPath + ".tmp", mysqlJsonPath, mysqlJsonPath + ".tmp" })
                {
                    try { if (File.Exists(file)) File.Delete(file); }
                    catch { /* 临时文件清不掉不影响结论 */ }
                }
            }

            Finish();
            return _fail == 0 ? 0 : 1;
        }

        // ==================================================================== 服务层

        private static void ServiceChecks(string json, string dbFile)
        {
            Section("服务层：配置存储与校验");

            var dir = Path.GetDirectoryName(json) ?? Path.GetTempPath();
            SeedDatabase(dbFile);

            var log = new FakeLog();
            var service = new DatabaseService(log, json);

            var profile = new DatabaseProfile
            {
                ProfileId = Guid.NewGuid(),
                Name = "生产追溯",
                FilePath = dbFile,
                OpenMode = DbOpenMode.ReadWrite,
                TimeoutSeconds = 5,
                MaxRows = 100
            };

            Check("保存 profile", service.TrySaveProfile(profile, out var err), err);
            Check("profile 出现在列表里", service.ListProfiles().Count == 1, "count=" + service.ListProfiles().Count);

            var dup = new DatabaseProfile { ProfileId = Guid.NewGuid(), Name = "生产追溯", FilePath = dbFile, TimeoutSeconds = 5, MaxRows = 100 };
            Check("同名 profile 被拒", !service.TrySaveProfile(dup, out err) && (err ?? "").Contains("同名"), err);

            var badExt = new DatabaseProfile { ProfileId = Guid.NewGuid(), Name = "坏扩展名", FilePath = Path.Combine(dir, "x.txt"), TimeoutSeconds = 5, MaxRows = 100 };
            Check("非 .db/.sqlite 扩展名被拒", !service.TrySaveProfile(badExt, out err) && (err ?? "").Contains(".db"), err);

            var badDir = new DatabaseProfile { ProfileId = Guid.NewGuid(), Name = "坏目录", FilePath = Path.Combine(dir, "no_such_dir", "x.db"), TimeoutSeconds = 5, MaxRows = 100 };
            Check("目录不存在被拒", !service.TrySaveProfile(badDir, out err) && (err ?? "").Contains("目录"), err);

            var roMissing = new DatabaseProfile { ProfileId = Guid.NewGuid(), Name = "只读缺文件", FilePath = Path.Combine(dir, "missing.db"), OpenMode = DbOpenMode.ReadOnly, TimeoutSeconds = 5, MaxRows = 100 };
            Check("只读 profile 要求文件已存在", !service.TrySaveProfile(roMissing, out err) && (err ?? "").Contains("已存在"), err);

            var roProfile = new DatabaseProfile
            {
                ProfileId = Guid.NewGuid(),
                Name = "只读追溯",
                FilePath = dbFile,
                OpenMode = DbOpenMode.ReadOnly,
                TimeoutSeconds = 5,
                MaxRows = 100
            };
            Check("保存只读 profile", service.TrySaveProfile(roProfile, out err), err);

            // ---- 命名语句 ----
            var write = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "WriteResult",
                Kind = DbStatementKind.WriteRow,
                Sql = "INSERT INTO result(lot, score, ok) VALUES (@lot, @score, @ok)",
                Parameters = new List<DbStatementParameter>
                {
                    new() { Name = "lot", DbType = DbTypeNames.Text, IsRequired = true },
                    new() { Name = "score", DbType = DbTypeNames.Real },
                    new() { Name = "ok", DbType = DbTypeNames.Boolean }
                }
            };
            Check("保存写行语句", service.TrySaveStatement(write, out err), err);

            var read = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "ReadAll",
                Kind = DbStatementKind.Query,
                Sql = "SELECT id, lot, score, ok FROM result ORDER BY id"
            };
            Check("保存查询语句", service.TrySaveStatement(read, out err), err);

            var count = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "CountAll",
                Kind = DbStatementKind.Query,
                Sql = "SELECT COUNT(*) FROM result"
            };
            Check("保存计数语句", service.TrySaveStatement(count, out err), err);

            var update = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "MarkAll",
                Kind = DbStatementKind.Execute,
                Sql = "UPDATE result SET ok = 0 WHERE id > 0",
                MaxAffectedRows = 1,
                Parameters = new List<DbStatementParameter>()
            };
            Check("保存执行语句", service.TrySaveStatement(update, out err), err);

            var dupStmt = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "ReadAll",
                Kind = DbStatementKind.Query,
                Sql = "SELECT 1"
            };
            Check("同 profile 内同名语句被拒", !service.TrySaveStatement(dupStmt, out err) && (err ?? "").Contains("同名"), err);

            var noWhere = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "BadUpdate",
                Kind = DbStatementKind.Execute,
                Sql = "UPDATE result SET ok = 0"
            };
            Check("UPDATE 不带 WHERE 被拒", !service.TrySaveStatement(noWhere, out err) && (err ?? "").Contains("WHERE"), err);

            var attach = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "BadAttach",
                Kind = DbStatementKind.Query,
                Sql = "ATTACH DATABASE 'x.db' AS x"
            };
            Check("ATTACH 被拒", !service.TrySaveStatement(attach, out err) && (err ?? "").Contains("ATTACH"), err);

            var pragmaWrite = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "BadPragma",
                Kind = DbStatementKind.Query,
                Sql = "PRAGMA user_version = 7"
            };
            Check("写 PRAGMA 被拒", !service.TrySaveStatement(pragmaWrite, out err) && (err ?? "").Contains("PRAGMA"), err);

            var pragmaRead = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "Columns",
                Kind = DbStatementKind.Query,
                Sql = "PRAGMA table_info(result)"
            };
            Check("只读 PRAGMA 白名单放行", service.TrySaveStatement(pragmaRead, out err), err);

            var placeholderMismatch = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "BadPlaceholder",
                Kind = DbStatementKind.WriteRow,
                Sql = "INSERT INTO result(lot) VALUES (@lot)",
                Parameters = new List<DbStatementParameter>()
            };
            Check("SQL 占位符没有参数定义 → 拒", !service.TrySaveStatement(placeholderMismatch, out err) && (err ?? "").Contains("@lot"), err);

            var unusedParam = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "BadUnused",
                Kind = DbStatementKind.WriteRow,
                Sql = "INSERT INTO result(lot) VALUES (@lot)",
                Parameters = new List<DbStatementParameter>
                {
                    new() { Name = "lot", DbType = DbTypeNames.Text, IsRequired = true },
                    new() { Name = "unused", DbType = DbTypeNames.Text }
                }
            };
            Check("参数表有 SQL 没用到的参数 → 拒", !service.TrySaveStatement(unusedParam, out err) && (err ?? "").Contains("unused"), err);

            var roWrite = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = roProfile.ProfileId,
                Name = "RoWrite",
                Kind = DbStatementKind.WriteRow,
                Sql = "INSERT INTO result(lot) VALUES (@lot)",
                Parameters = new List<DbStatementParameter> { new() { Name = "lot", DbType = DbTypeNames.Text, IsRequired = true } }
            };
            Check("只读 profile 拒绝写语句", !service.TrySaveStatement(roWrite, out err) && (err ?? "").Contains("只读"), err);

            var roQuery = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = roProfile.ProfileId,
                Name = "RoRead",
                Kind = DbStatementKind.Query,
                Sql = "SELECT COUNT(*) FROM result"
            };
            Check("只读 profile 允许查询语句", service.TrySaveStatement(roQuery, out err), err);

            // ---- 执行 ----
            Section("服务层：SQLite 执行");

            Check("写一行", service.TryExecute(write.StatementId, Params(("lot", "L1"), ("score", 0.5), ("ok", true)), DbCommandOptions.Default, out var exec, out err),
                err);
            Check("写一行影响 1 行", exec?.AffectedRows == 1, "affected=" + exec?.AffectedRows);
            Check("审计日志落了一条", log.Lines.Any(l => l.Contains("op=Execute") && l.Contains("WriteResult")), string.Join(" | ", log.Lines.Take(3)));

            Check("查询成功", service.TryQuery(read.StatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out var query, out err), err);
            Check("查询 1 行", query?.RowCount == 1, "rows=" + query?.RowCount);
            Check("首行首列是 L1", Equals(query?.Rows[0][1], "L1"), "scalar=" + query?.Rows[0][1]);
            Check("列名保持查询顺序", string.Join(",", query.Columns) == "id,lot,score,ok", string.Join(",", query.Columns));

            Check("参数化：注入串只当值",
                service.TryExecute(write.StatementId, Params(("lot", "x' OR 1=1 --"), ("score", 1.0), ("ok", true)), DbCommandOptions.Default, out _, out err)
                && CountRows(service, count.StatementId) == 2,
                err);

            var byLot = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "ByLot",
                Kind = DbStatementKind.Query,
                Sql = "SELECT id FROM result WHERE lot = @lot",
                Parameters = new List<DbStatementParameter> { new() { Name = "lot", DbType = DbTypeNames.Text, IsRequired = true } }
            };
            Check("保存按批号查询语句", service.TrySaveStatement(byLot, out err), err);

            Check("0 行是成功",
                service.TryQuery(byLot.StatementId, Params(("lot", "NOPE")), DbCommandOptions.Default, out var empty, out err)
                && empty.RowCount == 0,
                err);

            // 再补 3 行（共 5 行）用于截断/上限
            foreach (var lot in new[] { "L2", "L3", "L4" })
                service.TryExecute(write.StatementId, Params(("lot", lot), ("score", 2.0), ("ok", true)), DbCommandOptions.Default, out _, out _);

            Check("截断：MaxRows=2 → 只返回 2 行且 Truncated",
                service.TryQuery(read.StatementId, Array.Empty<DbParameterValue>(),
                    new DbCommandOptions { MaxRows = 2 }, out var truncated, out err)
                && truncated.RowCount == 2 && truncated.Truncated,
                err ?? ("rows=" + truncated?.RowCount));

            Check("影响行数超限 → 回滚并报错",
                !service.TryExecute(update.StatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out _, out err)
                && (err ?? "").Contains("超过上限"),
                err);
            Check("回滚后数据没变（ok 仍为 1）", CountOk(service, read.StatementId) == 5, "okRows=" + CountOk(service, read.StatementId));

            Check("测试语句默认回滚",
                service.TryTestStatement(write.StatementId, Params(("lot", "L5"), ("score", 3.0), ("ok", true)), DbCommandOptions.Default, false, out var tested, out err)
                && tested.RolledBack,
                err);
            Check("回滚后行数不变", CountRows(service, count.StatementId) == 5, "rows=" + CountRows(service, count.StatementId));

            Check("测试语句 commitForReal=true 真写",
                service.TryTestStatement(write.StatementId, Params(("lot", "L5"), ("score", 3.0), ("ok", true)), DbCommandOptions.Default, true, out var committed, out err)
                && !committed.RolledBack,
                err);
            Check("提交后行数 +1", CountRows(service, count.StatementId) == 6, "rows=" + CountRows(service, count.StatementId));

            Check("批量写 3 行",
                service.TryExecuteBatch(write.StatementId,
                    new List<IReadOnlyList<DbParameterValue>>
                    {
                        Params(("lot", "L6"), ("score", 1.0), ("ok", true)),
                        Params(("lot", "L7"), ("score", 1.0), ("ok", true)),
                        Params(("lot", "L8"), ("score", 1.0), ("ok", true))
                    },
                    DbCommandOptions.Default, out var batch, out err)
                && batch.SucceededRows == 3,
                err);
            Check("批量后行数 9", CountRows(service, count.StatementId) == 9, "rows=" + CountRows(service, count.StatementId));

            Check("批量任一行参数缺失 → 整批回滚",
                !service.TryExecuteBatch(write.StatementId,
                    new List<IReadOnlyList<DbParameterValue>>
                    {
                        Params(("lot", "L9"), ("score", 1.0), ("ok", true)),
                        Params(("score", 1.0), ("ok", true))   // 缺 lot
                    },
                    DbCommandOptions.Default, out var badBatch, out err)
                && badBatch.FailedRowIndex == 1,
                err);
            Check("整批回滚后行数仍 9", CountRows(service, count.StatementId) == 9, "rows=" + CountRows(service, count.StatementId));

            Check("缺必填参数 → 报 @lot",
                !service.TryExecute(write.StatementId, Params(("score", 1.0)), DbCommandOptions.Default, out _, out err)
                && (err ?? "").Contains("@lot"),
                err);

            Check("多余参数 → 报未定义",
                !service.TryExecute(write.StatementId, Params(("lot", "Lx"), ("nope", 1)), DbCommandOptions.Default, out _, out err)
                && (err ?? "").Contains("没有定义"),
                err);

            Check("类型不符（Real 收到 abc）→ 拒",
                !service.TryExecute(write.StatementId, Params(("lot", "Lx"), ("score", "abc")), DbCommandOptions.Default, out _, out err)
                && (err ?? "").Contains("期望数值"),
                err);

            Check("布尔文本 true 被接受",
                service.TryExecute(write.StatementId, Params(("lot", "Lb"), ("score", 1.0), ("ok", "true")), DbCommandOptions.Default, out _, out err),
                err);

            var disabled = new NamedStatement
            {
                StatementId = Guid.NewGuid(),
                ProfileId = profile.ProfileId,
                Name = "Disabled",
                Kind = DbStatementKind.Query,
                Sql = "SELECT 1",
                IsEnabled = false
            };
            Check("保存禁用语句", service.TrySaveStatement(disabled, out err), err);
            Check("禁用语句执行被拒",
                !service.TryQuery(disabled.StatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out _, out err)
                && (err ?? "").Contains("禁用"),
                err);

            Check("查询步骤拿到写语句 → 拒",
                !service.TryQuery(write.StatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out _, out err)
                && (err ?? "").Contains("不能用于查询"),
                err);

            Check("只读 profile 查询可用",
                service.TryQuery(roQuery.StatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out var roResult, out err)
                && roResult.RowCount == 1,
                err);

            using (var cts = new System.Threading.CancellationTokenSource())
            {
                cts.Cancel();
                var cancelled = false;
                try
                {
                    service.TryQuery(read.StatementId, Array.Empty<DbParameterValue>(),
                        new DbCommandOptions { CancellationToken = cts.Token }, out _, out _);
                }
                catch (OperationCanceledException) { cancelled = true; }
                Check("已取消的令牌 → OperationCanceledException（引擎记 Skipped，不是失败）", cancelled);
            }

            Section("服务层：持久化与容错");

            var reloaded = new DatabaseService(log, json);
            Check("重新加载：profile 在", reloaded.ListProfiles().Count == 2, "count=" + reloaded.ListProfiles().Count);
            Check("重新加载：语句在", reloaded.ListStatements(profile.ProfileId).Count >= 7, "count=" + reloaded.ListStatements(profile.ProfileId).Count);

            var brokenPath = json + ".broken.json";
            File.WriteAllText(brokenPath, "{ this is not json", new UTF8Encoding(false));
            var broken = new DatabaseService(log, brokenPath);
            Check("坏配置文件不抛、按空库继续", broken.ListProfiles().Count == 0);
            Check("坏配置文件留下 LastLoadError", !string.IsNullOrWhiteSpace(broken.LastLoadError), broken.LastLoadError);
            try { File.Delete(brokenPath); } catch { /* 清不掉不影响结论 */ }

            Check("删除语句", service.TryDeleteStatement(disabled.StatementId, out err), err);
            Check("删除后查不到", service.ListStatements(profile.ProfileId).All(s => s.StatementId != disabled.StatementId));
        }

        // ==================================================================== MySQL / MariaDB

        private static void MySqlProviderChecks(string jsonPath)
        {
            Section("MySQL/MariaDB：profile 与凭据");

            var log = new FakeLog();
            var service = new DatabaseService(log, jsonPath);

            var profile = new DatabaseProfile
            {
                ProfileId = Guid.NewGuid(),
                Name = "MES 生产库",
                Provider = DbProviderKind.MySql,
                Server = "10.0.0.5",
                Port = 3306,
                DatabaseName = "mes",
                UserId = "vm",
                OpenMode = DbOpenMode.ReadWrite,
                TimeoutSeconds = 10,
                MaxRows = 500
            };

            Check("MySQL profile 保存（含密码）", service.TrySaveProfile(profile, "s3cret", out var err), err);

            var reloaded = new DatabaseService(log, jsonPath);
            var info = reloaded.ListProfiles().FirstOrDefault();
            Check("MySQL 字段往返",
                info != null && info.Provider == DbProviderKind.MySql && info.Server == "10.0.0.5"
                && info.Port == 3306 && info.DatabaseName == "mes",
                info == null ? "no profile" : $"{info.Server}:{info.Port}/{info.DatabaseName}");
            Check("TargetText 拼法", info?.TargetText == "10.0.0.5:3306/mes", info?.TargetText);
            Check("Provider 显示名", info?.ProviderText == DbProviderNames.MySql, info?.ProviderText);
            Check("Caption 带 Provider", (info?.Caption ?? "").Contains("MySQL"), info?.Caption);

            var storedJson = File.ReadAllText(jsonPath, Encoding.UTF8);
            Check("明文密码不落盘", !storedJson.Contains("s3cret", StringComparison.Ordinal));
            Check("密文落盘", storedJson.Contains("PasswordCipher", StringComparison.Ordinal));

            Check("DbProfileInfo 不暴露密码字段",
                typeof(DbProfileInfo).GetProperty("PasswordCipher") == null
                && typeof(DbProfileInfo).GetProperty("Password") == null);

            Check("DPAPI 往返", DbCredentialProtector.Unprotect(DbCredentialProtector.Protect("s3cret")) == "s3cret");

            var badCipher = false;
            try { DbCredentialProtector.Unprotect("not-a-valid-cipher"); }
            catch (InvalidOperationException ex) { badCipher = ex.Message.Contains("重新输入密码"); }
            Check("坏密文给出可操作的原因", badCipher);

            var connStr = MySqlDbExecutor.BuildConnectionString(profile, "s3cret", 10);
            Check("连接串字段齐全",
                connStr.Contains("Server=10.0.0.5", StringComparison.OrdinalIgnoreCase)
                && connStr.Contains("Port=3306", StringComparison.OrdinalIgnoreCase)
                && connStr.Contains("Database=mes", StringComparison.OrdinalIgnoreCase)
                && connStr.Contains("User ID=vm", StringComparison.OrdinalIgnoreCase),
                connStr);
            Check("连接串关闭用户变量（@name 只当参数）",
                connStr.Contains("Allow User Variables=False", StringComparison.OrdinalIgnoreCase), connStr);

            Section("MySQL/MariaDB：profile 校验");

            var noServer = new DatabaseProfile { Name = "缺服务器", Provider = DbProviderKind.MySql, DatabaseName = "mes", UserId = "vm", Port = 3306, TimeoutSeconds = 5, MaxRows = 100 };
            Check("缺服务器被拒", !service.TrySaveProfile(noServer, out err) && (err ?? "").Contains("服务器"), err);

            var badPort = new DatabaseProfile { Name = "坏端口", Provider = DbProviderKind.MySql, Server = "h", Port = 70000, DatabaseName = "mes", UserId = "vm", TimeoutSeconds = 5, MaxRows = 100 };
            Check("端口越界被拒", !service.TrySaveProfile(badPort, out err) && (err ?? "").Contains("端口"), err);

            var noDb = new DatabaseProfile { Name = "缺库名", Provider = DbProviderKind.MySql, Server = "h", Port = 3306, UserId = "vm", TimeoutSeconds = 5, MaxRows = 100 };
            Check("缺数据库名被拒", !service.TrySaveProfile(noDb, out err) && (err ?? "").Contains("数据库名"), err);

            var noUser = new DatabaseProfile { Name = "缺用户", Provider = DbProviderKind.MySql, Server = "h", Port = 3306, DatabaseName = "mes", TimeoutSeconds = 5, MaxRows = 100 };
            Check("缺用户名被拒", !service.TrySaveProfile(noUser, out err) && (err ?? "").Contains("用户名"), err);

            Section("MySQL/MariaDB：语句方言校验");

            Check("SELECT 通过", StatementValidator.ValidateSql("SELECT * FROM result", DbStatementKind.Query, DbProviderKind.MySql, out err), err);
            Check("PRAGMA 在 MySQL 下被拒",
                !StatementValidator.ValidateSql("PRAGMA table_info(result)", DbStatementKind.Query, DbProviderKind.MySql, out err)
                && (err ?? "").Contains("SQLite"), err);
            Check("FOR UPDATE 被拒",
                !StatementValidator.ValidateSql("SELECT * FROM result FOR UPDATE", DbStatementKind.Query, DbProviderKind.MySql, out err)
                && (err ?? "").Contains("锁定读"), err);
            Check("UPDATE 带 WHERE 通过",
                StatementValidator.ValidateSql("UPDATE result SET ok = 0 WHERE id = @id", DbStatementKind.Execute, DbProviderKind.MySql, out err), err);
            Check("UPDATE 无 WHERE 被拒",
                !StatementValidator.ValidateSql("UPDATE result SET ok = 0", DbStatementKind.Execute, DbProviderKind.MySql, out err)
                && (err ?? "").Contains("WHERE"), err);
            Check("REPLACE 归写一行",
                StatementValidator.ValidateSql("REPLACE INTO result(lot) VALUES(@lot)", DbStatementKind.WriteRow, DbProviderKind.MySql, out err), err);
            Check("LOAD DATA 被拒",
                !StatementValidator.ValidateSql("LOAD DATA INFILE 'x' INTO TABLE result", DbStatementKind.WriteRow, DbProviderKind.MySql, out err)
                && (err ?? "").Contains("LOAD DATA"), err);
            Check("CALL 被拒",
                !StatementValidator.ValidateSql("CALL do_something()", DbStatementKind.Query, DbProviderKind.MySql, out err)
                && (err ?? "").Contains("CALL"), err);
            Check("# 行注释被剥离",
                StatementValidator.ValidateSql("SELECT 1 # 注释里有 UPDATE 关键字\n", DbStatementKind.Query, DbProviderKind.MySql, out err), err);

            var roProfile = new DatabaseProfile
            {
                Name = "只读 MES",
                Provider = DbProviderKind.MySql,
                Server = "h",
                Port = 3306,
                DatabaseName = "mes",
                UserId = "ro",
                OpenMode = DbOpenMode.ReadOnly,
                TimeoutSeconds = 5,
                MaxRows = 100
            };
            var writeStatement = new NamedStatement
            {
                Name = "Write",
                Kind = DbStatementKind.WriteRow,
                Sql = "INSERT INTO result(lot) VALUES(@lot)",
                Parameters = new List<DbStatementParameter> { new() { Name = "lot", DbType = DbTypeNames.Text, IsRequired = true } }
            };
            Check("只读 MySQL 档拒绝写语句",
                !StatementValidator.Validate(writeStatement, roProfile, out err) && (err ?? "").Contains("只读"), err);

            Section("MySQL/MariaDB：错误分类");

            Check("1062 → 约束", DbErrorClassifier.ClassifyMySqlNumber(1062) == DbErrorKind.Constraint);
            Check("1045 → 权限", DbErrorClassifier.ClassifyMySqlNumber(1045) == DbErrorKind.Permission);
            Check("1146 → 找不到", DbErrorClassifier.ClassifyMySqlNumber(1146) == DbErrorKind.NotFound);
            Check("1213 → 死锁", DbErrorClassifier.ClassifyMySqlNumber(1213) == DbErrorKind.Deadlock);
            Check("2013 → 暂时性", DbErrorClassifier.ClassifyMySqlNumber(2013) == DbErrorKind.Transient);
            Check("3024 → 超时", DbErrorClassifier.ClassifyMySqlNumber(3024) == DbErrorKind.Timeout);
            Check("未知号码 → Unknown", DbErrorClassifier.ClassifyMySqlNumber(9999) == DbErrorKind.Unknown);

            Section("MySQL/MariaDB：真实服务器（env-gated）");

            var testConnection = Environment.GetEnvironmentVariable("VM_MYSQL_TEST");
            if (string.IsNullOrWhiteSpace(testConnection))
            {
                // 不是断言：方言/事务只能真库证明，没配就明确说出来，别假装验过
                Console.WriteLine("  [INFO] 未设置 VM_MYSQL_TEST，跳过真实 MySQL 端到端（连接/查询/事务需真库证明）");
            }
            else
            {
                var builder = new MySqlConnector.MySqlConnectionStringBuilder(testConnection);
                var live = new DatabaseProfile
                {
                    ProfileId = Guid.NewGuid(),
                    Name = "env MySQL",
                    Provider = DbProviderKind.MySql,
                    Server = builder.Server,
                    Port = (int)builder.Port,
                    DatabaseName = builder.Database,
                    UserId = builder.UserID,
                    OpenMode = DbOpenMode.ReadWrite,
                    TimeoutSeconds = 10,
                    MaxRows = 100
                };

                var liveService = new DatabaseService(log, jsonPath + ".live.json");
                string version = null;
                var saved = liveService.TrySaveProfile(live, builder.Password, out err);
                var tested = saved && liveService.TryTest(live.ProfileId, out version, out _, out err);
                Check("env：保存并连接", saved && tested, err ?? version);

                var ping = new NamedStatement
                {
                    ProfileId = live.ProfileId,
                    Name = "Ping",
                    Kind = DbStatementKind.Query,
                    Sql = "SELECT 1 AS ok",
                    Parameters = new List<DbStatementParameter>()
                };
                Check("env：保存查询语句", liveService.TrySaveStatement(ping, out err), err);
                Check("env：查询返回 1",
                    liveService.TryQuery(ping.StatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out var liveResult, out err)
                    && Convert.ToInt64(liveResult.Rows[0][0]) == 1,
                    err);
            }
        }

        // ==================================================================== 插件层

        private static void PluginChecks()
        {
            Section("插件层：自定义配置视图接线");

            // 宿主按 `pluginInstance is IPluginCustomViewProvider` 决定用插件视图还是通用面板。
            // 只写 GetConfigView 方法而不声明接口 = 编译能过、宿主永远走通用面板
            // （现场表现：弹出 ProfileId/StatementId 这些内部字段，且都是只读）。
            foreach (var type in new[] { typeof(DatabaseQueryPlugin), typeof(DatabaseExecutePlugin), typeof(DatabaseWriteRowPlugin) })
            {
                Check($"{type.Name} 实现 IPluginCustomViewProvider",
                    typeof(IPluginCustomViewProvider).IsAssignableFrom(type));

                // 工具箱/流程栏图标来自 DisplayAttribute.ShortName；缺了就是空图标框
                var display = (System.ComponentModel.DataAnnotations.DisplayAttribute?)Attribute.GetCustomAttribute(
                    type, typeof(System.ComponentModel.DataAnnotations.DisplayAttribute));
                Check($"{type.Name} 有图标（Display.ShortName）",
                    !string.IsNullOrWhiteSpace(display?.ShortName), display?.ShortName ?? "(空)");
            }

            Section("插件层：老方案图标补全");

            var tool = new ToolItemModel
            {
                Name = "数据库查询",
                Category = "数据处理",
                Icon = "\uf002",
                ModuleTypeName = "Plugin.Database.DatabaseQueryPlugin, Plugin.Database"
            };
            var provider = new FakePluginProvider();
            provider.RegisterModule(tool);

            var solution = new SolutionModel();
            var flow = new FlowModel { FlowName = "图标补全" };
            var emptyIcon = new ActionStep("", "数据库查询", tool.ModuleTypeName, "空图标");
            var filledIcon = new ActionStep("\uf999", "数据库查询", tool.ModuleTypeName, "已有图标");
            var unknownType = new ActionStep("", "未知插件", "No.Such.Type, Nowhere", "未知插件");
            var nestedStep = new ActionStep("", "数据库查询", tool.ModuleTypeName, "嵌套步骤");
            var forStep = new ForStep("", "循环", "", "循环");
            forStep.Children.Add(new StepCollection());
            forStep.Children[0].Steps.Add(nestedStep);

            flow.Steps.Add(emptyIcon);
            flow.Steps.Add(filledIcon);
            flow.Steps.Add(unknownType);
            flow.Steps.Add(forStep);
            solution.Flows.Add(flow);

            StepIconBackfill.Apply(solution, provider);

            Check("空图标补成插件图标", emptyIcon.Icon == "\uf002", emptyIcon.Icon);
            Check("分类也一并补上", emptyIcon.Category == "数据处理", emptyIcon.Category);
            Check("已有图标不被覆盖", filledIcon.Icon == "\uf999", filledIcon.Icon);
            Check("未知插件类型跳过（旧方案引用已卸载插件是合法状态）", string.IsNullOrEmpty(unknownType.Icon), unknownType.Icon);
            Check("嵌套容器里的步骤也补", nestedStep.Icon == "\uf002", nestedStep.Icon);

            Section("插件层：端口重建与参数映射");

            var fake = new FakeDatabase();
            var plugin = new DatabaseWriteRowPlugin();
            var step = new FakeStepData();

            plugin.ApplyConfigValues(step);
            plugin.SetConfigContext(new PluginConfigContext { Database = fake });
            plugin.SelectedProfile = plugin.ProfileOptions.FirstOrDefault();
            plugin.SelectedStatement = plugin.StatementOptions.FirstOrDefault();

            Check("配置上下文能列出 profile", plugin.ProfileOptions.Count == 1, "count=" + plugin.ProfileOptions.Count);
            Check("选中 profile 后能列出语句", plugin.StatementOptions.Count == 1, "count=" + plugin.StatementOptions.Count);
            Check("选中语句后参数端口按定义重建",
                plugin.Inputs.ContainsKey("lot") && plugin.Inputs.ContainsKey("score") && plugin.Inputs.ContainsKey("ok"),
                string.Join(",", plugin.Inputs.Keys));
            Check("参数行与端口一一对应", plugin.ParameterRows.Count == 3, "rows=" + plugin.ParameterRows.Count);
            Check("输出行固定 3 个", plugin.OutputRows.Count == 3, "rows=" + plugin.OutputRows.Count);

            // 常量值：走 InputValues（base.ApplyConfigValues 先跑、端口后建，重建时要回灌）
            var step2 = new FakeStepData();
            step2.SetInputValue("lot", "L9");
            step2.SetInputValue("score", 0.25);
            step2.SetInputValue("ok", true);
            plugin.ApplyConfigValues(step2);
            Check("端口重建后常量值回灌",
                Equals(plugin.Inputs["lot"].Value, "L9") && Equals(plugin.Inputs["ok"].Value, true),
                "lot=" + plugin.Inputs["lot"].Value);

            var ctx = new VisionMaster.Services.ExecutionContext(new FakeLog()) { Database = fake };
            fake.ExecResult = new DbExecutionResult { AffectedRows = 1, ElapsedMs = 2.5 };
            fake.ExecOk = true;

            var ok = plugin.Execute(ctx);
            Check("写行成功", ok && plugin.Accepted.TypedValue && plugin.AffectedRows.TypedValue == 1, plugin.ErrorMessage.TypedValue);
            Check("参数按端口值传给服务",
                fake.LastParameters != null
                && Equals(fake.LastParameters.First(p => p.Name == "lot").Value, "L9")
                && Equals(fake.LastParameters.First(p => p.Name == "score").Value, 0.25),
                "params=" + string.Join(",", fake.LastParameters?.Select(p => p.Name + "=" + p.Value) ?? Array.Empty<string>()));

            // FailFast=false：只红不停线
            var soft = new DatabaseWriteRowPlugin { FailFast = false };
            soft.ApplyConfigValues(new FakeStepData());
            soft.SetConfigContext(new PluginConfigContext { Database = fake });
            soft.SelectedProfile = soft.ProfileOptions.First();
            soft.SelectedStatement = soft.StatementOptions.First();
            fake.ExecOk = false;
            fake.Error = "模拟写库失败";
            var softOk = soft.Execute(ctx);
            Check("FailFast=false：返回 false 且写 ErrorMessage",
                !softOk && (soft.ErrorMessage.TypedValue ?? "").Contains("模拟写库失败"), soft.ErrorMessage.TypedValue);

            // FailFast=true：抛（配合 RethrowOnException 中断当轮）
            var hard = new DatabaseWriteRowPlugin { FailFast = true };
            hard.ApplyConfigValues(new FakeStepData());
            hard.SetConfigContext(new PluginConfigContext { Database = fake });
            hard.SelectedProfile = hard.ProfileOptions.First();
            hard.SelectedStatement = hard.StatementOptions.First();

            var threw = false;
            try { hard.Execute(ctx); }
            catch (DatabaseStepException) { threw = true; }
            Check("FailFast=true：抛 DatabaseStepException（真正停线）", threw);

            // 查询输出映射
            fake.QueryOk = true;
            fake.QueryResult = new DbQueryResult
            {
                Columns = new[] { "id", "score", "blob", "none" },
                Rows = new List<object?[]> { new object?[] { 1L, 0.5, new byte[] { 1, 2 }, null } },
                RowCount = 1,
                Truncated = true,
                ElapsedMs = 3.5
            };
            var query = new DatabaseQueryPlugin();
            query.ApplyConfigValues(new FakeStepData());
            query.SetConfigContext(new PluginConfigContext { Database = fake });
            query.SelectedProfile = query.ProfileOptions.First();
            query.SelectedStatement = query.StatementOptions.First();
            Check("查询执行成功", query.Execute(ctx), query.ErrorMessage.TypedValue);
            Check("Scalar = 第一行第一列", Equals(query.Scalar.TypedValue, 1L), "scalar=" + query.Scalar.TypedValue);
            Check("RowCount/Truncated/ElapsedMs 映射",
                query.RowCount.TypedValue == 1 && query.Truncated.TypedValue && Math.Abs(query.ElapsedMs.TypedValue - 3.5) < 0.001,
                "rows=" + query.RowCount.TypedValue);
            Check("Columns 逗号分隔", query.Columns.TypedValue == "id,score,blob,none", query.Columns.TypedValue);
            Check("RowsJson：数值仍是数值、null 仍是 null、字节转 Base64",
                query.RowsJson.TypedValue.Contains("\"id\":1")
                && query.RowsJson.TypedValue.Contains("\"none\":null")
                && query.RowsJson.TypedValue.Contains(Convert.ToBase64String(new byte[] { 1, 2 })),
                query.RowsJson.TypedValue);

            fake.QueryResult = new DbQueryResult { Columns = new[] { "id" }, Rows = new List<object?[]>(), RowCount = 0 };
            Check("查询 0 行不算失败", query.Execute(ctx) && query.RowCount.TypedValue == 0 && query.Scalar.TypedValue == null,
                query.ErrorMessage.TypedValue);

            Section("插件层：确认校验与语句切换");

            var confirmPlugin = new DatabaseWriteRowPlugin();
            var confirmStep = new FakeStepData();
            confirmPlugin.ApplyConfigValues(confirmStep);
            confirmPlugin.SetConfigContext(new PluginConfigContext { Database = fake });
            confirmPlugin.SelectedProfile = confirmPlugin.ProfileOptions.First();
            confirmPlugin.SelectedStatement = confirmPlugin.StatementOptions.First();

            var valid = confirmPlugin.ValidateForConfirm(confirmStep, out var confirmError);
            Check("必填参数为空 → 确认被拒并点名 @lot",
                !valid && (confirmError ?? "").Contains("@lot"), confirmError);

            confirmPlugin.Inputs["lot"].Value = "L1";
            Check("填了必填参数 → 确认通过", confirmPlugin.ValidateForConfirm(confirmStep, out confirmError), confirmError);

            confirmPlugin.OnConfirm(confirmStep);
            Check("确认后值写进 InputValues", Equals(confirmStep.InputValues["lot"], "L1"),
                "lot=" + confirmStep.InputValues["lot"]);

            // 换语句：旧参数键要被清掉
            confirmStep.SetInputValue("score", 0.9);
            fake.Statement = new DbStatementInfo
            {
                StatementId = Guid.NewGuid(),
                ProfileId = fake.Profile.ProfileId,
                Name = "WriteOther",
                Kind = DbStatementKind.WriteRow,
                SqlPreview = "INSERT INTO other(a) VALUES (@a)",
                Parameters = new[] { new DbStatementParameter { Name = "a", DbType = DbTypeNames.Text, IsRequired = true } },
                IsEnabled = true
            };
            confirmPlugin.ReloadOptions();
            confirmPlugin.SelectedStatement = confirmPlugin.StatementOptions.First();
            Check("换语句后端口换成新参数", confirmPlugin.Inputs.ContainsKey("a") && !confirmPlugin.Inputs.ContainsKey("lot"),
                string.Join(",", confirmPlugin.Inputs.Keys));
            Check("换语句后旧参数键被清掉",
                !confirmStep.InputValues.ContainsKey("lot") && !confirmStep.InputValues.ContainsKey("score"),
                string.Join(",", confirmStep.InputValues.Keys));

            Section("插件层：配置态测试语句");

            fake.TestOk = true;
            fake.TestResult = new DbTestResult
            {
                Execution = new DbExecutionResult { AffectedRows = 1, ElapsedMs = 4.0 },
                RolledBack = true,
                ElapsedMs = 4.0
            };
            var testPlugin = new DatabaseExecutePlugin();
            testPlugin.ApplyConfigValues(new FakeStepData());
            testPlugin.SetConfigContext(new PluginConfigContext { Database = fake });
            testPlugin.SelectedProfile = testPlugin.ProfileOptions.First();
            fake.Statement = new DbStatementInfo
            {
                StatementId = Guid.NewGuid(),
                ProfileId = fake.Profile.ProfileId,
                Name = "MarkNg",
                Kind = DbStatementKind.Execute,
                SqlPreview = "UPDATE result SET ok = 0 WHERE id > 0",
                Parameters = Array.Empty<DbStatementParameter>(),
                IsEnabled = true
            };
            testPlugin.ReloadOptions();
            testPlugin.SelectedStatement = testPlugin.StatementOptions.First();
            testPlugin.TestAsync().GetAwaiter().GetResult();
            Check("测试成功 banner 明示已回滚",
                testPlugin.TestLevelKey == "ok" && testPlugin.TestMessage.Contains("已回滚"),
                testPlugin.TestMessage);

            fake.TestOk = false;
            fake.Error = "模拟测试失败";
            testPlugin.TestAsync().GetAwaiter().GetResult();
            Check("测试失败 banner 带原因",
                testPlugin.TestLevelKey == "fail" && testPlugin.TestMessage.Contains("模拟测试失败"),
                testPlugin.TestMessage);

            Section("插件层：只读与类型不匹配的预判");

            fake.Profile = new DbProfileInfo
            {
                ProfileId = Guid.NewGuid(),
                Name = "只读库",
                OpenMode = DbOpenMode.ReadOnly,
                TimeoutSeconds = 5,
                MaxRows = 100,
                IsEnabled = true
            };
            fake.Statement = new DbStatementInfo
            {
                StatementId = Guid.NewGuid(),
                ProfileId = fake.Profile.ProfileId,
                Name = "RoQuery",
                Kind = DbStatementKind.Query,
                SqlPreview = "SELECT 1",
                Parameters = Array.Empty<DbStatementParameter>(),
                IsEnabled = true
            };

            var roWritePlugin = new DatabaseWriteRowPlugin();
            roWritePlugin.ApplyConfigValues(new FakeStepData());
            roWritePlugin.SetConfigContext(new PluginConfigContext { Database = fake });
            roWritePlugin.SelectedProfile = roWritePlugin.ProfileOptions.First();
            roWritePlugin.SelectedStatement = roWritePlugin.StatementOptions.First();
            Check("只读 profile + 写步骤 → 配置问题可上屏",
                roWritePlugin.HasConfigProblem && (roWritePlugin.ConfigProblem ?? "").Contains("只读"),
                roWritePlugin.ConfigProblem);

            fake.Statement = new DbStatementInfo
            {
                StatementId = Guid.NewGuid(),
                ProfileId = fake.Profile.ProfileId,
                Name = "StillQuery",
                Kind = DbStatementKind.Query,
                SqlPreview = "SELECT 1",
                Parameters = Array.Empty<DbStatementParameter>(),
                IsEnabled = true
            };
            fake.Profile = new DbProfileInfo
            {
                ProfileId = Guid.NewGuid(),
                Name = "可写库",
                OpenMode = DbOpenMode.ReadWrite,
                TimeoutSeconds = 5,
                MaxRows = 100,
                IsEnabled = true
            };

            var kindPlugin = new DatabaseExecutePlugin();
            kindPlugin.ApplyConfigValues(new FakeStepData());
            kindPlugin.SetConfigContext(new PluginConfigContext { Database = fake });
            kindPlugin.SelectedProfile = kindPlugin.ProfileOptions.First();
            kindPlugin.SelectedStatement = kindPlugin.StatementOptions.First();
            Check("语句类型不匹配 → 配置问题点名需要的类型",
                kindPlugin.HasConfigProblem && (kindPlugin.ConfigProblem ?? "").Contains("执行"),
                kindPlugin.ConfigProblem);
        }

        // ==================================================================== 静态契约

        private static void StaticChecks(string root)
        {
            Section("静态契约：Provider 边界与产物");

            if (string.IsNullOrEmpty(root))
            {
                Check("定位仓库根", false, "找不到 VisionMaster.sln");
                return;
            }

            var pluginDir = Path.Combine(root, "Plugins", "Plugin.Database");
            var csproj = File.ReadAllText(Path.Combine(pluginDir, "Plugin.Database.csproj"));
            // 只看引用行：注释里出现 "Microsoft.Data.Sqlite"/"Core.Database" 是在解释边界，不算违规
            var packageRefs = csproj.Split('\n')
                .Where(l => l.Contains("<PackageReference", StringComparison.Ordinal))
                .ToList();
            var projectRefs = csproj.Split('\n')
                .Where(l => l.Contains("<ProjectReference", StringComparison.Ordinal))
                .ToList();

            Check("插件工程不引用任何 Provider 包",
                !packageRefs.Any(l => l.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                                      || l.Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase)
                                      || l.Contains("SqlClient", StringComparison.OrdinalIgnoreCase)),
                string.Join(" | ", packageRefs));
            Check("插件工程不引用 Core.Database",
                !projectRefs.Any(l => l.Contains("Core.Database", StringComparison.OrdinalIgnoreCase)),
                string.Join(" | ", projectRefs));

            var coreCsproj = File.ReadAllText(Path.Combine(root, "Services", "Core.Database", "Core.Database.csproj"));
            Check("Core.Database 引用 MySqlConnector",
                coreCsproj.Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase));
            Check("Core.Database 引用 DPAPI 包",
                coreCsproj.Contains("ProtectedData", StringComparison.OrdinalIgnoreCase));

            var sources = Directory.GetFiles(pluginDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .ToList();
            // 只认"真的 new 了 Provider 类型"：DbCommandOptions 这类契约名不算
            var forbidden = new System.Text.RegularExpressions.Regex(
                @"\bnew\s+(SqliteConnection|SqliteCommand|SqlCommand|DbCommand|DbDataReader)\b");
            var hits = new List<string>();
            foreach (var file in sources)
            {
                var text = File.ReadAllText(file);
                if (forbidden.IsMatch(text)) hits.Add(Path.GetFileName(file));
            }
            Check("插件源码没有 Provider 类型/SQL 命令构造", hits.Count == 0, string.Join(",", hits));

            var moduleDll = Path.Combine(root, "Modules", "Plugin.Database.dll");
            Check("Plugin.Database.dll 已投递到 Modules\\", File.Exists(moduleDll), moduleDll);
            if (File.Exists(moduleDll))
            {
                var dllTime = File.GetLastWriteTimeUtc(moduleDll);
                var newest = sources.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                Check("产物不旧于源码（红线③）", dllTime >= newest, $"dll={dllTime:O} src={newest:O}");

                try
                {
                    var refs = System.Reflection.Assembly.LoadFrom(moduleDll)
                        .GetReferencedAssemblies()
                        .Select(a => a.Name ?? string.Empty)
                        .ToList();
                    Check("插件 DLL 不引用 Core.Database",
                        refs.All(n => !string.Equals(n, "Core.Database", StringComparison.OrdinalIgnoreCase)),
                        string.Join(",", refs));
                    Check("插件 DLL 不引用 Provider",
                        refs.All(n => !n.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                                      && !n.Contains("SqlClient", StringComparison.OrdinalIgnoreCase)),
                        string.Join(",", refs));
                }
                catch (Exception ex)
                {
                    Check("读取插件 DLL 的程序集引用", false, ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        // ==================================================================== 辅助

        private static List<DbParameterValue> Params(params (string Name, object Value)[] items) =>
            items.Select(i => new DbParameterValue(i.Name, i.Value)).ToList();

        private static int CountRows(DatabaseService service, Guid countStatementId)
        {
            return service.TryQuery(countStatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out var result, out _)
                   && result.Rows.Count > 0
                ? Convert.ToInt32(result.Rows[0][0])
                : -1;
        }

        private static int CountOk(DatabaseService service, Guid readAllStatementId)
        {
            return service.TryQuery(readAllStatementId, Array.Empty<DbParameterValue>(), DbCommandOptions.Default, out var result, out _)
                ? result.Rows.Count(r => Convert.ToInt64(r[3]) == 1)
                : -1;
        }

        private static void SeedDatabase(string path)
        {
            using var conn = new SqliteConnection("Data Source=" + path);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS result(" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "lot TEXT NOT NULL," +
                "score REAL," +
                "ok INTEGER," +
                "created TEXT)";
            cmd.ExecuteNonQuery();
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("---- " + title + " ----");
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine("  [" + (ok ? "PASS" : "FAIL") + "] " + name
                              + (string.IsNullOrEmpty(detail) ? string.Empty : "  -> " + detail));
        }

        private static void Finish()
        {
            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : $">>> 存在 {_fail} 项失败 <<<");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }
    }

    // ======================================================================== 假件

    internal sealed class FakeLog : ILogService
    {
        public List<string> Lines { get; } = new();

        public void Success(params string[] messages) => Add("OK", messages);
        public void Error(params Exception[] messages) { foreach (var e in messages) Lines.Add("ERR " + e.Message); }
        public void Error(params string[] messages) => Add("ERR", messages);
        public void Info(params string[] messages) => Add("INF", messages);
        public void Warn(params string[] messages) => Add("WRN", messages);

        private void Add(string level, string[] messages)
        {
            foreach (var m in messages) Lines.Add(level + " " + m);
        }
    }

    internal sealed class FakeDatabase : IDatabaseAccess
    {
        public DbProfileInfo Profile = new()
        {
            ProfileId = Guid.NewGuid(),
            Name = "假库",
            OpenMode = DbOpenMode.ReadWrite,
            TimeoutSeconds = 5,
            MaxRows = 100,
            IsEnabled = true
        };

        public DbStatementInfo Statement = new()
        {
            ProfileId = Guid.Empty,
            Name = "WriteResult",
            Kind = DbStatementKind.WriteRow,
            SqlPreview = "INSERT INTO result(lot, score, ok) VALUES (@lot, @score, @ok)",
            Parameters = new[]
            {
                new DbStatementParameter { Name = "lot", DbType = DbTypeNames.Text, IsRequired = true },
                new DbStatementParameter { Name = "score", DbType = DbTypeNames.Real },
                new DbStatementParameter { Name = "ok", DbType = DbTypeNames.Boolean }
            },
            IsEnabled = true
        };

        public bool QueryOk = true;
        public bool ExecOk = true;
        public bool TestOk = true;
        public string Error = "假数据库错误";

        public DbQueryResult QueryResult = new() { Columns = new[] { "id" }, Rows = new List<object?[]>(), RowCount = 0 };
        public DbExecutionResult ExecResult = new() { AffectedRows = 1, ElapsedMs = 1 };
        public DbTestResult TestResult = new() { Execution = new DbExecutionResult { AffectedRows = 1 }, RolledBack = true };

        public List<DbParameterValue> LastParameters;

        public FakeDatabase()
        {
            Statement = new DbStatementInfo
            {
                StatementId = Statement.StatementId == Guid.Empty ? Guid.NewGuid() : Statement.StatementId,
                ProfileId = Profile.ProfileId,
                Name = Statement.Name,
                Kind = Statement.Kind,
                SqlPreview = Statement.SqlPreview,
                Parameters = Statement.Parameters,
                IsEnabled = true
            };
        }

        public IReadOnlyList<DbProfileInfo> ListProfiles() => new[] { Profile };

        public bool TryGetProfile(Guid profileId, out DbProfileInfo info, out string error)
        {
            info = Profile;
            error = null;
            return Profile.ProfileId == profileId;
        }

        public IReadOnlyList<DbStatementInfo> ListStatements(Guid profileId) => new[] { Statement };

        public bool TryGetStatement(Guid statementId, out DbStatementInfo info, out string error)
        {
            info = Statement;
            error = null;
            return Statement.StatementId == statementId;
        }

        public bool TryTest(Guid profileId, out string versionText, out double elapsedMs, out string error)
        {
            versionText = "3.41.2";
            elapsedMs = 1;
            error = null;
            return true;
        }

        public bool TryQuery(Guid statementId, IReadOnlyList<DbParameterValue> parameters, DbCommandOptions options,
            out DbQueryResult result, out string error)
        {
            LastParameters = parameters?.ToList();
            result = QueryResult;
            error = QueryOk ? null : Error;
            return QueryOk;
        }

        public bool TryExecute(Guid statementId, IReadOnlyList<DbParameterValue> parameters, DbCommandOptions options,
            out DbExecutionResult result, out string error)
        {
            LastParameters = parameters?.ToList();
            result = ExecResult;
            error = ExecOk ? null : Error;
            return ExecOk;
        }

        public bool TryExecuteBatch(Guid statementId, IReadOnlyList<IReadOnlyList<DbParameterValue>> rows,
            DbCommandOptions options, out DbBatchResult result, out string error)
        {
            result = new DbBatchResult { SucceededRows = rows?.Count ?? 0 };
            error = ExecOk ? null : Error;
            return ExecOk;
        }

        public bool TryTestStatement(Guid statementId, IReadOnlyList<DbParameterValue> parameters,
            DbCommandOptions options, bool commitForReal, out DbTestResult result, out string error)
        {
            LastParameters = parameters?.ToList();
            result = TestResult;
            error = TestOk ? null : Error;
            return TestOk;
        }
    }

    internal sealed class FakePluginProvider : IPluginProvider
    {
        private readonly Dictionary<string, ToolItemModel> _modules = new(StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<string, ToolItemModel> Empty = new Dictionary<string, ToolItemModel>();

        public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins => _modules;
        public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins => Empty;
        public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins => Empty;
        public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins => Empty;

        public void RegisterModule(ToolItemModel plugin)
        {
            if (plugin != null) _modules[plugin.Name] = plugin;
        }

        public void RegisterCamera(ToolItemModel plugin) { }
        public void RegisterLaser(ToolItemModel plugin) { }
        public void RegisterMotion(ToolItemModel plugin) { }

        public ToolItemModel GetModule(string name) => _modules.TryGetValue(name, out var value) ? value : null;
        public ToolItemModel GetCamera(string name) => null;
        public ToolItemModel GetLaser(string name) => null;
        public ToolItemModel GetMotion(string name) => null;
    }

    internal sealed class FakeStepData : IStepConfigData
    {
        public Guid StepId { get; } = Guid.NewGuid();
        public string Icon => "icon";
        public string StepName => "数据库写行";
        public string Description => "断言用步骤";
        public Dictionary<string, object> InputValues { get; } = new();
        public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();

        private readonly Dictionary<string, LinkReference> _links = new();

        public void SetInputValue(string key, object value) => InputValues[key] = value;

        public void RemoveInputValue(string key) => InputValues.Remove(key);

        public bool IsLinked(string inputPortName) => _links.ContainsKey(inputPortName);

        public string GetLinkedAddress(string inputPortName) =>
            _links.TryGetValue(inputPortName, out var link) ? link.DisplayAddress : null;

        public LinkReference GetLink(string inputPortName) =>
            _links.TryGetValue(inputPortName, out var link) ? link : null;

        public void SetLink(string inputPortName, LinkReference link) => _links[inputPortName] = link;

        public void RemoveLink(string inputPortName) => _links.Remove(inputPortName);
    }
}
