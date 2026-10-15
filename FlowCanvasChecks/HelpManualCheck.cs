using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 插件帮助手册的契约断言（纯静态扫描，不加载程序集、不启动界面）。
    ///
    /// 【为什么要守这件事】手册是"放对文件就生效"的约定式功能，约定一旦被踩，**症状是静默的**：
    /// 宿主 <c>HelpCatalogService</c> 找不到内嵌资源就退到"自动生成"兜底页——界面照样能打开、
    /// 目录树照样有节点、用户看不出异常，只有他自己点开才发现"这页不是我们写的那份"。
    /// 所以每条规则都得在这里钉死，让"写了手册但用户看不到"在 gate 里就是红的。
    ///
    /// 【四条口径】
    ///   ① **打包**：<c>Help\**\*.md</c> 必须被 csproj 的 <c>EmbeddedResource</c> 收进去（漏了 = 源码里有、DLL 里没有）。
    ///   ② **分册**：一个程序集里有 N 个插件类时，手册必须放 <c>Help\{插件类名}\</c>；
    ///      放在 <c>Help\</c> 根上会被这 N 个插件**共用**（串页），目录名拼错则一个都收不到。
    ///   ③ **取标题**：页名 = 正文第一个 <c># </c> 一级标题，且必须排在第一个正文行之前——
    ///      写在第二段以后的 <c># </c> 宿主不认（它一遇到正文就停止扫描）。
    ///   ④ **不许占位**：手册是给人看的内容，出现 TODO / 待补充 / 行数过薄，等于把兜底页换成更空的页面。
    /// </summary>
    internal static class HelpManualCheck
    {
        /// <summary>每页/每册正文行数下限（低于它就是占位页，不如让宿主继续显示自动生成页）</summary>
        private const int MinLinesPerManual = 70;

        private static readonly string[] PlaceholderMarks =
            { "TODO", "待补充", "待完善", "（略）", "(略)", "TBD" };

        public static void Run()
        {
            Section("[HM] 插件帮助手册：打包 / 分册 / 标题契约（静态扫描）");

            var root = RepoRoot();
            if (root == null)
            {
                Check("HM0 定位到仓库根", false, "找不到 AGENTS.md，后续扫描全部跳过");
                return;
            }
            Check("HM0 定位到仓库根", true, root);

            var pluginsDir = Path.Combine(root, "Plugins");
            var projects = Directory.GetDirectories(pluginsDir, "Plugin.*")
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();

            var entries = new List<ProjectScan>();
            foreach (var dir in projects)
            {
                var name = Path.GetFileName(dir);
                if (name.EndsWith(".Abstractions", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                    continue;
                entries.Add(Scan(dir, name));
            }

            // ① 覆盖闸门：每个插件类都必须有自己的手册（缺一个就红，红字列出缺的是谁）
            var noManual = new List<string>();
            foreach (var e in entries)
                foreach (var cls in e.PluginClasses)
                    if (e.PagesFor(cls).Count == 0)
                        noManual.Add($"{e.ProjectName}/{cls}");
            Check("HM1 每个插件类都有自带手册（无兜底自动生成页）", noManual.Count == 0,
                $"程序集 {entries.Count} 个 / 插件类 {entries.Sum(e => e.PluginClasses.Count)} 个；缺手册：{Describe(noManual)}");

            // ② 多插件程序集不许有扁平手册（会被同程序集的每个插件共用 = 串页）
            var shared = entries
                .Where(e => e.PluginClasses.Count > 1 && e.FlatPages.Count > 0)
                .Select(e => $"{e.ProjectName}（{e.PluginClasses.Count} 个插件）的 {e.FlatPages.Count} 页放在 Help 根上："
                            + string.Join(", ", e.FlatPages.Select(f => Path.GetFileName(f))))
                .ToList();
            Check("HM2 多插件程序集不许放扁平手册（防串页）", shared.Count == 0, Describe(shared));

            // ③ 分册目录名必须是真实存在的插件类名（拼错 = 宿主静默收不到，比没写更难发现）
            var badFolders = new List<string>();
            foreach (var e in entries)
            {
                foreach (var folder in e.HelpSubFolders)
                {
                    if (string.Equals(folder, "images", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (e.PluginClasses.Any(c => string.Equals(c, folder, StringComparison.Ordinal)))
                        continue;
                    badFolders.Add($"{e.ProjectName}/Help/{folder}");
                }
            }
            Check("HM3 Help 子目录名逐字等于插件类名（分册能被认出）", badFolders.Count == 0, Describe(badFolders));

            // ④ 有手册就必须被打包进 DLL
            var notEmbedded = entries
                .Where(e => e.MdFiles.Count > 0 && !e.CsprojHasHelpEmbeddedResource)
                .Select(e => e.ProjectName)
                .ToList();
            Check("HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）", notEmbedded.Count == 0,
                Describe(notEmbedded));

            // ⑤ 每页首个正文行就是 `# `（宿主只在这里取页名）
            var badTitle = entries
                .SelectMany(e => e.MdFiles)
                .Where(f => !FirstContentLineIsH1(f))
                .Select(f => RelativeToRoot(root, f))
                .ToList();
            Check("HM5 每页第一行内容就是 `# ` 一级标题（页名取得到）", badTitle.Count == 0, Describe(badTitle));

            // ⑥ 文件名前缀决定页序（宿主按资源名 Ordinal 排序）
            var badOrder = entries
                .SelectMany(e => e.MdFiles)
                .Where(f => !Regex.IsMatch(Path.GetFileName(f), @"^\d\d-"))
                .Select(f => RelativeToRoot(root, f))
                .ToList();
            Check("HM6 手册文件名是 `NN-xxx.md`（页序确定）", badOrder.Count == 0, Describe(badOrder));

            // ⑦ 编码闸门（AGENTS.md R1~R5 那一族事故：中文被按 ANSI 读再写回 = 静默损坏）
            var badEncoding = new List<string>();
            foreach (var file in entries.SelectMany(e => e.MdFiles))
            {
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    badEncoding.Add(RelativeToRoot(root, file) + "（有 BOM）");
                    continue;
                }
                try
                {
                    var text = new UTF8Encoding(false, true).GetString(bytes);
                    if (!text.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                        badEncoding.Add(RelativeToRoot(root, file) + "（正文没有中文）");
                }
                catch
                {
                    badEncoding.Add(RelativeToRoot(root, file) + "（不是合法 UTF-8）");
                }
            }
            Check("HM7 手册都是无 BOM 的合法 UTF-8 且含中文（编码纪律）", badEncoding.Count == 0, Describe(badEncoding));

            // ⑧ 内容体量：每个插件的手册正文总行数够厚（占位页不如没有）
            var thin = new List<string>();
            foreach (var e in entries)
                foreach (var cls in e.PluginClasses)
                {
                    var pages = e.PagesFor(cls);
                    var lines = pages.Sum(f => File.ReadLines(f).Count());
                    if (lines < MinLinesPerManual)
                        thin.Add($"{e.ProjectName}/{cls}={lines} 行");
                }
            Check($"HM8 每个插件的手册正文 ≥ {MinLinesPerManual} 行（不是占位页）", thin.Count == 0, Describe(thin));

            // ⑨ 不许出现占位标记
            var placeholder = new List<string>();
            foreach (var file in entries.SelectMany(e => e.MdFiles))
            {
                var text = File.ReadAllText(file);
                var hit = PlaceholderMarks.FirstOrDefault(m => text.Contains(m, StringComparison.Ordinal));
                if (hit != null)
                    placeholder.Add($"{RelativeToRoot(root, file)} 含「{hit}」");
            }
            Check("HM9 手册里没有 TODO / 待补充之类的占位标记", placeholder.Count == 0, Describe(placeholder));

            // ⑩ 宿主侧收窄规则还挂着（有人改回"整程序集共用一页"就红）
            var catalog = Path.Combine(root, @"VisionMaster\Services\Help\HelpCatalogService.cs");
            var catalogText = File.Exists(catalog) ? File.ReadAllText(catalog) : string.Empty;
            Check("HM10 宿主按插件类型收窄内嵌手册（分册机制在位）",
                catalogText.Contains("NarrowToPlugin", StringComparison.Ordinal)
                && catalogText.Contains("CollectEmbeddedPages(type.Assembly, source, type)", StringComparison.Ordinal),
                catalogText.Length == 0 ? "找不到 HelpCatalogService.cs" : "NarrowToPlugin + owner 传参都在");

            // ⑪ 宿主人力可见性：目录里不该再有"自动生成"角标的插件（HM1 的正向对照，
            //    但这条盯的是"运行时真合并出来多少本手册"，只在能读到摘要口径时才有意义）
            var hostHelp = Path.Combine(root, @"VisionMaster\Help");
            var hostPages = Directory.Exists(hostHelp)
                ? Directory.GetFiles(hostHelp, "*.md").Length
                : 0;
            Check("HM11 宿主自带手册在册（软件手册分组非空）", hostPages >= 4,
                $"VisionMaster\\Help 共 {hostPages} 页");
        }

        // ==================================================================
        //  扫描
        // ==================================================================

        private sealed class ProjectScan
        {
            public string ProjectName = string.Empty;
            public string Dir = string.Empty;
            public List<string> PluginClasses = new();
            public List<string> MdFiles = new();
            public List<string> FlatPages = new();
            public List<string> HelpSubFolders = new();
            public bool CsprojHasHelpEmbeddedResource;

            /// <summary>某插件类实际拿到的页：自己的分册优先，没有分册就用扁平页（与宿主规则一致）</summary>
            public List<string> PagesFor(string pluginClass)
            {
                var own = MdFiles
                    .Where(f => IsUnder(f, Path.Combine(Dir, "Help", pluginClass)))
                    .ToList();
                return own.Count > 0 ? own : FlatPages;
            }
        }

        private static ProjectScan Scan(string dir, string projectName)
        {
            var scan = new ProjectScan { ProjectName = projectName, Dir = dir };

            // 插件类：类上的 [Display(...)] 里带 GroupName（工具箱分组）。枚举成员的 [Display] 只有 Name，
            // 配置视图里偶尔也写 Display，所以"带 GroupName 的 class"是工具箱条目最稳的判据。
            var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !InBinOrObj(f))
                .ToList();
            foreach (var file in sources)
            {
                var text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text, "GroupName\\s*=\\s*\""))
                {
                    var tail = text.Substring(m.Index, Math.Min(text.Length - m.Index, 4000));
                    var cls = Regex.Match(tail, @"\bclass\s+(?<n>\w+)");
                    if (cls.Success && !scan.PluginClasses.Contains(cls.Groups["n"].Value))
                        scan.PluginClasses.Add(cls.Groups["n"].Value);
                }
            }

            var helpDir = Path.Combine(dir, "Help");
            if (Directory.Exists(helpDir))
            {
                scan.MdFiles = Directory.GetFiles(helpDir, "*.md", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();
                scan.FlatPages = scan.MdFiles
                    .Where(f => string.Equals(Path.GetDirectoryName(f), helpDir, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                scan.HelpSubFolders = Directory.GetDirectories(helpDir)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList();
            }

            var csproj = Directory.GetFiles(dir, "*.csproj").FirstOrDefault();
            if (csproj != null)
            {
                var proj = File.ReadAllText(csproj);
                scan.CsprojHasHelpEmbeddedResource = Regex.IsMatch(
                    proj, @"<EmbeddedResource[^>]*Include=""Help(\\\*\*\\\*|/\*\*/\*|\\\*\*\.md|\\[^""]*\*?)""",
                    RegexOptions.IgnoreCase);
            }

            return scan;
        }

        private static bool InBinOrObj(string path)
            => path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
               || path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase);

        private static bool IsUnder(string file, string folder)
            => Path.GetDirectoryName(file)?.StartsWith(folder + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) == true
               || string.Equals(Path.GetDirectoryName(file), folder, StringComparison.OrdinalIgnoreCase);

        /// <summary>页名契约：允许开头空行与 HTML 注释，但第一个正文行必须就是 <c># </c>。</summary>
        private static bool FirstContentLineIsH1(string file)
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("<!--", StringComparison.Ordinal)) continue;
                return line.StartsWith("# ", StringComparison.Ordinal);
            }
            return false;
        }

        private static string RelativeToRoot(string root, string file)
            => file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(root.Length).TrimStart('\\', '/')
                : file;

        private static string Describe(List<string> items)
        {
            if (items.Count == 0) return "无";
            var head = string.Join("、", items.Take(8));
            return items.Count > 8 ? $"{head}…（共 {items.Count} 项）" : head;
        }

        /// <summary>仓库根（以 AGENTS.md 为锚；范式 VariableBindingCheck.RepoRoot）</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
