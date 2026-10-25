using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Database;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 执行上下文装配的接线断言（2026-10-24 评审团 C1 + B3 的守门）。
    ///
    /// 两条缺陷都出在"上下文里到底装了哪些能力面"这件事上，而且都是**静默**的：
    ///  · C1：单次执行那条路径手写对象初始化器时漏了 Database，同一张图纸
    ///    "连续跑能查库、单次跑必报没有接入数据库服务"——没有任何断言盯着注入清单是否齐全；
    ///  · B3：并行分支上下文只搬了部分能力面，其余静默回落成空实现，
    ///    "算子放在并行组外面就正常，放进去就不好使"。
    ///
    /// 修法的要害是把"装配"收成单点（ExecutionContext.Create）并把"能力面名单"
    /// 变成可反射比对的清单（ExecutionContext.RuntimeCapabilities）。所以断言也分成两半：
    ///  · 静态/反射半边：清单与各装配点一一对齐，**新增能力面忘了登记即判红**（不需要新写用例）；
    ///  · 行为半边：真跑一遍引擎，从插件体内把拿到手的实例抓出来比对身份（"是不是同一个实例"，
    ///    而不是"是不是非空"——非空判断会被默认空实现骗过去）。
    ///
    /// 每条判据都必须能被反证（AGENTS.md R38）：行为半边全部走真实的
    /// FlowEngineService.TryRunSessionOnceAsync，不是直接 new 一个上下文自证。
    /// </summary>
    internal static class EngineContextWiringChecks
    {
        internal static void Run()
        {
            CapabilityManifestIsComplete();
            NoHandWrittenContextInitializer();
            PluginTestRunnerUsesSingleEntry();
            SingleRunInjectsDatabase();
            ParallelBranchInheritsCapabilities();
        }

        // ==================================================================
        //  ① 能力面清单本身
        // ==================================================================
        private static void CapabilityManifestIsComplete()
        {
            Section("[W1] 能力面清单：登记完整 / 无重复 / 默认值非空");

            var manifest = ExecutionContext.RuntimeCapabilities;
            Check("清单非空（RuntimeCapabilities 至少登记 5 项）", manifest.Length >= 5, $"count={manifest.Length}");

            // 去重：同一属性登记两次会让 Create 里的反射循环白跑一遍，也让"漏搬"判断失真
            var dupes = manifest.GroupBy(p => p.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("清单无重复项", dupes.Count == 0, dupes.Count == 0 ? "" : string.Join("；", dupes));

            // 清单项必须是 ExecutionContext 自己的可写属性（子类属性在 base 构造期还不存在）
            var bad = manifest.Where(p => p.DeclaringType != typeof(ExecutionContext) || !p.CanWrite).ToList();
            Check("清单项都是 ExecutionContext 上可写的属性", bad.Count == 0, string.Join("；", bad.Select(p => $"{p.DeclaringType?.Name}.{p.Name}")));

            // 默认值非空：插件的写法是"只判返回值不判空"，默认 null 会让插件侧 NRE
            var probe = new ExecutionContext(new StubLog());
            var emptyDefaults = manifest.Where(p => p.GetValue(probe) == null).Select(p => p.Name).ToList();
            Check("每项能力面默认值非空（简化构造即可用，插件不必判空）", emptyDefaults.Count == 0,
                emptyDefaults.Count == 0 ? "" : string.Join("；", emptyDefaults));

            // 已知五项的显式点名：清单被误删一项时上面几条可能还绿（比如只剩 1 项也满足 ≥5？不满足），
            // 但"删掉 Database 同时把计数门槛改小"这类改动仍需一条按名字钉住的判据（R38 反证）
            var names = manifest.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var expected = new[] { "Cameras", "Comms", "Database", "FlowInvoker", "Motions" }
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check("清单恰好是 Cameras/Motions/FlowInvoker/Comms/Database 五项",
                names.SequenceEqual(expected),
                $"实际=[{string.Join(",", names)}]");
        }

        // ==================================================================
        //  ② 装配点不许再手写对象初始化器
        // ==================================================================
        private static void NoHandWrittenContextInitializer()
        {
            Section("[W2] 装配单点：第一方代码不许再手写 new ExecutionContext { … } 装能力面");

            // 静态扫描的是"装配"这件事，不是"构造上下文"这件事：
            // 带 init 属性对象初始化器的 new ExecutionContext 才有可能漏搬（C1/B3 的形态），
            // 裸构造（断言夹具 / 简化构造）不在扫描面内。
            // 判据收在仓库源码目录，排除 bin/obj 与 _Retired（退役件不参与构建，R168）。
            var root = RepoRoot.Value;
            Check("能定位仓库根（扫描面存在）", root != null, root ?? "(未找到 VisionMaster.sln 所在目录)");
            if (root == null) return;

            var offenders = new List<string>();
            foreach (var file in EnumerateFirstPartySources(root))
            {
                var text = File.ReadAllText(file);
                // 只认真正的对象初始化器 `new ExecutionContext(...) {`（中间允许参数与换行）
                if (Regex.IsMatch(text, @"new\s+ExecutionContext\s*\([^;{]*\)\s*\{"))
                    offenders.Add(Rel(root, file));
            }

            Check("第一方代码无手写能力面初始化器（新增即判红，改走 ExecutionContext.Create）",
                offenders.Count == 0,
                offenders.Count == 0 ? $"扫描 {_scannedCount} 个 .cs" : string.Join("；", offenders.Take(8)));

            // 正向：唯一入口必须真的存在且被三条生产路径调用
            var hits = new List<string>();
            foreach (var file in EnumerateFirstPartySources(root))
            {
                var text = File.ReadAllText(file);
                if (Regex.IsMatch(text, @"ExecutionContext\.Create\(")) hits.Add(Rel(root, file));
            }

            var engineHit = hits.Any(h => h.EndsWith("Engine/FlowEngineService.cs", StringComparison.OrdinalIgnoreCase));
            var runnerHit = hits.Any(h => h.EndsWith("Engine/PluginTestRunner.cs", StringComparison.OrdinalIgnoreCase));
            Check("流程引擎与插件试运行都走 ExecutionContext.Create（装配单点接线在）",
                engineHit && runnerHit,
                $"命中=[{string.Join(",", hits)}]");
        }

        private static int _scannedCount;

        private static readonly Lazy<string?> RepoRoot = new(() =>
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        });

        /// <summary>第一方源码目录（相对仓库根）。只扫会参与构建的代码，退役件与产物一律不扫。</summary>
        private static readonly string[] SourceDirs =
        {
            "Core", "Engine", "VisionMaster", "UI", "Plugins", "Shard",
        };

        private static IEnumerable<string> EnumerateFirstPartySources(string root)
        {
            foreach (var dirName in SourceDirs)
            {
                var dir = Path.Combine(root, dirName);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = Rel(root, file);
                    if (rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.Contains("_Retired", StringComparison.OrdinalIgnoreCase)) continue;
                    _scannedCount++;
                    yield return file;
                }
            }
        }

        private static string Rel(string root, string full)
            => Path.GetRelativePath(root, full).Replace('\\', '/');

        // ==================================================================
        //  ③ 插件试运行路径
        // ==================================================================
        private static void PluginTestRunnerUsesSingleEntry()
        {
            Section("[W3] 插件试运行：能力面与宿主同一份装配（此前自己写一份清单）");

            // 这一条只做接线确认：PluginTestRunner 的上下文必须经 ExecutionContext.Create，
            // 且来源里五项都要给（少给一项就回落成空实现，试运行环境与真跑不一致）。
            var root = RepoRoot.Value;
            if (root == null) { Check("能定位仓库根（插件试运行接线）", false, "(未找到 VisionMaster.sln)"); return; }

            var path = Path.Combine(root, "Engine", "PluginTestRunner.cs");
            Check("PluginTestRunner.cs 存在", File.Exists(path), path);
            if (!File.Exists(path)) return;

            var text = File.ReadAllText(path);
            var usesCreate = text.Contains("ExecutionContext.Create(", StringComparison.Ordinal);
            Check("试运行上下文走 ExecutionContext.Create（不再手写初始化器）", usesCreate,
                usesCreate ? "" : "未找到 ExecutionContext.Create 调用");

            // 五条来源赋值都必须在（Database 曾经是漏得最彻底的一项）
            var missing = new[] { "Cameras", "Motions", "FlowInvoker", "Comms", "Database" }
                .Where(n => !Regex.IsMatch(text, $@"\b{n}\s*="))
                .ToList();
            Check("五项能力来源全部传给试运行上下文", missing.Count == 0,
                missing.Count == 0 ? "" : "缺：" + string.Join("；", missing));
        }

        // ==================================================================
        //  ④ C1：单次执行路径真的把数据库递进去了
        // ==================================================================
        /// <summary>
        /// 探针算子：在<b>插件体内</b>把上下文里的能力面实例抓出来。
        ///
        /// 为什么要在插件体内抓：C1 的症状正是"插件拿到的上下文里没有数据库"。
        /// 从宿主侧看 FlowEngineService._database 一直是好的，只有从执行侧取才谈得上反证。
        /// [ParallelSafe]：只写 static 引用（每条用例前 Reset），无共享可变状态。
        /// </summary>
        [ParallelSafe]
        internal sealed class ContextProbePlugin : VisionPluginBase
        {
            public static IExecutionContext? Last;
            public static void Reset() => Last = null;

            public override void RunAlgorithm(IExecutionContext context) => Last = context;
        }

        /// <summary>相机仓库桩：只要不是 NullCameraProvider，就证明宿主真的把能力递进来了</summary>
        private sealed class RecordingCameraProvider : ICameraProvider
        {
            public IReadOnlyList<CameraDescriptor> Cameras => Array.Empty<CameraDescriptor>();
            public bool TryGetDevice(Guid cameraId, out ICameraDevice device) { device = null!; return false; }
            public bool TryGetDeviceBySerial(string serialNo, out ICameraDevice device) { device = null!; return false; }
        }

        /// <summary>运动卡仓库桩（同上口径）</summary>
        private sealed class RecordingMotionProvider : IMotionProvider
        {
            public IReadOnlyList<MotionDescriptor> Cards => Array.Empty<MotionDescriptor>();
            public bool TryGetDevice(Guid cardId, out IMotionDevice device) { device = null!; return false; }
            public bool TryGetByKey(string address, out IMotionDevice device) { device = null!; return false; }
            public bool TryGetByName(string displayName, out IMotionDevice device) { device = null!; return false; }
            public void ReleaseAll() { }
        }

        /// <summary>流程调用器桩：不真调流程，只证明"能力面是宿主那一份"</summary>
        private sealed class RecordingFlowInvoker : IFlowInvoker
        {
            public FlowInvokeResult Invoke(string flowName, int timeoutMs, CancellationToken cancellationToken)
                => FlowInvokeResult.Fail("断言桩：不真的调用流程", 0);
        }

        /// <summary>通讯层桩（同上口径：不是 NullCommVariableAccess 就说明注入成功）</summary>
        private sealed class RecordingComms : ICommVariableAccess
        {
            public bool TryDescribe(string variableName, out CommVariableInfo info, out string? error)
            {
                info = default;
                error = "断言桩：未配置通讯变量";
                return false;
            }

            public bool TryReadNow(string variableName, out object? value, out string? error)
            {
                value = null;
                error = "断言桩";
                return false;
            }

            public bool TryWriteNow(string variableName, object? value, out object? writtenValue, out string? error)
            {
                writtenValue = value;
                error = "断言桩";
                return false;
            }

            public bool TryQueueWriteNow(string variableName, object? value, out object? writtenValue, out string? error)
            {
                writtenValue = value;
                error = "断言桩";
                return false;
            }
        }

        /// <summary>
        /// 永远报"没接入数据库服务"之外的第二种错误路径的数据库桩：
        /// 只要被调用就说明**注入成功**（区别于 NullDatabaseAccess 的回落）。
        /// </summary>
        private sealed class RecordingDatabase : IDatabaseAccess
        {
            public int Calls;
            public object Sync = new();

            public IReadOnlyList<DbProfileInfo> ListProfiles()
            {
                lock (Sync) Calls++;
                return Array.Empty<DbProfileInfo>();
            }

            public bool TryGetProfile(Guid profileId, out DbProfileInfo info, out string? error)
            {
                lock (Sync) Calls++;
                info = null!;
                error = "断言桩：未配置任何 profile";
                return false;
            }

            public IReadOnlyList<DbStatementInfo> ListStatements(Guid profileId) => Array.Empty<DbStatementInfo>();

            public bool TryGetStatement(Guid statementId, out DbStatementInfo info, out string? error)
            {
                info = null!;
                error = "断言桩：未配置任何语句";
                return false;
            }

            public bool TryTest(Guid profileId, out string? versionText, out double elapsedMs, out string? error)
            {
                versionText = null;
                elapsedMs = 0;
                error = "断言桩";
                return false;
            }

            public bool TryQuery(Guid statementId, IReadOnlyList<DbParameterValue> parameters, DbCommandOptions options,
                out DbQueryResult result, out string? error)
            {
                result = default;
                error = "断言桩";
                return false;
            }

            public bool TryExecute(Guid statementId, IReadOnlyList<DbParameterValue> parameters, DbCommandOptions options,
                out DbExecutionResult result, out string? error)
            {
                result = default;
                error = "断言桩";
                return false;
            }

            public bool TryExecuteBatch(Guid statementId, IReadOnlyList<IReadOnlyList<DbParameterValue>> rows,
                DbCommandOptions options, out DbBatchResult result, out string? error)
            {
                result = default;
                error = "断言桩";
                return false;
            }

            public bool TryTestStatement(Guid statementId, IReadOnlyList<DbParameterValue> parameters,
                DbCommandOptions options, bool commitForReal, out DbTestResult result, out string? error)
            {
                result = default;
                error = "断言桩";
                return false;
            }
        }

        private static void SingleRunInjectsDatabase()
        {
            Section("[W4] C1 反证：单次执行路径把宿主那份数据库（同一实例）递到插件手里");

            ContextProbePlugin.Reset();
            var flow = new FlowModel { FlowName = "单次执行能力面流程" };
            flow.Steps.Add(new ActionStep("P1", "上下文探针", typeof(ContextProbePlugin).AssemblyQualifiedName!, "抓上下文"));

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            var log = new StubLog();
            var database = new RecordingDatabase();
            var cameras = new RecordingCameraProvider();
            var motions = new RecordingMotionProvider();
            var comms = new RecordingComms();
            var engine = new FlowEngineService(
                new RuntimeManager(), log, workspace, null, new ResourceLockService(),
                cameras,
                motions: motions,
                comms: comms,
                database: database);
            // FlowInvoker 是属性注入（引擎与调用器互相引用，构造期解不了环）
            engine.FlowInvoker = new RecordingFlowInvoker();

            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            Check("单次执行：图纸编译通过", compiled.Success,
                compiled.Success ? "" : string.Join("；", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data, CompiledVersion = flow.Version };
            session.AddBlueprintsDeep(flow.Steps);

            bool ran = engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();
            Check("单次执行真的跑完（返回 true）", ran, $"ran={ran}");

            var ctx = ContextProbePlugin.Last;
            Check("插件体内拿到执行上下文", ctx != null, ctx?.GetType().Name ?? "null");
            if (ctx == null) return;

            var actual = ctx.Database;
            Check("★插件拿到的 Database 就是宿主注入的那一份（不是 NullDatabaseAccess 回落）",
                ReferenceEquals(actual, database),
                $"actual={actual?.GetType().Name}");
            Check("插件拿到的 Database 不是空实现（回落即判红，这是 C1 的原症状）",
                actual is not NullDatabaseAccess, actual?.GetType().Name ?? "null");

            // 其余四项顺带核一遍：单次执行这条路以前只漏 Database，但"只断言漏过的那项"
            // 会让下一次漏别项时无人发现（R38：闸要说清覆盖了什么）。
            // 判据取"与宿主那份同一实例"而不是"非空"——非空判断会被默认空实现骗过去。
            var expected = new (string Name, object Host)[]
            {
                ("Cameras", cameras), ("Motions", motions), ("Comms", comms),
                ("FlowInvoker", engine.FlowInvoker),
            };
            var wrong = new List<string>();
            foreach (var (name, host) in expected)
            {
                var got = ExecutionContext.RuntimeCapabilities.First(p => p.Name == name).GetValue(ctx);
                if (!ReferenceEquals(got, host)) wrong.Add($"{name}={got?.GetType().Name ?? "null"}");
            }
            Check("同路径其余四项能力面也是宿主那份实例（按清单逐项比对身份）",
                wrong.Count == 0, wrong.Count == 0 ? $"比对 {expected.Length} 项" : string.Join("；", wrong));

            // 反向对照（R38）：把 database 传 null 时，插件必须拿到空实现而不是 null——
            // 证明上面那条 ReferenceEquals 真的在区分"注入了"和"没注入"，而不是恒真
            var engineNoDb = new FlowEngineService(
                new RuntimeManager(), log, workspace, null, new ResourceLockService(),
                NullCameraProvider.Instance);
            ContextProbePlugin.Reset();
            var session2 = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data, CompiledVersion = flow.Version };
            session2.AddBlueprintsDeep(flow.Steps);
            engineNoDb.TryRunSessionOnceAsync(session2).GetAwaiter().GetResult();
            Check("反向对照：不注入数据库时插件拿到的是 NullDatabaseAccess（判据确实在区分注入与否）",
                ContextProbePlugin.Last?.Database is NullDatabaseAccess,
                ContextProbePlugin.Last?.Database?.GetType().Name ?? "(插件未被调用)");
        }

        // ==================================================================
        //  ⑤ B3：并行分支能力面与父上下文同源
        // ==================================================================
        /// <summary>并行分支里的探针：把分支上下文抓出来（每条分支各抓一份）</summary>
        [ParallelSafe]
        internal sealed class BranchProbePlugin : VisionPluginBase
        {
            private static readonly List<IExecutionContext> _seen = new();
            public static IExecutionContext[] Snapshot() { lock (_seen) return _seen.ToArray(); }
            public static void Reset() { lock (_seen) _seen.Clear(); }

            public override void RunAlgorithm(IExecutionContext context)
            {
                lock (_seen) _seen.Add(context);
            }
        }

        private static void ParallelBranchInheritsCapabilities()
        {
            Section("[W5] B3 反证：并行分支上下文的能力面与父上下文同一批实例");

            BranchProbePlugin.Reset();
            var par = new ParallelStep("P", "并行分组", "BuiltIn_Parallel", "能力面继承组")
            {
                // 全限定名：System.Linq 也有个 ParallelExecutionMode（隐式 using），不限定则 CS0104
                ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel,
            };
            par.Children[0].Steps.Add(new ActionStep("B1", "分支探针", typeof(BranchProbePlugin).AssemblyQualifiedName!, "分支1"));
            par.Children[1].Steps.Add(new ActionStep("B2", "分支探针", typeof(BranchProbePlugin).AssemblyQualifiedName!, "分支2"));

            var flow = new FlowModel { FlowName = "并行能力面流程" };
            flow.Steps.Add(par);

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            var log = new StubLog();
            var database = new RecordingDatabase();
            var cameras = new RecordingCameraProvider();
            var motions = new RecordingMotionProvider();
            var comms = new RecordingComms();
            var engine = new FlowEngineService(
                new RuntimeManager(), log, workspace, null, new ResourceLockService(),
                cameras,
                motions: motions,
                comms: comms,
                database: database);
            engine.FlowInvoker = new RecordingFlowInvoker();

            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            Check("并行图纸编译通过（能力面继承流程）", compiled.Success,
                compiled.Success ? "" : string.Join("；", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data, CompiledVersion = flow.Version };
            session.AddBlueprintsDeep(flow.Steps);
            engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();

            var seen = BranchProbePlugin.Snapshot();
            Check("两条分支都执行到并抓到分支上下文", seen.Length == 2, $"count={seen.Length}");
            if (seen.Length == 0) return;

            var branchCtxs = seen.OfType<ParallelBranchExecutionContext>().ToArray();
            Check("抓到的都是 ParallelBranchExecutionContext（确认分支路径）",
                branchCtxs.Length == seen.Length, $"ofType={branchCtxs.Length} total={seen.Length}");
            if (branchCtxs.Length == 0) return;

            // 逐一按清单比对：分支上下文与宿主的这一份**实例相同**才算继承到位。
            // 这里刻意不按类型名猜（"Null 开头即回落"那种判法会被"凭空 new 了一个非空实现"骗过去），
            // 一律 ReferenceEquals 比身份——B3 的修法就是把父上下文那五项原样搬过来。
            var hostFaces = new Dictionary<string, object>
            {
                ["Cameras"] = cameras,
                ["Motions"] = motions,
                ["FlowInvoker"] = engine.FlowInvoker,
                ["Comms"] = comms,
                ["Database"] = database,
            };

            var mismatch = new List<string>();
            foreach (var property in ExecutionContext.RuntimeCapabilities)
            {
                hostFaces.TryGetValue(property.Name, out var host);
                foreach (var branch in branchCtxs)
                {
                    var inBranch = property.GetValue(branch);
                    if (inBranch == null) { mismatch.Add($"{property.Name}=null(branch{branch.BranchIndex})"); continue; }
                    if (!ReferenceEquals(inBranch, host))
                        mismatch.Add($"{property.Name}≠父上下文({inBranch.GetType().Name},branch{branch.BranchIndex})");
                }
            }

            Check("★分支上下文五项能力面全部与父上下文同一实例（无一项回落/无一项新建）",
                mismatch.Count == 0,
                mismatch.Count == 0 ? $"分支 {branchCtxs.Length} 份 × {ExecutionContext.RuntimeCapabilities.Length} 项" : string.Join("；", mismatch.Take(8)));

            Check("★并行分支拿到的 Database 与宿主注入的是同一实例",
                branchCtxs.All(b => ReferenceEquals(b.Database, database)),
                string.Join("；", branchCtxs.Select(b => $"branch{b.BranchIndex}={b.Database?.GetType().Name}")));

            // 反向：分支上下文有独立的分支号（不是把父上下文原样递进来——那样上面几条同样是真，
            // 就证明不了"分支上下文确实是从父派生出来的另一份实例"）
            Check("反向对照：分支上下文是独立实例（分支号互不相同，说明确为逐分支派生）",
                branchCtxs.Select(b => b.BranchIndex).Distinct().Count() == branchCtxs.Length,
                "BranchIndex=" + string.Join(",", branchCtxs.Select(b => b.BranchIndex)));
        }
    }
}
