using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using UI.Core;

namespace VisionMaster.Services
{
    /// <summary>
    /// AvalonDock 布局助手：布局保存 / 加载 / 重置 / 面板激活的统一入口
    /// 关键点：XmlLayoutSerializer 只序列化布局结构（位置、大小、停靠关系），
    /// 不序列化面板内容（Content 是 UI 实例），因此加载后需按 ContentId 重建内容
    /// </summary>
    internal static class LayoutHelper
    {
        /// <summary>
        /// 布局文件路径（程序运行目录）
        /// </summary>
        public static readonly string LayoutFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Layout.xml");

        /// <summary>
        /// 出厂默认布局快照（首次启动时从 XAML 内置布局捕获，"恢复默认布局"的数据源）
        /// </summary>
        public static readonly string DefaultLayoutFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DefaultLayout.xml");

        /// <summary>
        /// 布局结构版本：Shell.xaml 默认布局的面板归属/停靠语义一变就要递增。
        /// 为什么不能只靠 ContentId 覆盖度校验：像"图元工具箱挪到右侧停靠栏、
        /// 工具箱从自动隐藏区改为常驻停靠栏"这类重排，ContentId 全部复用，
        /// 旧的 Layout.xml / .vms 内嵌布局照样通过覆盖度检查被原样恢复回来，
        /// 改造在老用户机器上等于没发生。版本属性就是 ContentId 之外的结构指纹。
        /// </summary>
        private const int CurrentLayoutSchemaVersion = 2;

        /// <summary>
        /// 布局结构版本的根元素属性名：保存时写入，下发给反序列化器之前摘除。
        /// XmlLayoutSerializer 对 LayoutRoot 上未知属性的行为不可靠，
        /// 绝不能让它看到我们自己塞进去的私有属性。
        /// </summary>
        private const string LayoutSchemaVersionAttributeName = "LayoutSchemaVersion";

        /// <summary>
        /// 布局加载/重载完成后触发（活动栏等外部状态在此同步；
        /// 反序列化会创建新的 LayoutContent 实例，订阅方必须重新挂接监听）
        /// </summary>
        public static event Action? LayoutLoaded;

        /// <summary>
        /// ContentId → 面板内容工厂（加载布局后按 ContentId 重建面板内容）
        /// </summary>
        private static readonly Dictionary<string, Func<object>> ContentFactories = new()
        {
            ["Panel_ToolView"] = () => new Views.ToolView(),
            ["Panel_ProcessView"] = () => new Views.ProcessView(),
            ["Panel_FlowCanvasView"] = () => new Views.FlowCanvasView(),
            ["Panel_FlowListView"] = () => new Views.FlowListView(),
            ["Panel_ImageView"] = () => new Views.ImageView(),
            ["Panel_LogView"] = () => new Views.LogView(),
            ["Panel_MonitorView"] = () => new Views.MonitorView(),
            ["Panel_ScadaEditorView"] = () => new Views.ScadaEditorView(),
            ["Panel_ScadaToolboxView"] = () => new Views.ScadaToolboxView(),
            ["Panel_ScadaPropertyView"] = () => new Views.ScadaPropertyView(),
            // Panel_DeviceStateView：占位面板，无内容
        };

        /// <summary>
        /// 旧 ContentId → 新 ContentId 兼容映射（旧方案布局文件加载时自动迁移到监控栏）
        /// </summary>
        private static readonly Dictionary<string, string> LegacyContentIdMap = new()
        {
            ["Panel_DataView"] = "Panel_MonitorView",      // 旧"数据栏"
            ["Panel_ModuleOutView"] = "Panel_MonitorView", // 旧"模块输出"
        };

        /// <summary>
        /// 查找主窗口中的 DockingManager
        /// </summary>
        public static DockingManager FindManager()
        {
            var root = Application.Current?.MainWindow;
            return root?.GetChildren<DockingManager>().FirstOrDefault();
        }

        /// <summary>
        /// 保存当前布局到 Layout.xml（作为未加载方案时的默认布局）
        /// </summary>
        public static void Save()
        {
            try
            {
                var xml = SaveToString();
                if (xml == null) return;
                File.WriteAllText(LayoutFilePath, xml);
            }
            catch (Exception ex)
            {
                // 布局保存失败不影响主流程
                Console.WriteLine($"布局保存失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 从 Layout.xml 加载默认布局（文件不存在或损坏时保持当前布局）
        /// </summary>
        /// <returns>是否成功加载</returns>
        public static bool Load()
        {
            CaptureDefaultLayoutSnapshot(); // 出厂快照缺失时补捕获（须在 XAML 默认布局就绪后调用）
            try
            {
                if (!File.Exists(LayoutFilePath)) return false;
                return LoadFromString(File.ReadAllText(LayoutFilePath), deleteFileOnError: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"布局加载失败，已重置为默认布局。原因：{ex.Message}");
                TryDeleteLayoutFile();
                return false;
            }
        }

        /// <summary>
        /// 首次启动把 XAML 内置默认布局快照到 DefaultLayout.xml（"恢复默认布局"的数据源；
        /// 只捕获一次，之后即使用户改乱布局也不覆盖）
        /// </summary>
        private static void CaptureDefaultLayoutSnapshot()
        {
            try
            {
                if (File.Exists(DefaultLayoutFilePath)) return;
                var xml = SaveToString();
                if (string.IsNullOrEmpty(xml)) return;
                File.WriteAllText(DefaultLayoutFilePath, xml);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"默认布局快照失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 序列化当前布局为 XML 文本（供解决方案 Config 持久化）
        /// </summary>
        /// <returns>布局 XML；失败返回 null</returns>
        public static string SaveToString()
        {
            try
            {
                var manager = FindManager();
                if (manager == null) return null;

                var serializer = new XmlLayoutSerializer(manager);
                using (var writer = new StringWriter())
                {
                    serializer.Serialize(writer);
                    // 出厂前打上结构版本戳：这是升级时判定"旧布局必须让位"的唯一依据
                    var doc = XDocument.Parse(writer.ToString());
                    doc.Root?.SetAttributeValue(LayoutSchemaVersionAttributeName, CurrentLayoutSchemaVersion);
                    return doc.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"布局序列化失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从 XML 文本恢复布局（供解决方案加载时调用）
        /// </summary>
        /// <param name="layoutXml">布局 XML 文本</param>
        /// <param name="deleteFileOnError">加载失败时是否删除布局文件（仅 Layout.xml 场景使用）</param>
        public static bool LoadFromString(string layoutXml, bool deleteFileOnError = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(layoutXml)) return false;

                var manager = FindManager();
                if (manager == null) return false;

                // 布局文件是"结构快照"：AvalonDock 只会还原文件里声明过的面板，不会凭空补出
                // 新版本新增的 ContentId；而 ContentId 全不变的布局重排，光靠覆盖度也查不出来。
                // 因此统一走"结构版本闸门 + 覆盖度"守卫，在下发之前拦截，不通过就放弃本次恢复，
                // 让 Shell.xaml 声明的默认布局生效。
                // 必须在 Deserialize 之前判断——一旦应用过旧布局，现场就被改坏了，返回 false 也无法回滚。
                // 版本不符只跳过恢复、不删用户的 Layout.xml：那是旧结构下的现场记录，删了才是真丢数据。
                if (!IsLayoutXmlUsable(layoutXml, out var rejectReason))
                {
                    Console.WriteLine($"布局文件被拒绝，已回退默认布局。原因：{rejectReason}");
                    return false;
                }

                // 校验通过后、下发之前摘掉私有版本属性：
                // XmlLayoutSerializer 对未知根属性的行为不可靠，不能让它看见我们塞进去的东西
                var doc = XDocument.Parse(layoutXml);
                doc.Root?.Attribute(LayoutSchemaVersionAttributeName)?.Remove();

                var serializer = new XmlLayoutSerializer(manager);
                using (var reader = new StringReader(doc.ToString()))
                {
                    serializer.Deserialize(reader);
                }

                // 反序列化只还原结构，按 ContentId 回填面板内容
                FillContents(manager);

                // 双保险（A 方案第 2 层）：对已经被旧 vms 持久化到 Hidden 的关键面板强制拉回可见。
                // CanHide=False 只能从源头禁止后续关闭，已持久化的 Hidden 条目仍需此处兜底。
                EnsureCriticalPanelsVisible(manager);

                LayoutLoaded?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"布局恢复失败：{ex.Message}");
                if (deleteFileOnError) TryDeleteLayoutFile();
                return false;
            }
        }

        /// <summary>
        /// 校验布局 XML 是否覆盖了 ContentFactories 登记的全部面板。
        /// 以工厂注册表为权威清单，新增面板时无需再改这里——注册即生效。
        /// </summary>
        private static bool LayoutXmlCoversAllPanels(string layoutXml)
        {
            foreach (var contentId in ContentFactories.Keys)
            {
                if (!layoutXml.Contains($"ContentId=\"{contentId}\"", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 布局 XML 下发前的统一守卫：结构版本闸门 + 面板覆盖度校验。
        /// LoadFromString 与 Reset 共用——出厂快照同样是结构快照，
        /// 改造前捕获的旧 DefaultLayout.xml 版本号必然过期，若放行，
        /// "恢复默认布局"就会把刚被替换掉的旧布局又搬回来。
        /// 一切解析异常一律按"不可用"处理，不许往外抛。
        /// </summary>
        private static bool IsLayoutXmlUsable(string layoutXml, out string reason)
        {
            try
            {
                var versionAttr = XDocument.Parse(layoutXml).Root?.Attribute(LayoutSchemaVersionAttributeName);

                // 属性缺失 = 版本机制引入之前的历史布局，视为 0 版
                int version = versionAttr != null && int.TryParse(versionAttr.Value, out var parsed) ? parsed : 0;
                if (version < CurrentLayoutSchemaVersion)
                {
                    reason = versionAttr == null
                        ? $"布局文件无结构版本属性（历史版本，视为 0），当前要求版本 {CurrentLayoutSchemaVersion}"
                        : $"布局结构版本 {version} 落后于当前版本 {CurrentLayoutSchemaVersion}";
                    return false;
                }

                if (!LayoutXmlCoversAllPanels(layoutXml))
                {
                    reason = "布局文件未覆盖全部面板（多为版本升级新增）";
                    return false;
                }

                reason = null;
                return true;
            }
            catch (Exception ex)
            {
                reason = $"布局 XML 解析失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 重置布局：立即恢复出厂默认布局（DefaultLayout.xml），并删除用户布局文件
        /// </summary>
        public static bool Reset()
        {
            var restored = false;
            try
            {
                if (File.Exists(DefaultLayoutFilePath))
                {
                    var snapshot = File.ReadAllText(DefaultLayoutFilePath);

                    // 出厂快照是"首次启动那一刻的 XAML 布局"，新增面板或布局重排后它就过期了
                    // （结构版本落后 / 缺面板）。过期快照不能用于恢复，直接作废删除，
                    // 下次启动 CaptureDefaultLayoutSnapshot 会按新布局重新捕获出厂快照。
                    if (IsLayoutXmlUsable(snapshot, out var snapshotReason))
                    {
                        restored = LoadFromString(snapshot, deleteFileOnError: false);
                    }
                    else
                    {
                        Console.WriteLine($"出厂布局快照过期，已作废待重新捕获。原因：{snapshotReason}");
                    }

                    if (!restored)
                    {
                        try { File.Delete(DefaultLayoutFilePath); }
                        catch { /* 文件被占用则忽略，下次启动会重新捕获 */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"默认布局恢复失败：{ex.Message}");
            }
            TryDeleteLayoutFile();
            return restored;
        }

        /// <summary>
        /// 按 ContentId 查找布局内容（含浮动窗口与隐藏面板）；找不到返回 null
        /// </summary>
        public static LayoutContent FindPanel(string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return null;

            var manager = FindManager();
            if (manager == null) return null;

            return EnumerateContents(manager)
                .FirstOrDefault(c => c.ContentId == contentId);
        }

        /// <summary>
        /// 面板当前是否可见（隐藏面板 / 关闭到自动隐藏区均视为不可见；文档选项卡常驻视为可见）
        /// </summary>
        public static bool IsPanelVisible(string contentId)
            => FindPanel(contentId) is LayoutContent c
               && (c is not LayoutAnchorable a || a.IsVisible);

        /// <summary>
        /// 面板是否可见且处于激活状态（活动栏高亮判定；兼容停靠面板与文档选项卡）
        /// </summary>
        public static bool IsPanelActive(string contentId)
            => FindPanel(contentId) is LayoutContent c
               && (c is not LayoutAnchorable a || a.IsVisible)
               && c.IsActive;

        /// <summary>
        /// 隐藏指定面板（活动栏再次点击时收起；停靠面板切自动隐藏，文档切到其他标签）
        /// </summary>
        public static void HidePanel(string contentId)
        {
            if (FindPanel(contentId) is LayoutAnchorable a && a.IsVisible)
                a.IsVisible = false; // 文档选项卡无"收起"语义（只能切走），仅处理停靠面板
        }

        /// <summary>
        /// 激活指定面板（按 ContentId）：隐藏的恢复显示、被遮挡的切到前台
        /// </summary>
        public static void ShowPanel(string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return;

            var manager = FindManager();
            if (manager == null) return;

            var content = EnumerateContents(manager)
                .FirstOrDefault(c => c.ContentId == contentId);
            if (content == null) return;

            // 停靠面板先恢复可见再激活；文档选项卡直接激活（切标签）
            if (content is LayoutAnchorable anchorable && !anchorable.IsVisible)
                anchorable.IsVisible = true;
            content.IsActive = true;
        }

        /// <summary>
        /// 关键面板 ContentId 集（必须在任何布局中可见，用户不能把它们藏进 Hidden）
        /// 画布 + 流程栏 = 核心工作流入口，工具箱（ToolView）已改为右侧常驻停靠栏，用户主动收起属合法态不动
        /// </summary>
        private static readonly HashSet<string> CriticalPanelContentIds = new()
        {
            "Panel_FlowCanvasView",
            "Panel_ProcessView",
        };

        /// <summary>
        /// 反序列化后兜底：把被持久化到 Hidden 区的关键面板强制拉回可见。
        /// （Shell.xaml 的 CanHide=False 只拦后续关闭，已经被隐藏保存的旧 vms 仍需此处救治）
        /// </summary>
        private static void EnsureCriticalPanelsVisible(DockingManager manager)
        {
            foreach (var content in EnumerateContents(manager))
            {
                if (!CriticalPanelContentIds.Contains(content.ContentId)) continue;
                if (content is LayoutAnchorable anchorable && !anchorable.IsVisible)
                {
                    anchorable.IsVisible = true;
                    Console.WriteLine($"关键面板 {content.ContentId} 从 Hidden 区强制恢复可见");
                }
            }
        }

        /// <summary>
        /// 按 ContentId 工厂回填面板内容（旧 ContentId 先迁移，无工厂的占位面板跳过）
        /// </summary>
        private static void FillContents(DockingManager manager)
        {
            foreach (var content in EnumerateContents(manager))
            {
                if (content.Content != null) continue;

                // 旧方案布局的 ContentId 迁移（如旧"数据栏/模块输出"→"监控栏"）
                if (content.ContentId != null && LegacyContentIdMap.TryGetValue(content.ContentId, out var migrated))
                    content.ContentId = migrated;

                if (ContentFactories.TryGetValue(content.ContentId, out var factory))
                {
                    content.Content = factory();
                }
            }
        }

        /// <summary>
        /// 遍历布局中的所有内容元素（含浮动窗口与隐藏面板）
        /// </summary>
        private static IEnumerable<LayoutContent> EnumerateContents(DockingManager manager)
        {
            foreach (var c in Walk(manager.Layout.RootPanel))
                yield return c;

            foreach (var window in manager.Layout.FloatingWindows)
            {
                object panel = window switch
                {
                    LayoutAnchorableFloatingWindow afw => afw.RootPanel,
                    LayoutDocumentFloatingWindow dfw => dfw.RootPanel,
                    _ => null
                };
                if (panel == null) continue;

                foreach (var c in Walk(panel))
                    yield return c;
            }

            foreach (var hidden in manager.Layout.Hidden)
                yield return hidden;
        }

        /// <summary>
        /// 递归遍历布局树（AvalonDock 无公开的统一 Children 接口，按容器类型分别处理）
        /// </summary>
        private static IEnumerable<LayoutContent> Walk(object element)
        {
            switch (element)
            {
                case null:
                    yield break;

                case LayoutAnchorable anchorable:
                    yield return anchorable;
                    break;

                case LayoutDocument document:
                    yield return document;
                    break;

                case LayoutAnchorablePane pane: // LayoutGroup<LayoutAnchorable>
                    foreach (var a in pane.Children)
                        yield return a;
                    break;

                case LayoutDocumentPane docPane: // LayoutGroup<LayoutDocument>
                    foreach (var d in docPane.Children)
                        yield return d;
                    break;

                case LayoutPanel panel: // LayoutGroup<ILayoutElement>
                    foreach (var c in panel.Children.SelectMany(Walk))
                        yield return c;
                    break;

                case LayoutAnchorablePaneGroup paneGroup: // LayoutGroup<ILayoutElement>
                    foreach (var c in paneGroup.Children.SelectMany(Walk))
                        yield return c;
                    break;

                case LayoutDocumentPaneGroup docPaneGroup: // LayoutGroup<ILayoutElement>
                    foreach (var c in docPaneGroup.Children.SelectMany(Walk))
                        yield return c;
                    break;
            }
        }

        private static bool TryDeleteLayoutFile()
        {
            try
            {
                if (File.Exists(LayoutFilePath))
                {
                    File.Delete(LayoutFilePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"布局文件删除失败：{ex.Message}");
                return false;
            }
        }
    }
}
