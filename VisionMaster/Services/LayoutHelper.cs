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
        ///
        /// v4（2026-10-19）：图层并入属性 pane 做标签页（原独立 240px 列），
        /// 右侧从 260+240+300 收成 260+260——面板归属变了，必须让旧布局让位。
        /// v5（2026-10-20）：新增「运行驾驶舱」文档（Panel_RunCockpitView），
        /// 文档区多了一个 ContentId，旧 Layout.xml 必须让位。
        /// v6（2026-10-20）：程序面板并入流程导航、右侧三列收成单列检查器，
        /// 面板归属/停靠语义再次变化，旧 Layout.xml 必须让位。
        /// </summary>
        private const int CurrentLayoutSchemaVersion = 6;

        /// <summary>
        /// 流程栏所在列的默认宽度（与 Shell.xaml 的 DockWidth 一致）。
        /// 依据：流程栏 v2 组头要同时放"类型标签 + 实例名 + N 分支 · M 算子 + 状态图标"，
        /// 340 时默认名称（如"并行分组_0"）能完整显示；320 是下限（名称开始截断但统计不丢）。
        /// </summary>
        private const double DefaultProcessColumnWidth = 340;

        /// <summary>
        /// 流程栏所在列的最小宽度。低于它组头右侧统计会把实例名挤到只剩两三个字。
        /// </summary>
        private const double MinProcessColumnWidth = 320;

        private const string ProcessViewContentId = "Panel_ProcessView";

        /// <summary>
        /// 本次启动捕获的出厂布局快照（内存副本）。
        /// 「恢复默认布局」优先用它：文件快照可能是旧版本留下的（结构版本/尺寸都可能过期），
        /// 而内存副本一定与当前 Shell.xaml 同步；文件被删/被占用时也能立即恢复，不必重启。
        /// </summary>
        private static string? _defaultLayoutXml;

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
        /// <summary>
        /// 四个中央文档的身份清单。布局文件可能因为历史保存路径缺项（例如组态/驾驶舱
        /// 在某次保存时已从树里摘出、缓存又丢了），加载后必须按清单补齐，否则模式切换
        /// 只能看到一个空文档窗格——表现就是“明明打开了方案，组态画面却空白”。
        /// </summary>
        private static readonly (string Id, string Title)[] CoreDocuments =
        {
            ("Panel_ImageView", "视觉图像"),
            ("Panel_FlowCanvasView", "流程画布"),
            ("Panel_ScadaEditorView", "组态画面"),
            ("Panel_RunCockpitView", "运行驾驶舱"),
        };

        private static readonly Dictionary<string, Func<object>> ContentFactories = new()
        {
            ["Panel_ToolView"] = () => new Views.ToolView(),
            ["Panel_ProcessView"] = () => new Views.ProcessView(),
            ["Panel_FlowCanvasView"] = () => new Views.FlowCanvasView(),
            ["Panel_ImageView"] = () => new Views.ImageView(),
            ["Panel_LogView"] = () => new Views.LogView(),
            ["Panel_MonitorView"] = () => new Views.MonitorView(),
            ["Panel_ScadaEditorView"] = () => new Views.ScadaEditorView(),
            ["Panel_RunCockpitView"] = () => new Views.RunCockpitView(),
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
        /// <param name="refreshDefaultSnapshot">
        /// 是否先刷新出厂快照（默认 true）。**只有启动路径能传 true**：那一刻内存里还是
        /// Shell.xaml 的默认布局；「加载布局」命令必须传 false，否则会把用户当前布局
        /// 当成出厂默认写进 DefaultLayout.xml。
        /// </param>
        /// <returns>是否成功加载</returns>
        public static bool Load(bool refreshDefaultSnapshot = true)
        {
            if (refreshDefaultSnapshot)
                CaptureDefaultLayoutSnapshot();

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
        /// 把当前 XAML 内置默认布局快照到 DefaultLayout.xml（"恢复默认布局"的数据源）。
        ///
        /// 【为什么每次启动都重捕获，而不是"只捕获一次"】
        /// 调用点在 LoadFromString 之前，此刻内存里必然是 Shell.xaml 的默认布局，
        /// 重捕获不会覆盖用户布局。反过来，"只捕获一次"会让历史快照永久留存：
        /// 实测 2026-10-18 的 DefaultLayout.xml 里流程栏列宽是 **25px**（早期布局未测量时捕获的），
        /// 点"恢复默认布局"等于把界面恢复成一条缝。快照必须跟着 Shell.xaml 走。
        ///
        /// 先 UpdateLayout 再序列化：OnContentRendered 时机通常已排好版，但保险起见让
        /// 宽高取实测值，而不是未测量时的占位值。
        /// </summary>
        private static void CaptureDefaultLayoutSnapshot()
        {
            try
            {
                FindManager()?.UpdateLayout();

                var xml = SaveToString();
                if (string.IsNullOrEmpty(xml)) return;

                _defaultLayoutXml = xml; // 内存副本：Reset 优先用它（文件可能被删/是旧版本）
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
            => SaveToString(FindManager());

        /// <summary><see cref="SaveToString()"/> 的指定宿主重载（断言宿主用）</summary>
        internal static string? SaveToString(DockingManager? manager)
        {
            try
            {
                if (manager == null) return null;

                // 模式隔离会把非当前模式的文档暂时摘出布局树；保存布局前必须全部放回，
                // 否则在组态模式退出时写出的 Layout.xml 只有组态画面，下次启动会被
                // “未覆盖全部面板”闸门拒绝（用户布局被误伤）。
                var mode = _currentShellMode;
                var previousApplying = IsApplyingShellMode;
                IsApplyingShellMode = true;
                try
                {
                    EnsureCoreDocuments(manager);
                    RestoreAllDocuments(manager);

                    // 组态模式会把日志 pane 收成 1px；序列化会把该瞬态写进 Layout.xml，
                    // 下次启动（Debug）因没有“收起前高度”记忆而无法恢复。保存前先还原高度。
                    RestoreLogPane(manager);

                    var serializer = new XmlLayoutSerializer(manager);
                    using (var writer = new StringWriter())
                    {
                        serializer.Serialize(writer);
                        // 出厂前打上结构版本戳：这是升级时判定"旧布局必须让位"的唯一依据
                        var doc = XDocument.Parse(writer.ToString());
                        doc.Root?.SetAttributeValue(LayoutSchemaVersionAttributeName, CurrentLayoutSchemaVersion);
                        var xml = doc.ToString();

                        // 序列化完成后恢复当前模式的文档可见性与日志高度；只动这两项，不动其它面板。
                        ApplyDocumentMode(manager, DocumentsForMode(mode), ActiveDocumentForMode(mode));
                        if (mode == ShellMode.Config) CollapseLogPane(manager);
                        return xml;
                    }
                }
                finally
                {
                    IsApplyingShellMode = previousApplying;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"布局序列化失败：{ex.Message}");
                return null;
            }
        }

        private static void RestoreAllDocuments(DockingManager manager)
        {
            foreach (var id in HiddenDocuments.Keys.ToList())
            {
                if (HiddenDocuments.TryGetValue(id, out var entry))
                    RestoreDocument(manager, entry.Document);
            }
        }

        /// <summary>
        /// 从 XML 文本恢复布局（供解决方案加载时调用）
        /// </summary>
        /// <param name="layoutXml">布局 XML 文本</param>
        /// <param name="deleteFileOnError">加载失败时是否删除布局文件（仅 Layout.xml 场景使用）</param>
        public static bool LoadFromString(string layoutXml, bool deleteFileOnError = false)
            => LoadFromString(FindManager(), layoutXml, deleteFileOnError);

        /// <summary><see cref="LoadFromString(string, bool)"/> 的指定宿主重载（断言宿主用）</summary>
        internal static bool LoadFromString(DockingManager? manager, string layoutXml, bool deleteFileOnError = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(layoutXml)) return false;

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

                var previousApplying = IsApplyingShellMode;
                IsApplyingShellMode = true;
                try
                {
                    var serializer = new XmlLayoutSerializer(manager);
                    using (var reader = new StringReader(doc.ToString()))
                    {
                        serializer.Deserialize(reader);
                    }

                    // 反序列化成功 = 布局树已被整体替换：此前摘出的 LayoutDocument 实例作废。
                    // 必须在 Deserialize 成功之后清：版本拒绝/解析失败时树没换，缓存仍指向有效文档，
                    // 清早了会让「组态画面」「运行驾驶舱」永久丢失（升级启动 + 每次保存的闭环）。
                    HiddenDocuments.Clear();

                    // 反序列化只还原结构，按 ContentId 回填面板内容
                    FillContents(manager);
                }
                finally
                {
                    IsApplyingShellMode = previousApplying;
                }

                // 新树里的日志 pane 高度来自文件，旧的“收起前高度”记忆不再适用
                _savedLogPaneHeight = null;

                // 双保险（A 方案第 2 层）：对已经被旧 vms 持久化到 Hidden 的关键面板强制拉回可见。
                // CanHide=False 只能从源头禁止后续关闭，已持久化的 Hidden 条目仍需此处兜底。
                EnsureCriticalPanelsVisible(manager);

                // 流程栏列宽兜底：旧布局里可能是被拖窄的绝对宽度（实测 230），
                // 低于最小宽度时拉回默认值（只动这一列，其余面板位置不变）。
                EnsureProcessColumnWidth(manager);

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
                if (version != CurrentLayoutSchemaVersion)
                {
                    reason = versionAttr == null
                        ? $"布局文件无结构版本属性（历史版本，视为 0），当前要求版本 {CurrentLayoutSchemaVersion}"
                        : $"布局结构版本 {version} 与当前版本 {CurrentLayoutSchemaVersion} 不一致";
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
                // 优先用本次启动的内存快照（一定与当前 Shell.xaml 同步）；
                // 内存快照不可用时再退回文件（兼容"启动时捕获失败"的极端情况）。
                var snapshot = _defaultLayoutXml;
                if (string.IsNullOrEmpty(snapshot) && File.Exists(DefaultLayoutFilePath))
                    snapshot = File.ReadAllText(DefaultLayoutFilePath);

                if (!string.IsNullOrEmpty(snapshot))
                {
                    // 出厂快照是"启动那一刻的 XAML 布局"，新增面板或布局重排后它就过期了
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
        /// <summary>
        /// 组态界面设计专属的三个面板（进入组态设计时显示，其余情况收起）。
        /// 顺序：图层在前、属性在后——两者现在是同一个 pane 的两个标签页，
        /// ShowPanel 会顺带 IsActive=true，最后显示的那个成为当前标签；属性是主面板，必须最后。
        /// </summary>
        private static readonly string[] ScadaDesignPanels =
        {
            "Panel_ScadaLayerView",      // 图层
            "Panel_ScadaToolboxView",    // 图元工具箱
            "Panel_ScadaPropertyView",   // 属性（主标签，最后显示以取得选中态）
        };

        /// <summary>
        /// 左侧工作面板（流程导航）：组态设计时不参与，收起后把整列 340px 让给画布。
        /// 退出组态设计时按原位置恢复（走 ShowAnchorable，R58）。
        /// </summary>
        private static readonly string[] LeftWorkPanels =
        {
            "Panel_ProcessView",
        };

        private const string LogPanelContentId = "Panel_LogView";

        /// <summary>组态模式收起前记录的底部日志 pane 高度（null = 当前未收起）。</summary>
        private static GridLength? _savedLogPaneHeight;

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

        /// <summary>
        /// 四种工作模式的上下文收敛：文档标签 + 左右面板 + 底部面板。
        /// 只改可见性/文档集合，不改面板归属与停靠结构（R43 不触发）。
        /// </summary>
        public static void ApplyShellMode(ShellMode mode) => ApplyShellMode(FindManager(), mode);

        private static ShellMode _currentShellMode = ShellMode.Debug;

        /// <summary>模式收敛进行中：ShellViewModel 的文档选中监听必须让路，避免递归。</summary>
        public static bool IsApplyingShellMode { get; private set; }

        private static string[] DocumentsForMode(ShellMode mode) => mode switch
        {
            ShellMode.Debug => new[] { "Panel_ImageView", "Panel_FlowCanvasView" },
            ShellMode.Run => new[] { "Panel_RunCockpitView" },
            ShellMode.Config => new[] { "Panel_ScadaEditorView" },
            _ => new[] { "Panel_ImageView", "Panel_FlowCanvasView", "Panel_ScadaEditorView", "Panel_RunCockpitView" },
        };

        private static string? ActiveDocumentForMode(ShellMode mode) => mode switch
        {
            ShellMode.Debug => "Panel_ImageView",
            ShellMode.Run => "Panel_RunCockpitView",
            ShellMode.Config => "Panel_ScadaEditorView",
            _ => null,
        };

        public static void ApplyShellMode(DockingManager? manager, ShellMode mode)
        {
            if (manager == null) return;
            if (IsApplyingShellMode) return;

            _currentShellMode = mode;

            var previousApplying = IsApplyingShellMode;
            IsApplyingShellMode = true;
            try
            {
                switch (mode)
                {
                    // 调试/组态的面板收敛语义与 ApplyScadaPanelMode 完全一致（R56）：
                    // 调试 = 非组态态（左栏 + 算子工具箱，隐藏组态三面板）；
                    // 组态 = 组态态（左栏收起、日志收 1px、属性/图层/图元显示、算子工具箱隐藏）。
                    // 运行/自由不是组态二态能表达的（Run 要藏左栏与工具箱，Free 要全显示），
                    // 因此保留显式编排，避免用 false 分支把 Run/Free 做坏。
                    case ShellMode.Debug:
                        ApplyDocumentMode(manager, DocumentsForMode(mode), ActiveDocumentForMode(mode));
                        ApplyScadaPanelMode(manager, false);
                        break;

                    case ShellMode.Config:
                        ApplyDocumentMode(manager, DocumentsForMode(mode), ActiveDocumentForMode(mode));
                        ApplyScadaPanelMode(manager, true);
                        break;

                    case ShellMode.Run:
                        ApplyDocumentMode(manager, DocumentsForMode(mode), ActiveDocumentForMode(mode));
                        HidePanel(manager, "Panel_ProcessView");
                        HidePanel(manager, "Panel_ToolView");
                        HidePanel(manager, "Panel_ScadaToolboxView");
                        HidePanel(manager, "Panel_ScadaPropertyView");
                        HidePanel(manager, "Panel_ScadaLayerView");
                        RestoreLogPane(manager);
                        break;

                    case ShellMode.Free:
                        ApplyDocumentMode(manager, DocumentsForMode(mode), ActiveDocumentForMode(mode));
                        ShowPanel(manager, "Panel_ProcessView");
                        // 同一 pane 内最后 Show 的标签胜出：属性主标签放最后
                        ShowPanel(manager, "Panel_ToolView");
                        ShowPanel(manager, "Panel_ScadaToolboxView");
                        ShowPanel(manager, "Panel_ScadaLayerView");
                        ShowPanel(manager, "Panel_ScadaPropertyView");
                        RestoreLogPane(manager);
                        break;
                }
            }
            finally
            {
                IsApplyingShellMode = previousApplying;
            }
        }

        /// <summary>
        /// 文档标签收敛：只保留 allowed 里的文档；其余从 LayoutDocumentPane 摘出并缓存，
        /// 切回该模式时按原 pane/原索引放回。文档不能走 HidePanel（它只处理 anchorable）。
        /// </summary>
        private static void ApplyDocumentMode(DockingManager manager, string[] allowed, string? activeContentId)
        {
            EnsureCoreDocuments(manager);
            var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (var doc in EnumerateContents(manager).OfType<LayoutDocument>().ToList())
            {
                if (doc.ContentId == null) continue;

                if (allowedSet.Contains(doc.ContentId))
                {
                    if (doc.Parent == null) RestoreDocument(manager, doc);
                }
                else
                {
                    HideDocument(doc);
                }
            }

            // 被上一模式摘出布局的文档不在 EnumerateContents 里；按缓存补回本模式允许的文档。
            foreach (var id in HiddenDocuments.Keys.ToList())
            {
                if (allowedSet.Contains(id) && HiddenDocuments.TryGetValue(id, out var entry))
                    RestoreDocument(manager, entry.Document);
            }

            // 只在“允许集里没有任何已选中文档”时才改活动标签：
            // 每次收敛都强行 IsSelected 会把用户正在看的图像/画布拽走（保存布局时也会翻标签）。
            var hasSelected = EnumerateContents(manager)
                .OfType<LayoutDocument>()
                .Any(d => d.ContentId != null && allowedSet.Contains(d.ContentId) && d.IsSelected);

            if (!hasSelected
                && !string.IsNullOrEmpty(activeContentId)
                && FindPanel(manager, activeContentId) is LayoutDocument active)
            {
                active.IsSelected = true;
                active.IsActive = true;
            }
        }

        private static readonly Dictionary<string, (LayoutDocument Document, LayoutDocumentPane Pane, int Index)> HiddenDocuments = new(StringComparer.Ordinal);

        private static void HideDocument(LayoutDocument doc)
        {
            if (doc.ContentId == null || doc.Parent == null) return;
            var pane = FindAncestorDocumentPane(doc);
            if (pane == null) return;

            var index = pane.Children.IndexOf(doc);
            if (index < 0) return;

            HiddenDocuments[doc.ContentId] = (doc, pane, index);
            pane.Children.Remove(doc);
        }

        private static void RestoreDocument(DockingManager manager, LayoutDocument doc)
        {
            if (doc.ContentId == null) return;

            if (!HiddenDocuments.TryGetValue(doc.ContentId, out var entry))
                return;

            if (IsAttachedTo(entry.Pane, manager.Layout))
            {
                entry.Pane.Children.Insert(Math.Min(entry.Index, entry.Pane.Children.Count), doc);
            }
            else
            {
                // 原 pane 已不在树上（布局重载/拆分）：挂到主文档窗格；找不到就新建一个，
                // 绝不静默丢文档（丢了就是“组态/驾驶舱标签消失”）。
                var target = manager.Layout.Descendents()
                    .OfType<LayoutDocumentPane>()
                    .FirstOrDefault(p => IsAttachedTo(p, manager.Layout));

                if (target == null)
                {
                    if (manager.Layout.RootPanel == null) return;
                    target = new LayoutDocumentPane();
                    manager.Layout.RootPanel.InsertChildAt(manager.Layout.RootPanel.ChildrenCount, target);
                }

                target.Children.Add(doc);
            }

            HiddenDocuments.Remove(doc.ContentId);
        }

        private static LayoutDocumentPane? FindAncestorDocumentPane(LayoutDocument doc)
        {
            ILayoutElement? current = doc;
            while (current?.Parent is ILayoutContainer parent)
            {
                if (parent is LayoutDocumentPane pane) return pane;
                current = parent as ILayoutElement;
            }
            return null;
        }

        /// <summary><see cref="ApplyScadaPanelMode(bool)"/> 的指定宿主重载（断言宿主用）</summary>
        public static void ApplyScadaPanelMode(DockingManager? manager, bool scadaDesign)
        {
            if (manager == null) return;

            if (scadaDesign)
            {
                foreach (var id in ScadaDesignPanels) ShowPanel(manager, id);
                // ShowPanel 逐个把 IsSelected 置 true；同 pane 内多个标签会同时为 true（AvalonDock
                // 的 IsSelected 不是互斥位）。属性是主标签，最后把它之外的组态标签显式压掉。
                foreach (var id in ScadaDesignPanels)
                {
                    if (id == "Panel_ScadaPropertyView") continue;
                    if (FindPanel(manager, id) is LayoutAnchorable other)
                        other.IsSelected = false;
                }
                if (FindPanel(manager, "Panel_ScadaPropertyView") is LayoutAnchorable property)
                    property.IsSelected = true;

                HidePanel(manager, OperatorToolboxPanel);

                // 组态设计时左栏（程序/流程栏）不参与，收起后整列宽度让给画布；
                // 底部日志收成 0 高（保留用户原来的可见性与标签选择，只动高度）。
                foreach (var id in LeftWorkPanels) HidePanel(manager, id);
                CollapseLogPane(manager);
            }
            else
            {
                ShowPanel(manager, OperatorToolboxPanel);
                foreach (var id in ScadaDesignPanels) HidePanel(manager, id);

                foreach (var id in LeftWorkPanels) ShowPanel(manager, id);
                RestoreLogPane(manager);
            }
        }

        /// <summary>
        /// 把底部日志 pane 收成 0 高。不用 HidePanel 是为了保住用户原来的可见性/标签选择
        /// （日志、监控栏、设备状态共用这个 pane，隐藏其中一个会把整组收起）。
        /// 重复调用幂等：已经是 0 时不再覆盖保存的高度。
        /// </summary>
        private static void CollapseLogPane(DockingManager manager)
        {
            // 已收起：重复调用直接返回（不能拿高度判断——收起后高度是 1，不是 0）
            if (_savedLogPaneHeight != null) return;

            var pane = FindAncestorAnchorablePane(manager, LogPanelContentId);
            if (pane == null) return;

            // 已经是伪值（1~29，多为上次收起写盘后被夹出来的）就别把它当“收起前高度”记下来
            _savedLogPaneHeight = pane.DockHeight.IsAbsolute && pane.DockHeight.Value < 30
                ? new GridLength(200)
                : pane.DockHeight;

            // 为什么是 1 而不是 0：AvalonDock 的 DockHeight 会把 0 夹回原值（实测 set0 读回 200），
            // 1px 则正常生效——视觉上就是一条边线，高度已经还给画布。
            pane.DockHeight = new GridLength(1);
        }

        /// <summary>
        /// 恢复底部日志 pane 的高度。
        /// 有“收起前高度”记忆时按记忆恢复；没有记忆但当前高度明显是被夹出来的伪值（1~29）时，
        /// 回到默认 200——否则组态模式退出/重启后日志条会永久停在一条缝。
        /// </summary>
        private static void RestoreLogPane(DockingManager manager)
        {
            var pane = FindAncestorAnchorablePane(manager, LogPanelContentId);
            if (pane == null) return;

            if (_savedLogPaneHeight != null)
            {
                pane.DockHeight = _savedLogPaneHeight.Value;
                _savedLogPaneHeight = null;
                return;
            }

            // 只治“伪值”，不碰用户合法拖窄（>=30 视为用户选择）
            if (pane.DockHeight.IsAbsolute && pane.DockHeight.Value < 30)
                pane.DockHeight = new GridLength(200);
        }

        /// <summary>
        /// 底部控制台展开 / 收起（32 ↔ 240）。只改 pane 高度，不改可见性与标签选择；
        /// 与组态模式的 CollapseLogPane 分开：后者用 1px 且带"保存前高度"记忆。
        /// </summary>
        public static void ToggleConsole() => ToggleConsole(FindManager());

        /// <summary><see cref="ToggleConsole()"/> 的指定宿主重载（断言宿主用）。</summary>
        public static void ToggleConsole(DockingManager? manager)
        {
            if (manager == null) return;
            // 组态模式的底部面板是刻意隐藏的，命令面板/状态栏都不该把它临时叫出来。
            if (_currentShellMode == ShellMode.Config) return;

            var pane = FindAncestorAnchorablePane(manager, LogPanelContentId);
            if (pane == null) return;

            var expanded = pane.DockHeight.IsAbsolute && pane.DockHeight.Value > 60;
            pane.DockHeight = new GridLength(expanded ? 32 : 240);
        }

        /// <summary>按 ContentId 找到锚定面板所在的 LayoutAnchorablePane（沿 Parent 链）。</summary>
        private static LayoutAnchorablePane? FindAncestorAnchorablePane(DockingManager manager, string contentId)
        {
            if (FindPanel(manager, contentId) is not LayoutAnchorable anchorable) return null;

            ILayoutElement current = anchorable;
            while (current?.Parent is ILayoutContainer parent)
            {
                if (parent is LayoutAnchorablePane pane) return pane;
                current = parent as ILayoutElement;
            }

            return null;
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
            {
                ShowAnchorable(manager, anchorable);

                // 同一 pane 的多个 anchorable 是标签页：IsActive 表示"整个 DockingManager 的活动内容"，
                // 切到哪个标签要看 IsSelected。属性/图层合并后，这里少一句就会停在最后一个 Show 的标签上。
                anchorable.IsSelected = true;
            }

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
        /// 流程栏列宽兜底：旧 Layout.xml / .vms 里这一列可能是用户拖窄的绝对宽度（实测 230），
        /// 反序列化后按 ContentId 找到流程栏，沿父链找到它所在的 LayoutAnchorablePane，把宽度拉回最小值以上。
        ///
        /// 为什么不能只改 Shell.xaml 的默认值：AvalonDock 恢复布局时用的是文件里的宽度，
        /// 会把默认布局整个覆盖掉；只改 XAML 对老用户等于没发生（与 R43 的"旧布局抵消改造"同族）。
        /// 为什么不用升结构版本：这里只修一列宽度，不该顺手丢掉用户其它面板的位置与高度。
        /// 失败只记日志、不影响启动（最坏情况是维持用户原来的窄列）。
        /// </summary>
        internal static void EnsureProcessColumnWidth(DockingManager manager)
        {
            try
            {
                var anchorable = EnumerateContents(manager)
                    .OfType<LayoutAnchorable>()
                    .FirstOrDefault(a => string.Equals(a.ContentId, ProcessViewContentId, StringComparison.Ordinal));
                if (anchorable == null) return;

                ILayoutElement current = anchorable;
                while (current?.Parent is ILayoutContainer parent)
                {
                    // v6：程序面板并入流程导航后，流程栏直接就是 LayoutAnchorablePane 的宽度；
                    // 旧结构里那层竖向 LayoutPanel 已不存在，不能再按 LayoutPanel 改宽。
                    if (parent is LayoutAnchorablePane pane)
                    {
                        pane.DockMinWidth = MinProcessColumnWidth;

                        // 只纠正"绝对且过窄"的宽度；星号宽度交给 AvalonDock 按窗口分配
                        if (pane.DockWidth.IsAbsolute && pane.DockWidth.Value < MinProcessColumnWidth)
                        {
                            var oldWidth = pane.DockWidth.Value;
                            pane.DockWidth = new GridLength(DefaultProcessColumnWidth);
                            Console.WriteLine(
                                $"流程栏列宽过窄（{oldWidth:0.#}），已拉回 {DefaultProcessColumnWidth}");
                        }

                        return;
                    }

                    current = parent as ILayoutElement;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"流程栏列宽校正失败（不影响启动）：{ex.Message}");
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
        /// 确保四个核心文档都在布局树里（缺失就按 ContentFactories 新建）。
        /// 这是“空文档窗格”的兜底：布局文件是结构快照，AvalonDock 不会凭 ContentId
        /// 自动补出文件里没写过的文档；而 SaveToString 一旦在缺项状态下跑过一次，
        /// 缺项就会永久留在 Layout.xml 里。
        /// </summary>
        private static void EnsureCoreDocuments(DockingManager manager)
        {
            foreach (var (id, title) in CoreDocuments)
            {
                if (FindPanel(manager, id) != null) continue;
                if (HiddenDocuments.ContainsKey(id)) continue;
                if (!ContentFactories.TryGetValue(id, out var factory)) continue;

                var pane = manager.Layout.Descendents()
                    .OfType<LayoutDocumentPane>()
                    .FirstOrDefault(p => IsAttachedTo(p, manager.Layout));
                if (pane == null)
                {
                    if (manager.Layout.RootPanel == null) return;
                    pane = new LayoutDocumentPane();
                    manager.Layout.RootPanel.InsertChildAt(manager.Layout.RootPanel.ChildrenCount, pane);
                }

                var document = new LayoutDocument
                {
                    Title = title,
                    ContentId = id,
                    CanClose = false,
                    Content = factory(),
                };
                pane.Children.Add(document);
                Console.WriteLine($"布局缺少核心文档 {id}，已按 ContentFactories 补回");
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
