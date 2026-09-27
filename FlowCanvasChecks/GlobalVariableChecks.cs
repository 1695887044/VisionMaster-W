using System;
using System.Collections.Generic;
using System.Linq;
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
