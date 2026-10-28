using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Core.Events;
using Newtonsoft.Json.Linq;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    /// <summary>
    /// [SL] 方案级账号本（2026-10-24）。
    ///
    /// 分节标题刻意不写成裸 "[SL]"：ScadaChecks 里 SolutionListAuditChecks 已经占了这个前缀
    /// （那是"方案列表审计"，与本组无关），撞名会让两组不相干的断言在日志里混成一节。
    /// 本组一律带编号：[SL-a*] 是 B1 的结构/身份前置，[SL1]…[SL25] 是规格书 §6.1 的原编号。
    ///
    /// 放 ScadaChecks 而不是新建宿主：本组要读 ScadaUserStore / SolutionPaths /
    /// SolutionService / SolutionAssetFolders，与 [US] [UC] [AU] [SF] 同一批引用集，
    /// 分家只会多养一个宿主（R182 的 gate 表也要多一行）。
    /// </summary>
    internal partial class Program
    {
        private static void SolutionUserChecks()
        {
            SlStructureAndIdentity();
            SlAccountBook();
            SlGroupsAndCatalog();
            SlContracts();
            SlAuditTagAndReader();
            SlSessionBoundary();
            SlSolutionSwitchCarryOver();
            SlDebugAutoLogin();
            SlPermissionMatrix();
            SlFlowBarGate();
        }

        private static string NewSolutionRoot(string tag)
        {
            var root = Path.Combine(Path.GetTempPath(), "sl-" + tag + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        /// <summary>
        /// 在指定方案根下造一份可登录的账号本（出厂 admin + 指定账号）。
        /// 用独立 store 写盘，不污染正在被测的那一份。
        /// </summary>
        private static void SeedSolutionBook(string root, string userName, string password, string groupId)
        {
            var store = new ScadaUserStore(
                storePath: null, audit: null, actorProvider: null,
                minPasswordLengthProvider: null, startUnbound: true,
                permissionReferenceCounter: _ => 0);
            if (!store.BindSolution(Guid.NewGuid(), Path.GetFileName(root), root, out var error))
                throw new InvalidOperationException("种子账号本绑定失败：" + error);
            if (!store.TryAddUser(userName, password, groupId, out error))
                throw new InvalidOperationException("种子账号创建失败：" + error);
        }

        private static SolutionModel NewSolution(string name, string root)
        {
            Directory.CreateDirectory(root);
            return new SolutionModel
            {
                SolutionName = name,
                SolutionFilePath = Path.Combine(root, name + ".vms"),
                SolutionId = Guid.NewGuid(),
                IsIdentityPersisted = true,
            };
        }

        private static string ReadAudit(string dir)
        {
            try
            {
                return string.Join("\n", Directory.GetFiles(dir, "*.csv")
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .Select(File.ReadAllText));
            }
            catch (IOException) { return string.Empty; }
            catch (UnauthorizedAccessException) { return string.Empty; }
        }

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static ScadaUserStore NewUnboundStore(Func<string, int>? permissionReferenceCounter = null,
                                                      Func<string?>? actorProvider = null)
            => new ScadaUserStore(null, null, actorProvider, null,
                                  startUnbound: true, permissionReferenceCounter: permissionReferenceCounter);

        private static void SeedAccountFile(string path,
                                            params (string Name, string Password, string GroupId)[] users)
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var seedPath = path + ".seed";
            var seed = new ScadaUserStore(seedPath);
            foreach (var u in users)
            {
                if (u.Name == ScadaUserStore.DefaultAdminName) continue;
                if (!seed.TryAddUser(u.Name, u.Password, u.GroupId, out var err))
                    throw new InvalidOperationException("夹具建账号失败：" + err);
            }
            File.Copy(seedPath, path, true);
            File.Delete(seedPath);
        }

        private static void SeedLegacyRoleAccountFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                "[{\"Name\":\"op9\",\"Salt\":\"AAAA\",\"Hash\":\"BBBB\",\"Iterations\":100000,\"Role\":2}]",
                new UTF8Encoding(false));
        }

        private static void SlAccountBook()
        {
            Section("[SL] 方案级三文件账号本（状态机 / 绑定 / 旧格式 fail-closed / 回滚）");

            {
                var store = NewUnboundStore();
                Check("[SL1-a] 启动未绑定时 BookState=NoSolution",
                    store.BookState == ScadaAccountBookState.NoSolution, "实际：" + store.BookState);
                Check("[SL1-b] 未绑定时 BookSource=Unbound",
                    store.BookSource == ScadaBookSource.Unbound, "实际：" + store.BookSource);
                Check("[SL1-c] 未绑定时 admin/admin123 登录被拒且原因含「尚未打开方案」",
                    !store.TryValidate(ScadaUserStore.DefaultAdminName, ScadaUserStore.DefaultAdminPassword,
                        out _, out var err) && err != null && err.Contains("尚未打开方案"), "err=" + err);
                Check("[SL1-d] 未绑定时账号清单为空",
                    store.Users.Count == 0, "实际 " + store.Users.Count + " 条");
                Check("[SL1-e] 未绑定时目录/组仍是内置种子（3 模板 + 3 自定义示例）",
                    store.PermissionCatalog.Count == 15 && store.Groups.Count == 6,
                    $"catalog={store.PermissionCatalog.Count} groups={store.Groups.Count}");
            }

            {
                var store = NewUnboundStore();
                Check("[SL2-a] 绑定未保存方案成功",
                    store.BindSolution(Guid.NewGuid(), "新方案", null, out var e0), e0 ?? "");
                Check("[SL2-b] BookState=UnsavedSolution",
                    store.BookState == ScadaAccountBookState.UnsavedSolution, "实际：" + store.BookState);
                Check("[SL2-c] 内存里有出厂管理员且 GroupId=tpl-admin",
                    store.TryValidate(ScadaUserStore.DefaultAdminName, ScadaUserStore.DefaultAdminPassword,
                        out var admin, out var e1) && admin!.GroupId == "tpl-admin", e1 ?? "");

                var succeeded = new List<string>();
                if (store.TryAddUser("op1", "pass1234", "G-lineop", out _)) succeeded.Add("增");
                if (store.TryRemoveUser(ScadaUserStore.DefaultAdminName, out _)) succeeded.Add("删");
                if (store.TryChangeGroup(ScadaUserStore.DefaultAdminName, "G-general", out _)) succeeded.Add("改组");
                if (store.TryResetPassword(ScadaUserStore.DefaultAdminName, "newpass1", out _)) succeeded.Add("重置");
                if (store.TryChangePassword(ScadaUserStore.DefaultAdminName,
                        ScadaUserStore.DefaultAdminPassword, "newpass1", out _)) succeeded.Add("改密");
                Check("[SL2-d] 未保存方案时五个写方法全部被拒",
                    succeeded.Count == 0, "居然成功的：" + string.Join(",", succeeded));

                store.TryRemoveUser(ScadaUserStore.DefaultAdminName, out var e2);
                store.TryChangeGroup(ScadaUserStore.DefaultAdminName, "G-general", out var e3);
                store.TryResetPassword(ScadaUserStore.DefaultAdminName, "newpass1", out var e4);
                store.TryChangePassword(ScadaUserStore.DefaultAdminName,
                    ScadaUserStore.DefaultAdminPassword, "newpass1", out var e5);
                Check("[SL2-e] 拒绝原因统一含「先保存」",
                    new[] { e2, e3, e4, e5 }.All(t => t != null && t.Contains("先保存")),
                    string.Join(" | ", new[] { e2, e3, e4, e5 }));
                Check("[SL2-f] 未保存方案不写盘（StorePath 为空）",
                    string.IsNullOrEmpty(store.StorePath), "实际：" + store.StorePath);
            }

            {
                var root = NewSolutionRoot("sl3");
                try
                {
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL3", null, out _);
                    Check("[SL3-a] AttachSolutionFolder 成功", store.AttachSolutionFolder(root, out var e), e ?? "");

                    var users = ScadaUserStore.UsersFilePath(root);
                    var groups = ScadaUserStore.GroupsFilePath(root);
                    var perms = ScadaUserStore.PermissionsFilePath(root);
                    Check("[SL3-b] 落点是 <根>\\Users 下三个文件",
                        File.Exists(users) && File.Exists(groups) && File.Exists(perms),
                        $"{File.Exists(users)}/{File.Exists(groups)}/{File.Exists(perms)}");

                    var raw = File.ReadAllText(users);
                    Check("[SL3-c] 用户文件是裸数组且只有五字段（没有 Role）",
                        raw.TrimStart().StartsWith("[") && raw.TrimEnd().EndsWith("]")
                            && !raw.Contains("\"Role\"") && raw.Contains("\"GroupId\""),
                        raw.Length > 80 ? raw.Substring(0, 80) : raw);
                    Check("[SL3-d] 文件里不含明文密码",
                        !raw.Contains(ScadaUserStore.DefaultAdminPassword, StringComparison.Ordinal), "明文密码落盘");
                    Check("[SL3-e] Attach 后 BookState=PersistedSolution、BookSource=Solution",
                        store.BookState == ScadaAccountBookState.PersistedSolution
                            && store.BookSource == ScadaBookSource.Solution,
                        store.BookState + "/" + store.BookSource);
                    Check("[SL3-f] Attach 后能写账号（按 GroupId）",
                        store.TryAddUser("op1", "pass1234", "G-lineop", out var e2), e2 ?? "");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("sl4");
                try
                {
                    var store = NewUnboundStore();
                    Check("[SL4-a] 绑定已保存方案（无 Users 目录）成功",
                        store.BindSolution(Guid.NewGuid(), "SL4", root, out var e), e ?? "");
                    Check("[SL4-b] 打开路径不创建 Users 目录",
                        !Directory.Exists(Path.Combine(root, SolutionPaths.UsersFolderName)), "打开就写盘");
                    Check("[SL4-c] 绑定后状态是 PersistedSolution（能改账号）",
                        store.BookState == ScadaAccountBookState.PersistedSolution, "实际：" + store.BookState);
                    Check("[SL4-d] 无账号文件时 BookSource=Factory",
                        store.BookSource == ScadaBookSource.Factory, "实际：" + store.BookSource);
                }
                finally { TryDelete(root); }
            }

            {
                var rootA = NewSolutionRoot("sl5a");
                var rootB = NewSolutionRoot("sl5b");
                try
                {
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "A", rootA, out _);
                    store.AttachSolutionFolder(rootA, out _);
                    Check("[SL5-a] 方案 A 建 op1 成功",
                        store.TryAddUser("op1", "passA123", "G-lineop", out var e), e ?? "");
                    Check("[SL5-a2] 方案 A 内存与磁盘都有 op1",
                        store.Users.Count == 2
                        && File.ReadAllText(ScadaUserStore.UsersFilePath(rootA)).Contains("op1"),
                        "users=" + string.Join(",", store.Users.Select(u => u.Name))
                        + " / file=" + (File.Exists(ScadaUserStore.UsersFilePath(rootA))
                            ? File.ReadAllText(ScadaUserStore.UsersFilePath(rootA)).Replace("\r", "").Replace("\n", " ")
                            : "<missing>"));
                    Check("[SL5-b] 切到方案 B 绑定成功",
                        store.BindSolution(Guid.NewGuid(), "B", rootB, out var e2), e2 ?? "");
                    Check("[SL5-c] A 的账号在 B 里登录失败",
                        !store.TryValidate("op1", "passA123", out _, out _), "账号本没换");
                    Check("[SL5-d0] 切回 A 绑定成功",
                        store.BindSolution(Guid.NewGuid(), "A", rootA, out var e3), e3 ?? "");
                    Check("[SL5-d] 切回 A 后 op1 可登录且带 G-lineop 权限快照",
                        store.TryValidate("op1", "passA123", out var u, out var loginError)
                            && u!.GroupId == "G-lineop" && u.Permissions.Contains(ScadaPermissionKeys.Start),
                        "loginError=" + loginError + " source=" + store.BookSource
                        + " lastError=" + (store.LastLoadError ?? "<null>")
                        + " users=" + string.Join(",", store.Users.Select(x => x.Name + ":" + x.GroupId)));
                }
                finally { TryDelete(rootA); TryDelete(rootB); }
            }

            {
                var root = NewSolutionRoot("sl6");
                try
                {
                    var users = ScadaUserStore.UsersFilePath(root);
                    SeedLegacyRoleAccountFile(users);
                    var before = File.ReadAllBytes(users);

                    var store = NewUnboundStore();
                    Check("[SL6-a] 绑定成功但 BookSource=UnsupportedLegacy",
                        store.BindSolution(Guid.NewGuid(), "SL6", root, out var e)
                            && store.BookSource == ScadaBookSource.UnsupportedLegacy,
                        e ?? "实际：" + store.BookSource);
                    Check("[SL6-b] 旧文件不产出任何账号，登录恒拒",
                        store.Users.Count == 0 && !store.TryValidate("op9", "legacy123", out _, out _),
                        "Users=" + store.Users.Count);
                    Check("[SL6-c] LastLoadError 含「旧账号文件格式不受支持，请重建账号」",
                        store.LastLoadError != null && store.LastLoadError.Contains("请重建账号"),
                        "err=" + store.LastLoadError);
                    Check("[SL6-d] CanWriteAccounts=false（fail-closed，不写回）",
                        !store.CanWriteAccounts, "居然可写");
                    Check("[SL6-e] 旧文件字节未变",
                        File.ReadAllBytes(users).SequenceEqual(before), "旧文件被改/被删");
                    Check("[SL6-f] TryRebuildAccountBook 后三文件可读、admin 可登录",
                        store.TryRebuildAccountBook(out var e2)
                            && store.TryValidate(ScadaUserStore.DefaultAdminName,
                                   ScadaUserStore.DefaultAdminPassword, out var admin, out _)
                            && admin!.GroupId == "tpl-admin", e2 ?? "");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("sl7");
                try
                {
                    var file = ScadaUserStore.UsersFilePath(root);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, "{ 这不是 JSON");

                    var store = NewUnboundStore();
                    Check("[SL7-a] 损坏文件仍能绑定成功（可读 ≠ 可解析）",
                        store.BindSolution(Guid.NewGuid(), "SL7", root, out var e), e ?? "");
                    Check("[SL7-b] 回退出厂管理员可登录",
                        store.TryValidate(ScadaUserStore.DefaultAdminName,
                            ScadaUserStore.DefaultAdminPassword, out _, out _), "回退失败");
                    Check("[SL7-c] LastLoadError 非空", !string.IsNullOrEmpty(store.LastLoadError), "静默空账号本");
                    Check("[SL7-d] BookSource=Factory",
                        store.BookSource == ScadaBookSource.Factory, "实际：" + store.BookSource);
                    Check("[SL7-e] 打开时**不**改名坏文件",
                        File.Exists(file) && Directory.GetFiles(Path.GetDirectoryName(file)!, "*.bad-*").Length == 0,
                        "打开路径产生了写副作用");
                    Check("[SL7-f] 一次真实写操作后坏文件被改名保留",
                        store.TryResetPassword(ScadaUserStore.DefaultAdminName, "newpass1", out var e2)
                            && Directory.GetFiles(Path.GetDirectoryName(file)!,
                                   ScadaUserStore.UsersFileName + ".bad-*").Length == 1, e2 ?? "证据没留下");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("sl8");
                try
                {
                    File.WriteAllText(Path.Combine(root, SolutionPaths.UsersFolderName), "我不是目录");
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL8", root, out _);
                    Check("[SL8-a] TryAddUser 返回 false",
                        !store.TryAddUser("op1", "pass1234", "G-lineop", out var e), "假成功");
                    Check("[SL8-b] 拒绝原因非空且含路径",
                        !string.IsNullOrEmpty(e) && e.Contains(root), "err=" + e);
                    Check("[SL8-c] 内存里没有该用户（回滚生效）",
                        store.Users.All(u => !string.Equals(u.Name, "op1", StringComparison.OrdinalIgnoreCase)),
                        "内存与磁盘分叉");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("sl9");
                try
                {
                    var store = NewUnboundStore(actorProvider: () => "someone-else");
                    store.BindSolution(Guid.NewGuid(), "SL9", root, out _);
                    store.AttachSolutionFolder(root, out _);
                    Check("[SL9-a] 删除最后一个管理员被拒",
                        !store.TryRemoveUser(ScadaUserStore.DefaultAdminName, out var e), "err=" + e);
                    Check("[SL9-b] 把最后一个管理员改到无 UserAdministration 的组被拒",
                        !store.TryChangeGroup(ScadaUserStore.DefaultAdminName, "G-general", out var e2),
                        "err=" + e2);
                    Check("[SL9-c] 管理员仍在且组未变",
                        store.Users.Count == 1 && store.Users[0].GroupId == "tpl-admin",
                        string.Join(",", store.Users.Select(u => u.Name + ":" + u.GroupId)));
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("sl10");
                try
                {
                    var store = NewUnboundStore(actorProvider: () => ScadaUserStore.DefaultAdminName);
                    store.BindSolution(Guid.NewGuid(), "SL10", root, out _);
                    store.AttachSolutionFolder(root, out _);
                    Check("[SL10-a] 夹具：再加一个管理员",
                        store.TryAddUser("admin2", "pass1234", "tpl-admin", out var e0), e0 ?? "");
                    Check("[SL10-b] 删除当前登录账号被拒",
                        !store.TryRemoveUser(ScadaUserStore.DefaultAdminName, out var e), "err=" + e);
                    Check("[SL10-c] 拒绝原因点明是当前登录账号",
                        e != null && e.Contains("当前登录"), "err=" + e);
                    Check("[SL10-d] 该账号仍在清单里",
                        store.Users.Any(u => u.Name == ScadaUserStore.DefaultAdminName), "被删掉了");
                }
                finally { TryDelete(root); }
            }

            {
                var store = NewUnboundStore();
                var missingRoot = Path.Combine(Path.GetTempPath(), "sl21-不存在-" + Guid.NewGuid().ToString("N"));
                Check("[SL21-a] 绑定不存在的根返回 false",
                    !store.BindSolution(Guid.NewGuid(), "SL21", missingRoot, out var e), "居然绑定成功了");
                Check("[SL21-b] 错误文案含完整路径",
                    e != null && e.Contains(missingRoot), "err=" + e);
                Check("[SL21-c] 绑定失败后状态是 NoSolution",
                    store.BookState == ScadaAccountBookState.NoSolution, "实际：" + store.BookState);
                Check("[SL21-d] admin/admin123 登录失败（不拿出厂管理员兜底）",
                    !store.TryValidate(ScadaUserStore.DefaultAdminName,
                        ScadaUserStore.DefaultAdminPassword, out _, out _), "拔网线后门");

                var root = NewSolutionRoot("sl21b");
                try
                {
                    var file = ScadaUserStore.UsersFilePath(root);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, "[]");
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    Check("[SL21-e] 文件被独占时绑定返回 false",
                        !store.BindSolution(Guid.NewGuid(), "SL21b", root, out var e2), "居然成功了");
                    Check("[SL21-f] 错误文案含路径与异常类型",
                        e2 != null && e2.Contains(file) && e2.Contains("Exception"), "err=" + e2);
                    Check("[SL21-g] 独占场景后仍是 NoSolution 且登不进",
                        store.BookState == ScadaAccountBookState.NoSolution
                            && !store.TryValidate(ScadaUserStore.DefaultAdminName,
                                   ScadaUserStore.DefaultAdminPassword, out _, out _), "回落到出厂管理员了");
                }
                finally { TryDelete(root); }
            }

            {
                var ctor = typeof(ScadaUserStore).GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length).First();
                var names = ctor.GetParameters().Select(p => p.Name).ToArray();
                Check("[SL23-a] 构造器上不存在 legacyStorePath 参数",
                    names.All(n => !string.Equals(n, "legacyStorePath", StringComparison.OrdinalIgnoreCase)),
                    string.Join(",", names));
                Check("[SL23-b] 构造器上有 permissionReferenceCounter（删除守卫的按钮引用扫描）",
                    names.Contains("permissionReferenceCounter"), string.Join(",", names));
                Check("[SL23-c] 显式 storePath 构造仍可用且不写盘", NewExplicitStorePathProbe(), "");
            }
        }

        private static bool NewExplicitStorePathProbe()
        {
            var dir = NewSolutionRoot("sl23c");
            try
            {
                var path = ScadaUserStore.UsersFilePath(dir);
                var store = new ScadaUserStore(path);
                return store.BookState == ScadaAccountBookState.PersistedSolution
                       && !File.Exists(path)
                       && store.TryValidate(ScadaUserStore.DefaultAdminName,
                              ScadaUserStore.DefaultAdminPassword, out var admin, out _)
                       && admin!.GroupId == "tpl-admin";
            }
            finally { TryDelete(dir); }
        }

        private static void SlGroupsAndCatalog()
        {
            Section("[SL-g] 组 / 权限目录 / 三文件回滚 / 旧格式（B3）");

            {
                var root = NewSolutionRoot("slg1");
                try
                {
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL-g1", root, out _);
                    Check("[SL-g1-a] 目录 15 条、组 6（3 模板 + 3 示例）、Users 1 条出厂管理员",
                        store.PermissionCatalog.Count == 15 && store.Groups.Count == 6 && store.Users.Count == 1,
                        $"{store.PermissionCatalog.Count}/{store.Groups.Count}/{store.Users.Count}");
                    Check("[SL-g1-b] BookSource=Factory",
                        store.BookSource == ScadaBookSource.Factory, "实际：" + store.BookSource);
                    Check("[SL-g1-c] 磁盘上仍 0 个文件（种内置不写盘）",
                        !Directory.Exists(Path.Combine(root, SolutionPaths.UsersFolderName))
                            || Directory.GetFiles(Path.Combine(root, SolutionPaths.UsersFolderName)).Length == 0,
                        "绑定路径偷偷写盘");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("slg2");
                try
                {
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL-g2", root, out _);
                    store.AttachSolutionFolder(root, out _);

                    Check("[SL-g2-a] 新建组成功",
                        store.TryAddGroup("测试组", new[] { ScadaPermissionKeys.ViewRuntime }, out var e), e ?? "");
                    var created = store.Groups.First(g => g.Name == "测试组");
                    Check("[SL-g2-b] 改权限成功",
                        store.TryUpdateGroup(created.Id, "测试组",
                            new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop }, out var e2), e2 ?? "");
                    Check("[SL-g2-c] 改名后 Id 不变",
                        store.TryUpdateGroup(created.Id, "测试组2",
                            new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop }, out var e3)
                            && store.Groups.Any(g => g.Id == created.Id && g.Name == "测试组2"), e3 ?? "");
                    string? e5 = null;
                    var addOp = store.TryAddUser("op1", "pass1234", created.Id, out var e4);
                    var revokeBlocked = !store.RevokeGroup(created.Id, out e5);
                    Check("[SL-g2-d] 有用户引用的组撤销被拒并报人数",
                        addOp && revokeBlocked && e5 != null && e5.Contains("1"),
                        (e4 ?? "") + " / " + (e5 ?? ""));
                    string? e6 = null;
                    var removed = store.TryRemoveUser("op1", out _);
                    var revoked = store.RevokeGroup(created.Id, out e6);
                    Check("[SL-g2-e] 删掉用户后组可撤销", removed && revoked, e6 ?? "");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("slg3");
                try
                {
                    var buttonRefs = new HashSet<string>(StringComparer.Ordinal);
                    var store = NewUnboundStore(permissionReferenceCounter: key =>
                        buttonRefs.Contains(key) ? 1 : 0);
                    store.BindSolution(Guid.NewGuid(), "SL-g3", root, out _);
                    store.AttachSolutionFolder(root, out _);

                    while (store.PermissionCatalog.Count < ScadaPermissionKeys.MaxCatalogCount)
                    {
                        if (!store.TryAddPermission("自定义" + store.PermissionCatalog.Count, out _, out var fill))
                            throw new InvalidOperationException("填满目录失败：" + fill);
                    }
                    Check("[SL-g3-a] 第 33 条被拒",
                        !store.TryAddPermission("第33条", out _, out var e) && e != null && e.Contains("32"), "err=" + e);
                    Check("[SL-g3-b] 内置 Key 不可删",
                        !store.TryRemovePermission(ScadaPermissionKeys.Stop, out var e2), "err=" + e2);

                    // 满 32 后先腾一个槽位，再建“示例权限”。
                    var spare = store.PermissionCatalog.Last(p => !p.IsBuiltIn).Key;
                    store.TryRemovePermission(spare, out _);
                    store.TryAddPermission("示例权限", out var groupKey, out _);
                    store.TryAddGroup("引用组", new[] { groupKey! }, out _);
                    Check("[SL-g3-c] 被组引用不可删（报「1 个组 / 0 个按钮」）",
                        !store.TryRemovePermission(groupKey!, out var e3)
                            && e3 != null && e3.Contains("1 个组") && e3.Contains("0 个按钮"), "err=" + e3);

                    var lastCustom = store.PermissionCatalog.Last(p => !p.IsBuiltIn && p.Key != groupKey).Key;
                    store.TryRemovePermission(lastCustom, out _);
                    store.TryAddPermission("按钮引用权限", out var btnKey, out _);
                    buttonRefs.Add(btnKey!);
                    Check("[SL-g3-d] 被按钮引用不可删（报「0 个组 / 1 个按钮」）",
                        !store.TryRemovePermission(btnKey!, out var e4)
                            && e4 != null && e4.Contains("0 个组") && e4.Contains("1 个按钮"), "err=" + e4);
                    Check("[SL-g3-e] 未接线按钮扫描时 fail-closed（不按 0 处理）",
                        !NewUnboundStore().TryRemovePermission("NoSuchKey", out var e5)
                            && e5 != null && e5.Contains("未接线"), "err=" + e5);
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("slg4");
                try
                {
                    var users = ScadaUserStore.UsersFilePath(root);
                    SeedLegacyRoleAccountFile(users);
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL-g4", root, out _);
                    Check("[SL-g4-a] Users 空、BookSource=UnsupportedLegacy、LastLoadError 含「请重建账号」",
                        store.Users.Count == 0
                            && store.BookSource == ScadaBookSource.UnsupportedLegacy
                            && (store.LastLoadError?.Contains("请重建账号") ?? false),
                        store.BookSource + " / " + store.LastLoadError);
                    Check("[SL-g4-b] TryValidate 恒 false",
                        !store.TryValidate("op9", "legacy123", out _, out _), "旧文件被当成新格式读了");
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("slg5");
                try
                {
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL-g5", root, out _);
                    store.AttachSolutionFolder(root, out _);

                    var groupsFile = ScadaUserStore.GroupsFilePath(root);
                    var permsFile = ScadaUserStore.PermissionsFilePath(root);
                    var groupsBefore = File.ReadAllBytes(groupsFile);
                    var permsBefore = File.ReadAllBytes(permsFile);

                    var usersFile = ScadaUserStore.UsersFilePath(root);
                    File.Delete(usersFile);
                    Directory.CreateDirectory(usersFile);

                    Check("[SL-g5-a] 写盘失败返回 false",
                        !store.TryAddGroup("回滚组", new[] { ScadaPermissionKeys.ViewRuntime }, out var e), "假成功");
                    Check("[SL-g5-b] 权限目录与组文件逐字节回到写前",
                        File.ReadAllBytes(groupsFile).SequenceEqual(groupsBefore)
                            && File.ReadAllBytes(permsFile).SequenceEqual(permsBefore), "回滚没生效：" + e);
                }
                finally { TryDelete(root); }
            }

            {
                var root = NewSolutionRoot("slg6");
                try
                {
                    var store = NewUnboundStore();
                    store.BindSolution(Guid.NewGuid(), "SL-g6", root, out _);
                    store.AttachSolutionFolder(root, out _);
                    store.TryAddUser("op1", "pass1234", "G-lineop", out _);

                    var groups = JArray.Parse(File.ReadAllText(ScadaUserStore.GroupsFilePath(root)));
                    var users = JArray.Parse(File.ReadAllText(ScadaUserStore.UsersFilePath(root)));
                    var groupIds = groups.Select(g => (string?)g["Id"]).Where(id => id != null).ToArray();
                    Check("[SL-g6-a] 三个文件都存在且可回读",
                        File.Exists(ScadaUserStore.UsersFilePath(root))
                            && File.Exists(ScadaUserStore.GroupsFilePath(root))
                            && File.Exists(ScadaUserStore.PermissionsFilePath(root)), "缺文件");
                    Check("[SL-g6-b] 每个用户的 GroupId 都能在组文件里解析",
                        users.All(u => groupIds.Contains((string?)u["GroupId"])),
                        string.Join(",", users.Select(u => (string?)u["GroupId"])));
                    Check("[SL-g6-c] 悬空 GroupId → 该用户拒绝登录", DanglingGroupIsRejected(root), "");
                }
                finally { TryDelete(root); }
            }
        }

        private static bool DanglingGroupIsRejected(string root)
        {
            var usersFile = ScadaUserStore.UsersFilePath(root);
            var raw = File.ReadAllText(usersFile);
            raw = raw.Replace("\"GroupId\": \"G-lineop\"", "\"GroupId\": \"G-missing\"");
            raw = raw.Replace("\"GroupId\":\"G-lineop\"", "\"GroupId\":\"G-missing\"");
            File.WriteAllText(usersFile, raw, new UTF8Encoding(false));
            var store = NewUnboundStore();
            store.BindSolution(Guid.NewGuid(), "SL-g6c", root, out _);
            return !store.TryValidate("op1", "pass1234", out _, out _);
        }

        private sealed class FakeWebSessionAdmin : IScadaWebSessionAdmin
        {
            public readonly List<string> RevokedGroups = new();
            public bool IsAvailable => true;
            public IReadOnlyList<ScadaWebSessionInfo> ListOnline() => Array.Empty<ScadaWebSessionInfo>();
            public bool TryKick(string sessionRef) => true;
            public int KickAll() => 0;
            public int RevokeGroup(string groupId) { RevokedGroups.Add(groupId); return 1; }
        }

        private sealed class CoordinatorFixture : IDisposable
        {
            public readonly string Root;
            public readonly string AuditDir;
            public readonly SolutionIdentityHolder Identity = new();
            public readonly ScadaAccessPolicy Policy = new();
            public readonly ScadaUserStore Store;
            public readonly ScadaAuditWriter Audit;
            public readonly FakeWebSessionAdmin Web = new();
            public readonly ScadaSessionCredentialVault Vault;
            public readonly SolutionSessionCoordinator Coordinator;
            public readonly List<string> Notifications = new();

            /// <summary>调试模式面板开关（夹具里可编程；默认开，便于测自动登录）。</summary>
            public bool DebugModeEnabled { get; set; } = true;

            public CoordinatorFixture(string tag)
            {
                var stamp = Guid.NewGuid().ToString("N");
                Root = Path.Combine(Path.GetTempPath(), $"sl-d-root-{tag}-{stamp}");
                AuditDir = Path.Combine(Path.GetTempPath(), $"sl-d-audit-{tag}-{stamp}");
                Directory.CreateDirectory(Root);
                Directory.CreateDirectory(AuditDir);

                Store = new ScadaUserStore(
                    storePath: null, audit: null, actorProvider: () => Policy.CurrentUserName,
                    startUnbound: true, permissionReferenceCounter: _ => 0);
                Audit = new ScadaAuditWriter(AuditDir);
                Vault = new ScadaSessionCredentialVault(Policy);
                Coordinator = new SolutionSessionCoordinator(
                    workspace: null, Store, Policy, Identity, Audit,
                    webSessionAdminProvider: () => Web,
                    onNotify: m => Notifications.Add(m),
                    credentials: Vault,
                    debugModeEnabledProvider: () => DebugModeEnabled);
                // 默认关掉"开发构建默认管理员"：否则每个夹具在绑定方案时都会自动登成 admin，
                // 把"未登录/登出"这些被测路径掩盖掉。[SL-f] 需要时单独打开。
                Coordinator.DebugAutoLoginAllowed = false;
                Coordinator.Start();
            }

            public void BindWithUsers()
            {
                Store.BindSolution(Guid.NewGuid(), "D", Root, out _);
                Store.AttachSolutionFolder(Root, out _);
                Store.TryAddUser("op1", "pass1234", "G-lineop", out _);
            }

            public void LoginAs(string userName)
            {
                var password = userName == "admin"
                    ? ScadaUserStore.DefaultAdminPassword : "pass1234";
                if (!Store.TryValidate(userName, password, out var user, out var error))
                    throw new InvalidOperationException("夹具登录失败：" + error);
                Policy.Login(user!);
                Vault.Remember(user!.Name, password);
            }

            public void Dispose()
            {
                Coordinator.Stop();
                TryDelete(Root);
                TryDelete(AuditDir);
            }
        }

        private static void SlSessionBoundary()
        {
            Section("[SL-d] 组 + 权限快照：改组只重判、撤销组/删自己才登出");

            using (var f = new CoordinatorFixture("d1"))
            {
                f.BindWithUsers();
                f.LoginAs("op1");
                var fired = 0;
                f.Coordinator.SessionBoundaryChanged += () => fired++;

                Check("[SL-d1-a] 改组成功（去掉 Start）",
                    f.Store.TryUpdateGroup("G-lineop", "产线操作员",
                        new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop }, out var e), e ?? "");

                Check("[SL-d1-b] 不登出", f.Policy.IsLoggedIn, "改组被当成降权踢下线了");
                Check("[SL-d1-c] Can(Start)=false、Can(Stop)=true",
                    !f.Policy.Can(ScadaPermissionKeys.Start, out _)
                        && f.Policy.Can(ScadaPermissionKeys.Stop, out _), "权限快照没换");
                Check("[SL-d1-d] SessionBoundaryChanged 触发 1 次", fired == 1, "实际 " + fired + " 次");
            }

            using (var f = new CoordinatorFixture("d2"))
            {
                f.BindWithUsers();
                f.Store.TryAddGroup("G-temp", new[] { ScadaPermissionKeys.ViewRuntime }, out _);
                var tempGroup = f.Store.Groups.First(g => g.Name == "G-temp");
                f.LoginAs("op1");
                f.Policy.RefreshPermissions(tempGroup.Id, tempGroup.Name, tempGroup.Permissions);
                f.Store.RevokeGroup(tempGroup.Id, out var e);
                Check("[SL-d2] 撤销当前组 → 强制登出（IsLoggedIn=false、CurrentGroupId=\"\"）",
                    !f.Policy.IsLoggedIn && f.Policy.CurrentGroupId.Length == 0, e ?? "");
            }

            using (var f = new CoordinatorFixture("d3"))
            {
                f.BindWithUsers();
                f.LoginAs("op1");
                f.Store.TryRemoveUser("op1", out _);
                f.Coordinator.HandleUserChanged("op1", ScadaUserChangeKind.Removed);
                Check("[SL-d3] 删当前用户 → 强制登出", !f.Policy.IsLoggedIn, "会话还挂在不存在的身份上");
            }

            using (var f = new CoordinatorFixture("d4"))
            {
                f.BindWithUsers();
                f.LoginAs("op1");
                var before = f.Policy.Permissions.ToArray();
                var fired = 0;
                f.Coordinator.SessionBoundaryChanged += () => fired++;

                f.Store.TryUpdateGroup("tpl-admin", "模板·管理员", new[] { ScadaPermissionKeys.UserAdministration }, out _);
                f.Coordinator.HandleGroupChanged("tpl-admin");

                Check("[SL-d4] 改别的组：权限与登录态不变且不触发 SessionBoundaryChanged",
                    f.Policy.IsLoggedIn && f.Policy.Permissions.SequenceEqual(before) && fired == 0,
                    $"fired={fired} perms={string.Join(",", f.Policy.Permissions)}");
            }

            using (var f = new CoordinatorFixture("d5"))
            {
                f.BindWithUsers();
                f.LoginAs("op1");
                f.Store.TryUpdateGroup("G-lineop", "产线操作员",
                    new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop }, out _);
                Check("[SL-d5] RevokeGroup 被调用 1 次且参数是组 Id",
                    f.Web.RevokedGroups.Count == 1 && f.Web.RevokedGroups[0] == "G-lineop",
                    string.Join(",", f.Web.RevokedGroups));
            }

            using (var f = new CoordinatorFixture("d6"))
            {
                f.BindWithUsers();
                f.LoginAs("op1");
                f.Store.TryUpdateGroup("G-lineop", "产线操作员",
                    new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop }, out _);
                var expected = f.Store.Groups.First(g => g.Id == "G-lineop").Permissions;
                Check("[SL-d6] 旧快照不被复用：Permissions 与 store 当前组逐项相等",
                    f.Policy.Permissions.SequenceEqual(expected),
                    string.Join(",", f.Policy.Permissions));
            }

            using (var f = new CoordinatorFixture("d7"))
            {
                f.BindWithUsers();
                f.LoginAs("op1");
                f.Store.TryChangeGroup("op1", "G-general", out _);
                f.Coordinator.HandleUserChanged("op1", ScadaUserChangeKind.GroupChanged);
                Check("[SL11] 改组只重判不登出（反向：删自己才登出）",
                    f.Policy.IsLoggedIn
                        && f.Policy.CurrentGroupId == "G-general"
                        && f.Policy.Can(ScadaPermissionKeys.Start, out _)
                        && !f.Policy.Can(ScadaPermissionKeys.AxisJog, out _),
                    $"group={f.Policy.CurrentGroupId} perms={string.Join(",", f.Policy.Permissions)}");
            }
        }

        private static void SlSolutionSwitchCarryOver()
        {
            Section("[SL-e] 切方案保持登录（同账号密码 / 权限取新方案 / 登出即清凭据）");

            // ---- e0：凭据仓本身 ----
            {
                var policy = new ScadaAccessPolicy(() => TimeSpan.FromHours(1));
                var vault = new ScadaSessionCredentialVault(policy);

                vault.Remember("u1", "p1");
                Check("[SL-e0-a] 记住后可取且有凭据", vault.HasCredential, "HasCredential=false");

                var snapshot = vault.Capture();
                Check("[SL-e0-b] Capture 取走即清空仓",
                    snapshot != null && !vault.HasCredential,
                    $"snapshot={(snapshot != null)} has={vault.HasCredential}");
                Check("[SL-e0-c] 快照带用户名与可用密码",
                    snapshot != null && snapshot.UserName == "u1" && !snapshot.IsEmpty,
                    snapshot?.UserName ?? "<null>");

                snapshot?.Dispose();
                Check("[SL-e0-d] Dispose 后快照为空（不可再用）",
                    snapshot != null && snapshot.IsEmpty,
                    snapshot?.IsEmpty.ToString() ?? "<null>");
            }

            // ---- e1：两边同账号同密码 → 保持登录，权限取新方案 ----
            using (var f = new CoordinatorFixture("e1"))
            {
                f.BindWithUsers();
                var solA = NewSolution("A", f.Root);
                f.Coordinator.HandleSolutionSwitched(solA);
                f.LoginAs("op1");

                Check("[SL-e1-a] 切前：已登录且凭据在仓",
                    f.Policy.IsLoggedIn && f.Vault.HasCredential,
                    $"logged={f.Policy.IsLoggedIn} has={f.Vault.HasCredential}");

                var rootB = NewSolutionRoot("e1b");
                SeedSolutionBook(rootB, "op1", "pass1234", "G-general");
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", rootB));

                Check("[SL-e1-b] 同账号密码 → 切方案后仍登录",
                    f.Policy.IsLoggedIn && f.Policy.CurrentUserName == "op1",
                    $"logged={f.Policy.IsLoggedIn} user={f.Policy.CurrentUserName}");

                Check("[SL-e1-c] 权限取新方案的组（G-general/普工：有 Start、无 AxisJog）",
                    f.Policy.CurrentGroupId == "G-general"
                    && f.Policy.CurrentGroupName == "普工"
                    && f.Policy.Can(ScadaPermissionKeys.Start, out _)
                    && !f.Policy.Can(ScadaPermissionKeys.AxisJog, out _),
                    $"group={f.Policy.CurrentGroupId}/{f.Policy.CurrentGroupName} perms={string.Join(",", f.Policy.Permissions)}");

                Check("[SL-e1-d] 凭据已回填（连续切方案仍能保持）", f.Vault.HasCredential,
                    "第二次切方案会因为没有凭据而登出");

                var audit = ReadAudit(f.AuditDir);
                Check("[SL-e1-e] 审计记「保持登录」且成功",
                    audit.Contains("保持登录", StringComparison.Ordinal)
                    && audit.Contains("成功", StringComparison.Ordinal),
                    $"bytes={audit.Length}");
                Check("[SL-e1-f] 审计里没有密码原文", !audit.Contains("pass1234", StringComparison.Ordinal),
                    "明文密码进了审计文件");
            }

            // ---- e2：密码不同 → 登出（与旧行为一致） ----
            using (var f = new CoordinatorFixture("e2"))
            {
                f.BindWithUsers();
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));
                f.LoginAs("op1");

                var rootB = NewSolutionRoot("e2b");
                SeedSolutionBook(rootB, "op1", "other9999", "G-general");
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", rootB));

                Check("[SL-e2-a] 密码不同 → 登出且组清空",
                    !f.Policy.IsLoggedIn && f.Policy.CurrentGroupId.Length == 0,
                    $"logged={f.Policy.IsLoggedIn} group={f.Policy.CurrentGroupId}");
                Check("[SL-e2-b] 凭据不残留", !f.Vault.HasCredential, "登出后凭据仓仍有值");

                var audit = ReadAudit(f.AuditDir);
                Check("[SL-e2-c] 审计记「保持登录」失败",
                    audit.Contains("保持登录", StringComparison.Ordinal)
                    && audit.Contains("失败", StringComparison.Ordinal),
                    $"bytes={audit.Length}");
                Check("[SL-e2-d] 失败原因上屏/落日志（不是静默登出）",
                    f.Notifications.Any(m => m.Contains("未保持登录", StringComparison.Ordinal)),
                    string.Join(" | ", f.Notifications));
            }

            // ---- e3：新方案没有这个账号 → 登出 ----
            using (var f = new CoordinatorFixture("e3"))
            {
                f.BindWithUsers();
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));
                f.LoginAs("op1");

                f.Coordinator.HandleSolutionSwitched(NewSolution("B", NewSolutionRoot("e3b")));

                Check("[SL-e3] 新方案没有该账号 → 登出", !f.Policy.IsLoggedIn,
                    "账号不存在却保持了登录");
            }

            // ---- e4：本来就没登录 → 不会凭空登录 ----
            using (var f = new CoordinatorFixture("e4"))
            {
                f.BindWithUsers();
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));

                var rootB = NewSolutionRoot("e4b");
                SeedSolutionBook(rootB, "op1", "pass1234", "G-general");
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", rootB));

                Check("[SL-e4] 未登录切方案 → 不会凭空登录", !f.Policy.IsLoggedIn,
                    "没有会话却登录了");
            }

            // ---- e5：手动登出清凭据 → 再切方案不会复活 ----
            using (var f = new CoordinatorFixture("e5"))
            {
                f.BindWithUsers();
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));
                f.LoginAs("op1");
                f.Policy.Logout();

                Check("[SL-e5-a] 登出即清凭据", !f.Vault.HasCredential, "凭据跟着会话一起活着");

                var rootB = NewSolutionRoot("e5b");
                SeedSolutionBook(rootB, "op1", "pass1234", "G-general");
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", rootB));

                Check("[SL-e5-b] 登出后切方案不会复活会话", !f.Policy.IsLoggedIn,
                    "超时/手动登出后切方案又登回来了");
            }

            // ---- e6：改密后旧凭据作废；本人改密用新密码回填 ----
            using (var f = new CoordinatorFixture("e6"))
            {
                f.BindWithUsers();
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));
                f.LoginAs("op1");

                var changed = f.Store.TryChangePassword("op1", "pass1234", "newpass99", out var changeError);
                Check("[SL-e6-a] 改密成功", changed, changeError ?? "");
                Check("[SL-e6-b] 改密事件清掉旧凭据", !f.Vault.HasCredential,
                    "旧密码仍留在仓里");

                // 登录弹窗在改密成功后会立刻用新明文回填（见 ScadaLoginViewModel.OnChangePassword）
                f.Vault.Remember("op1", "newpass99");

                var rootB = NewSolutionRoot("e6b");
                SeedSolutionBook(rootB, "op1", "newpass99", "G-general");
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", rootB));

                Check("[SL-e6-c] 新密码在新方案一致 → 保持登录",
                    f.Policy.IsLoggedIn && f.Policy.CurrentGroupId == "G-general",
                    $"logged={f.Policy.IsLoggedIn} group={f.Policy.CurrentGroupId}");
            }

            // ---- e7：切到未保存的新方案（出厂 admin/admin123）也能保持 ----
            using (var f = new CoordinatorFixture("e7"))
            {
                f.BindWithUsers();
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));
                f.LoginAs("admin");

                f.Coordinator.HandleSolutionSwitched(new SolutionModel
                {
                    SolutionName = "未保存方案",
                    SolutionId = Guid.NewGuid(),
                    IsIdentityPersisted = false,
                });

                Check("[SL-e7] 未保存方案（出厂 admin）同账号密码 → 保持登录",
                    f.Policy.IsLoggedIn && f.Policy.CurrentUserName == "admin",
                    $"logged={f.Policy.IsLoggedIn} user={f.Policy.CurrentUserName}");
            }

            // ---- e8：重开当前方案不触发会话边界（不登出、不重复审计） ----
            using (var f = new CoordinatorFixture("e8"))
            {
                f.BindWithUsers();
                var solA = NewSolution("A", f.Root);
                f.Coordinator.HandleSolutionSwitched(solA);
                f.LoginAs("op1");

                var fired = 0;
                f.Coordinator.SessionBoundaryChanged += () => fired++;
                f.Coordinator.HandleSolutionSwitched(solA);

                Check("[SL-e8] 重开同一方案：仍登录、凭据在、不触发会话边界",
                    f.Policy.IsLoggedIn && f.Vault.HasCredential && fired == 0,
                    $"logged={f.Policy.IsLoggedIn} has={f.Vault.HasCredential} fired={fired}");
            }
        }

        private static void SlDebugAutoLogin()
        {
            Section("[SL-f] 开发构建默认管理员（自动登录，仅未上线构建 / 每进程一次）");

            // ---- f1：首次打开方案 → 自动登成出厂管理员；手动登出后不再登回来 ----
            using (var f = new CoordinatorFixture("f1"))
            {
                f.BindWithUsers();
                f.Coordinator.DebugAutoLoginAllowed = true;
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));

                Check("[SL-f1-a] 未登录首次打开方案 → 自动以出厂管理员登录",
                    f.Policy.IsLoggedIn
                    && f.Policy.CurrentUserName == "admin"
                    && f.Policy.Can(ScadaPermissionKeys.UserAdministration, out _),
                    $"logged={f.Policy.IsLoggedIn} user={f.Policy.CurrentUserName} group={f.Policy.CurrentGroupId}");
                Check("[SL-f1-b] 自动登录也回填凭据（后续切方案可保持）",
                    f.Vault.HasCredential, "自动登录没有进入凭据仓");
                Check("[SL-f1-c] 审计记「调试自动登录」",
                    ReadAudit(f.AuditDir).Contains("调试自动登录", StringComparison.Ordinal),
                    "自动登录没有留痕");

                f.Policy.Logout();
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", f.Root));

                Check("[SL-f1-d] 手动登出后不再自动登录（每进程只尝试一次）",
                    !f.Policy.IsLoggedIn,
                    "登出后切个方案又自己登回来了");
            }

            // ---- f2：管理员改过出厂密码 → 不自动登录 ----
            using (var f = new CoordinatorFixture("f2"))
            {
                f.BindWithUsers();
                f.Store.TryChangePassword(ScadaUserStore.DefaultAdminName,
                    ScadaUserStore.DefaultAdminPassword, "changed99", out _);
                f.Coordinator.DebugAutoLoginAllowed = true;
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));

                Check("[SL-f2] 出厂密码已改 → 不自动登录（不是绕过密码）",
                    !f.Policy.IsLoggedIn,
                    "改过密码还能自动登进来");
            }

            // ---- f3：非开发构建（发版）→ 不自动登录 ----
            using (var f = new CoordinatorFixture("f3"))
            {
                f.BindWithUsers();
                // 夹具默认 DebugAutoLoginAllowed=false，等价于发版构建
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));

                Check("[SL-f3] 发版构建不自动登录", !f.Policy.IsLoggedIn,
                    "发版构建里出现了自动登录");
            }

            // ---- f4：自动登录的凭据也能跨方案保持 ----
            using (var f = new CoordinatorFixture("f4"))
            {
                f.BindWithUsers();
                f.Coordinator.DebugAutoLoginAllowed = true;
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));

                var rootB = NewSolutionRoot("f4b");   // 空根 → 出厂 admin/admin123
                f.Coordinator.HandleSolutionSwitched(NewSolution("B", rootB));

                Check("[SL-f4] 自动登录的凭据切到同密码方案仍保持",
                    f.Policy.IsLoggedIn
                    && f.Policy.CurrentUserName == "admin"
                    && f.Policy.CurrentGroupId == ScadaGroupDefaults.AdminTemplateId,
                    $"logged={f.Policy.IsLoggedIn} user={f.Policy.CurrentUserName} group={f.Policy.CurrentGroupId}");
            }

            // ---- f5：调试模式面板开关关着 → 不自动登录（开关是意图） ----
            using (var f = new CoordinatorFixture("f5"))
            {
                f.BindWithUsers();
                f.Coordinator.DebugAutoLoginAllowed = true;
                f.DebugModeEnabled = false;
                f.Coordinator.HandleSolutionSwitched(NewSolution("A", f.Root));

                Check("[SL-f5] 调试模式开关关着 → 不自动登录", !f.Policy.IsLoggedIn,
                    "面板开关关着还是自动登进来了");
            }
        }

        private static void SlPermissionMatrix()
        {
            Section("[SL16] ScadaPermissionGate：15 Key / 无角色 / 无恒放行档");

            var policy = new ScadaAccessPolicy(() => TimeSpan.FromHours(1));
            var gate = new ScadaPermissionGate(policy);

            var opPermissions = new[]
            {
                ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.ControlRun,
                ScadaPermissionKeys.Start, ScadaPermissionKeys.Initialize,
                ScadaPermissionKeys.Stop, ScadaPermissionKeys.ReportQuery,
                ScadaPermissionKeys.AxisJog,
            };
            policy.Login(new ScadaUserInfo("op1", "G-op", opPermissions));

            var solution = Enum.GetValues<SolutionAction>();
            var execution = Enum.GetValues<ExecutionAction>();
            var system = Enum.GetValues<SystemAction>();
            var flow = Enum.GetValues<FlowAction>();
            var all = solution.Select(a => ScadaPermissionGate.RequiredPermission(a))
                .Concat(execution.Select(a => ScadaPermissionGate.RequiredPermission(a)))
                .Concat(system.Select(a => ScadaPermissionGate.RequiredPermission(a)))
                .Concat(flow.Select(a => ScadaPermissionGate.RequiredPermission(a)))
                .ToArray();

            Check("[SL16-a] 四类动作 6+7+20+15=48 项全部有映射，且 Key 合法",
                solution.Length == 6 && execution.Length == 7 && system.Length == 20 && flow.Length == 15
                    && all.Length == 48
                    && all.All(k => k == ScadaPermissionKeys.Always
                                    || ScadaPermissionKeys.BuiltIn.Any(p => p.Key == k)),
                string.Join(",", all.Distinct()));

            Check("[SL16-b1] 组含 Stop → Can(Stop)=true",
                gate.Can(ScadaPermissionKeys.Stop, out _), "含 Stop 却被拒");
            policy.Login(new ScadaUserInfo("op2", "G-nostop",
                new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.ControlRun }));
            Check("[SL16-b2] 组不含 Stop → Can(Stop)=false（没有恒放行）",
                !gate.Can(ScadaPermissionKeys.Stop, out var stopReason) && stopReason != null,
                "Stop 被无条件放行了");

            policy.Logout();
            Check("[SL16-c] Always 未登录也 true，且不在 PermissionCatalog 里",
                gate.Can(ScadaPermissionKeys.Always, out _)
                    && ScadaPermissionKeys.BuiltIn.All(p => p.Key != ScadaPermissionKeys.Always),
                "Always 进了目录或被判 false");

            string? r1 = null;
            string? r2 = null;
            var unknownDenied = !gate.Can("NoSuchKey", out r1) && r1 != null && r1.Contains("NoSuchKey");
            var emptyDenied = !gate.Can(string.Empty, out r2) && r2 != null;
            Check("[SL16-d] 未知 Key → false 且原因含 Key 原文；空串 → false",
                unknownDenied && emptyDenied,
                (r1 ?? "") + " / " + (r2 ?? ""));

            policy.Login(new ScadaUserInfo("op1", "G-op", opPermissions));
            var canStartBefore = gate.Can(ScadaPermissionKeys.Start, out _);
            policy.RefreshPermissions("G-op", "操作员",
                new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.ControlRun });
            Check("[SL16-e] 改组后 Can(Start) 翻转，且不需要重建 gate",
                canStartBefore && !gate.Can(ScadaPermissionKeys.Start, out _), "旧快照被复用");

            Check("[SL16-f] PermissionFor 与 TryRequiredPermission 一致；未登记值回落 UserAdministration",
                solution.All(a => ScadaPermissionGate.PermissionFor(a) == ScadaPermissionGate.RequiredPermission(a))
                    && ScadaPermissionGate.PermissionFor((SolutionAction)9999)
                       == ScadaPermissionKeys.UserAdministration
                    && !ScadaPermissionGate.TryRequiredPermission((SolutionAction)9999, out _),
                "回落口径不一致");

            policy.Logout();
            Check("[SL16-g] 未登录：Can(ViewRuntime)=false、Can(Always)=true、Can(Stop)=false",
                !gate.Can(ScadaPermissionKeys.ViewRuntime, out _)
                    && gate.Can(ScadaPermissionKeys.Always, out _)
                    && !gate.Can(ScadaPermissionKeys.Stop, out _), "未登录判定不符");

            var described = solution.Select(a => ScadaPermissionGate.Describe(a))
                .Concat(execution.Select(a => ScadaPermissionGate.Describe(a)))
                .Concat(system.Select(a => ScadaPermissionGate.Describe(a)))
                .Concat(flow.Select(a => ScadaPermissionGate.Describe(a)))
                .ToArray();
            Check("[SL16-h] Describe 48 项非空且互不相同",
                described.All(d => !string.IsNullOrWhiteSpace(d)) && described.Distinct().Count() == 48,
                "重复/空白：" + described.Length);

            var methods = typeof(ScadaPermissionGate).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == nameof(ScadaPermissionGate.RequiredPermission))
                .ToArray();
            Check("[SL16-i] RequiredPermission 返回 string（没有角色类型）",
                methods.Length == 4 && methods.All(m => m.ReturnType == typeof(string)),
                string.Join(",", methods.Select(m => m.ReturnType.Name)));

            var msg = ScadaPermissionDenial.Message(
                ScadaPermissionGate.Describe(ExecutionAction.Stop), stopReason);
            Check("[SL16-j] Message 含动作中文名与原因",
                msg.Contains(ScadaPermissionGate.Describe(ExecutionAction.Stop))
                    && msg.Contains(stopReason!), msg);

            Check("[SL16-k] 有方案 + 未登录 → Can(EditStructure)=false（例外不在 gate 里）",
                !gate.Can(ScadaPermissionKeys.EditStructure, out _), "gate 自己放行了无方案例外");

            var banned = new[] { "Safety" + "Stop", "Ma" + "sk" };
            var forbidden = typeof(ScadaPermissionGate).GetMembers()
                .Concat(typeof(ScadaAccessPolicy).GetMembers())
                .Select(m => m.Name)
                .Where(n => banned.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            Check("[SL16-l] 门禁/策略上不存在已删除的旧成员",
                forbidden.Length == 0, string.Join(",", forbidden));
        }

        private sealed class FlowBarFixture : IDisposable
        {
            public ScadaAccessPolicy Policy { get; }
            public ScadaPermissionGate Gate { get; }
            public SolutionSessionCoordinator Coordinator { get; }
            public VisionMaster.ViewModels.FlowListViewModel Vm { get; }
            public string Root { get; }

            public FlowBarFixture(string tag, params string[] permissions)
            {
                Root = Path.Combine(Path.GetTempPath(), "sl-flowbar-" + tag + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);

                Policy = new ScadaAccessPolicy(() => TimeSpan.FromHours(1));
                Gate = new ScadaPermissionGate(Policy);

                var store = new ScadaUserStore(null, null, null, null, startUnbound: true,
                                               permissionReferenceCounter: _ => 0);
                var identity = new SolutionIdentityHolder();
                Coordinator = new SolutionSessionCoordinator(null, store, Policy, identity);

                Policy.Login(new ScadaUserInfo("op1", "G-flow", permissions));

                Vm = new VisionMaster.ViewModels.FlowListViewModel(
                    null!, null!, null!, null!, null!, null!,
                    Policy, Gate, Coordinator);
            }

            public void Dispose() => TryDelete(Root);
        }

        private static void SlFlowBarGate()
        {
            Section("[SL16-m…s] 流程栏 15 项接同一 gate（B3：Stop 是普通 Key）");

            var allKeys = ScadaPermissionKeys.BuiltIn.Select(p => p.Key)
                .Concat(new[] { ScadaPermissionKeys.Always }).ToArray();
            var flowActions = Enum.GetValues<FlowAction>();

            Check("[SL16-m] FlowAction 15 项全部映射到内置 Key 或 Always",
                flowActions.Length == 15
                    && flowActions.All(a => allKeys.Contains(ScadaPermissionGate.RequiredPermission(a))),
                string.Join(",", flowActions.Select(a => ScadaPermissionGate.RequiredPermission(a)).Distinct()));

            using (var withStop = new FlowBarFixture("stop-yes",
                       ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop))
            {
                Check("[SL16-n1] 含 Stop 的组：CanExecuteFlow(Stop)=true",
                    withStop.Vm.CanExecuteFlow(FlowAction.Stop), "含 Stop 却被拒");
            }
            using (var noStop = new FlowBarFixture("stop-no", ScadaPermissionKeys.ViewRuntime))
            {
                Check("[SL16-n2] 不含 Stop 的组：CanExecuteFlow(Stop)=false",
                    !noStop.Vm.CanExecuteFlow(FlowAction.Stop), "Stop 被恒放行了");
            }

            using (var f = new FlowBarFixture("flip", ScadaPermissionKeys.ViewRuntime))
            {
                var before = f.Vm.CanExecuteFlow(FlowAction.RunOnce);
                f.Policy.RefreshPermissions("G-flow", "流程组",
                    new[] { ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.ControlRun });
                Check("[SL16-o] 改组后 CanExecuteFlow(RunOnce) 翻转",
                    !before && f.Vm.CanExecuteFlow(FlowAction.RunOnce), "旧快照被复用");
            }

            using (var f = new FlowBarFixture("running",
                       ScadaPermissionKeys.ViewRuntime, ScadaPermissionKeys.Stop))
            {
                GlobalEventBus.Publish(MainRunState.RunningContinuous);
                Check("[SL16-q] 运行中 CanExecuteFlow(Stop) 仍为 true（不受运行锁影响）",
                    f.Vm.CanExecuteFlow(FlowAction.Stop), "运行态把停止变灰了");
                GlobalEventBus.Publish(MainRunState.NotStarted);
            }

            using (var f = new FlowBarFixture("anon", Array.Empty<string>()))
            {
                f.Policy.Logout();
                var denied = flowActions
                    .Where(a => ScadaPermissionGate.RequiredPermission(a) != ScadaPermissionKeys.Always)
                    .All(a => !f.Vm.CanExecuteFlow(a));
                Check("[SL16-r] 未登录：非 Always 的 14 项全 false，BrowseList=true",
                    denied && f.Vm.CanExecuteFlow(FlowAction.BrowseList), "未登录仍放行");
            }
        }

        private static void SlAuditTagAndReader()
        {
            Section("[SL-c] 审计：方案标签前缀 / 表头 7 列 / reader 复用 writer 常量");

            var day = new DateTime(2026, 10, 24, 14, 30, 0);
            var id = Guid.Parse("12345678-9abc-def0-1234-56789abcdef0");
            var tag = ScadaAuditSolutionTag.Build(id, identityPersisted: true, "方案A");
            var dir = Path.Combine(Path.GetTempPath(), "ScadaAuditTag_" + Guid.NewGuid().ToString("N"));

            try
            {
                Check("[SL14-a] 落盘 id 的标签是 [sol=<id 前 12 位> <名字>]",
                    tag == "[sol=123456789abc 方案A]", tag);

                Check("[SL14-b] 未落盘（IdentityPersisted=false）写 unsaved，不写随机 id",
                    ScadaAuditSolutionTag.Build(Guid.NewGuid(), identityPersisted: false, "新方案")
                        == "[sol=unsaved 新方案]", "未落盘时用 id 会让同一方案每次启动换标签");

                Check("[SL14-c] 无方案写 [sol=none]",
                    ScadaAuditSolutionTag.Build(null, identityPersisted: false, null) == "[sol=none]",
                    ScadaAuditSolutionTag.Build(null, identityPersisted: false, null));

                Check("[SL14-d] 名字里的 [ ] \\r \\n 被清洗成 _",
                    ScadaAuditSolutionTag.Build(id, true, "a]b[c\r\nd") == "[sol=123456789abc a_b_c__d]",
                    ScadaAuditSolutionTag.Build(id, true, "a]b[c\r\nd"));

                Check("[SL14-e] Build → TryParse 往返：id 前缀 / 名字 / 余文都对得上",
                    ScadaAuditSolutionTag.TryParse(tag + " 改密码", out var pid, out var unsaved,
                                                   out var pname, out var rest)
                        && pid.HasValue
                        && pid.Value.ToString("N").StartsWith("123456789abc", StringComparison.Ordinal)
                        && !unsaved && pname == "方案A" && rest == "改密码",
                    $"id={pid} unsaved={unsaved} name={pname} rest={rest}");

                Check("[SL14-f] 未归属老行（无 [sol= 前缀）解析失败但余文一字不动",
                    !ScadaAuditSolutionTag.TryParse("改密码", out _, out _, out _, out var rest2)
                        && rest2 == "改密码", "老行必须能读成「未归属」");

                Check("[SL14-g] [sol=unsaved 新方案] 解析成 unsaved=true、id=null",
                    ScadaAuditSolutionTag.TryParse("[sol=unsaved 新方案] x", out var pid2, out var unsaved2,
                                                   out var pname2, out _)
                        && pid2 == null && unsaved2 && pname2 == "新方案",
                    $"id={pid2} unsaved={unsaved2} name={pname2}");

                Check("[SL14-h] [sol=none] 解析成 id=null、unsaved=false、name=null",
                    ScadaAuditSolutionTag.TryParse("[sol=none] x", out var pid3, out var unsaved3,
                                                   out var pname3, out _)
                        && pid3 == null && !unsaved3 && pname3 == null,
                    $"id={pid3} unsaved={unsaved3} name={pname3}");

                Check("[SL14-i] 说明里自带 ] 不影响解析（只认第一个 ]）",
                    ScadaAuditSolutionTag.TryParse("[sol=123456789abc A] 变量[x] 1 → 2",
                                                   out _, out _, out _, out var rest3)
                        && rest3 == "变量[x] 1 → 2", rest3);

                Check("[SL15] 表头仍是 7 列、顺序不变（时间/操作者/对象/事件/动作/结果/说明）",
                    ScadaAuditReader.HeaderColumns.Count == 7
                        && ScadaAuditReader.HeaderColumns[0] == "时间"
                        && ScadaAuditReader.HeaderColumns[1] == "操作者"
                        && ScadaAuditReader.HeaderColumns[2] == "对象"
                        && ScadaAuditReader.HeaderColumns[3] == "事件"
                        && ScadaAuditReader.HeaderColumns[4] == "动作"
                        && ScadaAuditReader.HeaderColumns[5] == "结果"
                        && ScadaAuditReader.HeaderColumns[6] == "说明",
                    string.Join("/", ScadaAuditReader.HeaderColumns));

                Directory.CreateDirectory(dir);
                var writer = new ScadaAuditWriter(dir, retentionDays: 0, solutionTagProvider: () => tag);

                Check("[SL20-a] 带标签 provider 的 writer 写成功",
                    writer.Append(new ScadaAuditEntry(
                        "alice", "启动按钮", "按下", "启动流程 主流程",
                        ScadaAuditOutcome.Success, "净效果：0 → 1"), day),
                    writer.LastError ?? "Append 返回 false 但没有 LastError");

                var result = new ScadaAuditReader(dir).Read();
                Check("[SL20-b] reader 用 writer 的文件名常量枚举到当天文件",
                    result.Files.Count == 1
                        && Path.GetFileName(result.Files[0]) == ScadaAuditWriter.SuggestFileName(day),
                    "实际文件：" + string.Join(",", result.Files.Select(Path.GetFileName)));

                Check("[SL20-c] 读出 1 行、跳过 0 行",
                    result.Rows.Count == 1 && result.SkippedLines == 0,
                    $"rows={result.Rows.Count} skipped={result.SkippedLines}");

                var row = result.Rows.FirstOrDefault();
                Check("[SL20-d] 逐列对得上：标签被剥出成结构化字段，余文完整",
                    row != null
                        && row.Actor == "alice" && row.Subject == "启动按钮"
                        && row.EventText == "按下" && row.ActionText == "启动流程 主流程"
                        && row.OutcomeText == "成功"
                        && row.HasTag && row.SolutionId.HasValue
                        && row.SolutionId.Value.ToString("N").StartsWith("123456789abc", StringComparison.Ordinal)
                        && row.SolutionName == "方案A" && !row.UnsavedSolution
                        && row.Detail == tag + " 净效果：0 → 1"
                        && row.DetailRemainder == "净效果：0 → 1",
                    row == null ? "没读出行"
                        : $"{row.Actor}|{row.Subject}|{row.OutcomeText}|tag={row.HasTag}|{row.Detail}");

                Check("[SL20-e] 行号与来源文件可回溯",
                    row != null && row.LineNumber == 2 && row.SourceFile == result.Files[0],
                    row == null ? "没读出行" : $"line={row.LineNumber} file={row.SourceFile}");

                writer.Append(new ScadaAuditEntry(
                    "bob, jr", "对象\"X\"", "按下", "写变量",
                    ScadaAuditOutcome.Denied, "说明含,逗号和\"引号\""), day);

                var r2 = new ScadaAuditReader(dir).Read();
                var row2 = r2.Rows.ElementAtOrDefault(1);
                Check("[SL20-f] 含逗号/引号的字段读回来一字不差，且没被劈成多行",
                    r2.Rows.Count == 2 && r2.SkippedLines == 0
                        && row2?.Actor == "bob, jr" && row2?.Subject == "对象\"X\""
                        && row2?.OutcomeText == "被拒绝"
                        && row2?.DetailRemainder == "说明含,逗号和\"引号\"",
                    row2 == null ? "第二行没读出来" : $"{row2.Actor}|{row2.Subject}|{row2.DetailRemainder}");

                File.AppendAllText(Path.Combine(dir, ScadaAuditWriter.SuggestFileName(day)),
                    "这一行,只有,四列\r\n完全不是CSV\r\n", new UTF8Encoding(false));

                var r3 = new ScadaAuditReader(dir).Read();
                Check("[SL20-g] 非法行跳过且 SkippedLines 准确（2 条坏行，好行仍是 2 条）",
                    r3.Rows.Count == 2 && r3.SkippedLines == 2,
                    $"rows={r3.Rows.Count} skipped={r3.SkippedLines}");

                File.WriteAllText(Path.Combine(dir, "手工备份.csv"), "x");
                File.WriteAllText(Path.Combine(dir, "Audit-不是日期.csv"), "x");
                File.WriteAllText(Path.Combine(dir, "Audit-2026-13-45.csv"), "x");
                File.WriteAllText(Path.Combine(dir, "Audit-2026-10-24.txt"), "x");

                var files = new ScadaAuditReader(dir).EnumerateFiles();
                Check("[SL20-h] 只认「Audit- 开头 + .csv 结尾 + 中间能按 yyyy-MM-dd 解析」三重条件",
                    files.Count == 1 && Path.GetFileName(files[0]) == ScadaAuditWriter.SuggestFileName(day),
                    string.Join(",", files.Select(Path.GetFileName)));
            }
            finally
            {
                TryDelete(dir);
            }
        }

        private static void SlContracts()
        {
            Section("[SL-b] B3 跨层契约（组 + 权限快照）");

            var userInfo = typeof(ScadaUserInfo);
            Check("[SL-b1] ScadaUserInfo 在 VM.Scada 且没有 Role 成员",
                userInfo.Assembly.GetName().Name == "VM.Scada"
                    && userInfo.GetProperty("Role") == null
                    && userInfo.GetProperty("GroupId") != null
                    && userInfo.GetProperty("Permissions") != null,
                userInfo.Assembly.GetName().Name);

            var sessionInfo = typeof(ScadaWebSessionInfo);
            Check("[SL-b2] ScadaWebSessionInfo 有 GroupId/GroupName/Permissions、没有 Role",
                sessionInfo.GetProperty("Role") == null
                    && sessionInfo.GetProperty("GroupId") != null
                    && sessionInfo.GetProperty("GroupName") != null
                    && sessionInfo.GetProperty("Permissions") != null,
                string.Join(",", sessionInfo.GetProperties().Select(p => p.Name)));

            var policy = typeof(IScadaAccessPolicy);
            Check("[SL-b3] IScadaAccessPolicy 只有 Can(string,out string?)，没有 CurrentRole/CanOperate",
                policy.GetMethod("Can") != null
                    && policy.GetProperty("CurrentRole") == null
                    && policy.GetMethod("CanOperate") == null,
                string.Join(",", policy.GetMembers().Select(m => m.Name)));

            Check("[SL-b4] ScadaUserChangeKind 恰好 5 个成员且第 4 个叫 GroupChanged",
                Enum.GetValues<ScadaUserChangeKind>().Length == 5
                    && Enum.GetValues<ScadaUserChangeKind>()[3] == ScadaUserChangeKind.GroupChanged,
                string.Join(",", Enum.GetNames<ScadaUserChangeKind>()));

            var auth = typeof(IScadaUserAuthenticator).GetMethod("TryValidate")!;
            Check("[SL-b5] IScadaUserAuthenticator.TryValidate 的出参是 ScadaUserInfo?",
                auth.GetParameters()[2].ParameterType == typeof(ScadaUserInfo).MakeByRefType(),
                auth.GetParameters()[2].ParameterType.Name);

            var ctx = typeof(IScadaSolutionContext);
            Check("[SL-b6] IScadaSolutionContext 有 SolutionId/AuditTag/SolutionChanged",
                ctx.GetProperty("SolutionId") != null
                    && ctx.GetProperty("AuditTag") != null
                    && ctx.GetEvent("SolutionChanged") != null,
                string.Join(",", ctx.GetMembers().Select(m => m.Name)));

            var admin = typeof(IScadaWebSessionAdmin);
            Check("[SL-b7] IScadaWebSessionAdmin 有 RevokeGroup(groupId)",
                admin.GetMethod("RevokeGroup")?.GetParameters().FirstOrDefault()?.ParameterType == typeof(string),
                string.Join(",", admin.GetMethods().Select(m => m.Name)));

            Check("[SL-b8] 权限目录恰好 15 条内置、无 SafetyStop、Always 不在目录",
                ScadaPermissionKeys.BuiltIn.Count == 15
                    && ScadaPermissionKeys.BuiltIn.All(p => p.Key != "SafetyStop")
                    && ScadaPermissionKeys.BuiltIn.All(p => p.Key != ScadaPermissionKeys.Always),
                string.Join(",", ScadaPermissionKeys.BuiltIn.Select(p => p.Key)));
        }

        /// <summary>
        /// [SL-a] B1 前置：Users 进结构目录清单（SL3 的地基）+ 方案身份是否落盘（SL24 的地基）。
        /// </summary>
        private static void SlStructureAndIdentity()
        {
            Section("[SL-a] 方案目录结构与身份落盘标志");

            Check("[SL-a1] StructureFolderNames 含 Users",
                Array.IndexOf(SolutionPaths.StructureFolderNames, SolutionPaths.UsersFolderName) >= 0,
                "实际：" + string.Join(",", SolutionPaths.StructureFolderNames));

            Check("[SL-a2] StructureFolderNames 恰好 6 项（Image/Assets/Alarms/History/Logs/Users）",
                SolutionPaths.StructureFolderNames.Length == 6,
                "实际 " + SolutionPaths.StructureFolderNames.Length + " 项");

            // 反向钉住 §4.7「明确不改」：账号目录一旦混进资产清单，另存为就会去复制旧账号文件，
            // SL17「另存为写的是当前内存态」会被悄悄架空。
            Check("[SL-a3] AssetFolderNames 仍是 5 项（账号不靠复制产生）",
                SolutionAssetFolders.AssetFolderNames.Length == 5,
                "实际：" + string.Join(",", SolutionAssetFolders.AssetFolderNames));

            var dir = Path.Combine(Path.GetTempPath(), "sl-identity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var svc = new SolutionService();
                var solution = new SolutionModel { SolutionName = "身份方案" };

                Check("[SL-a4] 新建方案 IsIdentityPersisted=false",
                    solution.IsIdentityPersisted == false,
                    "新建即算已落盘 ⇒ 审计标签永远写 id，SL24 的 unsaved 分支不可达");

                var path = Path.Combine(dir, "身份方案.vms");
                var save = svc.SaveAsync(solution, path).GetAwaiter().GetResult();
                Check("[SL-a5] 保存成功", save.Success, save.Message);
                Check("[SL-a6] 保存后 IsIdentityPersisted=true",
                    solution.IsIdentityPersisted, "标签会继续写 unsaved（SL24 红）");

                var loaded = svc.LoadAsync(path).GetAwaiter().GetResult();
                Check("[SL-a7] 重新加载后 IsIdentityPersisted=true",
                    loaded.Success && loaded.Data.IsIdentityPersisted,
                    loaded.Success ? "persisted=" + loaded.Data.IsIdentityPersisted : loaded.Message);

                // 老方案夹具：JSON 里没有 SolutionId 键。因为 SolutionModel.SolutionId 带
                // "= Guid.NewGuid()" 初始化器，反序列化后它 != Guid.Empty，所以 LoadAsync 里
                // 那句 "if (SolutionId == Guid.Empty)" 对老文件永远不触发 —— 判据只能落在原始文本上。
                var legacyPath = Path.Combine(dir, "老方案.vms");
                var raw = File.ReadAllText(path);
                var legacyJson = raw.Replace("\"SolutionId\": \"" + solution.SolutionId + "\",", "");
                if (legacyJson == raw)
                    legacyJson = raw.Replace("\"SolutionId\":\"" + solution.SolutionId + "\",", "");
                File.WriteAllText(legacyPath, legacyJson);

                Check("[SL-a8] 夹具自身有效：老方案 JSON 确实不含 SolutionId 键",
                    !legacyJson.Contains("\"SolutionId\"", StringComparison.Ordinal),
                    "序列化格式变了，夹具失效（这条红说明要先修夹具，不是修产品代码）");

                var legacy = svc.LoadAsync(legacyPath).GetAwaiter().GetResult();
                Check("[SL-a9] 老方案加载后 IsIdentityPersisted=false 且仍拿到可用 Guid",
                    legacy.Success && legacy.Data.IsIdentityPersisted == false
                        && legacy.Data.SolutionId != Guid.Empty,
                    legacy.Success
                        ? "persisted=" + legacy.Data.IsIdentityPersisted + " id=" + legacy.Data.SolutionId
                        : legacy.Message);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { /* 临时目录，删不掉不影响结论 */ }
            }
        }
    }
}
