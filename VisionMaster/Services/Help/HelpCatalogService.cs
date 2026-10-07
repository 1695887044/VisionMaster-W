using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Media.Imaging;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services.Help
{
    /// <summary>
    /// 帮助目录合并服务：把"宿主自带手册 + 各插件自带手册"合并成一棵目录树。
    ///
    /// 为什么要合并（而不是各插件各弹各的帮助）
    /// ---------
    /// 使用者的心智是"我要查某个算子的用法"，不是"这个算子属于哪个 DLL"。合并成一本
    /// 目录后：分组沿用插件自己的 <c>[Display].GroupName</c>（与工具箱分类逐字一致），
    /// 用户不必学第二套分类；搜索、互链、"上一条/下一条"也才有统一的地基。
    ///
    /// 插件手册的三条来路（按优先级）
    /// ---------
    /// ① <b>约定路径</b>：插件程序集里 <c>Help\</c> 前缀的内嵌资源（每个 <c>.md</c> 一页，
    ///    文件名顺序即页序）——插件只要把 md 丢进工程并 <c>&lt;EmbeddedResource Include="Help\**" /&gt;</c>，
    ///    连接口都不用实现。图片同样走内嵌资源，按 <c>{程序集}.Help.images.xxx.png</c> 解析；
    /// ② <b>可选接口</b> <see cref="IPluginHelpProvider"/>（运行期生成正文 / 自定义标题顺序）；
    /// ③ <b>兜底自动页</b>：都没有的插件用 <c>[Display]</c> 元数据 + 注册期端口表生成一页，
    ///    保证"合并出来的目录里没有空节点"——不然 27 个插件里 25 个是空白，等于没有帮助。
    ///
    /// 时机与开销
    /// ---------
    /// <see cref="Build"/> 由启动自检链调用（与插件扫描同一阶段，程序集早已加载）。
    /// 这一步只登记"每页标题 + 取数口"：正文用 <see cref="HelpPage.LoadMarkdown"/> 现取，
    /// 所以启动只多花若干次"读首行拿标题"的小 IO，打开帮助窗口是秒开。
    /// </summary>
    public sealed class HelpCatalogService
    {
        /// <summary>宿主自带手册在目录里的分组名</summary>
        public const string HostGroupTitle = "软件手册";

        private readonly IPluginProvider _provider;
        private readonly ILogService _log;
        private readonly object _gate = new();
        private readonly List<HelpNode> _groups = new();

        /// <summary>中文按当前区域排序：插件名/分组名都是中文，序数排序出来的顺序没人看得懂</summary>
        private static readonly StringComparer NameComparer =
            StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), ignoreCase: false);

        /// <summary>
        /// 分组显示顺序：与工具箱的直觉顺序对齐（先常用、再处理链、最后设备与通信）。
        /// 表外的分组按中文排序排在后面——新增分组不需要改这里，只是排得靠后。
        /// </summary>
        private static readonly string[] CategoryOrder =
        {
            "常用工具", "图像处理", "缺陷检测", "测量", "定位", "标定",
            "数据处理", "逻辑判断", "逻辑控制", "变量操作", "流程控制",
            "运动控制", "相机", "相机驱动", "运动卡", "激光",
        };

        public HelpCatalogService(IPluginProvider provider, ILogService log)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>合并后的目录（分组级；<see cref="Build"/> 之前为空集合）</summary>
        public IReadOnlyList<HelpNode> Groups
        {
            get { lock (_gate) return _groups.ToList(); }
        }

        /// <summary>
        /// 是否已合并。
        /// 这几个计数/摘要字段在 <see cref="Build"/> 里写、界面侧无锁读——安全的前提是**时序**：
        /// Build 跑在启动自检链里（CreateShell → RunStartupChecksAsync 返回之前），
        /// 而帮助窗口只能由主界面打开，那时自检早已结束、不再有并发写。
        /// 若将来把 Build 挪到"首次打开时懒构建"，这几个读口必须一并加锁。
        /// </summary>
        public bool IsBuilt { get; private set; }

        /// <summary>合并结果一句话摘要（启动自检与状态栏用）</summary>
        public string Summary { get; private set; } = "尚未合并";

        public int HostPageCount { get; private set; }
        public int PluginTotal { get; private set; }
        public int PluginManualCount { get; private set; }

        /// <summary>内置流程控制节点数（If/While/For…）。它们不是插件，目录里照样列，但不算进插件计数</summary>
        public int BuiltInCount { get; private set; }

        /// <summary>合并目录。可重复调用（幂等：先清空再重建）。失败不抛——帮助不可用不该挡启动。</summary>
        public void Build()
        {
            lock (_gate)
            {
                _groups.Clear();
                HostPageCount = 0;
                PluginTotal = 0;
                PluginManualCount = 0;
                BuiltInCount = 0;

                var hostAssembly = typeof(HelpCatalogService).Assembly;
                var hostPages = CollectEmbeddedPages(hostAssembly, "软件内置");
                HostPageCount = hostPages.Count;
                if (hostPages.Count > 0)
                {
                    _groups.Add(new HelpNode
                    {
                        Title = HostGroupTitle,
                        Icon = "\uf02d",              // FA5: book
                        IsExpanded = true,
                        Children = hostPages,
                    });
                }

                // 四张插件表合并成一棵树：算子插件 + 相机/激光/运动卡驱动。
                // 驱动插件（不实现 IVisionPlugin）同样有配置界面，用户查它们的手册时
                // 不该因为"它不是流程步骤"而在帮助里找不到。
                var byCategory = new Dictionary<string, List<HelpNode>>(StringComparer.Ordinal);
                void Absorb(IReadOnlyDictionary<string, ToolItemModel> table)
                {
                    foreach (var model in table.Values)
                        AbsorbOne(model, byCategory);
                }

                Absorb(_provider.ModulePlugins);
                Absorb(_provider.CameraPlugins);
                Absorb(_provider.LaserPlugins);
                Absorb(_provider.MotionPlugins);

                foreach (var category in byCategory.Keys.OrderBy(CategoryRank).ThenBy(c => c, NameComparer))
                {
                    var topics = byCategory[category];
                    topics.Sort((a, b) => NameComparer.Compare(a.Title, b.Title));
                    _groups.Add(new HelpNode
                    {
                        Title = category,
                        Icon = "\uf07b",           // FA5: folder
                        Children = topics,
                    });
                }

                IsBuilt = true;
                Summary = $"宿主 {HostPageCount} 页；插件 {PluginTotal} 个（其中 {PluginManualCount} 个自带手册）"
                          + (BuiltInCount > 0 ? $"；内置节点 {BuiltInCount} 个" : string.Empty);
            }
        }

        private static int CategoryRank(string category)
        {
            var index = Array.IndexOf(CategoryOrder, category);
            return index >= 0 ? index : CategoryOrder.Length;
        }

        private void AbsorbOne(ToolItemModel model, Dictionary<string, List<HelpNode>> byCategory)
        {
            try
            {
                // 内置流程控制节点（If/While/For/Break/Continue/Return）也在这张表里，
                // 但它们是宿主自带的、不是插件：目录照样列（用户确实会在工具箱里看到它们），
                // 计数与"是否自带手册"的说法必须分开，否则摘要会写着"插件 44 个"——
                // 与实际插件数对不上，维护者一看就以为统计有 bug。
                var isBuiltIn = (model.ModuleTypeName ?? string.Empty)
                    .StartsWith("BuiltIn_", StringComparison.Ordinal);
                if (isBuiltIn)
                    BuiltInCount++;
                else
                    PluginTotal++;

                var type = ResolveType(model.ModuleTypeName);
                var pages = CollectPluginPages(type, model, isBuiltIn, out var fromManual);
                if (fromManual && !isBuiltIn)
                    PluginManualCount++;

                var category = string.IsNullOrWhiteSpace(model.Category) ? "未分类" : model.Category!;
                if (!byCategory.TryGetValue(category, out var list))
                {
                    list = new List<HelpNode>();
                    byCategory[category] = list;
                }

                list.Add(new HelpNode
                {
                    Title = string.IsNullOrWhiteSpace(model.Name) ? model.ModuleTypeName : model.Name!,
                    Icon = string.IsNullOrWhiteSpace(model.Icon) ? "\uf1b2" : model.Icon,   // FA5: cube
                    Badge = fromManual ? null : "自动生成",
                    Children = pages,
                });
            }
            catch (Exception ex)
            {
                // 单个插件取手册失败不能影响其它插件，也不能影响启动
                _log.Warn($"[帮助] 插件「{model.Name}」手册收集失败：{ex.Message}");
            }
        }

        /// <summary>按注册期落下的程序集限定名找回插件类型（程序集此刻必然已加载）。</summary>
        private static Type? ResolveType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;
            try
            {
                return Type.GetType(typeName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 收集一个插件的页：内嵌资源 → 可选接口 → 兜底自动页。
        /// <paramref name="fromManual"/> 区分"手册"与"自动生成页"（角标与摘要用）。
        /// </summary>
        private List<HelpNode> CollectPluginPages(
            Type? type, ToolItemModel model, bool isBuiltIn, out bool fromManual)
        {
            var source = string.IsNullOrWhiteSpace(model.Name) ? "插件" : model.Name!;

            if (type != null)
            {
                var embedded = CollectEmbeddedPages(type.Assembly, source);
                if (embedded.Count > 0)
                {
                    fromManual = true;
                    return embedded;
                }
            }

            if (type != null && typeof(IPluginHelpProvider).IsAssignableFrom(type))
            {
                try
                {
                    if (Activator.CreateInstance(type) is IPluginHelpProvider provider)
                    {
                        var sections = provider.GetHelpSections();
                        if (sections is { Count: > 0 })
                        {
                            fromManual = true;
                            return sections
                                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Markdown))
                                .Select(s => new HelpNode
                                {
                                    Title = string.IsNullOrWhiteSpace(s.Title) ? source : s.Title,
                                    Page = new HelpPage
                                    {
                                        Title = string.IsNullOrWhiteSpace(s.Title) ? source : s.Title,
                                        Source = source,
                                        LoadMarkdown = () => s.Markdown,
                                    },
                                })
                                .ToList();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"[帮助] 插件「{source}」IPluginHelpProvider 取数失败，改用自动生成页：{ex.Message}");
                }
            }

            fromManual = false;
            return new List<HelpNode>
            {
                new HelpNode
                {
                    Title = source,
                    Page = new HelpPage
                    {
                        Title = source,
                        Source = source,
                        LoadMarkdown = () => BuildAutoPageMarkdown(model, type, isBuiltIn),
                    },
                },
            };
        }

        /// <summary>
        /// 约定路径：读程序集里 <c>&lt;程序集名&gt;.Help.</c> 前缀的内嵌资源，一个 .md 一页。
        /// 文件名排序即页序（<c>01-</c>、<c>02-</c> 前缀），标题取正文第一个 <c># </c> 一级标题。
        /// </summary>
        private List<HelpNode> CollectEmbeddedPages(Assembly assembly, string source)
        {
            var pages = new List<HelpNode>();
            try
            {
                var prefix = assembly.GetName().Name + ".Help.";
                var names = assembly.GetManifestResourceNames()
                    .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                && n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                if (names.Count == 0)
                    return pages;

                // 图片解析表：{程序集}.Help.images.xxx.png ← 正文里的 images/xxx.png
                var imageResources = assembly.GetManifestResourceNames()
                    .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                && !n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(n => n.Substring(prefix.Length), n => n, StringComparer.OrdinalIgnoreCase);

                foreach (var name in names)
                {
                    var resourceName = name;
                    var shortName = Path.GetFileNameWithoutExtension(name.Substring(prefix.Length));
                    var text = ReadResourceText(assembly, resourceName);
                    var title = ExtractTitle(text, shortName);

                    pages.Add(new HelpNode
                    {
                        Title = title,
                        Page = new HelpPage
                        {
                            Title = title,
                            Source = source,
                            LoadMarkdown = () => ReadResourceText(assembly, resourceName),
                            LoadImage = relative => LoadEmbeddedImage(assembly, imageResources, relative),
                        },
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"[帮助] 读取「{source}」内嵌手册失败：{ex.Message}");
            }

            return pages;
        }

        private static string ReadResourceText(Assembly assembly, string resourceName)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return string.Empty;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static BitmapSource? LoadEmbeddedImage(
            Assembly assembly, Dictionary<string, string> imageResources, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            var key = relativePath.Replace('\\', '/').TrimStart('.', '/');
            if (!imageResources.TryGetValue(key, out var resourceName))
                return null;

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;   // 流关掉后位图仍可用
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;   // 图片读不出来就降级成文字，不让整页渲染失败
            }
        }

        /// <summary>取正文第一个一级标题作页名；没有就用文件名。</summary>
        private static string ExtractTitle(string markdown, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                foreach (var rawLine in markdown.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("# ", StringComparison.Ordinal))
                    {
                        var title = line.Substring(2).Trim().TrimEnd('#').Trim();
                        if (title.Length > 0)
                            return title;
                    }
                    else if (line.Length > 0 && !line.StartsWith("<!--", StringComparison.Ordinal))
                    {
                        break;   // 允许文件开头有注释/空行，但正文一旦开始就不再往下找标题
                    }
                }
            }
            return fallback;
        }

        /// <summary>
        /// 兜底自动页：把注册期已经拿到的元数据（显示名 / 分类 / 描述 / 端口表）排成一页。
        /// 参数表不在这里生成——参数以插件自己的配置界面为准，抄一份到手册里必然漂移。
        /// </summary>
        private static string BuildAutoPageMarkdown(ToolItemModel model, Type? type, bool isBuiltIn)
        {
            var sb = new StringBuilder();
            var name = string.IsNullOrWhiteSpace(model.Name) ? "未命名插件" : model.Name!;

            sb.Append("# ").Append(name).AppendLine();
            sb.AppendLine();
            sb.AppendLine(isBuiltIn
                ? "> 本页由软件根据内置节点说明自动生成。内置节点由软件自带（不属于插件），"
                  + "随流程引擎一起发布。"
                : "> 该插件尚未内置手册，本页由软件根据插件元数据自动生成。以下信息来自插件注册项，"
                  + "参数以流程中该步骤的「模块参数」界面为准。");
            sb.AppendLine();
            sb.Append("**所属分类**：").Append(string.IsNullOrWhiteSpace(model.Category) ? "未分类" : model.Category).AppendLine("  ");
            sb.Append("**插件类型**：`").Append(type?.FullName ?? model.ModuleTypeName).AppendLine("`");
            sb.AppendLine();

            sb.AppendLine("## 说明");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(model.Description) ? "（插件未提供描述）" : model.Description!);
            sb.AppendLine();

            AppendPortTable(sb, "输入端口", model.InputDefinitions);
            AppendPortTable(sb, "输出端口", model.OutputDefinitions);

            sb.AppendLine("## 怎么用");
            sb.AppendLine();
            sb.AppendLine("1. 在左侧算子工具箱中找到本插件（分类：" + (string.IsNullOrWhiteSpace(model.Category) ? "未分类" : model.Category) + "），拖入流程；");
            sb.AppendLine("2. 双击步骤或右键「模块参数」打开配置界面；");
            sb.AppendLine("3. 配置完成后编译并运行流程，输出结果可在监视窗口查看。");
            return sb.ToString();
        }

        private static void AppendPortTable(StringBuilder sb, string title, List<PortDefinition>? ports)
        {
            sb.Append("## ").Append(title).AppendLine();
            sb.AppendLine();
            if (ports == null || ports.Count == 0)
            {
                sb.AppendLine("（无）");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("| 端口 | 类型 | 说明 |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var port in ports)
            {
                sb.Append("| ").Append(Escape(port.Name))
                  .Append(" | ").Append(Escape(ShortTypeName(port.DataTypeName)))
                  .Append(" | ").Append(Escape(port.Description ?? string.Empty))
                  .AppendLine(" |");
            }
            sb.AppendLine();
        }

        private static string Escape(string? text) => (text ?? string.Empty).Replace("|", "\\|").Trim();

        /// <summary>`HObject, HalconDotNet, Version=…` → `HObject`（端口表里全名没法看）</summary>
        private static string ShortTypeName(string? assemblyQualifiedName)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedName))
                return string.Empty;
            var full = assemblyQualifiedName.Split(',')[0].Trim();
            var dot = full.LastIndexOf('.');
            return dot >= 0 && dot < full.Length - 1 ? full.Substring(dot + 1) : full;
        }
    }
}
