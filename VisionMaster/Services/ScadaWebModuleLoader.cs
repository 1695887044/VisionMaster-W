using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using VisionMaster.Lifetime;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 可选模块「SCADA 运行态 Web 客户端」的加载器。
    ///
    /// <b>为什么要在 RegisterTypes 阶段读配置文件</b>
    /// ---------
    /// 模块必须在 <c>App.RegisterTypes</c> 里就决定"加不加入 <c>_modules</c>"——那时容器还没构建，
    /// 拿不到 <see cref="AppSettingsService"/> 实例，所以这里<b>直接读 AppConfig.json</b>。
    /// 用 Newtonsoft 是刻意的：与 <see cref="AppSettingsService"/> 同一份读取口径
    /// （允许注释与尾逗号），否则"宿主能读、加载器读不了"会让同一份文件出现两种解释。
    ///
    /// <b>三种返回形态（调用方据此决定日志级别）</b>
    /// ---------
    ///   1. 未启用（<c>ScadaWeb.Enabled = false</c>）→ 返回 null、<c>diagnostic = null</c>。
    ///      <b>这是正常状态</b>，不许写 Warn：绝大多数现场根本不装这个模块，
    ///      每次启动都刷一条警告只会让人学会忽略警告。
    ///   2. 启用但缺件 / 程序集损坏 / Web\ 里有契约副本 → 返回 null + 诊断（调用方写 Warn）。
    ///   3. 成功 → 返回模块实例、<c>diagnostic = null</c>。
    ///
    /// <b>为什么不把 Web 程序集放进 Modules\</b>
    /// ---------
    /// <c>Modules\</c> 是算子插件目录（按 <c>Plugin*.dll</c> 扫描并反射全部类型），
    /// 把一个应用级模块混进去会参与算子扫描、也会被"插件清单缓存"当成插件登记。
    /// 单独一个 <c>Web\</c> 目录：拷不拷、装不装是二选一的事，删掉目录就等于卸载。
    ///
    /// <b>为什么检查 Web\ 里出现契约副本就拒绝加载</b>
    /// ---------
    /// 与红线"不要把 Core.Interfaces.dll 等公共契约程序集拷进 Modules\"同源：
    /// 两份 <c>VM.Core.dll</c> / <c>VM.Scada.dll</c> 会形成两套静态状态与两套类型身份，
    /// 表现是"接口明明实现了却转不过去"或"图投进去取不出来"——编译期全绿、运行期才炸。
    /// 判据不止公共契约：宿主自身（<c>VisionMaster.dll</c>）、运行时依赖（<c>halcondotnet.dll</c> /
    /// <c>Newtonsoft.Json.dll</c>）与框架（<c>Prism*.dll</c>）同理——只要 Web\ 里多一份，
    /// 类型身份就分家。这个检查把错误提前成一条可读的 Warn，而不是让人去猜为什么模块没起作用。
    /// </summary>
    public static class ScadaWebModuleLoader
    {
        /// <summary>Web 模块所在子目录（相对程序目录）</summary>
        public const string WebDirectoryName = "Web";

        /// <summary>Web 模块程序集文件名</summary>
        public const string AssemblyFileName = "VisionMaster.ScadaWeb.dll";

        /// <summary>
        /// 一旦出现在 <c>Web\</c> 里就说明"契约被复制了一份"的程序集。
        /// 它们必须只有宿主目录那一份（Web 工程只输出自己的 DLL，见 csproj 的 post-build 只拷 TargetPath）。
        /// 清单覆盖三层：宿主自身（VisionMaster）、公共契约（VM.* / Core.*）、
        /// 以及运行时依赖（Newtonsoft.Json / halcondotnet）——最后这层同样是"类型身份"问题：
        /// 两份 HALCON 包装会让 HImage 在 Web 侧与宿主侧互不认。
        /// </summary>
        private static readonly string[] ContractAssemblyNames =
        {
            "VM.Core.dll",
            "VM.Scada.dll",
            "Core.Interfaces.dll",
            "Core.Controls.dll",
            "VM.Scada.Controls.dll",
            "Newtonsoft.Json.dll",
            "VisionMaster.dll",
            "halcondotnet.dll"
        };

        /// <summary>
        /// 第三方框架副本的前缀：<c>Prism*.dll</c> 一律视为"宿主程序集副本"。
        /// 用前缀而不是逐个列名字，是因为 Prism 拆成多个包（Prism / Prism.Wpf / Prism.Unity /
        /// Prism.Container.*…），漏列一个就是一个静默的双类型身份现场。
        /// </summary>
        private const string ContractAssemblyPrefix = "Prism";

        /// <summary>
        /// 与 <see cref="AppSettingsService"/> 同口径的读取设置：宽松解析（注释/尾逗号照常接受）、
        /// 缺失成员不报错（老配置里没有 ScadaWeb 节时按"未启用"处理）。
        /// </summary>
        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        /// <summary>
        /// 尝试加载 Web 模块。
        /// </summary>
        /// <param name="baseDirectory">程序目录（<c>AppContext.BaseDirectory</c>）</param>
        /// <param name="diagnostic">
        /// 失败原因。返回 null 且 <c>diagnostic == null</c> 表示"未启用"这个正常状态；
        /// 非 null 时应由调用方写一条 Warn（A03：Release 下也要在日志里看得见）。
        /// </param>
        /// <returns>模块实例；未启用或任何一步失败时为 null（不抛异常——可选模块缺件不该拦启动）</returns>
        public static IAppModule? TryLoad(string baseDirectory, out string? diagnostic)
        {
            diagnostic = null;

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                diagnostic = "[ScadaWeb] 未加载：程序目录为空，无法定位 AppConfig.json / Web 模块";
                return null;
            }

            // ---------- 1. 读配置：只取 ScadaWeb 一节 ----------
            // 刻意**不**反序列化整个 AppConfigModel：那一份里还有方案清单、运行窗口枚举等，
            // 任何一节的损坏都会让整份解析失败——把"无关节坏了"变成"Web 模块不加载"是错误归因。
            // 这里只需要回答一个问题：ScadaWeb.Enabled 是不是 true。
            var configPath = Path.Combine(baseDirectory, "AppConfig.json");
            bool enabled;
            try
            {
                if (!File.Exists(configPath))
                {
                    diagnostic = $"[ScadaWeb] 未加载：找不到配置文件 {configPath}（未启用即正常，出现本行说明程序目录不对）";
                    return null;
                }

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                var probe = JsonConvert.DeserializeObject<ScadaWebConfigProbe>(json, JsonOptions);
                enabled = probe?.ScadaWeb?.Enabled == true;
            }
            catch (Exception ex)
            {
                // 解析失败不抛：启动链上"可选模块读不出配置"不该拦开机
                diagnostic = $"[ScadaWeb] 未加载：读取 {configPath} 失败（{ex.GetType().Name}：{ex.Message}）";
                return null;
            }

            if (!enabled)
                return null;   // 正常状态：未启用，不写任何诊断

            // ---------- 2. 定位程序集 ----------
            var webDirectory = Path.Combine(baseDirectory, WebDirectoryName);
            var assemblyPath = Path.Combine(webDirectory, AssemblyFileName);
            if (!File.Exists(assemblyPath))
            {
                diagnostic = $"[ScadaWeb] 已启用但找不到模块程序集：{assemblyPath}"
                    + "（Web 功能不会启动；打包时请确认 Web\\ 目录随交付目录一起拷贝）";
                return null;
            }

            // ---------- 3. 拒绝契约副本 ----------
            // 用目录枚举而不是逐个 File.Exists：
            //   · Windows 文件系统不区分大小写，枚举能把 "prism.wpf.DLL" 这类大小写变体也收进判定；
            //   · Prism* 是前缀规则，没有确定的文件名可以 File.Exists。
            List<string> duplicates;
            try
            {
                duplicates = Directory.GetFiles(webDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name) && IsContractAssemblyCopy(name!))
                    .Select(name => name!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                // 目录枚举失败（权限/路径异常）同样只降级成"Web 不可用"，不拦启动
                diagnostic = $"[ScadaWeb] 未加载：无法枚举 {webDirectory}（{ex.GetType().Name}：{ex.Message}）";
                return null;
            }

            if (duplicates.Count > 0)
            {
                diagnostic = $"[ScadaWeb] 未加载：Web\\ 目录里出现两份契约/宿主程序集副本（{string.Join("、", duplicates)}）。"
                    + "两份程序集 = 两套类型身份与两套静态状态，表现是接口转不过去、状态互相看不见；"
                    + "请删掉这些副本（Web 工程只应输出 VisionMaster.ScadaWeb.dll 本身）";
                return null;
            }

            // ---------- 4. 加载并找 IAppModule 实现 ----------
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var moduleType = assembly.GetTypes()
                    .FirstOrDefault(t => t.IsClass
                                         && !t.IsAbstract
                                         && typeof(IAppModule).IsAssignableFrom(t));

                if (moduleType == null)
                {
                    diagnostic = $"[ScadaWeb] 未加载：{assemblyPath} 里没有 IAppModule 实现"
                        + "（程序集版本不匹配或打包到了错误的 DLL）";
                    return null;
                }

                if (Activator.CreateInstance(moduleType) is not IAppModule module)
                {
                    diagnostic = $"[ScadaWeb] 未加载：{moduleType.FullName} 无法构造成 IAppModule 实例";
                    return null;
                }

                return module;
            }
            catch (BadImageFormatException ex)
            {
                diagnostic = $"[ScadaWeb] 未加载：{assemblyPath} 不是有效的 .NET 程序集（BadImageFormatException：{ex.Message}）";
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 只报前几条 LoaderExceptions：完整清单可能上百行，日志里没人看
                var details = ex.LoaderExceptions
                    .Where(e => e != null)
                    .Select(e => e!.Message)
                    .Distinct()
                    .Take(3)
                    .ToList();
                diagnostic = $"[ScadaWeb] 未加载：{assemblyPath} 类型加载失败（ReflectionTypeLoadException）："
                    + (details.Count > 0 ? string.Join("；", details) : ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                diagnostic = $"[ScadaWeb] 未加载：{assemblyPath} 的依赖找不到（FileNotFoundException：{ex.Message}）"
                    + "（Web\\ 只应放模块自身，宿主程序集必须从程序目录解析）";
            }
            catch (TypeLoadException ex)
            {
                diagnostic = $"[ScadaWeb] 未加载：{assemblyPath} 类型加载失败（TypeLoadException：{ex.Message}）";
            }
            catch (MissingMethodException ex)
            {
                diagnostic = $"[ScadaWeb] 未加载：模块缺少公共无参构造（MissingMethodException：{ex.Message}）";
            }
            catch (Exception ex)
            {
                // 兜底：可选模块的任何加载失败都只降级成"Web 不可用"，不拦启动
                diagnostic = $"[ScadaWeb] 未加载：加载 {assemblyPath} 失败（{ex.GetType().Name}：{ex.Message}）";
            }

            return null;
        }

        /// <summary>
        /// 判断 <c>Web\</c> 里的某个文件名是否属于"宿主/契约程序集副本"。
        /// 两条判据：固定清单（大小写不敏感）+ <c>Prism*.dll</c> 前缀。
        /// </summary>
        private static bool IsContractAssemblyCopy(string fileName)
        {
            if (fileName.StartsWith(ContractAssemblyPrefix, StringComparison.OrdinalIgnoreCase))
                return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

            return ContractAssemblyNames.Any(
                name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// AppConfig.json 的最小读取面：只要 <c>ScadaWeb</c> 一节。
        /// 刻意不复用 <see cref="AppConfigModel"/>（理由见 <see cref="TryLoad"/> 第 1 步的注释）。
        /// </summary>
        private sealed class ScadaWebConfigProbe
        {
            public ScadaWebSettings? ScadaWeb { get; set; }
        }
    }
}
