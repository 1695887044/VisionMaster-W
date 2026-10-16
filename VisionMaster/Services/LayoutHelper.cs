using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private const int CurrentLayoutSchemaVersion = 3;

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
            // 图层面板：原先漏登记。反序列化后 FillContents 靠本表回填内容，缺它 = 每次
            // "从 Layout.xml 恢复布局"之后图层面板变空壳；而覆盖度校验只遍历本表的 Keys，
            // 也不会因为缺它而拦下那份布局——属于"静默少一个面板"。
            ["Panel_ScadaLayerView"] = () => new Views.ScadaLayerView(),
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
        public static LayoutContent FindPanel(string contentId) => FindPanel(FindManager(), contentId);

        /// <summary>
        /// <see cref="FindPanel(string)"/> 的指定宿主重载。
        /// 为什么要有它：断言宿主（UIThemeSmokeTest）里的 Shell 窗口从不 Show，也不是
        /// <c>Application.Current.MainWindow</c>，<see cref="FindManager"/> 在那里取不到管理器；
        /// 只能显式把"它自己那份 DockingManager"传进来，否则运行时断言无从下手。
        /// </summary>
        public static LayoutContent FindPanel(DockingManager? manager, string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return null;
            if (manager == null) return null;

            return EnumerateContents(manager)
                .FirstOrDefault(c => c.ContentId == contentId);
        }

        /// <summary>
        /// 面板当前是否可见（隐藏面板 / 关闭到自动隐藏区均视为不可见；文档选项卡常驻视为可见）
        /// </summary>
        public static bool IsPanelVisible(string contentId)
            => IsPanelVisible(FindManager(), contentId);

        /// <summary>
        /// <see cref="IsPanelVisible(string)"/> 的指定宿主重载（断言宿主用）
        /// </summary>
        public static bool IsPanelVisible(DockingManager? manager, string contentId)
            => FindPanel(manager, contentId) is LayoutContent c
               && (c is not LayoutAnchorable a || a.IsVisible);

        /// <summary>
        /// 面板是否可见且处于激活状态（活动栏高亮判定；兼容停靠面板与文档选项卡）
        /// </summary>
        public static bool IsPanelActive(string contentId)
            => FindPanel(contentId) is LayoutContent c
               && (c is not LayoutAnchorable a || a.IsVisible)
               && c.IsActive;

        /// <summary>
        /// 文档面板是否是「中央文档区里当前选中的那个标签」（文档类型专用；停靠面板恒 false）。
        ///
        /// 为什么不能用 <see cref="IsPanelActive"/> 代替：<c>LayoutContent.IsActive</c> 表示
        /// "整个 DockingManager 的当前活动内容"——用户点一下右侧的「属性」或「图层」面板，
        /// 文档的 IsActive 就被抢走。拿它当"当前在组态设计"的判据会造成**自毁循环**：
        /// 判定退出组态 → 随即隐藏用户刚点的那个面板。
        /// <c>LayoutDocument.IsSelected</c> 只随"文档区的选中标签"变化，点停靠面板不会动它。
        /// </summary>
        public static bool IsDocumentSelected(string contentId)
            => IsDocumentSelected(FindManager(), contentId);

        /// <summary><see cref="IsDocumentSelected(string)"/> 的指定宿主重载（断言宿主用）</summary>
        public static bool IsDocumentSelected(DockingManager? manager, string contentId)
            => FindPanel(manager, contentId) is LayoutDocument d && d.IsSelected;

        /// <summary>
        /// 组态界面设计专属的三个面板（进入组态设计时显示，其余情况收起）。
        /// 「属性」是"选中图元才有内容"的上下文面板，但它只在组态场景才有意义，故一并收敛。
        /// </summary>
        private static readonly string[] ScadaDesignPanels =
        {
            "Panel_ScadaPropertyView",   // 属性
            "Panel_ScadaLayerView",      // 图层
            "Panel_ScadaToolboxView",    // 图元工具箱
        };

        /// <summary>算子工具箱（非组态场景下唯一保留的右侧面板）</summary>
        private const string OperatorToolboxPanel = "Panel_ToolView";

        /// <summary>
        /// 按"当前是否处于组态界面设计"收敛右侧面板：
        ///   组态设计 → 显示 属性 / 图层 / 图元工具箱，隐藏 算子工具箱；
        ///   其它情况 → 只显示 算子工具箱，属性与图层两列随之收成 0（画布变宽）。
        ///
        /// 为什么**先 Show 目标、再 Hide 对方**：「工具箱」与「图元工具箱」同处一个
        /// <c>LayoutAnchorablePane</c> 做标签页——若先 Hide 再 Show，中间会出现"该 pane 两个子项
        /// 都不可见"的瞬态，空 pane 的列宽会被置 0，表现为整列闪一下。先激活目标就没有这个窗口。
        ///
        /// 为什么放在这里而不是 ShellViewModel：断言宿主可以对着**它自己那份** <c>DockingManager</c>
        /// 直接调用（见下面的指定宿主重载）、验证可见性矩阵；写在 VM 里就只能写"源码里有这段"这种弱断言。
        ///
        /// 注意：本方法只改**可见性**，不改任何面板的 pane 归属，因此不触发
        /// <see cref="CurrentLayoutSchemaVersion"/> 递增（见 R43 的判据）。
        /// </summary>
        public static void ApplyScadaPanelMode(bool scadaDesign)
            => ApplyScadaPanelMode(FindManager(), scadaDesign);

        /// <summary><see cref="ApplyScadaPanelMode(bool)"/> 的指定宿主重载（断言宿主用）</summary>
        public static void ApplyScadaPanelMode(DockingManager? manager, bool scadaDesign)
        {
            if (manager == null) return;

            if (scadaDesign)
            {
                foreach (var id in ScadaDesignPanels) ShowPanel(manager, id);
                HidePanel(manager, OperatorToolboxPanel);
            }
            else
            {
                ShowPanel(manager, OperatorToolboxPanel);
                foreach (var id in ScadaDesignPanels) HidePanel(manager, id);
            }
        }

        /// <summary>
        /// 隐藏指定面板（活动栏再次点击时收起；停靠面板切自动隐藏，文档切到其他标签）
        /// </summary>
        public static void HidePanel(string contentId) => HidePanel(FindManager(), contentId);

        /// <summary><see cref="HidePanel(string)"/> 的指定宿主重载（断言宿主用）</summary>
        public static void HidePanel(DockingManager? manager, string contentId)
        {
            if (FindPanel(manager, contentId) is LayoutAnchorable a && a.IsVisible)
                a.IsVisible = false; // 文档选项卡无"收起"语义（只能切走），仅处理停靠面板
        }

        /// <summary>
        /// 激活指定面板（按 ContentId）：隐藏的恢复显示、被遮挡的切到前台
        /// </summary>
        public static void ShowPanel(string contentId) => ShowPanel(FindManager(), contentId);

        /// <summary><see cref="ShowPanel(string)"/> 的指定宿主重载（断言宿主用）</summary>
        public static void ShowPanel(DockingManager? manager, string contentId)
        {
            if (string.IsNullOrEmpty(contentId)) return;
            if (manager == null) return;

            var content = EnumerateContents(manager)
                .FirstOrDefault(c => c.ContentId == contentId);
            if (content == null) return;

            // 停靠面板先恢复可见再激活；文档选项卡直接激活（切标签）
            if (content is LayoutAnchorable anchorable)
                ShowAnchorable(manager, anchorable);
            content.IsActive = true;
        }

        /// <summary>
        /// 让一个隐藏中的停靠面板重新可见——**不许在这外面直接写 <c>anchorable.IsVisible = true</c>**。
        ///
        /// 为什么要有这一层（2026-10-16 真机崩溃，栈落在 LayoutHelper.ShowPanel）：
        /// AvalonDock 4.74.1 的 <c>LayoutAnchorable.Show()</c> 把 <c>Hide()</c> 时记下的"回哪一列、第几个位置"
        /// （<see cref="ILayoutPreviousContainer"/>.PreviousContainer + 内部属性 PreviousContainerIndex）
        /// 原样喂给 <c>Collection&lt;T&gt;.Insert</c>，**只判上界、不判下界**（index 超过子项数就改成追加，
        /// 负数照样塞进去）。索引为 <c>-1</c> 时（记忆里那一列已经不含本面板，
        /// <c>IndexOfChild(this)</c> 当场返回 -1）直接抛 <c>ArgumentOutOfRangeException</c>——真机那条栈。
        /// 而"清空记忆"的赋值排在插入语句**之后**，异常一抛坏记忆永久留着，本面板此后每 Show 一次炸一次；
        /// R56 又让"切中央文档区标签"就重收敛一次，等于把一次性故障放大成天天闪退
        /// （<c>App</c> 没有 DispatcherUnhandledException 兜底，必退）。
        /// 另一头更阴：记忆里那一列取不到时 <c>Show()</c> 整段跳过插入，不抛、也**不显示**（静默故障）。
        ///
        /// 口径：先用库自己的记忆判一次"这次 Show 落不落得回去"，落得回去就走原路（事件齐全）；
        /// 判废、或原路抛了/压根没生效，就我们自己把它挂回一个仍挂在布局树上的 pane。
        /// </summary>
        private static void ShowAnchorable(DockingManager manager, LayoutAnchorable anchorable)
        {
            if (anchorable.IsVisible) return;
            // 既不可见也不隐藏 = Parent 为 null（已从布局上摘干净）：Show() 这一态直接抛 InvalidOperationException
            if (!anchorable.IsHidden) return;

            var remembered = ReadPreviousContainer(anchorable);
            var rememberedIndex = ReadPreviousContainerIndex(anchorable);

            // 记忆判废（索引为负、或那一列已经不在当前这棵树上）就不许让库自己插。
            // 索引反射不到（库版本改了内部属性）时 rememberedIndex 为 null，算"不知道"：让库自己试，抛了照样走修复。
            bool memoryUnusable = rememberedIndex < 0
                || (remembered != null && !IsAttachedTo(remembered, manager.Layout));

            if (!memoryUnusable)
            {
                try
                {
                    anchorable.IsVisible = true;
                    if (anchorable.IsVisible) return; // 真显示出来了才算成功（记忆为空时库会静默跳过插入）
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"面板 {anchorable.ContentId} 按记忆恢复失败，改用显式挂回：{ex.Message}");
                }
            }

            AnchorToHomePane(manager, anchorable, remembered);
        }

        /// <summary>
        /// 读 <c>PreviousContainer</c>：出货程序集里它是 <see cref="LayoutContent"/> 上的
        /// **protected 属性**（getter 非公开；公开接口 <c>ILayoutPreviousContainer</c> 是 internal、
        /// 外部程序集转不过去——2026-10-16 实测：写成接口强转会 CS0122 编译不过，
        /// 写成 <c>GetField("PreviousContainer")</c> 会返回 null 静默失效）。
        /// 取不到返回 null，调用方据此退回"让库自己试"的老路径。**反射只读、不改写库的状态。**
        /// </summary>
        private static ILayoutContainer? ReadPreviousContainer(LayoutAnchorable anchorable)
        {
            for (var type = anchorable.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                var property = type.GetProperty("PreviousContainer",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (property?.GetValue(anchorable) is ILayoutContainer container) return container;
            }
            return null;
        }

        /// <summary>
        /// 读 <c>PreviousContainerIndex</c>：出货程序集里这条记忆是**公开属性**（基类
        /// <see cref="LayoutContent"/> 上，属性名同名、底层字段是 <c>_previousContainerIndex</c>）——
        /// 所以 <c>GetField("PreviousContainerIndex")</c> 同样取不到，必须走属性。
        /// 取不到返回 null，调用方据此退回"让库自己试"的老路径。**反射只读、不改写库的状态。**
        /// </summary>
        private static int? ReadPreviousContainerIndex(LayoutAnchorable anchorable)
        {
            for (var type = anchorable.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                var property = type.GetProperty("PreviousContainerIndex",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (property?.GetValue(anchorable) is int index) return index;
            }
            return null;
        }

        /// <summary>容器是否还挂在这棵布局树上（被 Hide 掏空、或反序列化后作废的 pane 的 Root 取不到当前 LayoutRoot）</summary>
        private static bool IsAttachedTo(ILayoutContainer container, LayoutRoot root)
            => ReferenceEquals((container as ILayoutElement)?.Root, root);

        /// <summary>
        /// 显式把隐藏的面板挂回布局树。落点四档：① 记忆里那一列**且它是空的**（被 Hide 掏空的原列，位置最准）；
        /// ② 树上第一个空列（本仓设计里被收起的面板都会留下一个空列，<c>Hide()</c> 并不摘除 pane 本身）；
        /// ③ 记忆里那一列（非空也认，宁可当标签页——至少还在同一侧）；
        /// ④ 兜底：在根面板末尾新加一列（宁可宽度不对、位置偏右，也不能让面板显示不出来）。
        /// 为什么要区分"空/非空"：走得到这里说明那条记忆已经判废（索引 -1 多半是"记忆里的列不含本面板"），
        /// 非空的记忆列很可能压根不是它的家，把它塞进去只是次优解，不如先找一个空列。
        /// 统一走 <c>InsertChildAt(ChildrenCount, …)</c>——与 <c>Show()</c> 成功路径同一个动作，
        /// <c>Parent</c> 由集合变更回调赋值，<c>Hidden</c> 的收尾照库自己的路子走。
        /// 修复之后再 Hide 一次，库会把有效索引重新记回去（本方法不在本仓留任何私有状态）。
        /// </summary>
        private static void AnchorToHomePane(DockingManager manager, LayoutAnchorable anchorable, ILayoutContainer? remembered)
        {
            // 库可能在抛异常**之前**已经把它插进去了（异常出在插入之后的语句），此时再插一次就是同一 pane 两个副本
            if (anchorable.IsVisible) return;

            var root = manager.Layout;
            var rememberedGroup = remembered as ILayoutGroup;
            bool rememberedUsable = rememberedGroup != null && IsAttachedTo(rememberedGroup, root);

            var emptyRemembered = rememberedUsable && rememberedGroup!.ChildrenCount == 0 ? rememberedGroup : null;
            var anyEmpty = root.Descendents()
                .OfType<LayoutAnchorablePane>()
                .FirstOrDefault(p => p.ChildrenCount == 0 && IsAttachedTo(p, root));
            var home = emptyRemembered ?? anyEmpty ?? (rememberedUsable ? rememberedGroup : null);

            if (home != null)
            {
                home.InsertChildAt(home.ChildrenCount, anchorable);
                anchorable.IsSelected = true;
                anchorable.IsActive = true;
                return;
            }

            if (root.RootPanel == null) return;
            var newPane = new LayoutAnchorablePane(anchorable) { DockWidth = new GridLength(260) };
            root.RootPanel.InsertChildAt(root.RootPanel.ChildrenCount, newPane);
            anchorable.IsSelected = true;
            anchorable.IsActive = true;
            Console.WriteLine($"面板 {anchorable.ContentId} 找不到可用的列，已在根面板末尾新建一列承载");
        }

        /// <summary>
        /// 关键面板 ContentId 集（必须在任何布局中可见，用户不能把它们藏进 Hidden）。
        /// 口径（2026-10-15 修正）：流程栏是 anchorable，靠本集合兜底"已被持久化成 Hidden 的旧现场"；
        /// 画布已改为中央文档区（LayoutDocument：CanHide 恒 false + CanClose=False），
        /// 结构上就进不了 Hidden——下面 `content is LayoutAnchorable` 的判据对它是空转。
        /// 条目仍然保留，作为"万一被拖成 anchorable 后又隐藏"的廉价双保险；
        /// 工具箱（ToolView）是右侧常驻停靠栏，用户主动收起属合法态，不在本集合。
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
            // 先快照：恢复可见会动布局树（挂回原列/新建列），拿惰性迭代器边遍历边改就是自毁
            foreach (var content in EnumerateContents(manager).ToList())
            {
                if (content is not LayoutAnchorable anchorable) continue;
                if (!CriticalPanelContentIds.Contains(anchorable.ContentId)) continue;
                if (anchorable.IsVisible) continue;

                ShowAnchorable(manager, anchorable);
                Console.WriteLine($"关键面板 {anchorable.ContentId} 从 Hidden 区强制恢复可见（{(anchorable.IsVisible ? "成功" : "失败：仍未显示")}）");
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
