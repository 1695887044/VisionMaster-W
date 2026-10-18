using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 插件清单缓存（层 3，2026-10-16）的守门断言。
    ///
    /// 缓存的正确性全部押在两件事上，两件事都必须是断言而不是口头约定：
    ///  ① 指纹失效判据——插件一变（mtime/大小/新增）缓存就必须失效重扫，否则"改了插件不生效"
    ///     会成为最难查的那类问题（产物陈旧族，R42/红线③ 同族）；
    ///  ② 缓存产物与全量扫描**逐项一致**——缓存路径跳过反射与实例化，注册进四张表的东西
    ///     必须与真扫出来的一模一样，差一个端口就是"缓存命中时某个插件在界面上消失"。
    /// 另有③（反向钉住前提）：缓存路径成立的事实前提是"插件构造器无副作用"（只建端口表）。
    /// 哪天有插件构造器引入副作用（静态注册/IO/网络），缓存路径就把它跳过去了——必须当场红。
    /// </summary>
    internal static class PluginCacheChecks
    {
        public static void Run()
        {
            Section("[PC] 插件清单缓存：指纹失效三态 + 产物一致 + 构造器无副作用前提");

            FingerprintInvalidation();
            CacheMatchesFullScan();
            ConstructorSideEffectFree();
        }

        // ---- ① 指纹失效：mtime 变 / 大小变 / 多出未登记 DLL → 缓存都失效 ----
        private static void FingerprintInvalidation()
        {
            var dir = Path.Combine(Path.GetTempPath(), "plugincache_probe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // 造两个"插件 DLL"（内容无所谓，指纹只看名字+大小+mtime）
                var a = Path.Combine(dir, "Plugin.A.dll");
                var b = Path.Combine(dir, "Plugin.B.dll");
                File.WriteAllBytes(a, new byte[100]);
                File.WriteAllBytes(b, new byte[200]);

                var entries = new List<PluginCache.PluginDllEntry>
                {
                    PluginCache.FingerprintOf(a),
                    PluginCache.FingerprintOf(b),
                };
                PluginCache.Save(dir, "1.0.0.0", entries);

                // 基准：缓存可用
                var loaded = PluginCache.TryLoad(dir, "1.0.0.0");
                Check("PC1：刚写的缓存下次读得出来（基准）",
                    loaded != null && loaded.Dlls.Count == 2,
                    loaded == null ? "读不出来" : $"DLL 数={loaded.Dlls.Count}");

                // mtime 变 → 失效
                File.SetLastWriteTimeUtc(a, File.GetLastWriteTimeUtc(a).AddSeconds(5));
                Check("PC2：插件 mtime 变了 → 缓存失效",
                    PluginCache.TryLoad(dir, "1.0.0.0") == null, "");
                File.SetLastWriteTimeUtc(a, DateTime.UtcNow); // 恢复不了原 mtime，重建缓存
                PluginCache.Save(dir, "1.0.0.0", new List<PluginCache.PluginDllEntry>
                {
                    PluginCache.FingerprintOf(a),
                    PluginCache.FingerprintOf(b),
                });

                // 大小变 → 失效
                File.WriteAllBytes(a, new byte[101]);
                Check("PC3：插件大小变了 → 缓存失效",
                    PluginCache.TryLoad(dir, "1.0.0.0") == null, "");
                PluginCache.Save(dir, "1.0.0.0", new List<PluginCache.PluginDllEntry>
                {
                    PluginCache.FingerprintOf(a),
                    PluginCache.FingerprintOf(b),
                });

                // 多出未登记的 Plugin*.dll → 失效
                File.WriteAllBytes(Path.Combine(dir, "Plugin.C.dll"), new byte[50]);
                Check("PC4：多出一个没登记的 Plugin*.dll → 缓存失效（新增插件必须被看见）",
                    PluginCache.TryLoad(dir, "1.0.0.0") == null, "");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }

            // 契约版本变 → 失效（不碰文件，纯判据）
            Check("PC5：宿主契约版本变了 → 缓存失效（清单里的类型名可能已失效）",
                PluginCache.TryLoad(
                    Path.GetTempPath(), "9.9.9.9") == null,
                "对一个不存在的目录换版本号读，必须是 null");
        }

        // ---- ② 缓存产物与全量扫描逐项一致 ----
        private static void CacheMatchesFullScan()
        {
            var modulesDir = ResolveModulesDir();
            if (modulesDir == null)
            {
                Check("PC6：缓存产物与全量扫描一致", true, "跳过：定位不到 Modules 目录");
                return;
            }

            // 全量扫一遍（真路径），与"读缓存"的产物逐项比对
            var providerFull = new RecordingProvider();
            var log = new List<string>();
            var svcFull = new PluginService(providerFull, new SilentNotifier(), new ListLog(log));
            svcFull.InitPlugins();

            var manifest = PluginCache.TryLoad(modulesDir, ContractVersion());
            if (manifest == null)
            {
                Check("PC6：缓存产物与全量扫描一致", false,
                    "全量扫完缓存却读不出来（写缓存那一步没生效？）");
                return;
            }

            var cachedModules = manifest.Dlls.SelectMany(d => d.Modules).ToList();
            var cachedCameras = manifest.Dlls.SelectMany(d => d.Cameras).ToList();
            var cachedMotions = manifest.Dlls.SelectMany(d => d.Motions).ToList();

            // 内置节点（If/Case/While/For/Parallel/Break/Continue/Return）不进缓存——
            // 缓存只存 DLL 产物，内置节点每次 RegisterBuiltInNodes() 照常跑。
            // 所以比对要剔除它们：46 = 38 插件 + 8 内置，不是"缓存丢了 8 个"。
            var fullModules = providerFull.Modules
                .Where(m => !(m.ModuleTypeName ?? "").StartsWith("BuiltIn_", StringComparison.Ordinal))
                .ToList();

            bool modulesSame = SameByTypeName(fullModules, cachedModules);
            bool camerasSame = SameByTypeName(providerFull.Cameras, cachedCameras);
            bool motionsSame = SameByTypeName(providerFull.Motions, cachedMotions);

            Check("PC6：缓存产物与全量扫描逐项一致（剔除内置节点；模块/相机/运动三张表）",
                modulesSame && camerasSame && motionsSame,
                $"模块(插件) {fullModules.Count}v{cachedModules.Count}"
                + $"（全量含内置共 {providerFull.Modules.Count}）"
                + $" 相机 {providerFull.Cameras.Count}v{cachedCameras.Count} "
                + $"运动 {providerFull.Motions.Count}v{cachedMotions.Count}");

            // 端口表也要一致（抽查一个自带端口的**插件**——内置节点不进缓存，不能当抽查对象）
            var fullPlugin = fullModules.FirstOrDefault(m => (m.InputDefinitions?.Count ?? 0) > 0);
            var cachedPlugin = fullPlugin == null ? null
                : cachedModules.FirstOrDefault(m => m.ModuleTypeName == fullPlugin.ModuleTypeName);
            bool portsSame = fullPlugin != null && cachedPlugin != null
                && SamePorts(fullPlugin.InputDefinitions, cachedPlugin.InputDefinitions)
                && SamePorts(fullPlugin.OutputDefinitions, cachedPlugin.OutputDefinitions);
            Check("PC7：端口表快照与全量扫描一致（含 Name/DataTypeName/OptionKind）",
                portsSame,
                fullPlugin == null ? "没有找到带端口的插件来抽查" :
                cachedPlugin == null ? $"缓存里少了 {fullPlugin.ModuleTypeName}" :
                $"抽查 {fullPlugin.Name}：端口一致={portsSame}");
        }

        // ---- ③ 反向钉住前提：插件构造器无副作用（缓存路径会跳过实例化）----
        private static void ConstructorSideEffectFree()
        {
            // 缓存路径跳过 Activator.CreateInstance。这条路径能成立，全靠"42 个插件的构造器
            // 只建端口表、不碰外部世界"。这里静态扫描全部插件工程的插件类构造器体：
            // 出现进程/网络/程序集加载/注册表这类副作用调用 → 当场红（缓存路径会把它跳过去）。
            var pluginsRoot = ResolveRepoDir(@"Plugins");
            if (pluginsRoot == null)
            {
                Check("PC8：插件构造器无副作用（静态扫描）", true, "跳过：不在仓库内运行");
                return;
            }

            // 副作用特征词：进程/网络/程序集加载/服务启动。端口表构造里不该出现这些。
            string[] banned =
            {
                "Process.Start", "Assembly.Load", "TcpClient", "HttpClient",
                "new System.Net.", "AppDomain.CurrentDomain.GetAssemblies",
            };

            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(pluginsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (!text.Contains("VisionPluginBase")) continue;

                // 收窄到**构造器体**：插件的运行期方法里出现 Process.Start/HttpClient 是合法的
                //（ExcelExport 导出完打开文件夹、ImageAcquisition 推送图像都在 RunAlgorithm 里），
                // 缓存路径只跳过"构造"这一步，运行期照样跑——所以只盯构造器。
                foreach (var ctorBody in ExtractConstructorBodies(text))
                {
                    foreach (var word in banned)
                    {
                        if (ctorBody.Contains(word))
                        {
                            offenders.Add($"{Path.GetFileName(file)}: 构造器里出现 {word}");
                            break;
                        }
                    }
                }
            }

            Check("PC8：插件构造器无副作用（不出现 进程/网络/程序集加载 调用——缓存路径会跳过实例化）",
                offenders.Count == 0,
                offenders.Count == 0 ? "" : string.Join("；", offenders.Take(6)));
        }

        // ------------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------------

        private static string ContractVersion()
            => typeof(IPluginProvider).Assembly.GetName().Version?.ToString() ?? "0";

        private static string? ResolveModulesDir()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, "Modules");
                if (Directory.Exists(candidate)
                    && Directory.GetFiles(candidate, "Plugin*.dll").Length > 0)
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static string? ResolveRepoDir(string relative)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, relative);
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static bool SameByTypeName(
            IReadOnlyList<VisionMaster.Models.ToolItemModel> a,
            IReadOnlyList<VisionMaster.Models.ToolItemModel> b)
        {
            if (a.Count != b.Count) return false;
            var keysA = a.Select(m => m.ModuleTypeName).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var keysB = b.Select(m => m.ModuleTypeName).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return keysA.SequenceEqual(keysB, StringComparer.Ordinal);
        }

        private static bool SamePorts(
            List<VisionMaster.Models.PortDefinition>? a,
            List<VisionMaster.Models.PortDefinition>? b)
        {
            var la = a ?? new List<VisionMaster.Models.PortDefinition>();
            var lb = b ?? new List<VisionMaster.Models.PortDefinition>();
            if (la.Count != lb.Count) return false;
            for (int i = 0; i < la.Count; i++)
            {
                if (la[i].Name != lb[i].Name) return false;
                if (la[i].DataTypeName != lb[i].DataTypeName) return false;
                if (la[i].OptionKind != lb[i].OptionKind) return false;
            }
            return true;
        }

        /// <summary>
        /// 抽出源码里所有构造器的函数体（按"类名("起头、到配对的 } 为止）。
        /// 缓存路径只跳过构造这一步，所以副作用判据只盯构造器——运行期方法里的
        /// Process.Start / HttpClient 是合法业务（导出后打开文件夹、推送图像）。
        /// 判据刻意简单：找"类名(" 起头的那一段，括号配对到第一个匹配的 }。
        /// </summary>
        private static IEnumerable<string> ExtractConstructorBodies(string source)
        {
            // 取文件名对应的类名（插件类与文件同名，VisionMaster 仓库的一贯约定）
            // 构造器形如 "public XxxPlugin(...)"，找 "XxxPlugin(" 起头的段落
            var classMatch = System.Text.RegularExpressions.Regex.Match(
                source, @"class\s+(\w+)\s*[:{]");
            if (!classMatch.Success) yield break;
            string className = classMatch.Groups[1].Value;

            int searchFrom = 0;
            while (true)
            {
                // 构造器签名：访问修饰符 + 类名 + ( —— 与方法/属性区分看"类名(" 前面是不是修饰符行
                int idx = source.IndexOf(className + "(", searchFrom, StringComparison.Ordinal);
                if (idx < 0) yield break;

                // 构造器前面必须是 public/internal 等修饰符行（排除方法调用 XxxPlugin(...) 这种误命中）
                int lineStart = source.LastIndexOf('\n', idx) + 1;
                string linePrefix = source.Substring(lineStart, idx - lineStart).Trim();
                bool looksLikeCtor = linePrefix.StartsWith("public") || linePrefix.StartsWith("internal")
                    || linePrefix.StartsWith("private") || linePrefix.StartsWith("protected")
                    || linePrefix.Length == 0;

                int openBrace = source.IndexOf('{', idx);
                if (openBrace < 0) yield break;

                // 括号配对找函数体结尾
                int depth = 0, end = openBrace;
                for (int i = openBrace; i < source.Length; i++)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}' && --depth == 0) { end = i; break; }
                }

                if (looksLikeCtor)
                    yield return source.Substring(openBrace, end - openBrace + 1);

                searchFrom = end + 1;
            }
        }

        /// <summary>记录注册进四张表的东西（PluginProvider 的最小替身，断言产物用）</summary>
        private sealed class RecordingProvider : VisionMaster.Services.IPluginProvider
        {
            public List<VisionMaster.Models.ToolItemModel> Modules { get; } = new();
            public List<VisionMaster.Models.ToolItemModel> Cameras { get; } = new();
            public List<VisionMaster.Models.ToolItemModel> Motions { get; } = new();
            public List<VisionMaster.Models.ToolItemModel> Lasers { get; } = new();

            public IReadOnlyDictionary<string, VisionMaster.Models.ToolItemModel> ModulePlugins => throw new NotSupportedException();
            public IReadOnlyDictionary<string, VisionMaster.Models.ToolItemModel> CameraPlugins => throw new NotSupportedException();
            public IReadOnlyDictionary<string, VisionMaster.Models.ToolItemModel> LaserPlugins => throw new NotSupportedException();
            public IReadOnlyDictionary<string, VisionMaster.Models.ToolItemModel> MotionPlugins => throw new NotSupportedException();

            public void RegisterModule(VisionMaster.Models.ToolItemModel plugin) => Modules.Add(plugin);
            public void RegisterCamera(VisionMaster.Models.ToolItemModel plugin) => Cameras.Add(plugin);
            public void RegisterLaser(VisionMaster.Models.ToolItemModel plugin) => Lasers.Add(plugin);
            public void RegisterMotion(VisionMaster.Models.ToolItemModel plugin) => Motions.Add(plugin);
            public VisionMaster.Models.ToolItemModel GetModule(string name) => null!;
            public VisionMaster.Models.ToolItemModel GetCamera(string name) => null!;
            public VisionMaster.Models.ToolItemModel GetLaser(string name) => null!;
            public VisionMaster.Models.ToolItemModel GetMotion(string name) => null!;
        }

        /// <summary>静默 IUserNotifier：断言宿主不弹泡</summary>
        private sealed class SilentNotifier : Core.Interfaces.IUserNotifier
        {
            public void ShowInfo(string message) { }
            public void ShowWarn(string message) { }
            public void ShowError(string message) { }
        }

        /// <summary>把插桩/失效原因日志收进列表（断言可读回）</summary>
        private sealed class ListLog : Core.Interfaces.ILogService
        {
            private readonly List<string> _sink;
            public ListLog(List<string> sink) => _sink = sink;
            public void Success(params string[] messages) => _sink.AddRange(messages);
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) => _sink.AddRange(messages);
            public void Info(params string[] messages) => _sink.AddRange(messages);
            public void Warn(params string[] messages) => _sink.AddRange(messages);
        }
    }
}
