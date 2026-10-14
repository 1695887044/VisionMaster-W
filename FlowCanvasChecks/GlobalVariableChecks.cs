using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        // 右栏视口 = 窗口宽 −(216+12+296+12) 三栏结构 −(14×2) 外边距 = 宽 −564。

        private static readonly string[] DetailCollapsedInLocalSource =
            { "SourceColumn", "ScanGroupColumn", "AddressColumn" };

        private static void RunDetailColumnWidthContract()
        {
            Section("[G] 变量管理：明细表列宽契约（星号列不许被固定列挤没）");

            var xamlPath = FindRepoFile(@"VisionMaster\Views\DialogViews\GlobalVariableView.xaml");
            if (xamlPath == null)
            {
                Check("【列宽】GlobalVariableView.xaml 定位", true, "跳过：定位不到视图文件");
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
            Check("【列宽】星号列（当前值）存在且 MinWidth ≥ 80", starTag != null && starMin >= 80,
                starTag == null ? "没找到 Width=\"*\" 的列" : $"MinWidth={starMin}");

            var fixedLocal = new List<string>();
            double fixedSum = 0;
            foreach (var tag in columnTags)
            {
                var w = Regex.Match(tag, @"[\s""]Width=""([^""]+)""");
                if (!w.Success || w.Groups[1].Value.EndsWith("*")) continue;
                if (!double.TryParse(w.Groups[1].Value, out var px)) continue;
                if (DetailCollapsedInLocalSource.Any(tag.Contains)) continue;
                fixedSum += px;
                fixedLocal.Add(px.ToString("0"));
            }

            var minWidth = double.TryParse(
                Regex.Match(text, @"Setter Property=""MinWidth"" Value=""(\d+)""").Groups[1].Value, out var mw) ? mw : 0;
            double localViewport = minWidth - 564;

            Check("【列宽】本地来源固定列合计 + 当前值下限 ≤ 最小窗口右栏视口（当前值列必须始终可见）",
                localViewport > 0 && fixedSum + starMin <= localViewport,
                $"固定列=[{string.Join(",", fixedLocal)}] 合计={fixedSum:0} + 星号下限={starMin:0} ≤ 视口({MinWidthText(minWidth)}−564)={localViewport:0}");

            var codeBehind = File.Exists(xamlPath.EndsWith(".xaml", StringComparison.Ordinal)
                ? xamlPath.Substring(0, xamlPath.Length - 5) + ".xaml.cs"
                : xamlPath + ".xaml.cs")
                ? File.ReadAllText(xamlPath.Substring(0, xamlPath.Length - 5) + ".xaml.cs")
                : string.Empty;
            //  "HookViewModel();" 出现 ≥2 次 = 订阅之外构造器里还有一次直调：
            //  Prism 在 InitializeComponent 解析期间就设好 DataContext（早于事件订阅），
            //  DataContextChanged 之后不会再发——只靠订阅，列收放一次都不会执行。
            Check("【列宽】代码后置仍按 IsNetworkSource 收放 来源/扫描组/地址 三列，且构造器直调 HookViewModel（防静默删线/漏补当前值）",
                DetailCollapsedInLocalSource.All(codeBehind.Contains)
                && codeBehind.Contains("IsNetworkSource")
                && Regex.Matches(codeBehind, "HookViewModel\\(\\);").Count >= 2,
                "");
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
                Check("【当前值】结构扫描定位", true, "跳过：定位不到视图/VM 文件");
                return;
            }

            string xaml = File.ReadAllText(xamlPath);
            string vm = File.ReadAllText(vmPath);
            Check("【当前值】视图：单元格挂 CellValueEditBehavior（根行+元素行≥2 处）+ 偏离才显示的↺；写值/恢复两按钮已撤出操作列",
                xaml.Contains("CellValueEditBehavior.Active")
                && Regex.Matches(xaml, "CellValueEditBehavior\\.Active").Count >= 2
                && xaml.Contains("{Binding IsValueDeviated")
                && !xaml.Contains("WriteValueCommand"),
                "");
            Check("【当前值】视图：明细表「初始值」列零残留（DefaultValueText/ChildDefaultValueText 不再被绑定），建表单有「初始值」项",
                !xaml.Contains("DefaultValueText")
                && xaml.Contains("{Binding NewInitialValueText")
                && xaml.Contains("{Binding ShowNewInitialValue"),
                "");
            Check("【当前值】VM：写值弹窗死命令清干净（WriteValueCommand/ExecuteWriteValue 零残留）",
                !vm.Contains("WriteValueCommand") && !vm.Contains("ExecuteWriteValue"),
                "");

            var behaviorPath = FindRepoFile(@"VisionMaster\Helpers\CellValueEditBehavior.cs");
            Check("【当前值】提交成功立刻失焦回读取模式：两格样式走 DialogCellInputQuiet（不再用常驻输入框样式），行为里有 Keyboard.ClearFocus",
                Regex.Matches(xaml, "DialogCellInputQuiet").Count >= 2
                && !xaml.Contains("Style=\"{StaticResource DialogCellInput}\"")
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
