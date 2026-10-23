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
    ///
    /// 【读者分层：用户页与开发页】（HM12~HM15）
    ///   同一插件的手册分两类读者：<b>用户页</b>（怎么用）与<b>开发页</b>（原理、宿主契约、移植清单，
    ///   给开发者与"接手软件全局合并"的人看）。两类都打进 DLL、宿主加载时<b>全量收集</b>，
    ///   只在帮助目录里按显隐开关裁显示——开关若裁加载，同一个 DLL 在不同配置下就交出两本不同的书，
    ///   合并方无法假设"手册是齐的"，正是内嵌手册这套约定要避免的事。
    ///   判据是<b>文件名</b>：<c>NN-dev-slug.md</c> 是开发页，其余是用户页。
    ///   为什么不用 <c>Help\dev\</c> 子目录：宿主 <c>NarrowToPlugin</c> 的规则是"本类型子目录有页就只收子目录"，
    ///   单插件程序集的手册原本是扁平的（<c>Help\01-x.md</c>），一旦多出 <c>Help\{类名}\dev\</c>，
    ///   <b>全部用户页会凭空消失</b>且不报错——所以这里把"开发页放子目录"也钉成红线。
    /// </summary>
    internal static class HelpManualCheck
    {
        /// <summary>每页/每册正文行数下限（低于它就是占位页，不如让宿主继续显示自动生成页）</summary>
        private const int MinLinesPerManual = 70;

        /// <summary>骨架档位：厚度与页数下限按档浮动，不搞"一刀切 8 页"（口径见方案说明书 §3）</summary>
        private enum ManualTier
        {
            /// <summary>薄条目：纯参数/一次性读写，无画布、无设备驱动</summary>
            S,
            /// <summary>标准条目：多端口检测/数据类</summary>
            M,
            /// <summary>重条目：自带画布交互、脚本引擎、设备驱动、大体量算法</summary>
            L,
        }

        /// <summary>未登记条目按这一档要求——漏登记只会要求更严，不会变成零要求</summary>
        private const ManualTier DefaultTier = ManualTier.M;

        private static int MinUserPages(ManualTier t) => t switch
        {
            ManualTier.L => 5,
            ManualTier.S => 3,
            _ => 4,
        };

        private static int MinDevPages(ManualTier t) => t switch
        {
            ManualTier.L => 3,
            ManualTier.S => 1,
            _ => 2,
        };

        private static int MinDevLines(ManualTier t) => t switch
        {
            ManualTier.L => 300,
            ManualTier.S => 100,
            _ => 150,
        };

        /// <summary>
        /// 逐插件类档位表（判据：源码体量 + 是否自带画布/视图 + 是否设备驱动或脚本引擎 + 断言密度）。
        /// <b>HM16 钉住"每个插件类都在表里"</b>：新增条目必须先登记档位，否则闸门红。
        /// 改档位数值要同步方案说明书 <c>docs\方案设计\插件帮助手册分层铺开方案说明书.md</c> §3。
        /// </summary>
        private static readonly Dictionary<string, ManualTier> Tiers = new(StringComparer.Ordinal)
        {
            // —— L 档（重交互 / 硬件 / 脚本 / 大体量）——
            ["CreateRoiPlugin"] = ManualTier.L,
            ["BeadInspectPlugin"] = ManualTier.L,
            ["BlobDetectPlugin"] = ManualTier.L,
            ["CalibrationPlugin"] = ManualTier.L,
            ["CaliperMeasurePlugin"] = ManualTier.L,
            ["ColorCheckPlugin"] = ManualTier.L,
            ["ImageAcquisitionPlugin"] = ManualTier.L,
            ["ImageScriptPlugin"] = ManualTier.L,
            ["MatchingPlugin"] = ManualTier.L,
            ["PoseTransformPlugin"] = ManualTier.L,
            ["PreProcessingPlugin"] = ManualTier.L,
            ["ZMotionCard"] = ManualTier.L,

            // —— M 档（标准检测 / 数据 / 单卡驱动）——
            ["CSharpScriptPlugin"] = ManualTier.M,
            ["CodeReaderPlugin"] = ManualTier.M,
            // DlAnomaly：5 用户页 / 3 开发页 / 最长开发页 210 行 —— 超 M（4/2/150），
            // 未到 L（5/3/300，最长开发页差 90 行），按"交了的必须合法、必须够厚"登记 M。
            // 后续把 12-dev-invariants.md 补到 300 行以上再升 L。
            ["DlAnomalyPlugin"] = ManualTier.M,
            // DlSegment：5 用户页 / 3 开发页 / 最长开发页 185 行 —— 超 M（4/2/150），
            // 未到 L（5/3/300），与 DlAnomaly 同档登记 M。
            ["DlSegmentPlugin"] = ManualTier.M,
            ["RegionColorPlugin"] = ManualTier.M,
            ["DataRecordPlugin"] = ManualTier.M,
            ["DlClassifyPlugin"] = ManualTier.M,
            ["ExcelExportPlugin"] = ManualTier.M,
            ["GeometryRelationPlugin"] = ManualTier.M,
            // ImageDiff：5 用户页 / 3 开发页 / 最长开发页 237 行 —— 超 M（4/2/150），
            // 未到 L（5/3/300），按"交了的必须合法、必须够厚"登记 M（HM13 按此下限判）。
            ["ImageDiffPlugin"] = ManualTier.M,
            ["HandEyePlugin"] = ManualTier.M,
            ["HikvisionCameraDevice"] = ManualTier.M,
            ["VirtualMotionCard"] = ManualTier.M,
            ["SimMotionCard"] = ManualTier.M,
            ["SimCameraDevice"] = ManualTier.S,
            ["OcrPlugin"] = ManualTier.M,
            ["ResultDisplayPlugin"] = ManualTier.M,
            ["ResultUploadPlugin"] = ManualTier.M,
            ["VariableAssignmentPlugin"] = ManualTier.M,
            ["YoloPlugin"] = ManualTier.M,

            // —— S 档（薄工具 / 运动步骤 / 通讯步骤）——
            ["ComparisonPlugin"] = ManualTier.S,
            ["DataConversionPlugin"] = ManualTier.S,
            ["DelayPlugin"] = ManualTier.S,
            ["LogicOperationPlugin"] = ManualTier.S,
            ["MathCalculationPlugin"] = ManualTier.S,
            ["GeometryFinderPlugin"] = ManualTier.S,
            ["ImageAssignPlugin"] = ManualTier.S,
            ["NetworkCameraDevice"] = ManualTier.S,
            ["RunFlowPlugin"] = ManualTier.S,
            ["CommReadNowPlugin"] = ManualTier.S,
            ["CommWriteNowPlugin"] = ManualTier.S,
            ["MotionEnablePlugin"] = ManualTier.S,
            ["MotionHomePlugin"] = ManualTier.S,
            ["MotionIoPlugin"] = ManualTier.S,
            ["MotionMovePlugin"] = ManualTier.S,
            ["MotionReadStatusPlugin"] = ManualTier.S,
            ["MotionStopPlugin"] = ManualTier.S,
            ["MotionWaitPlugin"] = ManualTier.S,
            ["DatabaseQueryPlugin"] = ManualTier.S,
            ["DatabaseExecutePlugin"] = ManualTier.S,
            ["DatabaseWriteRowPlugin"] = ManualTier.S,
        };

        private static ManualTier TierOf(string pluginClass)
            => Tiers.TryGetValue(pluginClass, out var t) ? t : DefaultTier;

        /// <summary>
        /// 开发页的文件名标记。<b>与 <c>HelpCatalogService.DeveloperPageMark</c> 同源</b>，
        /// 两处必须一起改（HM14 钉着宿主那一份还挂着这个名字）。
        /// </summary>
        private const string DevPageMark = "dev-";

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

            // ⑥ 文件名前缀决定页序（宿主按资源名 Ordinal 排序）。
            //   顺带钉死"文件名里除扩展名不许有第二个点"：宿主取页名用的是资源名的最后一个点
            //   （MSBuild 把目录分隔符换成点，分册目录段就这么粘在页名前面），带点的页名会被截断。
            var badOrder = entries
                .SelectMany(e => e.MdFiles)
                .Where(f => !Regex.IsMatch(Path.GetFileName(f), @"^\d\d-")
                            || Path.GetFileNameWithoutExtension(f).Contains('.'))
                .Select(f => RelativeToRoot(root, f))
                .ToList();
            Check("HM6 手册文件名是 `NN-xxx.md`（页序确定、且名字里不许有额外的点）", badOrder.Count == 0, Describe(badOrder));

            // ⑥b 同一分册内不许两张页撞同一个 `NN-` 前缀（铺开时"旧页没退场、新页又写一遍"撞上的）。
            //    页序 = 资源名 Ordinal 序，撞号后这两页谁在前是随机的，且界面看不出问题。
            var dupPrefix = new List<string>();
            foreach (var e in entries)
                foreach (var cls in e.PluginClasses)
                    foreach (var g in e.PagesFor(cls)
                                 .Where(f => Path.GetFileName(f).Length >= 3)
                                 .GroupBy(f => Path.GetFileName(f).Substring(0, 3))
                                 .Where(g => g.Count() > 1))
                        dupPrefix.Add($"{e.ProjectName}/{cls} 前缀「{g.Key}」有 {g.Count()} 张："
                            + string.Join("、", g.Select(f => Path.GetFileName(f))));
            Check("HM6b 同一分册内 `NN-` 前缀不重复（页序唯一）", dupPrefix.Count == 0, Describe(dupPrefix));

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

            // ⑫ 开发页判据是"文件名 NN-dev-"，写歪了宿主认不出 = 静默当用户页显示
            //    （只抓"文件名里有一个整段叫 dev"的错写法；03-devices 这类正常英文名不管）
            var badDevName = entries
                .SelectMany(e => e.MdFiles)
                .Where(f => !IsDevPage(f) && HasDevSegment(f))
                .Select(f => RelativeToRoot(root, f))
                .ToList();
            Check("HM12 开发页文件名是 `NN-dev-xxx.md`（宿主认得出这是开发章节）", badDevName.Count == 0,
                Describe(badDevName));

            // ⑫b 开发页不许另立子目录：宿主 NarrowToPlugin 是"本类型子目录有页就只收子目录"，
            //     扁平手册的程序集一旦多出 Help\{类名}\dev\，全部用户页会被静默挤掉（不报错）。
            var nested = new List<string>();
            foreach (var e in entries)
                foreach (var f in e.MdFiles)
                    if (SubFoldersUnderHelp(e, f) >= 2)
                        nested.Add(RelativeToRoot(root, f));
            Check("HM12b 手册最多一层目录（开发页另立子目录会挤掉用户页）", nested.Count == 0, Describe(nested));

            // ⑬ 已交的开发章节必须够厚——**下限按档位浮动**（L 300 / M 150 / S 100 行）。
            //    一刀切会把薄条目逼成注水文档（违反 R41"事实来源只有源码"），
            //    所以这里按插件类查档位表；欠账（0 开发页）仍不判红，见 HM15 台账。
            var thinDev = new List<string>();
            foreach (var e in entries)
                foreach (var cls in e.PluginClasses)
                {
                    var pages = e.DevPagesFor(cls);
                    if (pages.Count == 0) continue;
                    var floor = MinDevLines(TierOf(cls));
                    var lines = pages.Sum(f => File.ReadLines(f).Count());
                    if (lines < floor)
                        thinDev.Add($"{e.ProjectName}/{cls}（{TierOf(cls)} 档）开发章节 {lines} 行 < {floor}");
                }
            Check("HM13 已交的开发章节达到所在档位的行数下限（L300/M150/S100，交了就不是随笔）", thinDev.Count == 0,
                Describe(thinDev));

            // ⑭ 读者分层的链路四处都在：宿主认标记 → 节点带标记 → 视图按标记裁 → 配置存开关。
            //    断任何一处，症状都是"开关形同虚设"（开发页永远可见 / 永远看不到），编译期全绿。
            var links = new List<string>();
            void RequireLink(string relPath, params string[] needles)
            {
                var file = Path.Combine(root, relPath);
                var text = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
                foreach (var needle in needles)
                    if (!text.Contains(needle, StringComparison.Ordinal))
                        links.Add($"{relPath} 缺「{needle}」");
            }
            RequireLink(@"VisionMaster\Services\Help\HelpCatalogService.cs",
                $"DeveloperPageMark = \"{DevPageMark}\"", "IsDeveloperPage", "IsDevDoc");
            RequireLink(@"VisionMaster\Services\Help\HelpModels.cs", "IsDevDoc");
            RequireLink(@"VisionMaster\ViewModels\DialogViewModels\HelpViewModel.cs",
                "PruneDevDocs", "ShowDevDocs", "_visibleTree");
            RequireLink(@"VisionMaster\Views\DialogViews\HelpView.xaml", "ShowDevDocs");
            RequireLink(@"Core\Models\AppConfigModel.cs", "ShowDeveloperDocs");
            Check("HM14 开发章节显隐链路四处齐全（标记→节点→裁树→开关→配置）", links.Count == 0, Describe(links));

            // ⑮ 欠账台账（不判红，只把数字摊开）：工具箱 42 个条目，已交开发章节的只有 X 本。
            //    判红会把"分批铺开"变成"一次性全有或全线红"，所以这里只做可见性；
            //    真正的红线是 HM12/HM13——交了的必须合法、必须够厚。
            var devDone = new List<string>();
            foreach (var e in entries)
                foreach (var cls in e.PluginClasses)
                    if (e.DevPagesFor(cls).Count > 0)
                        devDone.Add($"{e.ProjectName}/{cls}");
            Check($"HM15 开发章节覆盖台账（欠账不判红，交了的看 HM12/HM13）", devDone.Count > 0,
                $"已交 {devDone.Count} / 插件类 {entries.Sum(e => e.PluginClasses.Count)} 个："
                + string.Join("、", devDone) + "；欠账清单不在此列（逐个补）");

            // ⑯ 档位表必须与插件类一一对应：表里不许留已消失的条目（档位过期），
            //    新增条目必须先登记档位。漏登记虽然仍按 M 要求（DefaultTier），但那是兜底，
            //    不是"允许不登记"——不钉这一条，档位表会悄悄和语料脱节，L 档新条目被按 M 放行。
            var allClasses = entries.SelectMany(e => e.PluginClasses).Distinct(StringComparer.Ordinal).ToList();
            var unregistered = allClasses.Where(c => !Tiers.ContainsKey(c))
                .OrderBy(c => c, StringComparer.Ordinal).ToList();
            var stale = Tiers.Keys.Where(c => !allClasses.Contains(c, StringComparer.Ordinal))
                .OrderBy(c => c, StringComparer.Ordinal).ToList();
            Check("HM16 档位表与插件类一一对应（无未登记、无过期条目）",
                unregistered.Count == 0 && stale.Count == 0,
                $"插件类 {allClasses.Count} 个 / 表内 {Tiers.Count} 项；"
                + $"未登记：{Describe(unregistered)}；过期：{Describe(stale)}");

            // ⑰ 骨架完整度闸门（按档位查"用户页数 / 开发页数 / 开发行数"三项）。
            //    铺开阶段它只是台账（那时判红等于全线红，角色分批交册会被自己的进度拦住），
            //    2026-10-16 42 条交齐后翻成红线：此后删一页、把开发章节写成随笔，当场拦住。
            //    判据全部来自 Tiers / Min*（档位按源码体量+断言密度定），"注水凑页"过不了 HM13，
            //    "整册缩水"过不了这一条——两条合起来才是"按档位铺开"这个口径的完整守门。
            var shortfall = new List<string>();
            int met = 0;
            foreach (var e in entries)
                foreach (var cls in e.PluginClasses)
                {
                    var tier = TierOf(cls);
                    var pages = e.PagesFor(cls);
                    var devPages = e.DevPagesFor(cls);
                    var userPages = pages.Count - devPages.Count;
                    var devLines = devPages.Sum(f => File.ReadLines(f).Count());
                    var miss = new List<string>();
                    if (userPages < MinUserPages(tier)) miss.Add($"用户页 {userPages}<{MinUserPages(tier)}");
                    if (devPages.Count < MinDevPages(tier)) miss.Add($"开发页 {devPages.Count}<{MinDevPages(tier)}");
                    if (devLines < MinDevLines(tier)) miss.Add($"开发行 {devLines}<{MinDevLines(tier)}");
                    if (miss.Count == 0) met++;
                    else shortfall.Add($"{e.ProjectName}/{cls}（{tier}）：" + string.Join("、", miss));
                }
            Check("HM17 骨架完整度闸门（按档位：用户页/开发页/开发行数三项齐）", shortfall.Count == 0,
                $"达标 {met} / 插件类 {allClasses.Count}；未达档 {shortfall.Count} 条：{Describe(shortfall)}");
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

            /// <summary>某插件类的<b>开发章节</b>页（判据 = 文件名 <c>NN-dev-</c>，与宿主同源）</summary>
            public List<string> DevPagesFor(string pluginClass)
                => PagesFor(pluginClass).Where(IsDevPage).ToList();
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

        /// <summary>
        /// 开发页判据：去掉 <c>NN-</c> 序号前缀后以 <c>dev-</c> 起头。
        /// 与宿主 <c>HelpCatalogService.IsDeveloperPage</c> 同一套规则，两处必须同步（HM14 盯宿主那一份）。
        /// </summary>
        private static bool IsDevPage(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length >= 3 && char.IsAsciiDigit(name[0]) && char.IsAsciiDigit(name[1]) && name[2] == '-')
                name = name.Substring(3);
            return name.StartsWith(DevPageMark, StringComparison.Ordinal);
        }

        /// <summary>文件名里有一个整段叫 <c>dev</c>（标记写歪了位置的开发页，如 <c>11-architecture-dev.md</c>）</summary>
        private static bool HasDevSegment(string file)
            => Path.GetFileNameWithoutExtension(file)
                .Split('-')
                .Any(s => string.Equals(s, "dev", StringComparison.OrdinalIgnoreCase));

        /// <summary>这页手册落在 Help 下第几层目录里（0 = 扁平，1 = 类型分册，≥2 = 又套了一层）</summary>
        private static int SubFoldersUnderHelp(ProjectScan e, string file)
        {
            var rel = Path.GetRelativePath(Path.Combine(e.Dir, "Help"), file);
            return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - 1;
        }

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
