using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    /// <summary>
    /// [UC] 账号存储的并发安全：Web 登录在<b>后台线程</b>上调用
    /// <see cref="IScadaUserAuthenticator.TryValidate"/>，桌面用户管理（登录框的"用户管理"页）
    /// 在另一条线程上增删账号 / 改密——两条线同时跑时，既不许抛异常，也不许出现
    /// "正确密码被拒""错误密码被放行"这类只在并发下才冒头的静默故障。
    ///
    /// 为什么必须在真线程上跑、不能只读代码
    /// ---------
    /// · 锁写漏了的表现是"偶发登录失败"，单线程断言永远绿；
    /// · PBKDF2 放进锁里不会报错，只会让另一条线程卡住——只有并发 + 超时才看得见；
    /// · "改密与校验并发"要验的是最终状态（旧密码失效、新密码可用），
    ///   这需要一个真的时间窗口，不是一次顺序调用能摆出来的。
    ///
    /// 与 <c>Program</c> 里其它段同一风格（<c>Check(名称, 条件, 明细)</c>），但本文件是
    /// <b>顶层 public 类</b>，所以自带一份计数与打印，不并进 Program 的汇总——
    /// 接线只需在 Main 里加一行 <c>UserStoreConcurrencyChecks.Run();</c>。
    ///
    /// 时间预算：PBKDF2 十万次 ≈ 几十毫秒一次；本段约 1600 次校验 + 200 次写操作 + 30 次刷新，
    /// 8 条校验线程并行时通常十几秒（16 核实测 ~12 秒）；UC16~UC18 的竞态段再加约二十次 PBKDF2。
    /// 所有 Join 都带超时——<b>死锁会判红，而不是把断言宿主挂死</b>。账号文件一律落在临时目录，不碰真实用户目录。
    ///
    /// <b>变异反证（做过的）</b>：把 <c>ScadaUserStore</c> 里的 <c>lock (_gate)</c> /
    /// <c>lock (_writeGate)</c> 全部换成 <c>if (true)</c>（13 + 7 处）再跑本段，
    /// <b>UC9 准确变红</b>（"Users 快照条数异常：0" + <c>InvalidOperationException: Collection was modified</c>）；
    /// 还原后 22 条全绿。<b>注意 UC3~UC5 在摘锁后仍然是绿的</b>——这正是"只做校验 + 增删"
    /// 抓不到漏锁的证据（查找只占几微秒、靶子账号又从不被改动），所以枚举线程与刷新线程不是凑数：
    /// 这条断言的有效性全靠它们。
    ///
    /// UC16~UC18 是"提交前复核（CAS）"那两条安全路径的守门人：UC16/UC17 盯
    /// <c>ResetPassword</c> 的哈希比对与存在性重查（重置 × 改密 / 重置 × 删号），
    /// UC18 盯 <c>TryValidate</c> 在 PBKDF2 之后对"账号还在不在 / 密码还是不是那个 / 角色还是不是那个"
    /// 的复核；UC17 与 UC18 的时间窗口由 <c>ScadaUserStore.AfterSnapshotForTest</c> 钉住
    /// （生产路径恒 null），不靠运气撞时序。
    /// </summary>
    public static class UserStoreConcurrencyChecks
    {
        /// <summary>并发校验的线程数（按派工要求：8 条）</summary>
        private const int ReaderThreads = 8;

        /// <summary>每条校验线程的次数（正确密码 / 错误密码各一半）</summary>
        private const int ValidationsPerThread = 200;

        /// <summary>写线程的增删轮数（每轮 Add 一个唯一账号、随后 Remove 掉）</summary>
        private const int WriterIterations = 100;

        /// <summary>"刷新"线程的 <see cref="ScadaUserStore.Load"/> 次数（等价于桌面点刷新）</summary>
        private const int ReloadIterations = 30;

        /// <summary>死循环枚举 <see cref="ScadaUserStore.Users"/> 的线程数（专抓"返回内部集合/换表不原子"）</summary>
        private const int ListerThreads = 2;

        private const string TargetUser = "并发用户";
        private const string TargetPassword = "conc1234";
        private const string WrongPassword = "wrong999";

        private static int _pass;
        private static int _fail;

        /// <summary>本段最后一次运行的计数（Program 的汇总要把 UC 段并进去）</summary>
        public static (int Pass, int Fail) LastResult { get; private set; }

        /// <summary>独立入口：Main 里只加一行 <c>UserStoreConcurrencyChecks.Run();</c></summary>
        public static void Run()
        {
            Section("[UC] 账号存储并发安全（Web 后台线程校验 × 桌面用户管理）");

            string dir = Path.Combine(Path.GetTempPath(), "ScadaUsersConc_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);

                RunValidateWhileMutating(Path.Combine(dir, "Concurrent.json"));
                RunDefaultPasswordWhileChanging(Path.Combine(dir, "DefaultPwd.json"));
                RunTwoChangePasswordsRace(Path.Combine(dir, "TwoChanges.json"));
                RunResetVersusChangeRace(Path.Combine(dir, "ResetVsChange.json"));
                RunResetVersusRemoveRace(Path.Combine(dir, "ResetVsRemove.json"));
                RunResetVersusRemoveSimultaneous(Path.Combine(dir, "ResetVsRemoveSim.json"));
                RunValidateVersusRoleChange(Path.Combine(dir, "ValidateVsRole.json"));
            }
            catch (Exception ex)
            {
                Check("[UC0] 并发断言全程未抛异常", false, ex.ToString());
            }
            finally
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch
                {
                    // 临时目录清不掉不影响断言结论（下次运行会换一个新目录）。
                }
            }

            Console.WriteLine($"  [UC] 本段汇总：通过 {_pass} / 失败 {_fail}");
            LastResult = (_pass, _fail);
        }

        /// <summary>
        /// ① 8 条后台线程各做 200 次校验（正确/错误密码各半），同时另一条线程增删 100 个账号、
        /// 一条线程反复 <see cref="ScadaUserStore.Load"/>（等于桌面点"刷新"）、两条线程死循环枚举
        /// <see cref="ScadaUserStore.Users"/>。
        /// 断言：不抛、正确密码全成功、错误密码全失败、最终账号数与预期一致。
        ///
        /// 为什么还要后两条：只做"校验 + 增删"的话，<b>把锁全摘掉也照样全绿</b>（实测：
        /// 查找只占几微秒，撞上增删的窗口极小；靶子账号又从不被改动，结果永远正确）。
        /// 枚举 <c>Users</c> 的窗口大得多——锁一旦缺失，"集合已修改"会成片冒出来，这是本段
        /// <b>唯一</b>实测能变红的判据。刷新那条（<c>Load</c> 与校验并发）在 16 核上摘锁也没抓到，
        /// 它守的是语义（换表不撕碎清单、不把登录挡在写事务后面），不指望它变红。
        /// </summary>
        private static void RunValidateWhileMutating(string path)
        {
            var store = new ScadaUserStore(path);

            // 用接口调：Web 侧拿到的就是这一个接口（它看不到宿主工程里的具体类）。
            IScadaUserAuthenticator authenticator = store;

            bool created = store.TryAddUser(TargetUser, TargetPassword, ScadaRole.Engineer, out string? createError);
            Check("[UC1] 并发校验的靶子账号建得出来（走真实写盘，不是内存里塞一条）",
                created && File.Exists(path),
                created ? path : createError ?? "null");

            // 0=正确密码通过 1=错误密码被拒 2=加成功 3=删成功 4=Users 枚举次数 5=Load 次数
            var counters = new int[6];
            var errors = new ConcurrentQueue<string>();
            var listErrors = new ConcurrentQueue<string>();

            using var start = new ManualResetEventSlim(false);
            using var writersDone = new ManualResetEventSlim(false);

            var readers = new List<Thread>();

            for (int i = 0; i < ReaderThreads; i++)
            {
                int index = i;
                var reader = new Thread(() =>
                {
                    start.Wait();
                    for (int n = 0; n < ValidationsPerThread; n++)
                    {
                        try
                        {
                            if ((n & 1) == 0)
                            {
                                if (authenticator.TryValidate(TargetUser, TargetPassword, out var role, out var error)
                                    && role == ScadaRole.Engineer)
                                {
                                    Interlocked.Increment(ref counters[0]);
                                }
                                else
                                {
                                    errors.Enqueue($"正确密码被拒（role={role}，error={error ?? "null"}）");
                                }
                            }
                            else
                            {
                                if (!authenticator.TryValidate(TargetUser, WrongPassword, out _, out var error)
                                    && error == "用户名或密码不正确")
                                {
                                    Interlocked.Increment(ref counters[1]);
                                }
                                else
                                {
                                    errors.Enqueue($"错误密码没有被拒（error={error ?? "null"}）");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Enqueue("校验线程抛异常：" + ex);
                        }
                    }
                })
                { IsBackground = true, Name = $"UC-Reader{index}" };

                readers.Add(reader);
                reader.Start();
            }

            var writers = new List<Thread>();

            var mutator = new Thread(() =>
            {
                start.Wait();
                for (int n = 0; n < WriterIterations; n++)
                {
                    string name = $"临时账号{n:D3}";
                    try
                    {
                        if (store.TryAddUser(name, "temp1234", ScadaRole.Operator, out var addError))
                            Interlocked.Increment(ref counters[2]);
                        else
                            errors.Enqueue($"AddUser({name}) 失败：{addError ?? "null"}");

                        if (store.TryRemoveUser(name, out var removeError))
                            Interlocked.Increment(ref counters[3]);
                        else
                            errors.Enqueue($"RemoveUser({name}) 失败：{removeError ?? "null"}");
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue("写线程抛异常：" + ex);
                    }
                }
            })
            { IsBackground = true, Name = "UC-Writer" };

            writers.Add(mutator);
            mutator.Start();

            // 等价于桌面用户管理页的"刷新"：它会整表换掉内存清单，
            // 与登录校验并发时最容易暴露"清单被撕成两半"。
            var reloader = new Thread(() =>
            {
                start.Wait();
                for (int n = 0; n < ReloadIterations; n++)
                {
                    try
                    {
                        store.Load();
                        Interlocked.Increment(ref counters[5]);
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue("刷新线程抛异常：" + ex);
                    }
                }
            })
            { IsBackground = true, Name = "UC-Reload" };

            writers.Add(reloader);
            reloader.Start();

            var listers = new List<Thread>();
            for (int i = 0; i < ListerThreads; i++)
            {
                int index = i;
                var lister = new Thread(() =>
                {
                    start.Wait();
                    while (!writersDone.IsSet)
                    {
                        try
                        {
                            int count = store.Users.Count;

                            // 全程只有两个稳定账号，写线程最多再挂一个临时账号；
                            // 出现 0/1/4 就说明"清空 + 填入"被看见了中间态。
                            if (count < 2 || count > 3)
                                listErrors.Enqueue($"Users 快照条数异常：{count}");

                            Interlocked.Increment(ref counters[4]);
                        }
                        catch (Exception ex)
                        {
                            listErrors.Enqueue("枚举线程抛异常：" + ex);
                        }
                    }
                })
                { IsBackground = true, Name = $"UC-Lister{index}" };

                listers.Add(lister);
                lister.Start();
            }

            start.Set();

            bool writersJoined = JoinAll(writers, TimeSpan.FromMinutes(3));
            writersDone.Set();
            bool listersJoined = JoinAll(listers, TimeSpan.FromMinutes(1));
            bool readersJoined = JoinAll(readers, TimeSpan.FromMinutes(3));
            bool joined = writersJoined && listersJoined && readersJoined;

            Check("[UC2] 8 条校验线程 × 200 次 + 增删线程 × 100 轮 + 刷新线程 × 30 次 + 枚举线程：全部在超时内结束（没有死锁）",
                joined,
                joined ? $"线程 {readers.Count + writers.Count + listers.Count} 条全部结束" : "有线程未在 3 分钟内结束");

            Check("[UC3] 并发全程不抛异常、写操作全部成功（锁漏了会在这里以异常或失败返回暴露）",
                errors.IsEmpty,
                errors.IsEmpty ? "0 条异常/失败" : string.Join(" | ", errors.Take(5)));

            int expectedPerKind = ReaderThreads * (ValidationsPerThread / 2);

            Check($"[UC4] 正确密码 {expectedPerKind} 次全部通过（并发下不许出现「密码对却登不进去」）",
                counters[0] == expectedPerKind,
                $"通过 {counters[0]} 次");

            Check($"[UC5] 错误密码 {expectedPerKind} 次全部被拒且文案不变（不许因为并发放行）",
                counters[1] == expectedPerKind,
                $"被拒 {counters[1]} 次");

            Check($"[UC6] 增删各 {WriterIterations} 次全部成功（写事务串行化：并发登录不会把写盘挤掉）",
                counters[2] == WriterIterations && counters[3] == WriterIterations,
                $"加 {counters[2]} / 删 {counters[3]}");

            var finalUsers = store.Users;
            Check("[UC7] 最终账号数与预期一致：出厂 admin + 靶子账号 = 2（临时账号删干净，没留半条）",
                finalUsers.Count == 2
                && finalUsers.Any(u => u.Name == ScadaUserStore.DefaultAdminName)
                && finalUsers.Any(u => u.Name == TargetUser && u.Role == ScadaRole.Engineer),
                string.Join(" / ", finalUsers.Select(u => $"{u.Name}:{u.Role.DisplayName()}")));

            var reloaded = new ScadaUserStore(path);
            Check("[UC8] 并发结束后磁盘与内存一致：换个实例读回来也是这两个账号（写盘没被跳过）",
                reloaded.Users.Count == 2
                && reloaded.TryValidate(TargetUser, TargetPassword, out _, out _),
                $"{reloaded.Users.Count} 个账号");

            Check("[UC9] 边写边读 Users：快照始终是完整清单、不抛「集合已修改」（复制语义 + 锁内换表）",
                listersJoined && counters[4] > 0 && listErrors.IsEmpty,
                listErrors.IsEmpty ? $"枚举 {counters[4]} 次" : string.Join(" | ", listErrors.Take(5)));

            Check("[UC10] Load() 与登录校验并发：刷新跑满 30 次，结束后靶子账号照常登录（换表不撕碎清单）",
                writersJoined && counters[5] == ReloadIterations
                && store.TryValidate(TargetUser, TargetPassword, out var afterReload, out _)
                && afterReload == ScadaRole.Engineer,
                $"刷新 {counters[5]} 次");
        }

        /// <summary>
        /// ② 「是否还在用出厂密码」与「改密」并发：不抛异常；改完之后旧密码失效、新密码可用、
        /// 出厂密码提示熄灭。这条验的是"校验读的是锁内复制的快照"——拿活的记录引用出锁时，
        /// 改密那一瞬间可能读到"盐换了、哈希还没换"的半截状态（表现是密码对却验不过）。
        /// </summary>
        private static void RunDefaultPasswordWhileChanging(string path)
        {
            var store = new ScadaUserStore(path);   // 出厂 admin / admin123

            const string newPassword = "changed1234";
            int probes = 0;
            var probeErrors = new ConcurrentQueue<string>();

            using var stop = new ManualResetEventSlim(false);

            var prober = new Thread(() =>
            {
                try
                {
                    while (!stop.IsSet)
                    {
                        store.IsUsingDefaultPassword(ScadaUserStore.DefaultAdminName);
                        Interlocked.Increment(ref probes);
                    }
                }
                catch (Exception ex)
                {
                    probeErrors.Enqueue(ex.ToString());
                }
            })
            { IsBackground = true, Name = "UC-DefaultPwdProbe" };

            prober.Start();

            // 让探测线程先真的跑起来：每次 PBKDF2 几十毫秒，30ms 足以让它进到计算里，
            // 于是下面这次改密与它构成一个真实的重叠窗口。
            Thread.Sleep(30);

            bool changed = store.TryChangePassword(ScadaUserStore.DefaultAdminName,
                                                   ScadaUserStore.DefaultAdminPassword,
                                                   newPassword,
                                                   out string? changeError);

            stop.Set();
            bool joined = prober.Join(TimeSpan.FromMinutes(1));

            Check("[UC11] 「是否还在用出厂密码」与改密并发：探测线程跑过、全程不抛异常",
                joined && probes > 0 && probeErrors.IsEmpty,
                probeErrors.IsEmpty ? $"探测 {probes} 次" : string.Join(" | ", probeErrors.Take(3)));

            Check("[UC12] 改密成功：旧密码立即失效、新密码立即可用（校验与提交落在同一份数据上）",
                changed
                && !store.TryValidate(ScadaUserStore.DefaultAdminName, ScadaUserStore.DefaultAdminPassword, out _, out _)
                && store.TryValidate(ScadaUserStore.DefaultAdminName, newPassword, out var role, out _)
                && role == ScadaRole.Administrator,
                changeError ?? "改密成功");

            Check("[UC13] 改密之后出厂密码提示熄灭（不做缓存，就不存在「忘了失效」这一档）",
                !store.IsUsingDefaultPassword(ScadaUserStore.DefaultAdminName),
                "");
        }

        /// <summary>
        /// ③ 两个线程拿同一个旧密码并发改密：只能有一个成功，另一个必须被拦下
        /// （"密码已被他人修改"，或旧密码在它验之前就已经不对了）——绝不许两个都报成功，
        /// 而磁盘上只剩后写的那个。
        /// </summary>
        private static void RunTwoChangePasswordsRace(string path)
        {
            var store = new ScadaUserStore(path);

            const string first = "first1234";
            const string second = "second1234";

            using var start = new ManualResetEventSlim(false);
            var results = new (bool Ok, string? Error)[2];

            var threads = new Thread[2];
            for (int i = 0; i < threads.Length; i++)
            {
                int index = i;
                threads[i] = new Thread(() =>
                {
                    start.Wait();
                    try
                    {
                        bool ok = store.TryChangePassword(
                            ScadaUserStore.DefaultAdminName,
                            ScadaUserStore.DefaultAdminPassword,
                            index == 0 ? first : second,
                            out var error);
                        results[index] = (ok, error);
                    }
                    catch (Exception ex)
                    {
                        results[index] = (false, "抛异常：" + ex.Message);
                    }
                })
                { IsBackground = true, Name = $"UC-ChangePwd{index}" };

                threads[i].Start();
            }

            start.Set();
            bool joined = JoinAll(threads, TimeSpan.FromMinutes(1));

            int successes = results.Count(r => r.Ok);

            Check("[UC14] 同一旧密码的两个并发改密：恰好一个成功（另一个报「密码已被他人修改」或旧密码已失效）",
                joined && successes == 1
                && results.All(r => r.Ok
                                    || r.Error == "密码已被他人修改"
                                    || r.Error == "用户名或密码不正确"),
                string.Join(" | ", results.Select((r, i) => $"#{i}:{(r.Ok ? "成功" : r.Error ?? "null")}")));

            string winner = results[0].Ok ? first : second;
            string loser = results[0].Ok ? second : first;

            Check("[UC15] 并发改密后磁盘与内存一致：赢家的密码能用、输家的不能用（换个实例读回来也一样）",
                joined && successes == 1
                && store.TryValidate(ScadaUserStore.DefaultAdminName, winner, out _, out _)
                && !store.TryValidate(ScadaUserStore.DefaultAdminName, loser, out _, out _)
                && new ScadaUserStore(path).TryValidate(ScadaUserStore.DefaultAdminName, winner, out _, out _),
                $"赢家 {(results[0].Ok ? "#0" : "#1")}");
        }

        /// <summary>
        /// ④ 用户<b>自助改密先提交</b>、管理员<b>重置后提交</b>（重置更早发起）：重置必须失败。
        ///
        /// 这正是现场那一档：用户 A 刚把密码改成自己记得住的那个，管理员更早发起、更晚提交的
        /// 重置把 A 的新密码覆盖回去，两条审计都记"成功"——现场两个人各以为知道自己现在的密码。
        /// 重置路径"不校验旧密码"是对的，但<b>不校验旧密码 ≠ 不比对任何东西</b>：提交前仍要比对
        /// 自己进入时看到的那份哈希。
        ///
        /// 窗口用 <see cref="ScadaUserStore.AfterSnapshotForTest"/> 钉住（重置取到快照后暂停，
        /// 改密在暂停期间提交），不靠两个线程赛跑碰运气——自然赛跑里重置只要一次 PBKDF2、
        /// 改密要两次，重置几乎总是先提交，于是"重置覆盖别人的新密码"这条反而测不到
        /// （实测：把重置的比对整段摘掉，自然赛跑仍然是绿的）。
        /// </summary>
        private static void RunResetVersusChangeRace(string path)
        {
            var store = new ScadaUserStore(path);   // 出厂 admin / admin123

            const string selfNew = "selfChanged1";
            const string resetNew = "adminReset1";

            var reset = (Ok: false, Error: (string?)null);
            bool paused = false;
            bool joined = false;
            bool changed = false;
            string? changeError = null;

            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                try
                {
                    store.AfterSnapshotForTest = () =>
                    {
                        entered.Set();
                        release.Wait(TimeSpan.FromMinutes(1));
                    };

                    var resetThread = new Thread(() =>
                    {
                        try
                        {
                            reset = (store.TryResetPassword(ScadaUserStore.DefaultAdminName, resetNew, out var error), error);
                        }
                        catch (Exception ex)
                        {
                            reset = (false, "抛异常：" + ex.Message);
                        }
                    })
                    { IsBackground = true, Name = "UC-ResetVsChange-Reset" };

                    resetThread.Start();

                    // 重置已经取到"旧哈希"的快照、卡在 PBKDF2 之前；此刻用户自助改密提交成功。
                    paused = entered.Wait(TimeSpan.FromSeconds(30));
                    changed = store.TryChangePassword(ScadaUserStore.DefaultAdminName,
                                                      ScadaUserStore.DefaultAdminPassword,
                                                      selfNew,
                                                      out changeError);

                    release.Set();
                    joined = resetThread.Join(TimeSpan.FromMinutes(1));
                }
                finally
                {
                    release.Set();
                    store.AfterSnapshotForTest = null;
                }
            }

            int successes = (changed ? 1 : 0) + (reset.Ok ? 1 : 0);

            Check("[UC16] 改密先提交、重置后提交（重置更早发起）：恰好一个成功，重置必须报「密码已被他人修改」（不许覆盖用户的新密码）",
                paused && changed && joined && successes == 1 && !reset.Ok && reset.Error == "密码已被他人修改",
                $"暂停点={paused} / 改密 {(changed ? "成功" : changeError ?? "null")} / 重置 {(reset.Ok ? "成功（故障）" : reset.Error ?? "null")}");

            // 换一个实例读回来：磁盘上必须也是用户刚设的那一个（写事务串行化 + 提交前比对一起保证）。
            var reloaded = new ScadaUserStore(path);

            Check("[UC16b] 并发之后：用户的新密码可用、重置写的新密码不可用，磁盘与内存一致（重读实例结论相同）",
                paused && joined && changed && !reset.Ok
                && store.TryValidate(ScadaUserStore.DefaultAdminName, selfNew, out _, out _)
                && !store.TryValidate(ScadaUserStore.DefaultAdminName, resetNew, out _, out _)
                && reloaded.TryValidate(ScadaUserStore.DefaultAdminName, selfNew, out _, out _)
                && !reloaded.TryValidate(ScadaUserStore.DefaultAdminName, resetNew, out _, out _),
                $"用户新密码 {selfNew} 可用 / 重置密码被拒 / 重读实例一致");
        }

        /// <summary>
        /// ⑤ 管理员<b>重置</b>密码 与 <b>删号</b> 并发：删号先落地时，重置必须失败。
        ///
        /// 这里刻意把删除放在重置的 PBKDF2 窗口内（重置先取快照、正在算哈希时删号提交），
        /// 就是"更早发起、更晚提交"的形状：重置若在提交前不重新查一次记录，就会对着一条
        /// 已经摘出清单的记录报"重置成功"——正是"用户已删但重置成功"那一档静默故障。
        ///
        /// 窗口用 <see cref="ScadaUserStore.AfterSnapshotForTest"/> 钉住，不靠 <c>Thread.Sleep</c>：
        /// Windows 计时器粒度约 15ms，与一次 PBKDF2 同量级，睡下去反而可能让重置先提交
        /// （第一版就是这么红的：重置成功、删号也成功）。
        /// </summary>
        private static void RunResetVersusRemoveRace(string path)
        {
            const string target = "重置靶子";
            const string oldPassword = "target123";
            const string resetPassword = "resetNew1";

            var store = new ScadaUserStore(path);
            bool added = store.TryAddUser(target, oldPassword, ScadaRole.Operator, out string? addError);

            var reset = (Ok: false, Error: (string?)null);
            bool removed = false;
            string? removeError = null;
            bool paused = false;
            bool joined = false;

            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                try
                {
                    store.AfterSnapshotForTest = () =>
                    {
                        entered.Set();
                        release.Wait(TimeSpan.FromMinutes(1));
                    };

                    var resetThread = new Thread(() =>
                    {
                        try
                        {
                            reset = (store.TryResetPassword(target, resetPassword, out var error), error);
                        }
                        catch (Exception ex)
                        {
                            reset = (false, "抛异常：" + ex.Message);
                        }
                    })
                    { IsBackground = true, Name = "UC-ResetVsRemove-Reset" };

                    resetThread.Start();

                    // 重置已经取到"账号还在"的快照、正卡在 PBKDF2 之前；现在删号必定先进写事务。
                    paused = entered.Wait(TimeSpan.FromSeconds(30));
                    removed = store.TryRemoveUser(target, out removeError);

                    release.Set();
                    joined = resetThread.Join(TimeSpan.FromMinutes(1));
                }
                finally
                {
                    release.Set();
                    store.AfterSnapshotForTest = null;
                }
            }

            bool userGone = !store.Users.Any(u => u.Name == target);

            Check("[UC17] 重置与删号并发（删号在重置的 PBKDF2 窗口内落地）：重置必须失败、账号确实没了（不许「用户已删但重置成功」）",
                added && paused && removed && joined && userGone
                && !reset.Ok
                && reset.Error == $"账号「{target}」不存在",
                $"建账号={(added ? "成功" : addError ?? "null")} / 暂停点={paused} / 删号 {(removed ? "成功" : removeError ?? "null")} / 账号{(userGone ? "已删" : "仍在")} / 重置 {(reset.Ok ? "成功（故障）" : reset.Error ?? "null")}");
        }

        /// <summary>
        /// ⑥ 重置与删号<b>同时起跑</b>（谁先进写事务谁算数）：不抛异常，最终状态与两个返回值自洽。
        ///
        /// 两种合法结局：重置先提交（此时账号还在 → 新密码可用）随后被删；或者删号先提交
        /// （此时重置只能报"账号不存在"）。<b>不合法</b>的是"删号成功却还能重置成功"——
        /// 那意味着重置在一条已经不存在的记录上落了笔。
        /// </summary>
        private static void RunResetVersusRemoveSimultaneous(string path)
        {
            const string target = "重置靶子";
            const string oldPassword = "target123";
            const string resetPassword = "resetNew1";

            var store = new ScadaUserStore(path);
            bool added = store.TryAddUser(target, oldPassword, ScadaRole.Operator, out string? addError);

            using var start = new ManualResetEventSlim(false);
            var reset = (Ok: false, Error: (string?)null);
            var remove = (Ok: false, Error: (string?)null);

            var resetThread = new Thread(() =>
            {
                start.Wait();
                try
                {
                    reset = (store.TryResetPassword(target, resetPassword, out var error), error);
                }
                catch (Exception ex)
                {
                    reset = (false, "抛异常：" + ex.Message);
                }
            })
            { IsBackground = true, Name = "UC-ResetVsRemove-Sim-Reset" };

            var removeThread = new Thread(() =>
            {
                start.Wait();
                try
                {
                    remove = (store.TryRemoveUser(target, out var error), error);
                }
                catch (Exception ex)
                {
                    remove = (false, "抛异常：" + ex.Message);
                }
            })
            { IsBackground = true, Name = "UC-ResetVsRemove-Sim-Remove" };

            resetThread.Start();
            removeThread.Start();
            start.Set();

            bool joined = JoinAll(new[] { resetThread, removeThread }, TimeSpan.FromMinutes(1));
            bool userGone = !store.Users.Any(u => u.Name == target);

            // 靶子是普通操作员，删号只有"成功"和"账号不存在"两种结局，所以返回值与清单必须一一对应。
            bool removeConsistent = remove.Ok == userGone;

            // 重置失败只可能是"账号不存在"；重置成功且账号还在时，新密码必须真的能用。
            bool resetConsistent = reset.Ok
                ? userGone || store.TryValidate(target, resetPassword, out _, out _)
                : reset.Error == $"账号「{target}」不存在";

            Check("[UC17b] 重置与删号同时起跑：不抛异常，返回值与最终清单自洽（删号成功 ⇔ 账号不在清单里）",
                added && joined && removeConsistent && resetConsistent,
                $"建账号={(added ? "成功" : addError ?? "null")} / 重置 {(reset.Ok ? "成功" : reset.Error ?? "null")} / 删号 {(remove.Ok ? "成功" : remove.Error ?? "null")} / 账号{(userGone ? "已删" : "仍在")}");
        }

        /// <summary>
        /// ⑦ 登录校验与改角色 / 删号并发：PBKDF2 期间账号变了，登录必须失败。
        ///
        /// 这里用 <see cref="ScadaUserStore.AfterSnapshotForTest"/> 把"快照已取、PBKDF2 未算"
        /// 这个窗口钉住：不钉的话，改角色只要没正好撞上那几十毫秒，断言就是绿的——
        /// 而绿的原因不是"没有被并发问题影响"，只是"没测到"。
        /// </summary>
        private static void RunValidateVersusRoleChange(string path)
        {
            var store = new ScadaUserStore(path);   // 出厂 admin

            const string target = "角色靶子";
            const string password = "role1234";

            // 造一个管理员靶子：把它降成操作员要过"至少留一个管理员"——出厂 admin 正好顶上。
            bool added = store.TryAddUser(target, password, ScadaRole.Administrator, out string? addError);

            // ---- ① 降权发生在 PBKDF2 期间 ----
            bool loginOk = true;
            ScadaRole loginRole = ScadaRole.Operator;
            string? loginError = null;
            bool loginThrew = false;

            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                try
                {
                    store.AfterSnapshotForTest = () =>
                    {
                        entered.Set();
                        release.Wait(TimeSpan.FromMinutes(1));
                    };

                    var loginThread = new Thread(() =>
                    {
                        try
                        {
                            loginOk = store.TryValidate(target, password, out loginRole, out loginError);
                        }
                        catch (Exception ex)
                        {
                            loginThrew = true;
                            loginError = ex.Message;
                        }
                    })
                    { IsBackground = true, Name = "UC-RoleRace-Login" };

                    loginThread.Start();
                    bool paused = entered.Wait(TimeSpan.FromSeconds(30));

                    bool demoted = store.TrySetRole(target, ScadaRole.Operator, out string? demoteError);

                    release.Set();
                    bool joined = loginThread.Join(TimeSpan.FromMinutes(1));

                    Check("[UC18] PBKDF2 期间被降权：登录必须失败，文案是「账号权限已变化，请重新登录」（不许拿旧角色登进去）",
                        added && paused && demoted && joined && !loginThrew && !loginOk
                        && loginError == "账号权限已变化，请重新登录",
                        $"建账号={(added ? "成功" : addError ?? "null")} / 暂停点={paused} / 降权={demoted}{(demoted ? "" : "（" + demoteError + "）")} / 登录 {(loginOk ? "成功，角色=" + loginRole : loginError ?? "null")}");
                }
                finally
                {
                    release.Set();
                    store.AfterSnapshotForTest = null;
                }
            }

            bool reLogin = store.TryValidate(target, password, out var currentRole, out string? reError);
            Check("[UC18b] 放行之后重新登录：拿到的是当前角色（操作员），不是快照里的旧角色（管理员）",
                reLogin && currentRole == ScadaRole.Operator
                && store.Users.Any(u => u.Name == target && u.Role == ScadaRole.Operator),
                reLogin ? $"角色 {currentRole.DisplayName()}" : reError ?? "null");

            // ---- ② 删号发生在 PBKDF2 期间 ----
            bool deleteLoginOk = true;
            string? deleteLoginError = null;
            bool deleteLoginThrew = false;

            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                try
                {
                    store.AfterSnapshotForTest = () =>
                    {
                        entered.Set();
                        release.Wait(TimeSpan.FromMinutes(1));
                    };

                    var loginThread = new Thread(() =>
                    {
                        try
                        {
                            deleteLoginOk = store.TryValidate(target, password, out _, out deleteLoginError);
                        }
                        catch (Exception ex)
                        {
                            deleteLoginThrew = true;
                            deleteLoginError = ex.Message;
                        }
                    })
                    { IsBackground = true, Name = "UC-DeleteRace-Login" };

                    loginThread.Start();
                    bool paused = entered.Wait(TimeSpan.FromSeconds(30));

                    // 靶子此时是操作员，删掉它不触发"至少留一个管理员"。
                    bool removed = store.TryRemoveUser(target, out string? removeError);

                    release.Set();
                    bool joined = loginThread.Join(TimeSpan.FromMinutes(1));

                    Check("[UC18c] PBKDF2 期间账号被删：登录必须失败，文案是「账号已被删除」（不许已删账号照常登入）",
                        paused && removed && joined && !deleteLoginThrew && !deleteLoginOk
                        && deleteLoginError == "账号已被删除",
                        $"暂停点={paused} / 删号={removed}{(removed ? "" : "（" + removeError + "）")} / 登录 {(deleteLoginOk ? "成功（故障）" : deleteLoginError ?? "null")}");
                }
                finally
                {
                    release.Set();
                    store.AfterSnapshotForTest = null;
                }
            }
        }

        /// <summary>逐条 Join；任一超时即返回 false（不无限等）。</summary>
        private static bool JoinAll(IEnumerable<Thread> threads, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;

            foreach (var thread in threads)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) return false;
                if (!thread.Join(remaining)) return false;
            }

            return true;
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"---- {title} ----");
        }

        private static void Check(string name, bool ok, string detail)
        {
            // 并发用例会从后台线程 Check；非原子 += 会丢计数，日志有红条而汇总 0 失败。
            if (ok) System.Threading.Interlocked.Increment(ref _pass);
            else System.Threading.Interlocked.Increment(ref _fail);
            Console.WriteLine($"  [{(ok ? "√通过" : "×失败")}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  →  " + detail)}");
        }
    }
}
