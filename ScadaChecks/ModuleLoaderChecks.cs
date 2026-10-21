using System;
using System.IO;
using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Lifetime;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [ML] 可选模块「SCADA 运行态 Web 客户端」的加载器（<see cref="ScadaWebModuleLoader"/>）
        /// 与模块自身幂等性（<c>ScadaWebModule</c>）。
        ///
        /// 覆盖的六件事
        /// ---------
        /// ① 目录里没有 <c>AppConfig.json</c> → 返回 null + 诊断（程序目录不对）；
        /// ② <c>ScadaWeb.Enabled = false</c> → 返回 null 且诊断<b>为 null</b>（正常状态不许刷 Warn），
        ///    且**零 LoadFrom**——这条用"Web\ 里放一个坏 DLL"反证：真去加载就会拿到 BadImageFormat 诊断；
        /// ③ 启用但 <c>Web\VisionMaster.ScadaWeb.dll</c> 缺失 → null + 诊断带完整路径；
        /// ④ 启用且 Web\ 里出现 <c>VM.Core.dll</c> 副本 → null + 诊断写明"两份契约/宿主程序集副本"
        ///    （两份程序集 = 两套类型身份，与红线"契约只许宿主目录一份"同源）；
        /// ⑤ 启用 + 真模块 DLL → 非 null、类型与 <c>Name</c> 正确（证明加载器不只会在错误路径上返回 null）；
        /// ⑥ 同一模块 <c>Initialize</c> 连续调两次只执行一次（第二次静默返回，不重复写日志/注册退出任务）。
        ///
        /// 为什么真模块 DLL 要从宿主输出目录取、而不是引用 Web 工程
        /// ---------
        /// 加载器测的是"运行时按路径 LoadFrom 能否拿到 IAppModule"——引用 Web 工程会让类型在编译期
        /// 就进了断言宿主，测出来的只是"类型存在"，测不到加载路径。所以从
        /// <c>VisionMaster\bin\&lt;Debug|Release&gt;\net9.0-windows\Web\</c> 拷一份到临时目录再加载。
        /// 找不到就判红（不写"跳过"）：缺件恰恰说明 Web 工程没构建，那正是该修的事（R38）。
        ///
        /// 夹具纪律
        /// ---------
        /// 所有文件都落在<b>临时目录</b>，不碰真实程序目录；模块 DLL 一旦被 LoadFrom 就锁住文件，
        /// 所以清理失败只吞掉异常（临时目录，不影响断言结果）。
        ///
        /// 接线一行：<c>ModuleLoaderChecks.Run();</c>
        /// </summary>
        public static class ModuleLoaderChecks
        {
            /// <summary>接线一行：<c>ModuleLoaderChecks.Run();</c></summary>
            public static void Run() => RunModuleLoaderChecks();
        }

        private static void RunModuleLoaderChecks()
        {
            Section("[ML] SCADA Web 模块加载器：开关 / 缺件 / 契约副本 / 真模块 / 幂等");

            var root = Path.Combine(
                Path.GetTempPath(),
                "ScadaChecks.ModuleLoader." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                CheckMissingConfig(root);
                CheckDisabledDoesNotLoad(root);
                CheckMissingAssembly(root);
                CheckContractCopyRejected(root);

                var module = CheckRealModuleLoads(root);
                if (module != null)
                    CheckInitializeIsIdempotent(module);
            }
            finally
            {
                // 真模块 DLL 已被 LoadFrom 锁住时删除会抛 IOException：临时目录，忽略即可
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        // ==================================================================
        //  ① 没有 AppConfig.json：程序目录不对
        // ==================================================================
        private static void CheckMissingConfig(string root)
        {
            var directory = Path.Combine(root, "no-config");
            Directory.CreateDirectory(directory);

            var module = ScadaWebModuleLoader.TryLoad(directory, out var diagnostic);

            Check("[ML1] 目录里没有 AppConfig.json → 不加载，诊断指向缺失的配置文件",
                module == null && diagnostic != null
                    && diagnostic.Contains("AppConfig.json", StringComparison.Ordinal),
                module == null
                    ? diagnostic ?? "diagnostic=null（缺配置被当成正常状态吞掉了）"
                    : "竟然返回了模块");
        }

        // ==================================================================
        //  ② Enabled=false：正常状态，零 LoadFrom
        // ==================================================================
        private static void CheckDisabledDoesNotLoad(string root)
        {
            var directory = Path.Combine(root, "disabled");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "AppConfig.json"), "{\"ScadaWeb\":{\"Enabled\":false}}");

            // 反证用的坏 DLL：Enabled=false 若还去 LoadFrom，会先撞 BadImageFormat，
            // 返回的就不是 "null + diagnostic=null" 这个正常状态了。
            var webDirectory = Path.Combine(directory, ScadaWebModuleLoader.WebDirectoryName);
            Directory.CreateDirectory(webDirectory);
            File.WriteAllText(
                Path.Combine(webDirectory, ScadaWebModuleLoader.AssemblyFileName),
                "this is not a PE file");

            var module = ScadaWebModuleLoader.TryLoad(directory, out var diagnostic);

            Check("[ML2] Enabled=false → 不加载且不产生诊断（零 LoadFrom，坏 DLL 都不碰）",
                module == null && diagnostic == null,
                module != null
                    ? "竟然返回了模块"
                    : diagnostic ?? "null + null（正常状态）");
        }

        // ==================================================================
        //  ③ 启用但缺模块 DLL
        // ==================================================================
        private static void CheckMissingAssembly(string root)
        {
            var directory = Path.Combine(root, "missing");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "AppConfig.json"), "{\"ScadaWeb\":{\"Enabled\":true}}");

            var expectedPath = Path.Combine(
                directory,
                ScadaWebModuleLoader.WebDirectoryName,
                ScadaWebModuleLoader.AssemblyFileName);

            var module = ScadaWebModuleLoader.TryLoad(directory, out var diagnostic);

            Check("[ML3] Enabled=true 但 Web\\ 缺模块 DLL → 不加载，诊断带完整路径",
                module == null && diagnostic != null
                    && diagnostic.Contains(expectedPath, StringComparison.OrdinalIgnoreCase),
                module == null
                    ? diagnostic ?? "diagnostic=null（缺件没有留下原因）"
                    : "竟然返回了模块");
        }

        // ==================================================================
        //  ④ Web\ 里出现宿主/契约程序集副本
        // ==================================================================
        private static void CheckContractCopyRejected(string root)
        {
            var directory = Path.Combine(root, "contract-copy");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "AppConfig.json"), "{\"ScadaWeb\":{\"Enabled\":true}}");

            var webDirectory = Path.Combine(directory, ScadaWebModuleLoader.WebDirectoryName);
            Directory.CreateDirectory(webDirectory);

            // 占位模块 DLL：契约检查排在加载之前，内容不必是真程序集
            File.WriteAllText(
                Path.Combine(webDirectory, ScadaWebModuleLoader.AssemblyFileName),
                "placeholder");

            var contractSource = Path.Combine(AppContext.BaseDirectory, "VM.Core.dll");
            if (!File.Exists(contractSource))
            {
                Check("[ML4] Web\\ 里出现 VM.Core.dll → 拒绝加载（两份契约/宿主程序集副本）", false,
                    $"断言夹具缺失：{contractSource}（VM.Core 没有被复制到断言宿主输出目录）");
                return;
            }

            File.Copy(contractSource, Path.Combine(webDirectory, "VM.Core.dll"), overwrite: true);

            var module = ScadaWebModuleLoader.TryLoad(directory, out var diagnostic);
            var ok = module == null && diagnostic != null
                && diagnostic.Contains("两份契约", StringComparison.Ordinal)
                && diagnostic.Contains("宿主程序集副本", StringComparison.Ordinal)
                && diagnostic.Contains("VM.Core.dll", StringComparison.OrdinalIgnoreCase);

            Check("[ML4] Web\\ 里出现 VM.Core.dll → 拒绝加载（两份契约/宿主程序集副本）",
                ok,
                module == null ? diagnostic ?? "diagnostic=null" : "竟然返回了模块");
        }

        // ==================================================================
        //  ⑤ 真模块 DLL：加载路径通
        // ==================================================================
        private static IAppModule? CheckRealModuleLoads(string root)
        {
            var sourceDll = FindBuiltModuleDll();
            if (sourceDll == null)
            {
                Check("[ML5] 真模块 DLL → 非 null、类型与 Name 正确", false,
                    "找不到 VisionMaster\\bin\\<Debug|Release>\\net9.0-windows\\Web\\"
                    + ScadaWebModuleLoader.AssemblyFileName
                    + "；先构建 Web\\VisionMaster.ScadaWeb");
                return null;
            }

            var directory = Path.Combine(root, "real");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "AppConfig.json"), "{\"ScadaWeb\":{\"Enabled\":true}}");

            var webDirectory = Path.Combine(directory, ScadaWebModuleLoader.WebDirectoryName);
            Directory.CreateDirectory(webDirectory);
            File.Copy(
                sourceDll,
                Path.Combine(webDirectory, ScadaWebModuleLoader.AssemblyFileName),
                overwrite: true);

            var module = ScadaWebModuleLoader.TryLoad(directory, out var diagnostic);

            Check("[ML5] 真模块 DLL → 非 null、类型与 Name 正确",
                module != null
                    && module.Name == "SCADA Web 运行态模块"
                    && module.GetType().FullName == "VisionMaster.ScadaWeb.ScadaWebModule",
                module == null
                    ? diagnostic ?? "diagnostic=null"
                    : $"Name={module.Name}, Type={module.GetType().FullName}");

            return module;
        }

        // ==================================================================
        //  ⑥ Initialize 幂等：连续两次只执行一次
        // ==================================================================
        private static void CheckInitializeIsIdempotent(IAppModule module)
        {
            var log = new CountingLogService();
            var lifetime = new AppLifetimeService(log);
            var container = new StubContainerProvider(log);

            string? failure = null;
            try
            {
                module.Initialize(container, lifetime);
                // 第二次必须静默返回：不重复写日志、不重复注册退出任务（重复注册会让退出链跑两遍）
                module.Initialize(container, lifetime);
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}：{ex.Message}";
            }

            Check("[ML6] Initialize 连续调用两次只执行一次（日志 1 条、第二次不抛）",
                failure == null && log.InfoCount == 1,
                failure ?? $"InfoCount={log.InfoCount}（期望 1）");
        }

        /// <summary>
        /// 从断言宿主的输出目录向上找仓库根，再定位宿主的
        /// <c>VisionMaster\bin\&lt;Debug|Release&gt;\net9.0-windows\Web\VisionMaster.ScadaWeb.dll</c>。
        /// 先 Debug 后 Release：常规开发与主会话的断言都跑 Debug 产物。
        /// </summary>
        private static string? FindBuiltModuleDll()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            for (var depth = 0; depth < 8 && directory != null; depth++, directory = directory.Parent)
            {
                var binRoot = Path.Combine(directory.FullName, "VisionMaster", "bin");
                if (!Directory.Exists(binRoot))
                    continue;

                foreach (var configuration in new[] { "Debug", "Release" })
                {
                    var candidate = Path.Combine(
                        binRoot,
                        configuration,
                        "net9.0-windows",
                        ScadaWebModuleLoader.WebDirectoryName,
                        ScadaWebModuleLoader.AssemblyFileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 只给幂等断言用的容器桩：<c>Initialize</c> 只解析 <see cref="ILogService"/>，
        /// 其余类型一律返回 null（真去解析说明产品代码长出了断言不知道的新依赖，应判红而不是静默放过）。
        /// </summary>
        private sealed class StubContainerProvider : IContainerProvider
        {
            private readonly ILogService _log;

            public StubContainerProvider(ILogService log) => _log = log;

            public IScopedProvider CurrentScope => null!;

            public IScopedProvider CreateScope()
                => throw new NotSupportedException("断言桩不创建作用域");

            // 接口签名是非空 object：拿不到就 null!（Initialize 只解析 ILogService，其余不该被解析）
            public object Resolve(Type type) => type == typeof(ILogService) ? _log : null!;

            public object Resolve(Type type, params (Type Type, object Instance)[] parameters)
                => Resolve(type);

            public object Resolve(Type type, string name) => Resolve(type);

            public object Resolve(Type type, string name, params (Type Type, object Instance)[] parameters)
                => Resolve(type);
        }

        /// <summary>
        /// 只数 <c>Info</c> 次数的日志桩：模块初始化成功会写恰好一行 <c>[ScadaWeb] 模块已加载…</c>，
        /// 第二次 <c>Initialize</c> 若没被幂等守卫拦住就会变成 2 条。
        /// </summary>
        private sealed class CountingLogService : ILogService
        {
            public int InfoCount { get; private set; }

            public void Success(params string[] messages) { }

            public void Error(params Exception[] messages) { }

            public void Error(params string[] messages) { }

            public void Info(params string[] messages) => InfoCount++;

            public void Warn(params string[] messages) { }
        }
    }
}
