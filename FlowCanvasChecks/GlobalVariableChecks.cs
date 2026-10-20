using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Interfaces.Io;
using VisionMaster.Models;
using VisionMaster.ViewModels.DialogViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 变量管理（GlobalVariableView / GlobalVariableManagerViewModel）的断言。
    ///
    /// 为什么值得单独钉住
    /// ---------
    /// 两个都在界面上"看着正常"、实际不能用：
    ///  ① **数组元素行不跟随父级过滤**：切到"网络变量"（或输入搜索词）时，本地数组变量的
    ///     [0]/[1] 会作为"没有变量名、没有来源"的残行留在表里 —— 用户看不懂那是什么，
    ///     也没有任何入口能操作它；
    ///  ② **数组编辑入口是死的**：它走 EasyDialog.ShowPropertyGridSync，而 UI 库的 PropertyGrid
    ///     是反射式生成器体系，生成器里没有"集合"这一类，于是承载对象上的
    ///     ObservableCollection 根本不被渲染 —— 弹窗里只有一行只读的"元素数"，
    ///     新建数组（长度 0）后既看不到元素也没地方加。
    ///
    /// 这两条都只有真跑一遍逻辑才看得出来，所以这里直接驱动纯函数与模型节点（不碰 WPF 控件）。
    /// </summary>
    internal static class GlobalVariableChecks
    {
        public static void Run()
        {
            RunFilterContract();
            RunSystemSourceContract();
            RunElementEditContract();
            RunArrayBuildContract();
            RunDetailColumnWidthContract();
            RunCurrentValueEditContract();
        }

        // ==================================================================
        //  ① 明细表筛选：子节点必须跟随父级
        // ==================================================================

        private static void RunFilterContract()
        {
            Section("[G] 变量管理：明细筛选（来源 / 搜索 / 数组子行跟随父级）");

            // 本地数组变量 Barcode（2 个元素）+ 一条连接上的网络变量 Test
            var barcode = Root("Barcode", isNetwork: false, sourceLabel: "本地", description: "条码结果");
            barcode.Children.Add(Child(barcode, "[0]", "SN-A"));
            barcode.Children.Add(Child(barcode, "[1]", "SN-B"));

            var test = Root("Test", isNetwork: true, sourceLabel: "Conn_01", description: "测试点位", address: "x=3;0");

            var displayNodes = new List<VariableNode> { barcode, barcode.Children[0], barcode.Children[1], test };

            var local = GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Local", "");
            Check("选中「本地变量」：只剩本地根节点 + 它的数组子行",
                Names(local) == "Barcode,[0],[1]",
                "实际：" + Names(local));

            var network = GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Conn_01", "");
            Check("★选中「网络变量」：数组子行不许漏出来（旧实现在这里漏 2 行无主 [0]/[1]）",
                Names(network) == "Test",
                "实际：" + Names(network));

            var searched = GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Local", "Test");
            Check("搜索命中的是网络变量时，本地根与它的子行一起消失",
                Names(searched) == "",
                "实际：" + Names(searched));

            var searchHitParent = GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Local", "条码");
            Check("搜索命中数组根（按描述）时，子行跟着一起留下",
                Names(searchHitParent) == "Barcode,[0],[1]",
                "实际：" + Names(searchHitParent));

            var searchByAddress = GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Conn_01", "x=3");
            Check("搜索按地址命中网络变量",
                Names(searchByAddress) == "Test",
                "实际：" + Names(searchByAddress));

            var mismatchedSource = GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Conn_99", "");
            Check("选中不存在的连接：什么都不显示（也不许漏子行）",
                Names(mismatchedSource) == "",
                "实际：" + Names(mismatchedSource));
        }

        // ==================================================================
        //  ①b 来源树：系统变量（运动 / IO）必须是只读分区
        // ==================================================================

        /// <summary>
        /// 系统变量与本地变量同住 <c>Workspace.GlobalVariables</c>，只靠"来源树 + 筛选"分层。
        ///
        /// 三条判据各对应一个真实后果：
        /// ① 不单独分组 → 它们混在"本地变量"里，用户以为可以改初始值/删除；
        /// ② 删除没有守卫 → 删掉后宿主下一次 Rebuild 又把它加回来（"删了又活"），
        ///    而删的那一刻引用它的画面/流程已经断链；
        /// ③ 初始值仍可编辑 → 写了也不回灌设备，纯假象。
        /// </summary>
        private static void RunSystemSourceContract()
        {
            Section("[G] 变量管理：系统变量只读分区");

            var local = Root("Barcode", isNetwork: false, sourceLabel: "本地");
            local.DataType = typeof(string);   // Root() 默认造数组节点；这里要标量才能验"初始值可编辑"
            local.OriginalModel = new LocalVariableModel
            {
                VariableId = Guid.NewGuid(),
                Name = "Barcode",
                DataType = typeof(string)
            };

            var system = Root("System.Motion.X.Position", isNetwork: false, sourceLabel: "本地");
            system.DataType = typeof(double);   // 标量：让"不可编辑"只能由系统身份解释，而不是数组类型
            system.OriginalModel = new SystemVariableModel(
                Guid.NewGuid(), "System.Motion.X.Position", typeof(double), "轴 X 位置（只读）");

            // 第二个系统变量：IO 接入层下线后，IO 点位就是普通网络变量，
            // 系统只读分区的第二个样本改用另一类系统变量（相机状态），
            // 保证"多个系统变量都进系统分区"这条仍然被覆盖。
            var system2 = Root("System.Camera.Cam0.Online", isNetwork: false, sourceLabel: "本地");
            system2.DataType = typeof(bool);    // 同上：标量，排除"数组不可编辑"的干扰
            system2.OriginalModel = new SystemVariableModel(
                Guid.NewGuid(), "System.Camera.Cam0.Online", typeof(bool), "相机在线（只读）");

            var displayNodes = new List<VariableNode> { local, system, system2 };

            Check("「本地变量」不再包含系统变量",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(
                    displayNodes, "Local", "")) == "Barcode",
                "实际：" + Names(GlobalVariableManagerViewModel.FilterDisplayNodes(displayNodes, "Local", "")));

            Check("「系统变量」包含全部系统变量（两个都在）",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(
                    displayNodes, GlobalVariableManagerViewModel.SystemSourceKey, ""))
                == "System.Motion.X.Position,System.Camera.Cam0.Online",
                "实际：" + Names(GlobalVariableManagerViewModel.FilterDisplayNodes(
                    displayNodes, GlobalVariableManagerViewModel.SystemSourceKey, "")));

            Check("系统变量节点标记为系统维护（删除/改名据此拦）", system.IsSystemOwned,
                system.OriginalModel?.VariableType.ToString() ?? "(无模型)");
            Check("第二个系统变量节点同样标记为系统维护", system2.IsSystemOwned,
                system2.OriginalModel?.VariableType.ToString() ?? "(无模型)");
            Check("本地变量节点不被误判为系统维护", !local.IsSystemOwned,
                local.OriginalModel?.VariableType.ToString() ?? "(无模型)");

            Check("系统变量的「初始值」不可编辑（写了也不会回灌设备）", !system.IsEditableNode,
                $"IsEditableNode={system.IsEditableNode}");
            Check("第二个系统变量的「初始值」不可编辑", !system2.IsEditableNode,
                $"IsEditableNode={system2.IsEditableNode}");
            Check("本地变量的「初始值」仍可编辑（没有误伤）", local.IsEditableNode,
                $"IsEditableNode={local.IsEditableNode}");

            Check("系统变量的「当前值」只读（走 IVariableDisplaySource）", system.IsValueReadOnly,
                $"IsValueReadOnly={system.IsValueReadOnly}");
            Check("第二个系统变量的「当前值」只读", system2.IsValueReadOnly,
                $"IsValueReadOnly={system2.IsValueReadOnly}");
        }

        // ==================================================================
        //  ② 数组元素就地编辑（VariableNode.ChildDefaultValueText）
        // ==================================================================

        private static void RunElementEditContract()
        {
            Section("[G] 数组元素就地编辑：写回父数组 / 类型校验 / 留空用默认值");

            var model = new LocalVariableModel
            {
                Name = "Barcode",
                DataType = typeof(string[]),
                DefaultValue = new[] { "A", "B" },
                Value = new[] { "A", "B" },
            };

            var element = ChildOf(model, index: 1, elementType: typeof(string), current: "B");
            element.ChildDefaultValueText = "C";

            var def = (string[])model.DefaultValue;
            var cur = (string[])model.Value;
            Check("改元素 → 初始值与当前值同步写下标",
                def[1] == "C" && cur[1] == "C" && def[0] == "A",
                $"DefaultValue=[{string.Join(",", def)}] Value=[{string.Join(",", cur)}]");
            Check("节点自身回显同步更新", element.ChildDefaultValue?.ToString() == "C",
                "ChildDefaultValue=" + element.ChildDefaultValue);

            // 类型不符：必须抛（绑定引擎据此标红），且**模型不被污染**
            var intModel = new LocalVariableModel
            {
                Name = "Numbers",
                DataType = typeof(int[]),
                DefaultValue = new[] { 1, 2 },
                Value = new[] { 1, 2 },
            };
            var intElement = ChildOf(intModel, index: 0, elementType: typeof(int), current: 1);

            bool threw = false;
            try { intElement.ChildDefaultValueText = "abc"; }
            catch (ArgumentException) { threw = true; }

            var intDef = (int[])intModel.DefaultValue;
            Check("非法值抛异常、且模型保持原值（不污染）",
                threw && intDef[0] == 1, $"抛异常={threw} 模型[0]={intDef[0]}");

            // 留空 = 用该类型的默认值
            intElement.ChildDefaultValueText = "";
            Check("int 元素留空 → 写入 0", ((int[])intModel.DefaultValue)[0] == 0,
                "模型[0]=" + ((int[])intModel.DefaultValue)[0]);

            var stringModel = new LocalVariableModel
            {
                Name = "Texts",
                DataType = typeof(string[]),
                DefaultValue = new[] { "x" },
                Value = new[] { "x" },
            };
            var stringElement = ChildOf(stringModel, index: 0, elementType: typeof(string), current: "x");
            stringElement.ChildDefaultValueText = "";
            Check("string 元素留空 → 写入 null（引用类型没有可实例化的默认值）",
                ((string?[])stringModel.DefaultValue)[0] == null, "");

            // 非元素节点（ElementIndex = -1）不得误写
            var stray = new VariableNode
            {
                Name = "stray",
                DataType = typeof(string),
                OriginalModel = model,
                Level = 1,
                ChildDefaultValue = "keep",
            };
            stray.ChildDefaultValueText = "changed";
            Check("ElementIndex 未设（-1）时不写任何东西（防止误改父数组）",
                ((string[])model.DefaultValue)[1] == "C", "模型[1]=" + ((string[])model.DefaultValue)[1]);
        }

        // ==================================================================
        //  ③ 数组文本 → 数组（弹窗确定时的转换）
        // ==================================================================

        private static void RunArrayBuildContract()
        {
            Section("[G] 数组编辑：文本转数组（留空用默认值 / 非法值明确失败）");

            // 注意入参是**元素类型**（typeof(int)），不是数组类型（typeof(int[])）
            var ints = GlobalVariableManagerViewModel.TryBuildArray(typeof(int), new[] { "1", "2", "" }, out string e1);
            Check("int[]：正常值写入、空串按 0 处理",
                ints != null && string.Join(",", (int[])ints) == "1,2,0",
                ints == null ? "失败：" + e1 : string.Join(",", (int[])ints));

            var doubles = GlobalVariableManagerViewModel.TryBuildArray(typeof(double), new[] { "1.5", "2" }, out string e2);
            Check("double[]：按元素类型转换（不是按字符串拼）",
                doubles != null && string.Join(",", (double[])doubles) == "1.5,2",
                doubles == null ? "失败：" + e2 : string.Join(",", (double[])doubles));

            var bad = GlobalVariableManagerViewModel.TryBuildArray(typeof(int), new[] { "abc" }, out string e3);
            Check("int[]：非法元素整体失败并给出原因（不半途写入）",
                bad == null && !string.IsNullOrWhiteSpace(e3), "error=" + e3);

            var empty = GlobalVariableManagerViewModel.TryBuildArray(typeof(string), Array.Empty<string>(), out string e4);
            Check("空列表 → 长度 0 的数组（新建数组的初始形态，必须能建成）",
                empty != null && ((string?[])empty).Length == 0, empty == null ? "失败：" + e4 : "");

            var texts = GlobalVariableManagerViewModel.TryBuildArray(typeof(string), new[] { "a", "", "c" }, out string e5);
            Check("string[]：空串保留为 null 元素（而不是被丢弃，长度必须与界面一致）",
                texts != null && ((string?[])texts).Length == 3 && ((string?[])texts)[1] == null,
                texts == null ? "失败：" + e5 : string.Join(",", ((string?[])texts).Select(s => s ?? "<null>")));

            var misuse = GlobalVariableManagerViewModel.TryBuildArray(typeof(int[]), new[] { "1" }, out string e6);
            Check("误传数组类型时明确失败（而不是抛一句指不到问题所在的 InvalidCastException）",
                misuse == null && e6.Contains("元素类型"), "error=" + e6);
        }

        // ==================================================================
        //  ④ 明细表列宽契约：固定列不许吃光右栏视口（"当前值"列消失事故）
        // ==================================================================
        //
        // 【事故】2026-10-11「变量明细显示不全」：8 列固定宽合计 750，而默认窗口
        // 1240 下右栏视口只有 ~660 —— 星号"当前值"列被压成 0 宽整列消失，
        // 类型/初始值单元格被硬裁成 "St"/"Product_"，横向滚动条还是 Disabled
        // （超宽部分静默裁掉，连滚动兜底都没有）。
        //
        // 【怎么判】静态扫 GlobalVariableView.xaml（剥注释）：
        //  ① 横向滚动条必须是 Auto（Disabled = 裁掉最右列且不可达）；
        //  ② 星号列必须存在且带 MinWidth（宁可出滚动条也不许变 0 宽）；
        //  ③ 本地来源下的固定列合计 + 星号下限 ≤ 最小窗口的右栏视口
        //    （本地来源下"来源/扫描组/地址"三列被代码后置整列收起，不计入）；
        //  ④ 代码后置的收放线仍在（IsNetworkSource + 三个列名），防静默删线。
        //
        // 右栏视口 = 窗口宽 −(232+12) 两栏结构 −(16×2) 外边距 = 宽 −276（2026-10-20 Web v1）。
        // 同时断言本地（424+160）与网络（736+160）两档——网络来源是最紧的一档。
        // 2026-10-20 用户按 1200 窄档定稿：220/104/120/104/88/*160/100（早期 280/88/100 已废弃）。

        private static readonly string[] DetailCollapsedInLocalSource =
            { "SourceColumn", "ScanGroupColumn", "AddressColumn" };

        private static void RunDetailColumnWidthContract()
        {
            Section("[G] 变量管理：明细表列宽契约（星号列不许被固定列挤没）");

            var xamlPath = FindRepoFile(@"VisionMaster\Views\DialogViews\GlobalVariableView.xaml");
            if (xamlPath == null)
            {
                Check("【列宽】GlobalVariableView.xaml 定位", false, "定位不到视图文件（不允许跳过）");
                return;
            }

            string text = Regex.Replace(File.ReadAllText(xamlPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            var columnTags = Regex.Matches(text, @"<DataGrid(?:Template|Text|CheckBox)Column\b[^>]*?>")
                .Select(m => m.Value)
                .ToList();

            Check("【列宽】DataGrid 横向滚动条 = Auto（Disabled 会静默裁掉超宽部分）",
                Regex.IsMatch(text, @"<DataGrid\b[^>]*?HorizontalScrollBarVisibility=""Auto"""),
                "");

            var starTag = columnTags.FirstOrDefault(t => Regex.IsMatch(t, @"Width=""\*"""));
            var starMin = starTag == null
                ? 0
                : double.TryParse(Regex.Match(starTag, @"MinWidth=""(\d+(?:\.\d+)?)""").Groups[1].Value, out var sm) ? sm : 0;
            Check("【列宽】星号列（当前值）存在且 MinWidth ≥ 160", starTag != null && starMin >= 160,
                starTag == null ? "没找到 Width=\"*\" 的列" : $"MinWidth={starMin}");

            // 2026-10-20 用户按 1200 窄档定稿：220/104/120/104/88/*/100。
            // 只判"合计 ≤ 视口"会让任意列被改窄也照样绿（列宽回归就是这么漏掉的），必须逐列钉住。
            string[] expectedWidths = { "220", "104", "120", "104", "88", "*", "100" };
            string[] expectedMinWidths = { "220", "104", "120", "104", "88", "160", "100" };
            var actualWidths = columnTags
                .Select(t => Regex.Match(t, @"[\s""]Width=""([^""]+)"""))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .ToList();
            var actualMinWidths = columnTags
                .Select(t => Regex.Match(t, @"[\s""]MinWidth=""([^""]+)"""))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .ToList();
            Check("【列宽】逐列宽度与 2026-10-20 定稿一致（变量名/类型/来源/扫描组/地址/当前值/操作）",
                actualWidths.SequenceEqual(expectedWidths),
                $"actual=[{string.Join(",", actualWidths)}] expected=[{string.Join(",", expectedWidths)}]");
            Check("【列宽】逐列 MinWidth 与定稿一致（窄档锁定，不靠星号列兜底）",
                actualMinWidths.SequenceEqual(expectedMinWidths),
                $"actual=[{string.Join(",", actualMinWidths)}] expected=[{string.Join(",", expectedMinWidths)}]");

            var fixedLocal = new List<string>();
            double fixedLocalSum = 0;
            double fixedNetworkSum = 0;
            foreach (var tag in columnTags)
            {
                var w = Regex.Match(tag, @"[\s""]Width=""([^""]+)""");
                if (!w.Success || w.Groups[1].Value.EndsWith("*")) continue;
                if (!double.TryParse(w.Groups[1].Value, out var px)) continue;
                fixedNetworkSum += px;
                if (DetailCollapsedInLocalSource.Any(tag.Contains)) continue;
                fixedLocalSum += px;
                fixedLocal.Add(px.ToString("0"));
            }

            // 2026-10-20 两栏结构：左栏 232 + 间隔 12 + 外边距 16×2 = 276。
            // 必须锚定 WindowStyle 里的 MinWidth，不能取全文件第一个 Setter（转换器/局部样式可能先出现）。
            var windowStyle = Regex.Match(text, @"<prism:Dialog\.WindowStyle>.*?</prism:Dialog\.WindowStyle>", RegexOptions.Singleline);
            var minWidth = windowStyle.Success
                && double.TryParse(Regex.Match(windowStyle.Value, @"Setter Property=""MinWidth"" Value=""(\d+)""").Groups[1].Value, out var mw)
                ? mw
                : 0;
            double viewport = minWidth - 276;

            Check("【列宽】本地来源固定列合计 + 当前值下限 ≤ 最小窗口右栏视口（当前值列必须始终可见）",
                viewport > 0 && fixedLocalSum + starMin <= viewport,
                $"固定列=[{string.Join(",", fixedLocal)}] 合计={fixedLocalSum:0} + 星号下限={starMin:0} ≤ 视口({MinWidthText(minWidth)}−276)={viewport:0}");

            Check("【列宽】网络来源固定列合计 + 当前值下限 ≤ 最小窗口右栏视口（最紧的一档）",
                viewport > 0 && fixedNetworkSum + starMin <= viewport,
                $"网络固定列合计={fixedNetworkSum:0} + 星号下限={starMin:0} ≤ 视口={viewport:0}");

            Check("【列宽】行高/交替色/窗口下限契约",
                text.Contains("RowHeight=\"{x:Static sys:Double.NaN}\"")
                && text.Contains("MinRowHeight=\"40\"")
                && text.Contains("AlternationCount=\"1\"")
                && minWidth == 1200,
                $"minWidth={minWidth:0}");

            // 固定列必须同时写 MinWidth：否则 1200 窄档下 DataGrid 会把固定列各压 5px，
            // 类型/来源 badge 被裁（2026-10-20 实测），而不是按设计出横向滚动条。
            var fixedWithoutMin = columnTags
                .Where(t => Regex.IsMatch(t, @"[\s""]Width=""[^""]+"""))
                .Where(t => !Regex.IsMatch(t, @"Width=""\*""") && !Regex.IsMatch(t, @"MinWidth=""\d+(?:\.\d+)?"""))
                .ToList();
            Check("【列宽】所有固定列都带 MinWidth（窄档改为横向滚动而不是压窄列）",
                fixedWithoutMin.Count == 0,
                string.Join(" | ", fixedWithoutMin.Select(t => t.Replace("\r", " ").Replace("\n", " ").Trim())));

            var codeBehind = File.Exists(xamlPath.EndsWith(".xaml", StringComparison.Ordinal)
                ? xamlPath.Substring(0, xamlPath.Length - 5) + ".xaml.cs"
                : xamlPath + ".xaml.cs")
                ? File.ReadAllText(xamlPath.Substring(0, xamlPath.Length - 5) + ".xaml.cs")
                : string.Empty;
            var codeNoComments = Regex.Replace(codeBehind, @"//.*?$", string.Empty, RegexOptions.Multiline);
            //  "HookViewModel();" 出现 ≥2 次 = 订阅之外构造器里还有一次直调：
            //  Prism 在 InitializeComponent 解析期间就设好 DataContext（早于事件订阅），
            //  DataContextChanged 之后不会再发——只靠订阅，列收放一次都不会执行。
            Check("【列宽】代码后置仍按 IsNetworkSource 收放 来源/扫描组/地址 三列，且构造器直调 HookViewModel（防静默删线/漏补当前值）",
                DetailCollapsedInLocalSource.All(codeNoComments.Contains)
                && codeNoComments.Contains("IsNetworkSource")
                && Regex.Matches(codeNoComments, "HookViewModel\\(\\);").Count >= 2,
                "");

            Check("【列宽】左栏分组使用 PropertyGroupDescription(IsNetwork)",
                codeNoComments.Contains("PropertyGroupDescription")
                && codeNoComments.Contains("nameof(VariableSourceNode.IsNetwork)"),
                "");
            var allToolTips = Regex.Matches(text, @"<ToolTip\b[^>]*>").Count;
            var styledToolTips = Regex.Matches(text, @"<ToolTip\b[^>]*Style=""\{StaticResource DialogToolTipStyle\}""[^>]*>").Count;
            Check("【列宽】类型缩写转换器、展开箭头 OneWay 与 ToolTip 显式样式在位",
                text.Contains("TypeAbbreviationConverter")
                && text.Contains("IsChecked=\"{Binding IsExpanded, Mode=OneWay}\"")
                && allToolTips > 0
                && styledToolTips == allToolTips,
                $"tooltips={styledToolTips}/{allToolTips}");
            string[] namedElements =
            {
                "AddButton", "ManageGroupsButton", "SystemBanner", "EmptySourceOverlay", "SearchEmptyOverlay",
            };
            var missingNamed = namedElements.Where(n => !text.Contains($"x:Name=\"{n}\"", StringComparison.Ordinal)).ToList();
            Check("【列宽】冒烟依赖的命名元素全部在位（防 FindName 为空被当成可见/收起）",
                missingNamed.Count == 0,
                string.Join(",", missingNamed));
        }

        private static string MinWidthText(double minWidth) => minWidth > 0 ? $"MinWidth={minWidth:0}" : "未读到";

        /// <summary>从输出目录往上找仓库根，再拼相对路径（定位不到返回 null，由调用方跳过）</summary>
        private static string? FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // ==================================================================
        //  ⑤ 当前值就地编辑（写值语义 / 基准不被污染 / 编辑中暂停推送 / 结构收放）
        // ==================================================================
        //
        // 【背景】2026-10-11 交互收敛：写值进"当前值"单元格（本地写内存 / 网络下发设备），
        // "恢复初始值"从操作列挪进当前值单元格、仅偏离基准时出现，操作列只剩 3 个通用动作。
        //
        // 【三条红线】
        //  ① 就地编辑绝不触碰 DefaultValue——基准被悄悄改掉，"恢复"就恢复不到用户认定的基准；
        //  ② 转换统一走 VariableValueConverter（布尔认 1/0/是/否/on/off、不变文化），不许另立规则；
        //  ③ 编辑中（IsEditingValue）值推送暂停——轮询不许冲掉输入中的草稿。
        //
        // 【结构断言】视图含 CellValueEditBehavior/IsValueDeviated 且不再有 WriteValueCommand；
        //  VM 里写值弹窗死代码清干净。

        private static void RunCurrentValueEditContract()
        {
            Section("[G] 变量管理：当前值就地编辑（提交写值 / 基准不被污染 / 编辑中暂停推送）");

            // —— 本地 int 标量：提交写 Value、基准分毫不动 ——
            var model = new LocalVariableModel
            {
                Name = "Qty",
                DataType = typeof(int),
                DefaultValue = 7,
                Value = 7,
            };
            var node = new VariableNode { OriginalModel = model, Name = "Qty", DataType = typeof(int), Level = 0 };

            Check("【当前值】显示文本与偏离标记初值", node.CurrentValueText == "7" && !node.IsValueDeviated,
                $"text={node.CurrentValueText} deviated={node.IsValueDeviated}");

            var commit1 = node.CommitEditValue("42");
            Check("★【当前值】提交写入当前值、初始值（基准）分毫不动",
                commit1 == null && Equals(model.Value, 42) && Equals(model.DefaultValue, 7) && node.IsValueDeviated,
                $"err={commit1 ?? "无"} Value={model.Value} DefaultValue={model.DefaultValue}");

            var commit2 = node.CommitEditValue("abc");
            Check("【当前值】非法文本提交失败并给出原因、模型保持干净",
                commit2 != null && Equals(model.Value, 42),
                $"err={commit2 ?? "<无>"} Value={model.Value}");

            // —— 布尔走 VariableValueConverter 的 PLC 约定（1/0、是/否、on/off） ——
            var flag = new LocalVariableModel { Name = "Flag", DataType = typeof(bool), DefaultValue = false, Value = false };
            var flagNode = new VariableNode { OriginalModel = flag, Name = "Flag", DataType = typeof(bool), Level = 0 };
            var r1 = flagNode.CommitEditValue("1");
            var vAfterOne = flag.Value;
            var r2 = flagNode.CommitEditValue("off");
            Check("【当前值】布尔按 PLC 约定解析（1=真 / off=假），不是 ChangeType 的英文异常",
                r1 == null && Equals(vAfterOne, true) && r2 == null && Equals(flag.Value, false),
                $"1→err={r1 ?? "无"},v={vAfterOne}；off→err={r2 ?? "无"},v={flag.Value}");

            // —— 回到基准（↺ 的模型路径）：恢复后偏离标记消失 ——
            flag.ResetToDefault();
            flagNode.RefreshValueDisplay();
            Check("【当前值】恢复初始值后偏离标记消失", !flagNode.IsValueDeviated,
                $"Value={flag.Value} DefaultValue={flag.DefaultValue}");

            // —— 网络变量：未绑定通信管理器 → 下发失败必须如实报错、镜像值不被污染 ——
            var netVar = new NetworkVariableModel { Name = "MW10", DataType = typeof(int), DefaultValue = 5, Value = 5 };
            var netNode = new VariableNode { OriginalModel = netVar, Name = "MW10", DataType = typeof(int), Level = 0 };
            var netCommit = netNode.CommitEditValue("9");
            Check("【当前值】网络变量写入失败如实报错（未绑定管理器）、镜像值不变",
                netCommit != null && Equals(netVar.Value, 5),
                $"err={netCommit ?? "<无>"} Value={netVar.Value}");

            // —— 编辑中暂停推送：轮询/流程的值更新不许冲掉输入中的草稿 ——
            int pushCount = 0;
            node.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(VariableNode.CurrentValueText)) pushCount++; };
            node.BeginEditValue();
            model.Value = 99;
            node.RefreshValueDisplay();
            Check("【当前值】编辑中模型值推送被暂停（草稿不被轮询冲掉）", node.IsEditingValue && pushCount == 0,
                $"editing={node.IsEditingValue} pushes={pushCount}");

            node.CancelEditValue();
            Check("【当前值】放弃编辑（Esc）退出编辑态并回读模型值", !node.IsEditingValue && node.CurrentValueText == "99" && pushCount == 1,
                $"editing={node.IsEditingValue} text={node.CurrentValueText} pushes={pushCount}");

            // —— 数组元素行：就地写当前值元素，基准数组分毫不动 ——
            var arr = new LocalVariableModel
            {
                Name = "Codes",
                DataType = typeof(string[]),
                DefaultValue = new[] { "A", "B" },
                Value = new[] { "A", "B" },
            };
            var child = new VariableNode { OriginalModel = arr, Name = "[1]", DataType = typeof(string), ElementIndex = 1, Level = 1 };
            Check("【当前值】元素行显示文本取自父数组当前值对应下标", child.CurrentValueText == "B",
                $"text={child.CurrentValueText}");
            var childCommit = child.CommitEditValue("X9");
            Check("★【当前值】元素行提交只写 Value[i]、基准数组分毫不动",
                childCommit == null && ((string[])arr.Value)[1] == "X9" && ((string[])arr.DefaultValue)[1] == "B",
                $"err={childCommit ?? "无"} Value[1]={((string[])arr.Value)[1]} DefaultValue[1]={((string[])arr.DefaultValue)[1]}");

            // —— 结构收放：单元格化之后的视图/VM 形状 ——
            var xamlPath = FindRepoFile(@"VisionMaster\Views\DialogViews\GlobalVariableView.xaml");
            var vmPath = FindRepoFile(@"VisionMaster\ViewModels\DialogViewModels\GlobalVariableManagerViewModel.cs");
            if (xamlPath == null || vmPath == null)
            {
                Check("【当前值】结构扫描定位", false, "定位不到视图/VM 文件（不允许跳过）");
                return;
            }

            string xaml = File.ReadAllText(xamlPath);
            string vm = File.ReadAllText(vmPath);
            string xamlNoComments = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            string vmNoComments = Regex.Replace(vm, @"//.*?$", string.Empty, RegexOptions.Multiline);
            Check("【当前值】视图：单元格挂 CellValueEditBehavior（根行+元素行≥2 处）+ 偏离才显示的↺；写值/恢复两按钮已撤出操作列",
                xamlNoComments.Contains("CellValueEditBehavior.Active")
                && Regex.Matches(xamlNoComments, "CellValueEditBehavior\\.Active").Count >= 2
                && xamlNoComments.Contains("{Binding IsValueDeviated")
                && !xamlNoComments.Contains("WriteValueCommand"),
                "");
            Check("【当前值】视图：明细表「初始值」列零残留（DefaultValueText/ChildDefaultValueText 不再被绑定）",
                !xamlNoComments.Contains("DefaultValueText"),
                "");

            // 2026-10-15 新增两条（中栏表单撤掉、「＋ 添加变量」改走属性网格弹窗）：
            //  ② 录入入口必须在位——它从"中栏底部按钮"搬到"右栏卡片头"，控件换了位置但不能消失；
            //  ③ 基准入口不能丢——「初始值」从中栏表单搬进弹窗草稿（VariableDraft.InitialValue）。
            //     原来那条 "{Binding NewInitialValueText}" / "{Binding ShowNewInitialValue}" 是
            //     "表单长在主视图里"的形状断言，主视图已经没有表单了，继续钉它是钉一个不存在的形状；
            //     改成钉草稿的**反射契约**（见 ③），证明的是"这个字段真的会被属性网格渲染出来"。
            //
            // ① 先剥注释再扫（与同文件 :234 的列宽契约同口径）：不剥的话"把绑定挪进注释"也能保绿。
            Check("★【当前值】添加变量入口在位：视图 XAML 绑 AddCommand（中栏底部按钮已搬到右栏卡片头）",
                xamlNoComments.Contains("{Binding AddCommand}"),
                "");

            //  ③ 判据必须**按属性粒度**：早期写成"整文件同时含 InitialValue 与 [SuperDisplay]"，
            //     两个关键词各自被别的属性满足 —— 把 [SuperDisplay] 从 InitialValue 上摘掉它照样绿，
            //     是典型的"改坏了也不红"（R38 那类静默放行口）。
            //     改成问 FlatPropertyGrid 自己：GetVisibleProperties 只认 [SuperDisplay] 且 Visible==true，
            //     所以"InitialValue 在可见集里" = "弹窗里真有这一行"（范式照 ParallelGroupConfigChecks W5）。
            var draft = new LocalVariableDraft();
            var visibleDraftProps = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(draft)
                .Select(p => p.Name)
                .ToList();
            Check("★【当前值】初始值（基准）入口没丢：添加变量弹窗草稿的 InitialValue 带 [SuperDisplay]（属性网格真会渲染这一行）",
                visibleDraftProps.Contains(nameof(LocalVariableDraft.InitialValue)),
                $"草稿可见行 {visibleDraftProps.Count} 个：{string.Join("/", visibleDraftProps)}");

            // 网络草稿多出的设备侧字段同样必须可见（连接/扫描组/存储区/偏移量/位偏移 + 两条只读说明）
            var netVisible = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(new NetworkVariableDraft(null, null, null, null))
                .Select(p => p.Name)
                .ToList();
            bool netAllShown = new[]
            {
                nameof(NetworkVariableDraft.ConnectionName),
                nameof(NetworkVariableDraft.ScanGroupName),
                nameof(NetworkVariableDraft.AreaName),
                nameof(NetworkVariableDraft.Offset),
                nameof(NetworkVariableDraft.BitOffset),
                nameof(NetworkVariableDraft.AddressPreview),
                nameof(NetworkVariableDraft.CoilAreaHint),
            }.All(netVisible.Contains);
            Check("★【当前值】网络草稿的设备侧字段全部可见（缺一行 = 弹窗里静默少个输入框）",
                netAllShown,
                $"可见 {netVisible.Count} 行：{string.Join("/", netVisible)}");
            Check("【当前值】VM：写值弹窗死命令清干净（WriteValueCommand/ExecuteWriteValue 零残留）",
                !vmNoComments.Contains("WriteValueCommand") && !vmNoComments.Contains("ExecuteWriteValue"),
                "");

            var behaviorPath = FindRepoFile(@"VisionMaster\Helpers\CellValueEditBehavior.cs");
            Check("【当前值】提交成功立刻失焦回读取模式：两格样式走 DialogCellInputQuiet（不再用常驻输入框样式），行为里有 Keyboard.ClearFocus",
                Regex.Matches(xamlNoComments, "DialogCellInputQuiet").Count >= 2
                && !xamlNoComments.Contains("Style=\"{StaticResource DialogCellInput}\"")
                && behaviorPath != null && File.ReadAllText(behaviorPath).Contains("Keyboard.ClearFocus()"),
                "");
        }

        // ==================================================================
        //  构造辅助
        // ==================================================================

        private static string Names(IEnumerable<VariableNode> nodes) => string.Join(",", nodes.Select(n => n.Name));

        private static VariableNode Root(string name, bool isNetwork, string sourceLabel, string? description = null, string? address = null)
            => new()
            {
                Name = name,
                DataType = typeof(string[]),
                TypeName = "String[]",
                Description = description,
                Address = address,
                Level = 0,
                IsNetwork = isNetwork,
                SourceLabel = sourceLabel,
            };

        private static VariableNode Child(VariableNode parent, string name, string value)
            => new()
            {
                Name = name,
                DataType = typeof(string),
                TypeName = "String",
                ChildDefaultValue = value,
                ChildValue = value,
                Level = 1,
                IsNetwork = parent.IsNetwork,
                SourceLabel = parent.SourceLabel,
            };

        /// <summary>造一个"指向某变量数组第 index 项"的元素节点（与 CreateChildNodes 的产物同形）</summary>
        private static VariableNode ChildOf(IVariable model, int index, Type elementType, object? current)
            => new()
            {
                Name = $"[{index}]",
                DataType = elementType,
                TypeName = elementType.Name,
                OriginalModel = model,
                ElementIndex = index,
                ChildDefaultValue = current,
                ChildValue = current,
                Level = 1,
            };
    }
}
