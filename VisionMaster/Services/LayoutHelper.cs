using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Core.Interfaces;
using UI.Core;

namespace VisionMaster.Services
{
    /// <summary>
    /// 底部控制台的三种状态。
    ///
    /// 为什么要有这个枚举，而不是像以前那样只看 <c>DockHeight &gt; 60</c>：
    /// 抽屉条（<c>ConsoleDrawerBar</c>）的可见性、面板高度、箭头方向必须是同一份状态的三处输出；
    /// 从高度反推会把"用户把分隔条拖到 50px"误判成收起，而且组态模式（隐藏）与用户收起
    /// （抽屉条可见）在高度上都是 1px，反推根本分不开。
    /// </summary>
    public enum ConsolePaneState
    {
        /// <summary>组态模式：面板压到 1px，且抽屉条也不显示（底部整块让给画布）。</summary>
        Hidden,

        /// <summary>收起：面板压到 1px，抽屉条（页签 + 计数 + 摘要 + 箭头）显示。</summary>
        Collapsed,

        /// <summary>展开：面板 240（或用户拖出的高度），抽屉条隐藏，由面板自己的页签条接管。</summary>
        Expanded,
    }

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
        /// v7（2026-10-26）：底部面板组删掉「设备状态」占位页签、日志页改名「控制台」、
        /// 控制台收起改由 Shell 抽屉条承载。**必须升版**：旧 Layout.xml 里存着
        /// Panel_DeviceStateView，反序列化会把它原样恢复成一个空壳标签（正是这次要删的东西），
        /// 而 ContentId 覆盖度校验不会因此报错——属于"改造在老用户机器上等于没发生"。
        /// </summary>
        private const int CurrentLayoutSchemaVersion = 7;

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
        /// 底部控制台状态的根元素属性名：保存时写入（Collapsed / Expanded），
        /// 下发前摘除。只持久化用户可见的两态；组态模式的 Hidden 是瞬态，写成 Collapsed。
        /// </summary>
        private const string LayoutConsoleStateAttributeName = "ConsoleState";

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
            // 底部面板组只剩「控制台 / 监控栏」两个页签；原「设备状态」占位面板已于 2026-10-26 删除
            // （空壳标签，设备在线信息按设计基线归运行驾驶舱 KPI 卡），故这里不再有它的条目。
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

                    // 控制台收起（或组态隐藏）时整块面板不在停靠树里；序列化前必须放回，
                    // 否则写出的 Layout.xml 里没有它，下次启动控制台直接消失。
                    var consoleState = CurrentConsoleState;
                    // 组态的 Hidden 是模式瞬态，不能带给下次启动的调试模式；写盘按收起处理。
                    var persistedConsoleState = consoleState == ConsolePaneState.Hidden
                        ? ConsolePaneState.Collapsed
                        : consoleState;
                    RestoreLogPane(manager);

                    var serializer = new XmlLayoutSerializer(manager);
                    try
                    {
                        using (var writer = new StringWriter())
                        {
                            serializer.Serialize(writer);
                            // 出厂前打上结构版本戳：这是升级时判定”旧布局必须让位”的唯一依据
                            var doc = XDocument.Parse(writer.ToString());
                            doc.Root?.SetAttributeValue(LayoutSchemaVersionAttributeName, CurrentLayoutSchemaVersion);
                            doc.Root?.SetAttributeValue(LayoutConsoleStateAttributeName, persistedConsoleState.ToString());
                            return doc.ToString();
                        }
                    }
                    finally
                    {
                        // 成功/失败都要恢复当前模式的文档可见性与控制台状态；只动这两项，不动其它面板。
                        // 为什么放 finally：Serialize 抛异常时界面已经处于“全部文档放回 + 日志展开”的临时态，
                        // 旧写法在 catch 里直接 return null，会把用户界面永久停在这个临时态。
                        try
                        {
                            ApplyDocumentMode(manager, DocumentsForMode(mode), ActiveDocumentForMode(mode));
                            if (consoleState != ConsolePaneState.Expanded) ApplyConsoleState(manager, consoleState);
                        }
                        catch (Exception restoreEx)
                        {
                            Console.WriteLine($"布局序列化后恢复当前模式失败：{restoreEx.Message}");
                        }
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
                ConsolePaneState? savedConsoleState = null;
                var consoleStateAttr = doc.Root?.Attribute(LayoutConsoleStateAttributeName);
                if (consoleStateAttr != null
                    && Enum.TryParse<ConsolePaneState>(consoleStateAttr.Value, out var parsedConsoleState))
                {
                    savedConsoleState = parsedConsoleState;
                }

                doc.Root?.Attribute(LayoutSchemaVersionAttributeName)?.Remove();
                doc.Root?.Attribute(LayoutConsoleStateAttributeName)?.Remove();

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

                    // 旧现场可能把右侧检查器面板拖成了中央文档（CanDockAsTabbedDocument 之前是默认 true）；
                    // 这里把 4 个检查器面板从 LayoutDocument 迁回右侧 LayoutAnchorablePane。
                    EnsureInspectorPanelsAreAnchorables(manager);

                    // 旧现场也可能把检查器面板停靠在左侧工作列（与流程导航同一个 pane）：
                    // 那份归属会被 Layout.xml 一次次原样恢复，必须在恢复布局的同一拍里纠正。
                    EnsureInspectorPanelsOnRightColumn(manager);

                    // XmlLayoutSerializer 会新建 LayoutAnchorable，XML 里没写的属性回落库默认
                    // （CanDockAsTabbedDocument=true / AutoHideWidth=0 / CanFloat=true）。
                    // 旧布局没有这些新属性时必须按当前策略补回，否则“工具箱被拖成中央文档”会复发。
                    ApplyPanelPolicies(manager);
                }
                finally
                {
                    IsApplyingShellMode = previousApplying;
                }

                // 新树里的日志 pane 高度来自文件，旧的“收起前高度”记忆与缓存 pane 都不再适用
                _savedLogPaneHeight = null;
                _consolePaneCache = null;
                _consolePaneParent = null;
                _consolePaneIndex = -1;

                // 双保险（A 方案第 2 层）：对已经被旧 vms 持久化到 Hidden 的关键面板强制拉回可见。
                // CanHide=False 只能从源头禁止后续关闭，已持久化的 Hidden 条目仍需此处兜底。
                EnsureCriticalPanelsVisible(manager);

                // 流程栏列宽兜底：旧布局里可能是被拖窄的绝对宽度（实测 230），
                // 低于最小宽度时拉回默认值（只动这一列，其余面板位置不变）。
                EnsureProcessColumnWidth(manager);

                // 右侧检查器列宽兜底：旧布局里是 320（v2 的 ≥1500 值），当前按 ≤1500 规则用 300。
                // 只纠正"恰好 320"的旧默认，用户自己拖过的其它宽度保持不动。
                EnsureInspectorColumnWidth(manager);

                // 控制台状态：带属性的新布局按用户上次状态恢复；老布局没有属性按模式默认
                // （调试/自由收起 32、运行展开 240、组态隐藏）。这是 A 方案"默认不占 200"的落点。
                ApplyConsoleState(manager, savedConsoleState ?? ConsoleStateForMode(_currentShellMode));

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
               && (c is not LayoutAnchorable a || (a.IsVisible && !a.IsAutoHidden));

        /// <summary>
        /// 面板是否可见且处于激活状态（活动栏高亮判定；兼容停靠面板与文档选项卡）
        /// </summary>
        public static bool IsPanelActive(string contentId)
            => FindPanel(contentId) is LayoutContent c
               && (c is not LayoutAnchorable a || (a.IsVisible && !a.IsAutoHidden))
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

        /// <summary>底部面板组的第二个页签（抽屉条"监控栏"入口用）。</summary>
        private const string MonitorPanelContentId = "Panel_MonitorView";

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
        public static void ApplyScadaPanelMode(bool scadaDesign, bool respectUserHidden = false)
            => ApplyScadaPanelMode(FindManager(), scadaDesign, respectUserHidden);

        /// <summary>
        /// 四种工作模式的上下文收敛：文档标签 + 左右面板 + 底部面板。
        /// 只改可见性/文档集合，不改面板归属与停靠结构（R43 不触发）。
        /// </summary>
        public static void ApplyShellMode(ShellMode mode, bool respectUserHidden = false)
            => ApplyShellMode(FindManager(), mode, respectUserHidden);

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

        /// <summary>各模式的底部控制台默认态（新布局/老布局没有 ConsoleState 属性时使用）。</summary>
        private static ConsolePaneState ConsoleStateForMode(ShellMode mode) => mode switch
        {
            ShellMode.Run => ConsolePaneState.Expanded,
            ShellMode.Config => ConsolePaneState.Hidden,
            _ => ConsolePaneState.Collapsed,
        };

        private static string? ActiveDocumentForMode(ShellMode mode) => mode switch
        {
            ShellMode.Debug => "Panel_ImageView",
            ShellMode.Run => "Panel_RunCockpitView",
            ShellMode.Config => "Panel_ScadaEditorView",
            _ => null,
        };

        public static void ApplyShellMode(DockingManager? manager, ShellMode mode, bool respectUserHidden = false)
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
                        ApplyScadaPanelMode(manager, false, respectUserHidden);
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
                        // 同一 pane 内最后 Show 的标签胜出：属性主标签放最后。
                        // 尊重用户收起：启动/布局加载时不把已经进 Hidden/自动隐藏区的工具箱强行拉出来。
                        // 判据用 IsPanelVisible（自动隐藏的 IsVisible 仍为 true，不能只看 IsVisible）。
                        if (!(respectUserHidden && !IsPanelVisible(manager, OperatorToolboxPanel)))
                        {
                            ShowPanel(manager, OperatorToolboxPanel);
                        }
                        ShowPanel(manager, "Panel_ScadaToolboxView");
                        ShowPanel(manager, "Panel_ScadaLayerView");
                        ShowPanel(manager, "Panel_ScadaPropertyView");
                        // 自由模式的控制台默认也是 32；不在这里 RestoreLogPane，
                        // 否则启动/切文档会把 LoadFromString 恢复的收起态洗回 200。
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
        public static void ApplyScadaPanelMode(DockingManager? manager, bool scadaDesign, bool respectUserHidden = false)
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
                // 尊重用户收起：启动/布局加载时工具箱已在 Hidden 区或自动隐藏区（胶囊态）就不再 Show；
                // 显式切模式（respectUserHidden=false）仍按调试模式的默认面板集显示。
                // 判据必须用 IsPanelVisible：自动隐藏时 IsVisible 仍为 true（Parent 是 LayoutAnchorGroup），
                // 只看 IsVisible 会在重启后把用户的图钉收起强行拉回来（2026-10-26 真机）。
                if (!(respectUserHidden && !IsPanelVisible(manager, OperatorToolboxPanel)))
                {
                    ShowPanel(manager, OperatorToolboxPanel);
                }

                foreach (var id in ScadaDesignPanels) HidePanel(manager, id);

                foreach (var id in LeftWorkPanels) ShowPanel(manager, id);
                // 不在这里 RestoreLogPane：A 方案把调试/自由的控制台默认改成 32，
                // 控制台状态由 LoadFromString（ConsoleState）/ SetShellMode（切模式收起）/
                // 用户手动 Toggle 维护；这里再展开会把启动时恢复的收起态洗回 200。
            }
        }

        /// <summary>
        /// 底部控制台状态变化（收起 / 展开 / 组态隐藏）。抽屉条与状态栏按钮都以此为准。
        ///
        /// 为什么用静态事件：改高度只有 DockingManager 能做（<see cref="LayoutHelper"/> 是静态类），
        /// 而 UI 状态在 ShellViewModel 手里；不通知就会变成"面板 1px 了但抽屉条不出现"的分叉。
        /// </summary>
        public static event Action<ConsolePaneState>? ConsoleStateChanged;

        /// <summary>当前底部控制台状态（唯一事实源；由 <see cref="ApplyConsoleState"/> 维护）。</summary>
        public static ConsolePaneState CurrentConsoleState { get; private set; } = ConsolePaneState.Collapsed;

        /// <summary>
        /// 收起 / 展开底部控制台。只改 pane 高度与状态，不改可见性与标签选择。
        /// 组态模式下直接返回：底部面板是刻意隐藏的，状态栏按钮与命令面板都不该把它叫出来。
        /// </summary>
        public static void ToggleConsole() => ToggleConsole(FindManager());

        /// <summary><see cref="ToggleConsole()"/> 的指定宿主重载（断言宿主用）。</summary>
        public static void ToggleConsole(DockingManager? manager)
        {
            if (manager == null) return;
            if (_currentShellMode == ShellMode.Config) return;

            ApplyConsoleState(
                manager,
                CurrentConsoleState == ConsolePaneState.Expanded
                    ? ConsolePaneState.Collapsed
                    : ConsolePaneState.Expanded);
        }

        /// <summary>
        /// 展开底部控制台（抽屉条上的页签点击、视图菜单「控制台」等外部入口共用）。
        /// 与 <see cref="ToggleConsole"/> 分开：这些入口的语义是"我要看这块"，不是"切一下"。
        /// </summary>
        public static void ExpandConsole() => ExpandConsole(FindManager());

        /// <summary><see cref="ExpandConsole()"/> 的指定宿主重载（断言宿主用）。</summary>
        public static void ExpandConsole(DockingManager? manager)
        {
            if (manager == null) return;
            ApplyConsoleState(manager, ConsolePaneState.Expanded);
        }

        /// <summary>收起底部控制台（启动无布局 / 切回调试·自由时的模式默认）。</summary>
        public static void CollapseConsole() => CollapseConsole(FindManager());

        /// <summary><see cref="CollapseConsole()"/> 的指定宿主重载（断言宿主用）。</summary>
        public static void CollapseConsole(DockingManager? manager)
        {
            if (manager == null) return;
            ApplyConsoleState(manager, ConsolePaneState.Collapsed);
        }

        /// <summary>
        /// 底部 pane 的状态落点：高度 + 记忆高度 + 广播状态。
        /// </summary>
        private static void ApplyConsoleState(DockingManager manager, ConsolePaneState state)
        {
            if (state == ConsolePaneState.Expanded) AttachConsolePane(manager);
            else DetachConsolePane(manager);

            if (CurrentConsoleState == state) return;
            CurrentConsoleState = state;
            ConsoleStateChanged?.Invoke(state);
        }

        /// <summary>收起时缓存下来的整块底部面板（null = 未收起）。</summary>
        private static LayoutAnchorablePane? _consolePaneCache;
        private static ILayoutGroup? _consolePaneParent;
        private static int _consolePaneIndex = -1;
        private static string? _consoleActivePanelId;

        /// <summary>
        /// 收起：把整块底部面板从停靠树里摘下来缓存。
        ///
        /// 为什么不是把 <c>DockHeight</c> 压到 1px：AvalonDock 的页签条有约 20px 最小高度，
        /// 1px 只会让它**画到面板外**（屏幕上就是抽屉条上方多出一条重复页签，2026-10-26 真机实测），
        /// 而 <c>LayoutAnchorablePane</c> 没有 <c>ShowHeader</c> 可以关掉它。
        /// 摘下来 = 屏幕上真的没有它，抽屉条才是唯一的一条。
        ///
        /// 为什么不用 <c>IsVisible=false</c>（Hidden 区）：恢复时库按"记忆容器"插回，
        /// 容器已不在树上时会退化成"找任意空 pane / 末尾新建一列"，控制台可能跑到别的列；
        /// 缓存整块 pane 则是**原父容器、原索引**插回，位置不会漂。
        /// </summary>
        private static void DetachConsolePane(DockingManager manager)
        {
            if (_consolePaneCache != null) return;   // 已经收起

            var pane = FindAncestorAnchorablePane(manager, LogPanelContentId);
            if (pane == null) return;

            _consoleActivePanelId = (pane.SelectedContent as LayoutContent)?.ContentId ?? LogPanelContentId;

            if (_savedLogPaneHeight == null)
            {
                // 伪值（1~29，多为上次收起写盘后被夹出来的）不能当"收起前高度"记下来
                _savedLogPaneHeight = pane.DockHeight.IsAbsolute && pane.DockHeight.Value < 30
                    ? new GridLength(240)
                    : pane.DockHeight;
            }

            // 只有父容器里还有别的子项时才摘：摘成空列会把整列宽度留给空白
            if (pane.Parent is ILayoutGroup parent && parent.ChildrenCount > 1)
            {
                _consolePaneParent = parent;
                _consolePaneIndex = parent.IndexOfChild(pane);
                parent.RemoveChild(pane);
                _consolePaneCache = pane;
                return;
            }

            // 兜底（父容器拿不到 / 是唯一子项）：退回压高度，至少不崩
            pane.DockHeight = new GridLength(1);
        }

        /// <summary>
        /// 展开：把缓存的 pane 按原父容器、原索引插回，再还原高度与用户收起前看的那一页。
        /// </summary>
        private static void AttachConsolePane(DockingManager manager)
        {
            var reattached = false;
            if (_consolePaneCache != null && _consolePaneParent != null)
            {
                var index = Math.Max(0, Math.Min(_consolePaneIndex, _consolePaneParent.ChildrenCount));
                _consolePaneParent.InsertChildAt(index, _consolePaneCache);
                _consolePaneCache = null;
                _consolePaneParent = null;
                _consolePaneIndex = -1;
                reattached = true;
            }

            var pane = FindAncestorAnchorablePane(manager, LogPanelContentId);
            if (pane == null) return;

            if (_savedLogPaneHeight != null)
            {
                pane.DockHeight = _savedLogPaneHeight.Value;
                _savedLogPaneHeight = null;
            }
            else if (pane.DockHeight.IsAbsolute && pane.DockHeight.Value < 30)
            {
                // 没有记忆但当前是"伪值"：回到默认展开高度。只治伪值，不碰用户合法拖窄（>=30）。
                pane.DockHeight = new GridLength(240);
            }

            // 只在"刚从收起态插回"这一次还原用户看的页；用完即清，
            // 否则之后每次模式收敛（都会走 Expanded）都会把用户刚切过去的页签拽回旧的。
            if (reattached && _consoleActivePanelId != null
                && FindPanel(manager, _consoleActivePanelId) is LayoutAnchorable active)
            {
                active.IsSelected = true;
                active.IsActive = true;
                _consoleActivePanelId = null;
            }
        }

        /// <summary>
        /// 底部面板组当前选中的 ContentId（抽屉条用它高亮"当前页"）。
        /// 只读：取不到就返回 null，调用方按"不知道"处理，不要猜成日志页。
        /// </summary>
        public static string? GetConsoleSelectedPanelId()
        {
            var manager = FindManager();
            if (manager == null) return null;

            var pane = FindAncestorAnchorablePane(manager, LogPanelContentId);
            return (pane?.SelectedContent as LayoutContent)?.ContentId;
        }

        /// <summary>
        /// 组态模式：整块底部面板摘出停靠树。语义 = <see cref="ConsolePaneState.Hidden"/>：
        /// 抽屉条也不显示，底部整块让给画布。
        /// </summary>
        private static void CollapseLogPane(DockingManager manager)
            => ApplyConsoleState(manager, ConsolePaneState.Hidden);

        /// <summary>
        /// 恢复底部日志 pane（离开组态 / 进入运行或自由）。语义 = <see cref="ConsolePaneState.Expanded"/>。
        /// </summary>
        private static void RestoreLogPane(DockingManager manager)
            => ApplyConsoleState(manager, ConsolePaneState.Expanded);

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
        /// 隐藏指定面板到 AvalonDock 的 Hidden 区（<c>IsVisible=false</c> → <c>Hide()</c>）。
        /// <para><b>这不是“自动隐藏/图钉”</b>：<c>IsAutoHidden</c> 只能由用户点图钉或
        /// <c>ToggleAutoHide()</c> 产生，Parent 是 <c>LayoutAnchorGroup</c>；本方法产生的是
        /// Parent=LayoutRoot 的 Hidden 条目。两者在 <see cref="IsPanelVisible"/> 里都算不可见，
        /// 但恢复路径不同（<see cref="ShowAnchorable"/> 分别走 ToggleAutoHide / Show）。</para>
        /// 文档选项卡无“收起”语义（只能切走），仅处理停靠面板。
        /// </summary>
        public static void HidePanel(string contentId) => HidePanel(FindManager(), contentId);

        /// <summary><see cref="HidePanel(string)"/> 的指定宿主重载（断言宿主用）</summary>
        public static void HidePanel(DockingManager? manager, string contentId)
        {
            if (FindPanel(manager, contentId) is LayoutAnchorable a && a.IsVisible)
                a.IsVisible = false; // 文档选项卡无"收起"语义（只能切走），仅处理停靠面板
        }

        /// <summary>
        /// 让「自定义胶囊」与 AvalonDock 自带的自动隐藏页签只出现一个。
        ///
        /// <para>图钉按下后，库会在右边缘生成一条 <see cref="LayoutAnchorControl"/>（33px 竖排页签）；
        /// 我们的胶囊轨道是叠在 DockingManager 上的 18px 覆盖层，两者会并排/互相遮挡，
        /// 现场表现就是「工具箱旁边还有一条被挡住的工具箱」。</para>
        ///
        /// <para>口径：<paramref name="capsuleIds"/> 里的面板已有自定义胶囊，库页签必须隐藏；
        /// 不在集合里的面板（例如流程导航）仍保留库页签，避免把别人的恢复入口一起藏掉。
        /// 只改可视层 <see cref="UIElement.Visibility"/>，不动 <c>IsAutoHidden</c>/布局模型，
        /// 因此 <see cref="ShowAnchorable"/> 的 ToggleAutoHide 收回路径不受影响。</para>
        /// </summary>
        public static void SyncAutoHideTabs(DockingManager? manager, IEnumerable<string>? capsuleIds)
        {
            if (manager == null) return;

            var wanted = capsuleIds == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(capsuleIds, StringComparer.Ordinal);

            foreach (var tab in FindVisualDescendants<LayoutAnchorControl>(manager))
            {
                if (tab.Model is not LayoutAnchorable anchorable) continue;
                var target = wanted.Contains(anchorable.ContentId) ? Visibility.Collapsed : Visibility.Visible;
                if (tab.Visibility != target) tab.Visibility = target;
            }
        }

        private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var sub in FindVisualDescendants<T>(child)) yield return sub;
            }
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

            // 控制台收起时整块面板不在停靠树里（见 DetachConsolePane），先展开再按 ContentId 查，
            // 否则视图菜单「控制台 / 监控栏」点下去会静默无反应。
            if (_consolePaneCache != null
                && (contentId == LogPanelContentId || contentId == MonitorPanelContentId))
            {
                ApplyConsoleState(manager, ConsolePaneState.Expanded);
            }

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

                // 落点错了就地迁回：检查器面板只能待在右侧检查器列，不许与流程导航共列
                // （2026-10-28 现场：Layout.xml 把工具箱停在左侧工作列，之后每次点胶囊都回那一列）。
                if (IsInspectorPanel(contentId))
                    EnsureInspectorPanelsOnRightColumn(manager);
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
            // 自动隐藏（图钉按下）是**第三种**状态：Parent 是 LayoutAnchorGroup，
            // IsAutoHidden=true、IsHidden=false，而 IsVisible 仍为 true（Parent 非 LayoutRoot）。
            // 所以这一段必须排在 IsVisible 早退**之前**；库的 Show() 只认 Hidden 区，
            // 对这种状态直接抛 InvalidOperationException。
            // 收回走 ToggleAutoHide() 的“收回停靠”分支——它按 group.PreviousContainer 把整组
            // 移回原 pane，并自己处理 PreviousContainer 为空的情况（新建 pane），
            // 不碰 Dock()/Show() 那条缺负索引判据的老路（R58 的 2026-10-16 真机闪退同族）。
            if (anchorable.IsAutoHidden)
            {
                var log = TryResolveLogService();
                try
                {
                    var group = anchorable.Parent as LayoutAnchorGroup;
                    Console.WriteLine($"[ShowPanel] {anchorable.ContentId} 自动隐藏收回前 groupParent={group?.Parent?.GetType().Name ?? "null"}");
                    log?.Warn($"[ShowPanel] {anchorable.ContentId} 自动隐藏收回前 groupParent={group?.Parent?.GetType().Name ?? "null"}");
                    anchorable.ToggleAutoHide();
                    Console.WriteLine($"[ShowPanel] {anchorable.ContentId} 自动隐藏收回后 auto={anchorable.IsAutoHidden} parent={anchorable.Parent?.GetType().Name ?? "null"}");
                    log?.Warn($"[ShowPanel] {anchorable.ContentId} 自动隐藏收回后 auto={anchorable.IsAutoHidden} parent={anchorable.Parent?.GetType().Name ?? "null"}");
                    if (!anchorable.IsAutoHidden) return;
                }
                catch (Exception ex)
                {
                    log?.Warn($"[ShowPanel] {anchorable.ContentId} 自动隐藏收回异常：{ex.GetType().Name} {ex.Message}");
                    Console.WriteLine($"面板 {anchorable.ContentId} 从自动隐藏区收回失败：{ex.Message}");
                }
            }

            if (anchorable.IsVisible) return;
            // 既不可见也不隐藏 = Parent 为 null（已从布局上摘干净）：Show() 这一态直接抛 InvalidOperationException
            if (!anchorable.IsHidden) return;

            var remembered = ReadPreviousContainer(anchorable);
            var rememberedIndex = ReadPreviousContainerIndex(anchorable);

            // 记忆判废（索引为负、那一列已不在当前树上、或压根不是 LayoutAnchorablePane）
            // 就不许让库自己插。最后一条是 2026-10-26 真机布局实测补的：工具箱的 PreviousContainerId
            // 指向中央 LayoutDocumentPane（旧“拖成文档”现场留下的记忆），索引 2 有效、容器也在树上，
            // 旧判据会放行；库的 Show() 会真的把 LayoutAnchorable 插进文档 pane（实测不抛异常）。
            // 索引反射不到（库版本改了内部属性）时 rememberedIndex 为 null，算“不知道”：让库自己试，抛了照样走修复。
            bool rememberedIsAnchorablePane = remembered is LayoutAnchorablePane;
            bool memoryUnusable = rememberedIndex < 0
                || (remembered != null && !IsAttachedTo(remembered, manager.Layout))
                || (remembered != null && !rememberedIsAnchorablePane);

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
            // 落点只认 LayoutAnchorablePane：remembered 可能是 LayoutDocumentPane（旧现场）或
            // LayoutAnchorGroup（自动隐藏组），把 anchorable 塞进这两种容器都不是“回到右侧列”。
            // 另外：检查器面板的记忆列若是左侧工作列（流程导航那一列），必须判废——
            // 否则"工具箱与流程栏挤在同一个 pane"会被一次次原样恢复并持久化（2026-10-28 现场）。
            var processColumn = FindProcessColumnPane(manager);
            bool Acceptable(LayoutAnchorablePane? pane)
                => pane != null
                   && IsAttachedTo(pane, root)
                   && !(IsInspectorPanel(anchorable.ContentId) && ReferenceEquals(pane, processColumn));

            var rememberedGroup = remembered as LayoutAnchorablePane;
            var usableRemembered = Acceptable(rememberedGroup) ? rememberedGroup : null;
            var emptyRemembered = usableRemembered != null && usableRemembered.ChildrenCount == 0 ? usableRemembered : null;
            var anyEmpty = root.Descendents()
                .OfType<LayoutAnchorablePane>()
                .FirstOrDefault(p => p.ChildrenCount == 0 && Acceptable(p));
            var home = emptyRemembered ?? anyEmpty ?? usableRemembered;

            if (home != null)
            {
                home.InsertChildAt(home.ChildrenCount, anchorable);
                anchorable.IsSelected = true;
                anchorable.IsActive = true;
                return;
            }

            if (root.RootPanel == null) return;
            var newPane = new LayoutAnchorablePane(anchorable) { DockWidth = new GridLength(DefaultPaneWidthFor(anchorable.ContentId)) };
            root.RootPanel.InsertChildAt(root.RootPanel.ChildrenCount, newPane);
            anchorable.IsSelected = true;
            anchorable.IsActive = true;
            Console.WriteLine($"面板 {anchorable.ContentId} 找不到可用的列，已在根面板末尾新建一列承载");
        }

        /// <summary>
        /// 兜底新建 pane 的默认宽度：检查器 300、流程导航 340、其余 260。
        /// 旧实现一律 260，右侧检查器从胶囊/隐藏区回来时会比设计窄 40px（且不会被 320→300 的兜底纠正）。
        /// </summary>
        private static double DefaultPaneWidthFor(string? contentId)
        {
            if (!string.IsNullOrEmpty(contentId) && InspectorPanelIds.Contains(contentId, StringComparer.Ordinal))
                return 300;
            if (string.Equals(contentId, ProcessViewContentId, StringComparison.Ordinal))
                return DefaultProcessColumnWidth;
            return 260;
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

                // 自动隐藏（Parent=LayoutAnchorGroup）时 IsVisible 仍为 true，但它不是“看得见”。
                // 关键面板的口径是“必须常驻可见”，所以这里同时排除 IsAutoHidden；
                // ShowAnchorable 会走 ToggleAutoHide 把它收回原 pane。
                if (anchorable.IsVisible && !anchorable.IsAutoHidden) continue;

                ShowAnchorable(manager, anchorable);
                var restored = anchorable.IsVisible && !anchorable.IsAutoHidden;
                Console.WriteLine($"关键面板 {anchorable.ContentId} 从 Hidden/自动隐藏区强制恢复可见（{(restored ? "成功" : "失败：仍未显示")}）");
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
        /// 右侧检查器列宽兜底：v2 默认 320 是 ≥1500px 视口的值，当前窗口约 1310，
        /// 按设计稿 ≤1500 规则用 300。只纠正"恰好 320"的旧默认；用户自己拖过的
        /// 其它宽度（例如 280/340）保持不动，避免把用户布局改坏。
        /// </summary>
        internal static void EnsureInspectorColumnWidth(DockingManager manager)
        {
            try
            {
                // 检查器可能被旧布局/自动隐藏拆成多列（真机 Layout.xml：属性一列 300、工具箱一列 304）。
                // 每一列都要归一化，不能只修“第一个可见面板”的那一列。
                var panes = new HashSet<LayoutAnchorablePane>();
                foreach (var contentId in InspectorPanelIds)
                {
                    if (FindPanel(manager, contentId) is not LayoutAnchorable anchorable) continue;

                    if (anchorable.Parent is LayoutAnchorablePane current && IsAttachedTo(current, manager.Layout))
                        panes.Add(current);
                    else if (ReadPreviousContainer(anchorable) is LayoutAnchorablePane previous && IsAttachedTo(previous, manager.Layout))
                        panes.Add(previous);
                }

                foreach (var pane in panes)
                {
                    // 最小宽度也拉回设计值：旧布局可能是 320（v2 的 ≥1500 值）。
                    pane.DockMinWidth = 300;

                    // 只纠正已知的漂移值，不碰用户自己拖出的 280/340：
                    // 320 = v2 旧默认；304 = 自动隐藏组收回时留下的列宽（真机 Layout.xml 实测）；
                    // 260 = AnchorToHomePane 旧兜底新建的列宽。
                    if (pane.DockWidth.IsAbsolute
                        && (Math.Abs(pane.DockWidth.Value - 320) < 0.1
                            || Math.Abs(pane.DockWidth.Value - 304) < 0.1
                            || Math.Abs(pane.DockWidth.Value - 260) < 0.1))
                    {
                        var oldWidth = pane.DockWidth.Value;
                        pane.DockWidth = new GridLength(300);
                        Console.WriteLine($"右侧检查器列宽 {oldWidth:0.#}（已知漂移值）已按 ≤1500 规则拉回 300");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"右侧检查器列宽校正失败（不影响启动）：{ex.Message}");
            }
        }

        /// <summary>
        /// 右侧检查器的 4 个面板：属性 / 图层 / 工具箱 / 图元工具箱。
        /// 它们只能待在右侧 LayoutAnchorablePane，不能被拖成中央文档。
        /// </summary>
        private static readonly string[] InspectorPanelIds =
        {
            "Panel_ScadaPropertyView",
            "Panel_ScadaLayerView",
            "Panel_ToolView",
            "Panel_ScadaToolboxView",
        };

        /// <summary>
        /// 把被旧布局拖成 LayoutDocument 的检查器面板迁回右侧 LayoutAnchorablePane。
        ///
        /// 现场（2026-10-26）：工具箱被双击/拖到中央文档区，变成"视觉图像 / 流程画布 / 工具箱"的文档标签；
        /// 点胶囊钉回时找不到 anchorable，右侧永远是空的。新布局已用 CanDockAsTabbedDocument=False 拦住；
        /// 这条负责救治已经落盘的旧现场（不升结构版本、不丢其它面板位置）。
        /// </summary>
        private static void EnsureInspectorPanelsAreAnchorables(DockingManager manager)
        {
            try
            {
                foreach (var contentId in InspectorPanelIds)
                {
                    var document = EnumerateContents(manager)
                        .OfType<LayoutDocument>()
                        .FirstOrDefault(d => string.Equals(d.ContentId, contentId, StringComparison.Ordinal));
                    if (document == null) continue;

                    // 落点优先"不是左侧工作列的空列"：旧布局里左列常因流程导航被收起而空着，
                    // 按"第一个空列"挑会把检查器面板搬进流程栏那一列（2026-10-28 现场）。
                    var processColumn = FindProcessColumnPane(manager);
                    var rightPane = manager.Layout.Descendents()
                                        .OfType<LayoutAnchorablePane>()
                                        .FirstOrDefault(p => p.ChildrenCount == 0
                                                             && !ReferenceEquals(p, processColumn)
                                                             && IsAttachedTo(p, manager.Layout))
                                    ?? FindInspectorColumnPane(manager, processColumn)
                                    ?? CreateInspectorPane(manager);
                    if (rightPane == null) continue;

                    var content = document.Content;
                    document.Content = null;
                    if (document.Parent is ILayoutContainer parent)
                        parent.RemoveChild(document);

                    var anchorable = new LayoutAnchorable
                    {
                        Title = document.Title,
                        ContentId = contentId,
                        Content = content,
                        CanClose = false,
                        CanHide = true,
                        CanDockAsTabbedDocument = false,
                        AutoHideWidth = 300,
                    };
                    rightPane.Children.Add(anchorable);
                    anchorable.IsSelected = true;
                    anchorable.IsActive = true;
                    Console.WriteLine($"检查器面板 {contentId} 曾以文档形式停靠，已迁回右侧 LayoutAnchorablePane");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查器面板归位失败（不影响启动）：{ex.Message}");
            }
        }

        /// <summary>
        /// 布局恢复后的面板策略兜底。
        ///
        /// <para>为什么必须有：<c>XmlLayoutSerializer.Deserialize</c> 会**新建** <c>LayoutAnchorable</c>，
        /// 不是复用 XAML 里那个实例；XML 里没写的属性一律回落库默认值：
        /// <c>CanDockAsTabbedDocument=true</c>、<c>AutoHideWidth=0</c>、<c>CanFloat=true</c>。
        /// 旧 <c>Layout.xml</c> 没有这些新属性 → 恢复后“检查器页签不能再拖成文档 / 自动隐藏浮层 300 /
        /// 不许浮动”三条守卫全部丢失（2026-10-26 真机布局实测：工具箱 XML 里两项都没有）。</para>
        ///
        /// <para>策略只作用于“有明确归属”的面板：检查器 4 个固定右侧列、流程导航固定左侧列、
        /// 底部控制台/监控栏固定底部抽屉。中央文档仍保留可拆分/浮动（设计基线明确允许）。</para>
        /// </summary>
        private static void ApplyPanelPolicies(DockingManager manager)
        {
            foreach (var contentId in InspectorPanelIds)
            {
                if (FindPanel(manager, contentId) is not LayoutAnchorable anchorable) continue;
                anchorable.CanClose = false;
                anchorable.CanHide = true;
                anchorable.CanDockAsTabbedDocument = false;
                anchorable.CanFloat = false;
                anchorable.AutoHideWidth = 300;
            }

            if (FindPanel(manager, ProcessViewContentId) is LayoutAnchorable process)
            {
                process.CanClose = false;
                process.CanHide = true;
                process.CanDockAsTabbedDocument = false;
                process.CanFloat = false;
            }

            foreach (var id in new[] { LogPanelContentId, MonitorPanelContentId })
            {
                if (FindPanel(manager, id) is not LayoutAnchorable bottom) continue;
                bottom.CanClose = false;
                bottom.CanHide = false;
                bottom.CanFloat = false;
            }

            foreach (var (id, _) in CoreDocuments)
            {
                if (FindPanel(manager, id) is LayoutDocument document)
                    document.CanClose = false;
            }
        }

        /// <summary>
        /// 左侧工作列（流程导航的设计归属列）。判据三档，都要求容器仍挂在当前布局树上：
        /// ① 流程导航当前停靠的那个 <see cref="LayoutAnchorablePane"/>；
        /// ② 根面板**第一个子项**是 anchorable pane（本仓默认布局：左栏 → 文档区 → 检查器列）；
        /// ③ 都不成立返回 null —— 布局被用户改成别的形状时不猜，宁可不迁也不乱迁。
        /// </summary>
        private static LayoutAnchorablePane? FindProcessColumnPane(DockingManager manager)
        {
            if (FindPanel(manager, ProcessViewContentId) is LayoutAnchorable process
                && process.Parent is LayoutAnchorablePane current
                && IsAttachedTo(current, manager.Layout))
            {
                return current;
            }

            if (manager.Layout.RootPanel?.Children.FirstOrDefault() is LayoutAnchorablePane first
                && IsAttachedTo(first, manager.Layout))
            {
                return first;
            }

            return null;
        }

        private static bool IsInspectorPanel(string? contentId)
            => !string.IsNullOrEmpty(contentId) && InspectorPanelIds.Contains(contentId, StringComparer.Ordinal);

        /// <summary>
        /// 右侧检查器列（属性/图层/工具箱/图元工具箱的家）。
        /// ① 其它检查器面板当前或记忆所在的列（按 <see cref="InspectorPanelIds"/> 顺序，属性优先）；
        /// ② 根面板里最后一个空列（<c>Hide()</c> 不摘 pane，检查器列被掏空后仍是落点）。
        /// <paramref name="excluded"/> 用来排除左侧工作列。
        /// </summary>
        private static LayoutAnchorablePane? FindInspectorColumnPane(DockingManager manager, LayoutAnchorablePane? excluded)
        {
            foreach (var contentId in InspectorPanelIds)
            {
                if (FindPanel(manager, contentId) is not LayoutAnchorable anchorable) continue;

                var pane = anchorable.Parent as LayoutAnchorablePane
                           ?? ReadPreviousContainer(anchorable) as LayoutAnchorablePane;
                if (pane != null && !ReferenceEquals(pane, excluded) && IsAttachedTo(pane, manager.Layout))
                    return pane;
            }

            return manager.Layout.RootPanel?.Children
                .OfType<LayoutAnchorablePane>()
                .LastOrDefault(p => p.ChildrenCount == 0
                                    && !ReferenceEquals(p, excluded)
                                    && IsAttachedTo(p, manager.Layout));
        }

        /// <summary>
        /// 检查器面板不得停靠在左侧工作列（与流程导航同一个 <see cref="LayoutAnchorablePane"/>）。
        ///
        /// <para>为什么必须有：列宽有 <see cref="EnsureInspectorColumnWidth"/>、可见性有
        /// <see cref="ApplyScadaPanelMode"/>，唯独"落在哪一列"没有任何约束。工具箱一旦被拖到/收回
        /// 左列，<c>Layout.xml</c> 会原样持久化；下次启动恢复布局、点胶囊走
        /// <c>ToggleAutoHide()</c> 收回原 pane，于是"流程栏 + 工具箱"变成同一容器里的两个标签，
        /// 而且每启动一次就复现一次（2026-10-28 现场）。</para>
        ///
        /// <para>只动归属、不动可见性与宽度；迁完把活动内容还给检查器列。返回迁移条数（断言宿主用）。</para>
        /// </summary>
        internal static int EnsureInspectorPanelsOnRightColumn(DockingManager? manager)
        {
            if (manager == null) return 0;

            try
            {
                var processColumn = FindProcessColumnPane(manager);
                if (processColumn == null) return 0;

                var misplaced = InspectorPanelIds
                    .Select(id => FindPanel(manager, id) as LayoutAnchorable)
                    .OfType<LayoutAnchorable>()
                    .Where(a => a.Parent is LayoutAnchorablePane pane && ReferenceEquals(pane, processColumn))
                    .ToList();
                if (misplaced.Count == 0) return 0;

                var home = FindInspectorColumnPane(manager, processColumn) ?? CreateInspectorPane(manager);
                if (home == null || ReferenceEquals(home, processColumn)) return 0;

                var moved = 0;
                foreach (var anchorable in misplaced)
                {
                    if (anchorable.Parent is not LayoutAnchorablePane source) continue;

                    source.Children.Remove(anchorable);
                    home.Children.Add(anchorable);
                    moved++;
                    Console.WriteLine($"检查器面板 {anchorable.ContentId} 曾停靠在左侧工作列，已迁回右侧检查器列");
                }

                if (moved > 0)
                {
                    // 被迁的都是停靠中的可见面板；活动内容留在被掏空的那一列会让胶囊/焦点状态分叉
                    misplaced[^1].IsSelected = true;
                    misplaced[^1].IsActive = true;
                }

                return moved;
            }
            catch (Exception ex)
            {
                // 布局修复失败不许带崩启动（App 没有全局异常兜底）
                Console.WriteLine($"检查器面板列归属修复失败（不影响启动）：{ex.Message}");
                return 0;
            }
        }

        private static LayoutAnchorablePane? CreateInspectorPane(DockingManager manager)
        {
            if (manager.Layout.RootPanel == null) return null;

            var pane = new LayoutAnchorablePane
            {
                DockWidth = new GridLength(300),
                DockMinWidth = 300,
            };
            manager.Layout.RootPanel.InsertChildAt(manager.Layout.RootPanel.ChildrenCount, pane);
            return pane;
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

            // 自动隐藏区（图钉按下）不在 RootPanel、也不在 Hidden：Parent 是 LayoutAnchorGroup，
            // 挂在 LayoutRoot.{Left,Right,Top,Bottom}Side 上。漏掉这四棵子树 → FindPanel 返回 null，
            // 胶囊点下去 ShowPanel 直接 return（2026-10-26 真机"点胶囊没反应"的根因）。
            foreach (var side in new[] { manager.Layout.LeftSide, manager.Layout.RightSide, manager.Layout.TopSide, manager.Layout.BottomSide })
            {
                if (side == null) continue;
                foreach (var c in Walk(side))
                    yield return c;
            }
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

                case LayoutAnchorGroup anchorGroup: // 自动隐藏组：Children 是 LayoutAnchorable
                    foreach (var c in anchorGroup.Children.SelectMany(Walk))
                        yield return c;
                    break;

                case LayoutAnchorSide anchorSide: // 自动隐藏区的一侧：Children 是 LayoutAnchorGroup
                    foreach (var c in anchorSide.Children.SelectMany(Walk))
                        yield return c;
                    break;
            }
        }

        /// <summary>
        /// 取“与指定面板同处一个自动隐藏组”的其它面板 ContentId。
        ///
        /// <para>AvalonDock 的 <c>ToggleAutoHide()</c> 是 **pane 级**：图钉收起时会把整列所有标签
        /// 移进同一个 <c>LayoutAnchorGroup</c>，收回时也整组搬回原 pane。胶囊是 **panel 级**，
        /// 点一枚后必须把同组其它面板再收回去，否则组态模式点“属性”会把本应隐藏的“工具箱”
        /// 一起带回来（2026-10-26 评审结论）。</para>
        /// </summary>
        public static IReadOnlyList<string> GetAutoHiddenGroupSiblingIds(string contentId)
            => GetAutoHiddenGroupSiblingIds(FindManager(), contentId);

        /// <summary><see cref="GetAutoHiddenGroupSiblingIds(string)"/> 的指定宿主重载（断言宿主用）。</summary>
        internal static IReadOnlyList<string> GetAutoHiddenGroupSiblingIds(DockingManager? manager, string contentId)
        {
            if (manager == null) return Array.Empty<string>();
            if (FindPanel(manager, contentId) is not LayoutAnchorable anchorable) return Array.Empty<string>();
            if (anchorable.Parent is not LayoutAnchorGroup group) return Array.Empty<string>();

            var ids = new List<string>();
            foreach (var child in group.Children)
            {
                var id = child.ContentId;
                if (string.IsNullOrEmpty(id) || string.Equals(id, contentId, StringComparison.Ordinal)) continue;
                ids.Add(id);
            }
            return ids;
        }

        /// <summary>排障留痕：断言宿主没有 Prism 容器时返回 null（日志绝不能让布局操作失败）。</summary>
        private static ILogService? TryResolveLogService()
        {
            try { return Prism.Ioc.ContainerLocator.Container.Resolve<ILogService>(); }
            catch { return null; }
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
