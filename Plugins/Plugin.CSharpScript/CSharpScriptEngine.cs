using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// Roslyn C# 脚本宿主：负责把用户脚本正文（顶层语句）编译成可复用的委托并执行。
    ///
    /// 关键约定（探针实测确认）：
    /// - 脚本正文必须是【顶层语句】——CSharpScript 会忽略手写的 Main/自定义入口，只执行顶层。
    ///   因此用户可以在正文里定义 class / 方法 / LINQ / using，但最终"干活"的语句要放在顶层。
    /// - Context 通过 globalsType=<see cref="ScriptContext"/> 注入，脚本里以顶层名 <c>Context</c> 访问。
    /// - 同一脚本正文按 SHA256 指纹缓存已编译的 <see cref="Script{TResult}"/>，内容不变零成本复用。
    /// </summary>
    public static class CSharpScriptEngine
    {
        // 指纹 → 已编译脚本（可跨实例复用；脚本对象本身不可变，状态靠每次传入的 globals 承载）
        private static readonly Dictionary<string, Script<object>> CallCache = new();
        private static readonly object CacheLock = new();

        private static ScriptOptions _options;
        private static readonly object OptionsLock = new();

        /// <summary>
        /// 编译脚本正文（顶层语句）。
        /// 返回 null 表示存在编译错误，错误详情经 <paramref name="error"/> 带回（带行列号的中文提示）。
        /// </summary>
        public static Script<object> Compile(string body, out string error, bool force = false)
        {
            error = null;
            body ??= string.Empty;

            string fingerprint = ComputeFingerprint(body);
            if (!force)
            {
                lock (CacheLock)
                {
                    if (CallCache.TryGetValue(fingerprint, out var cached) && cached != null)
                        return cached;
                }
            }

            Script<object> script;
            try
            {
                // 注意：本工程命名空间末段为 CSharpScript，会遮蔽 Roslyn 的同名类型，
                // 故这里用 global:: 全限定到 Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript。
                script = global::Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.Create<object>(
                    body,
                    GetOptions(),
                    typeof(ScriptContext));
            }
            catch (Exception ex)
            {
                error = "脚本创建失败: " + ex.Message;
                return null;
            }

            // 显式编译并提取诊断（编译错误不抛在 Create，而在首次运行；这里提前拦住，给出行列定位）
            var diagnostics = script.Compile();
            var err = diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (err != null)
            {
                error = FormatDiagnostic(err);
                return null;
            }

            lock (CacheLock)
            {
                if (CallCache.Count > 64) CallCache.Clear();
                CallCache[fingerprint] = script;
            }
            return script;
        }

        /// <summary>
        /// 执行已编译脚本。返回值仅作可选输出，不决定成功与否；
        /// 成功/失败契约由"抛异常 = 失败"与 <see cref="ScriptContext.Fail"/> 决定，交由宿主判定。
        /// </summary>
        public static async Task<object> RunAsync(Script<object> script, ScriptContext globals, CancellationToken token)
        {
            var result = await script.RunAsync(globals, token).ConfigureAwait(false);
            // 脚本内的未处理异常会被包在 result.Exception（CompilationErrorException 在 Compile 阶段已拦）
            if (result.Exception != null)
                throw result.Exception;
            return result.ReturnValue;
        }

        // ───────── 选项 / 引用集 / Imports ─────────

        private static ScriptOptions GetOptions()
        {
            lock (OptionsLock)
            {
                if (_options != null) return _options;

                var refs = new List<MetadataReference>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hostSimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void AddAssembly(string path)
                {
                    if (string.IsNullOrEmpty(path)) return;
                    if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return;
                    if (!seen.Add(path)) return;
                    try
                    {
                        refs.Add(MetadataReference.CreateFromFile(path));
                        hostSimpleNames.Add(Path.GetFileNameWithoutExtension(path));
                    }
                    catch { /* 个别无法读取的程序集跳过，不阻断整体 */ }
                }

                // 1) 运行期可信平台程序集清单（BCL + 已加载的全部引用，最省事且最完整）
                string tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
                if (!string.IsNullOrEmpty(tpa))
                    foreach (var p in tpa.Split(Path.PathSeparator))
                        AddAssembly(p);

                // 2) 兜底并入当前已加载程序集的物理路径（覆盖 TPA 未列出的私有依赖）
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (!a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                            AddAssembly(a.Location);
                    }
                    catch { /* 反射取 Location 失败忽略 */ }
                }

                // 2.5) 宿主根目录整目录扫描。
                //
                // 为什么单靠 1) 和 2) 不够：deps.json 里登记为 "type": "reference" 的库
                // （halcondotnet 就是这一类）**不会**被塞进运行期 TPA，于是它能不能进引用集，
                // 完全取决于"编译脚本那一刻它有没有恰好被加载过"。
                // 表现就是同一份脚本、同一份插件：无界面宿主里先跑了采集（碰过 HImage）→ 编译通过；
                // 主程序里若脚本先编译 → 报「The type or namespace name 'HImage' could not be found」。
                // 这里把宿主根目录里的 dll 全部登记一遍，把"引用集依赖加载顺序"这个不确定性从根上掐掉。
                // 原生 dll（halcon.dll / hcanvas.dll 等）不是托管程序集，CreateFromFile 会抛异常，
                // 被 AddAssembly 内部的 catch 就地丢弃，不影响其余引用。
                try
                {
                    var hostDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (!string.IsNullOrEmpty(hostDir) && Directory.Exists(hostDir))
                        foreach (var dll in Directory.GetFiles(hostDir, "*.dll"))
                            AddAssembly(dll);
                }
                catch { /* 目录不可枚举不致命：引用集少几项，总好过整个脚本引擎起不来 */ }

                // 3) ExternalLibs 扩展目录：仅加载白名单登记且哈希校验通过的 dll（默认拒绝）
                LoadWhitelistedExtensions(refs, hostSimpleNames);

                var imports = new[]
                {
                    "System",
                    "System.Collections.Generic",
                    "System.Linq",
                    "System.Text",
                    "System.Math",
                    "HalconDotNet"
                };

                _options = ScriptOptions.Default
                    .WithReferences(refs)
                    .WithImports(imports);
                return _options;
            }
        }

        // ───────── ExternalLibs 扩展库白名单（fail-closed） ─────────

        /// <summary>扩展库审计消息的严重级别。</summary>
        public enum ExternalLibSeverity { Info, Warning, Error }

        // 审计记录：扫描时暂存，宿主首次执行经 TakeExternalLibsAudit 取走并投递到 ILogService
        private static readonly List<(ExternalLibSeverity Sev, string Msg)> ExternalAudit = new();
        private static readonly object AuditLock = new();

        // 白名单通过的程序集简单名 → 物理路径（运行期类型解析靠 Default.Resolving 兜底）
        private static readonly Dictionary<string, string> ExternalLibPaths = new(StringComparer.OrdinalIgnoreCase);
        private static bool _resolvingHooked;

        /// <summary>取走（并清空）扩展库审计记录。全局只会被投递一次。</summary>
        public static List<(ExternalLibSeverity Sev, string Msg)> TakeExternalLibsAudit()
        {
            lock (AuditLock)
            {
                var snapshot = ExternalAudit.ToList();
                ExternalAudit.Clear();
                return snapshot;
            }
        }

        private static void Audit(ExternalLibSeverity sev, string msg)
        {
            lock (AuditLock) ExternalAudit.Add((sev, msg));
        }

        /// <summary>
        /// 扫描宿主目录下 ExternalLibs\，只把 whitelist.json 登记且 SHA256 一致的 dll 加入脚本引用集。
        /// 纪律：默认拒绝——清单缺失/解析失败/未登记/哈希不符/开关关闭，一律不加载，只留审计日志。
        /// 目录与清单都不存在时视为"未启用该功能"，静默跳过。
        /// </summary>
        private static void LoadWhitelistedExtensions(List<MetadataReference> refs, HashSet<string> hostSimpleNames)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "ExternalLibs");
            var manifest = Path.Combine(dir, "whitelist.json");
            if (!Directory.Exists(dir)) return;
            if (!File.Exists(manifest))
            {
                Audit(ExternalLibSeverity.Error, "[扩展库白名单] ExternalLibs 目录存在但缺少 whitelist.json：拒绝加载任何扩展 dll（默认拒绝）。");
                return;
            }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(manifest, Encoding.UTF8)); }
            catch (Exception ex)
            {
                Audit(ExternalLibSeverity.Error, $"[扩展库白名单] whitelist.json 解析失败({ex.Message})：拒绝加载任何扩展 dll（默认拒绝）。");
                return;
            }

            // 总开关：必须显式 "enabled": true 才放行（缺省/写错一律视为关闭）
            if (!(root["enabled"] is JValue sw && sw.Value is bool on && on))
            {
                Audit(ExternalLibSeverity.Info, "[扩展库白名单] 开关 enabled 非 true：扩展库加载已被手动关闭。");
                return;
            }

            var entries = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            if (root["libraries"] is JArray arr)
                foreach (var item in arr.OfType<JObject>())
                {
                    var f = item["file"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(f))
                        entries[Path.GetFileName(f.Trim())] = item;
                }

            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                var name = Path.GetFileName(dll);
                try
                {
                    if (!entries.TryGetValue(name, out var entry))
                    {
                        // 未登记：忽略，但把真实哈希打出来——方便管理员确认后一键登记（而不是直接放行）
                        Audit(ExternalLibSeverity.Warning,
                            $"[扩展库白名单] 发现未登记 dll，已忽略：{name}（实际 SHA256={ComputeFileSha256(dll)}，如确需使用请登记进 whitelist.json）");
                        continue;
                    }

                    var simple = Path.GetFileNameWithoutExtension(dll);
                    if (hostSimpleNames.Contains(simple))
                    {
                        // 防影子替换：与宿主同名一律以宿主版本为准，扩展副本不加载（halcondotnet 版本冲突教训）
                        Audit(ExternalLibSeverity.Info, $"[扩展库白名单] {name} 与宿主已有程序集同名：以宿主版本为准，跳过扩展副本。");
                        continue;
                    }

                    var actual = ComputeFileSha256(dll);
                    var expected = entry["sha256"]?.Value<string>()?.Trim();
                    if (string.IsNullOrEmpty(expected) || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        Audit(ExternalLibSeverity.Error,
                            $"[扩展库白名单] 哈希不符，疑似被篡改，拒绝加载：{name}（清单={expected ?? "未填"} 实际={actual}）");
                        continue;
                    }

                    refs.Add(MetadataReference.CreateFromFile(dll));
                    ExternalLibPaths[simple] = dll;   // 运行期真实执行到该程序集时靠 Resolving 兜底加载
                    HookResolving();
                    Audit(ExternalLibSeverity.Info,
                        $"[扩展库白名单] 校验通过，已加入脚本引用：{name}（批准人：{entry["approvedBy"]?.Value<string>() ?? "?"}，哈希：{actual.Substring(0, 12)}…）");
                }
                catch (Exception ex)
                {
                    Audit(ExternalLibSeverity.Error, $"[扩展库白名单] 处理 {name} 异常：{ex.Message}");
                }
            }
        }

        /// <summary>挂一次全局 Resolving 兜底：编译引用了 ExternalLibs 里的 dll 后，运行期按简单名从原路径加载。</summary>
        private static void HookResolving()
        {
            if (_resolvingHooked) return;
            _resolvingHooked = true;
            AssemblyLoadContext.Default.Resolving += (ctx, an) =>
            {
                if (an != null && ExternalLibPaths.TryGetValue(an.Name, out var p) && File.Exists(p))
                    return ctx.LoadFromAssemblyPath(p);
                return null;
            };
        }

        private static string ComputeFileSha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        // ───────── 诊断中文定位 ─────────

        private static string FormatDiagnostic(Diagnostic diag)
        {
            var span = diag.Location.GetLineSpan();
            int line = span.StartLinePosition.Line + 1;      // 0-based → 1-based
            int col = span.StartLinePosition.Character + 1;
            string msg = diag.GetMessage();
            // 去掉 Roslyn 默认的英文资源标识前缀噪声（如有），保留人话
            return $"第 {line} 行 第 {col} 列: {msg}";
        }

        private static string ComputeFingerprint(string body)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(body ?? string.Empty)));
        }
    }
}
