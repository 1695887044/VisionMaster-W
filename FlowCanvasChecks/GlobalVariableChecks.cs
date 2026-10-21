using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using Core.Interfaces.Io;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Dialogs;
using VisionMaster;
using VisionMaster.Communications;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;
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
            RunArrayBuildContract();
            RunArrayEditContract();
            RunDetailColumnWidthContract();
            RunCurrentValueEditContract();
            RunNetworkArrayReadOnlyContract();
            RunVariableGroupContract();
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
            Check("string[]：空串原样保留（条码/文本数组里「空槽」是合法值，旧实现写成了 null）",
                texts != null && ((string?[])texts).Length == 3 && ((string?[])texts)[1] == "",
                texts == null ? "失败：" + e5 : string.Join(",", ((string?[])texts).Select(s => s ?? "<null>")));

            var thousands = GlobalVariableManagerViewModel.TryBuildArray(typeof(int), new[] { "1,000" }, out string e7, out int i7);
            Check("int[]：千分位分隔符明确拒绝（默认解析会把 1,000 读成 1000）",
                thousands == null && i7 == 0 && e7.Contains("千分位"), "error=" + e7);

            var badIndex = GlobalVariableManagerViewModel.TryBuildArray(typeof(int), new[] { "1", "abc", "3" }, out string e8, out int i8);
            Check("int[]：失败时指出第几项（弹窗据此把那一行标红）",
                badIndex == null && i8 == 1 && e8.Contains("第 2 项"), "error=" + e8);

            var misuse = GlobalVariableManagerViewModel.TryBuildArray(typeof(int[]), new[] { "1" }, out string e6);
            Check("误传数组类型时明确失败（而不是抛一句指不到问题所在的 InvalidCastException）",
                misuse == null && e6.Contains("元素类型"), "error=" + e6);
        }

        // ==================================================================
        //  ③b 数组编辑 v2（设计基线 global-variables v2）
        // ==================================================================
        //
        // 【背景】旧入口只有一个「强制覆盖集合...」：弹窗装填 DefaultValue、确认时同时写
        // Value 与 DefaultValue。流程写过数组之后，用户看到的是基准、点确定却把屏幕上的
        // 运行值换掉；元素级只有"改当前值"，没有复位、没有摘要、长度变化也不重建。
        //
        // 【v2 四条口径】
        //  ① 偏离判定逐元素（数组按引用比会永远判"已偏离"）；
        //  ② 元素行 ↺ 只恢复该下标，且基准里没有这一项时不给 ↺；
        //  ③ 就地改元素必须补广播 ValueChanged（引用没变，订阅方靠它刷新）；
        //  ④ 弹窗默认只写当前值，勾选才写基准；长度变化时子行必须重建。

        private static void RunArrayEditContract()
        {
            Section("[G] 变量管理：数组编辑 v2（当前值/基准分离 / 元素复位 / 摘要 / 长度重建）");

            // —— 偏离判定必须逐元素 ——
            var model = new LocalVariableModel
            {
                Name = "Codes",
                DataType = typeof(string[]),
                DefaultValue = new[] { "A", "B" },
                Value = new[] { "A", "B" },
            };
            var root = new VariableNode { OriginalModel = model, Name = "Codes", DataType = typeof(string[]), Level = 0 };

            Check("数组偏离：内容相同（不同实例）→ 未偏离",
                !root.IsValueDeviated, $"Value=[{Join(model.Value)}] Default=[{Join(model.DefaultValue)}]");

            model.Value = new[] { "A", "C" };
            root.RefreshValueDisplay();
            Check("数组偏离：内容不同 → 已偏离", root.IsValueDeviated, "");

            model.Value = new[] { "A", "B", "C" };
            root.RefreshValueDisplay();
            Check("数组偏离：长度不同 → 已偏离", root.IsValueDeviated, "");

            // —— 元素级偏离 / 复位 ——
            model.DefaultValue = new[] { "A", "B", "C" };
            model.Value = new[] { "A", "X", "C" };
            var child0 = new VariableNode { OriginalModel = model, Name = "[0]", DataType = typeof(string), ElementIndex = 0, Level = 1 };
            var child1 = new VariableNode { OriginalModel = model, Name = "[1]", DataType = typeof(string), ElementIndex = 1, Level = 1 };

            Check("元素偏离：值不同才显示 ↺（同值行不显示）",
                child1.IsElementDeviated && !child0.IsElementDeviated,
                $"c0={child0.IsElementDeviated} c1={child1.IsElementDeviated}");

            int changed = 0;
            model.ValueChanged += (_, __) => changed++;
            bool restored = child1.ResetElementValue();
            Check("★元素复位：只恢复该下标，其它元素与基准分毫不动",
                restored && ((string[])model.Value)[1] == "B"
                && ((string[])model.Value)[0] == "A" && ((string[])model.DefaultValue)[2] == "C",
                $"Value=[{Join(model.Value)}] Default=[{Join(model.DefaultValue)}]");
            Check("★元素复位：补广播一次 ValueChanged（引用没变，订阅方靠它刷新）",
                changed == 1, $"count={changed}");
            Check("元素复位后该行不再显示 ↺", !child1.IsElementDeviated, "");

            // 基准比当前值短：没有可恢复的项 → 不给 ↺、复位空跑
            var shortBase = new LocalVariableModel
            {
                Name = "Codes2",
                DataType = typeof(string[]),
                DefaultValue = Array.Empty<string>(),
                Value = new[] { "SN1", "SN2" },
            };
            var orphan = new VariableNode { OriginalModel = shortBase, Name = "[0]", DataType = typeof(string), ElementIndex = 0, Level = 1 };
            Check("基准比当前值短 → 元素不显示 ↺（那种'恢复'会把元素清空，不是用户要的动作）",
                !orphan.IsElementDeviated && !orphan.ResetElementValue(), "");

            // —— 元素就地编辑同样要广播 ——
            var editModel = new LocalVariableModel
            {
                Name = "Codes3",
                DataType = typeof(string[]),
                DefaultValue = new[] { "A" },
                Value = new[] { "A" },
            };
            var editChild = new VariableNode { OriginalModel = editModel, Name = "[0]", DataType = typeof(string), ElementIndex = 0, Level = 1 };
            int editChanged = 0;
            editModel.ValueChanged += (_, __) => editChanged++;
            string? editError = editChild.CommitEditValue("Z");
            Check("★元素就地编辑：写进 Value[0] 且补广播 ValueChanged",
                editError == null && ((string[])editModel.Value)[0] == "Z" && editChanged == 1,
                $"err={editError ?? "无"} changed={editChanged}");

            // —— 摘要 ——
            var summaryModel = new LocalVariableModel
            {
                Name = "S",
                DataType = typeof(int[]),
                DefaultValue = new[] { 1, 2, 3, 4 },
                Value = new[] { 1, 2, 3, 4 },
            };
            var summaryRoot = new VariableNode { OriginalModel = summaryModel, Name = "S", DataType = typeof(int[]), Level = 0 };
            Check("数组摘要：前 2 项 + …", summaryRoot.ArraySummary == "1 · 2 …", summaryRoot.ArraySummary);

            summaryModel.Value = Array.Empty<int>();
            summaryRoot.RefreshValueDisplay();
            Check("数组摘要：空集合明确写出来（不是空白）", summaryRoot.ArraySummary == "空集合", summaryRoot.ArraySummary);

            // —— 弹窗结果落地：默认只写当前值 ——
            var applyModel = new LocalVariableModel
            {
                Name = "A",
                DataType = typeof(int[]),
                DefaultValue = new[] { 1 },
                Value = new[] { 1 },
            };
            var edited = new[] { 7, 8 };
            GlobalVariableManagerViewModel.ApplyArrayEdit(applyModel, edited, syncBaseline: false);
            Check("★弹窗确认（默认）：只写当前值，基准分毫不动",
                ReferenceEquals(applyModel.Value, edited) && ((int[])applyModel.DefaultValue)[0] == 1,
                $"Value=[{Join(applyModel.Value)}] Default=[{Join(applyModel.DefaultValue)}]");

            var syncModel = new LocalVariableModel
            {
                Name = "B",
                DataType = typeof(int[]),
                DefaultValue = new[] { 1 },
                Value = new[] { 1 },
            };
            GlobalVariableManagerViewModel.ApplyArrayEdit(syncModel, new[] { 3, 4 }, syncBaseline: true);
            Check("★弹窗确认（勾选）：当前值与基准一起换，且不是同一个实例",
                ((int[])syncModel.Value)[1] == 4 && ((int[])syncModel.DefaultValue)[1] == 4
                && !ReferenceEquals(syncModel.Value, syncModel.DefaultValue),
                "");

            // —— 运行时长度变化 → 子行必须重建 ——
            var liveModel = new LocalVariableModel
            {
                Name = "L",
                DataType = typeof(string[]),
                DefaultValue = Array.Empty<string>(),
                Value = new[] { "a" },
            };
            Check("长度判定：长度对上 → 不重建；对不上 → 重建",
                !GlobalVariableManagerViewModel.ShouldRebuildArrayRows(liveModel, currentChildCount: 1)
                && GlobalVariableManagerViewModel.ShouldRebuildArrayRows(liveModel, currentChildCount: 3),
                "");

            liveModel.Value = new[] { "a", "b", "c" };
            var longerDefault = new LocalVariableModel
            {
                DataType = typeof(string[]),
                DefaultValue = new[] { "x", "y" },
                Value = new[] { "x" },
            };
            Check("长度判定：按 max(当前值, 基准) 算，基准更长时也不误重建",
                !GlobalVariableManagerViewModel.ShouldRebuildArrayRows(liveModel, currentChildCount: 3)
                && !GlobalVariableManagerViewModel.ShouldRebuildArrayRows(longerDefault, currentChildCount: 2),
                "");

            var nullValue = new LocalVariableModel
            {
                DataType = typeof(string[]),
                DefaultValue = new[] { "x", "y" },
                Value = null,
            };
            Check("长度判定：当前值为 null 时按 0 计（与 CreateChildNodes 的 max 口径一致）",
                !GlobalVariableManagerViewModel.ShouldRebuildArrayRows(nullValue, currentChildCount: 2)
                && GlobalVariableManagerViewModel.ShouldRebuildArrayRows(nullValue, currentChildCount: 0),
                "");

            // —— 基准更长：多出来的行不能给"点了没反应"的 ↺ ——
            var longerBase = new LocalVariableModel
            {
                Name = "Codes4",
                DataType = typeof(string[]),
                DefaultValue = new[] { "A", "B", "C" },
                Value = new[] { "A" },
            };
            var extraRow = new VariableNode { OriginalModel = longerBase, Name = "[2]", DataType = typeof(string), ElementIndex = 2, Level = 1 };
            Check("基准更长：多出来的行不给 ↺（当前值没有该下标，复位写不进去）",
                !extraRow.IsElementDeviated && !extraRow.ResetElementValue(), "");

            // —— 工厂创建的数组：基准与当前值必须是独立实例 ——
            var factoryVar = VariableFactory.CreateLocal("F", typeof(int[]), "", new[] { 1, 2 }) as LocalVariableModel;
            Check("★工厂创建的数组：基准与当前值不同实例（否则元素编辑会改掉基准）",
                factoryVar != null && !ReferenceEquals(factoryVar.DefaultValue, factoryVar.Value),
                "");

            if (factoryVar != null)
            {
                var factoryChild = new VariableNode { OriginalModel = factoryVar, Name = "[0]", DataType = typeof(int), ElementIndex = 0, Level = 1 };
                factoryChild.CommitEditValue("9");
                Check("★工厂数组：元素编辑后基准保持原值",
                    ((int[])factoryVar.DefaultValue)[0] == 1 && ((int[])factoryVar.Value)[0] == 9,
                    $"Default=[{Join(factoryVar.DefaultValue)}] Value=[{Join(factoryVar.Value)}]");
            }

            // —— 弹窗 VM 行为：装填当前值 / 取消零改动 / 确认回传 / 校验失败不关窗 ——
            var dialogModel = new LocalVariableModel
            {
                Name = "D",
                DataType = typeof(int[]),
                DefaultValue = new[] { 1 },
                Value = new[] { 2, 3 },
            };
            var dialogVm = new ArrayEditorViewModel();
            dialogVm.OnDialogOpened(new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, dialogModel },
            });
            Check("★弹窗装填的是当前值（不是基准）",
                dialogVm.Elements.Count == 2 && dialogVm.Elements[0].Value == "2" && dialogVm.Elements[1].Value == "3",
                $"count={dialogVm.Elements.Count} first={(dialogVm.Elements.Count > 0 ? dialogVm.Elements[0].Value : "<空>")}");

            Check("★弹窗默认不勾「同时写入初始值（基准）」", !dialogVm.SyncBaseline, "");

            // —— v3：逐行基准 / 已修改标记 / 行内 ↺ / 恢复全部 ——
            var changedVm = new ArrayEditorViewModel();
            var changedModel = new LocalVariableModel
            {
                Name = "C",
                DataType = typeof(int[]),
                DefaultValue = new[] { 1 },
                Value = new[] { 2, 3 },
            };
            changedVm.OnDialogOpened(new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, changedModel },
            });

            Check("★弹窗：改过的行标「已修改」并给出基准值（新增行没有基准不计入）",
                changedVm.Elements[0].IsChanged && changedVm.Elements[0].BaselineText == "1"
                && !changedVm.Elements[1].IsChanged && changedVm.ChangedCount == 1,
                $"c0={changedVm.Elements[0].IsChanged} base0={changedVm.Elements[0].BaselineText} " +
                $"c1={changedVm.Elements[1].IsChanged} count={changedVm.ChangedCount}");

            Check("★弹窗：写入范围默认「只写当前值」，后果说明跟随选择",
                changedVm.IsCurrentOnly && changedVm.ScopeHint.Contains("保持不变"),
                changedVm.ScopeHint);

            changedVm.WriteBothCommand.Execute();
            Check("★弹窗：选「同时写初始值」后说明改为写入基准",
                changedVm.SyncBaseline && !changedVm.IsCurrentOnly && changedVm.ScopeHint.Contains("同时成为初始值"),
                changedVm.ScopeHint);

            changedVm.ResetRowCommand.Execute(changedVm.Elements[0]);
            Check("★弹窗：行内 ↺ 只恢复该行到基准",
                changedVm.Elements[0].Value == "1" && !changedVm.Elements[0].IsChanged && changedVm.ChangedCount == 0,
                $"value={changedVm.Elements[0].Value} count={changedVm.ChangedCount}");

            changedVm.ResetAllCommand.Execute();
            Check("★弹窗：恢复全部为基准 → 列表回到基准长度与值",
                changedVm.Elements.Count == 1 && changedVm.Elements[0].Value == "1" && changedVm.ChangedCount == 0,
                $"count={changedVm.Elements.Count} value={(changedVm.Elements.Count > 0 ? changedVm.Elements[0].Value : "<空>")}");

            ButtonResult? closedWith = null;
            IDialogParameters? closedParams = null;
            dialogVm.RequestClose = MakeDialogCloseListener(result =>
            {
                closedWith = result.Result;
                closedParams = result.Parameters;
            });

            dialogVm.CancelCommand.Execute();
            Check("★弹窗取消：回传 Cancel、活模型零改动",
                closedWith == ButtonResult.Cancel
                && ((int[])dialogModel.Value).Length == 2 && ((int[])dialogModel.DefaultValue)[0] == 1,
                $"result={closedWith}");

            closedWith = null;
            dialogVm.ConfirmCommand.Execute();
            Array? returnedArray = null;
            bool returnedSync = true;
            if (closedParams != null)
            {
                closedParams.TryGetValue<Array>(ArrayEditorViewModel.ArrayKey, out returnedArray);
                returnedSync = closedParams.TryGetValue<bool>(ArrayEditorViewModel.SyncBaselineKey, out var s) && s;
            }
            Check("★弹窗确定：回传 OK + 新数组 + 勾选状态，模型在落地前不动",
                closedWith == ButtonResult.OK && returnedArray is int[] { Length: 2 }
                && !returnedSync && ((int[])dialogModel.Value).Length == 2,
                $"result={closedWith} arrayLen={(returnedArray as int[])?.Length ?? -1} sync={returnedSync}");

            var errorVm = new ArrayEditorViewModel();
            errorVm.OnDialogOpened(new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, new LocalVariableModel { Name = "E", DataType = typeof(double[]), DefaultValue = new[] { 1.0 }, Value = new[] { 1.0, 2.0 } } },
            });
            errorVm.Elements[1].Value = "abc";
            bool errorClosed = false;
            errorVm.RequestClose = MakeDialogCloseListener(_ => errorClosed = true);
            errorVm.ConfirmCommand.Execute();
            Check("★弹窗校验失败：不关窗、出错行标红、错误文案指到第 2 项",
                !errorClosed && errorVm.HasError && errorVm.Elements[1].HasError && errorVm.ErrorText.Contains("第 2 项"),
                errorVm.ErrorText);

            // —— 结构：视图 / 弹窗 / 注册 ——
            var xamlPath = FindRepoFile(@"VisionMaster\Views\DialogViews\GlobalVariableView.xaml");
            var editorViewPath = FindRepoFile(@"VisionMaster\Views\DialogViews\ArrayEditorView.xaml");
            var editorVmPath = FindRepoFile(@"VisionMaster\ViewModels\DialogViewModels\ArrayEditorViewModel.cs");
            var appPath = FindRepoFile(@"VisionMaster\App.xaml.cs");

            if (xamlPath == null || editorViewPath == null || editorVmPath == null || appPath == null)
            {
                Check("【数组 v2】结构扫描定位", false, "定位不到视图 / 弹窗 / 注册文件（不允许跳过）");
                return;
            }

            string xamlNoComments = Regex.Replace(File.ReadAllText(xamlPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            string editorXaml = Regex.Replace(File.ReadAllText(editorViewPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            string editorVm = File.ReadAllText(editorVmPath);
            string app = File.ReadAllText(appPath);

            Check("★视图：数组根改为「摘要 + 编辑数组… + ↺」，旧「强制覆盖集合」零残留",
                xamlNoComments.Contains("ArraySummary") && xamlNoComments.Contains("编辑数组…")
                && !xamlNoComments.Contains("强制覆盖集合"),
                "");

            Check("★视图：元素行挂逐元素 ↺（IsElementDeviated + ResetElementCommand）",
                xamlNoComments.Contains("IsElementDeviated") && xamlNoComments.Contains("ResetElementCommand"),
                "");

            Check("★弹窗视图：写入范围分段 / 逐行基准 / 修改汇总 / 恢复全部 齐备",
                editorXaml.Contains("只写当前值") && editorXaml.Contains("同时写初始值")
                && editorXaml.Contains("ScopeHint") && editorXaml.Contains("BaselineText")
                && editorXaml.Contains("IsChanged") && editorXaml.Contains("ResetAllCommand")
                && editorXaml.Contains("恢复全部为基准"),
                "");

            Check("★弹窗 VM：装填当前值 + 确认走 TryBuildArray + 回传 SyncBaseline",
                editorVm.Contains("_model?.Value as Array ?? _baseline")
                && editorVm.Contains("TryBuildArray") && editorVm.Contains("SyncBaselineKey"),
                "");

            Check("★宿主注册：ArrayEditor 弹窗已注册",
                app.Contains("RegisterDialog<ArrayEditorView, ArrayEditorViewModel>"),
                "");
        }

        /// <summary>断言消息里展示数组内容（null → 空串）</summary>
        private static string Join(object? value)
            => value is Array array ? string.Join(",", array.Cast<object?>().Select(v => v?.ToString() ?? "")) : "";

        /// <summary>
        /// 造一个能观察关窗结果的 <see cref="DialogCloseListener"/>（与 HelpProbe / ScadaChecks 同源）。
        /// Prism 9 的这个类型是类、回调构造器是 internal 的；断言宿主里没有容器，
        /// 所以反射走一次那个 internal 构造器拿它当替身。
        /// </summary>
        private static DialogCloseListener MakeDialogCloseListener(Action<IDialogResult> onClosed)
        {
            var ctor = typeof(DialogCloseListener).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<IDialogResult>) },
                modifiers: null);

            if (ctor == null)
                throw new MissingMethodException("Prism.Dialogs.DialogCloseListener", ".ctor(Action<IDialogResult>)");

            return (DialogCloseListener)ctor.Invoke(new object[] { onClosed });
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
            { "GroupColumn", "ScanGroupColumn", "AddressColumn" };

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
            Check("【列宽】逐列宽度与 2026-10-20 定稿一致（变量名/类型/分组/扫描组/地址/当前值/操作）",
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
                "AddButton", "ManageVariableGroupsButton", "SystemBanner", "EmptySourceOverlay", "SearchEmptyOverlay",
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

        // ==================================================================
        //  ⑥ 网络数组：只读展示 + 元素行写入拦截
        // ==================================================================

        /// <summary>
        /// 网络数组第一期只读。三件事必须同时成立，少一件都会让用户以为"能改"：
        /// ① 根行走 NetworkArrayRoot（显示摘要），不能落到 ReadOnlyRoot 把 "System.Int32[]" 上屏；
        /// ② 元素行走 ReadOnlyChild（只读文本），不能走本地数组的 EditableChild；
        /// ③ 即使绕过界面直接调模型，元素提交/复位也必须被拒（否则改了镜像、下一拍被设备覆盖）。
        /// </summary>
        private static void RunNetworkArrayReadOnlyContract()
        {
            Section("[G] 变量管理：网络数组只读（根摘要 / 元素行 / 写入拦截）");

            var netArray = new NetworkVariableModel
            {
                Name = "Arr",
                DataType = typeof(int[]),
                ConnectionName = "PLC1",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 3
                },
                Value = new[] { 1, 2, 3 }
            };

            var root = new VariableNode
            {
                Name = "Arr",
                DataType = typeof(int[]),
                TypeName = "Int32[]",
                Level = 0,
                IsNetwork = true,
                OriginalModel = netArray
            };

            var child = new VariableNode
            {
                Name = "[0]",
                DataType = typeof(int),
                TypeName = "Int32",
                Level = 1,
                IsNetwork = true,
                OriginalModel = netArray,
                ElementIndex = 0,
                ChildValue = 1
            };

            var converter = new GlobalVariableValueModeConverter();
            object Mode(VariableNode node, string mode) => converter.Convert(
                new object?[]
                {
                    node.IsRootNode,
                    node.IsChildNode,
                    node.IsArrayRootNode,
                    node.IsEditableNode,
                    node.IsValueReadOnly,
                    node.IsSystemOwned,
                    node.TypeName,
                    node.OriginalModel
                },
                typeof(Visibility), mode, CultureInfo.InvariantCulture);

            Check("[G] 网络数组根走 NetworkArrayRoot（显示摘要，不显示 System.Int32[]）",
                Equals(Mode(root, "NetworkArrayRoot"), Visibility.Visible)
                && Equals(Mode(root, "ArrayRoot"), Visibility.Collapsed)
                && Equals(Mode(root, "ReadOnlyRoot"), Visibility.Collapsed),
                $"NetworkArrayRoot={Mode(root, "NetworkArrayRoot")} ArrayRoot={Mode(root, "ArrayRoot")} ReadOnlyRoot={Mode(root, "ReadOnlyRoot")}");

            Check("[G] 网络数组元素行走 ReadOnlyChild（不能走本地数组的 EditableChild）",
                Equals(Mode(child, "ReadOnlyChild"), Visibility.Visible)
                && Equals(Mode(child, "EditableChild"), Visibility.Collapsed),
                $"ReadOnlyChild={Mode(child, "ReadOnlyChild")} EditableChild={Mode(child, "EditableChild")}");

            string? commitError = child.CommitEditValue("9");
            Check("[G] 网络数组元素提交被拒，且镜像数组未被改动",
                commitError != null && ((int[])netArray.Value!)[0] == 1,
                commitError ?? "提交竟然成功了");

            Check("[G] 网络数组元素复位被拒（没有元素级基准）",
                !child.ResetElementValue(), "ResetElementValue 返回 true");

            Check("[G] 建行口径把配置的元素个数算进来（第一拍值到达前后不反复重建）",
                !GlobalVariableManagerViewModel.ShouldRebuildArrayRows(netArray, 3)
                && GlobalVariableManagerViewModel.ShouldRebuildArrayRows(netArray, 0),
                $"3 行→{GlobalVariableManagerViewModel.ShouldRebuildArrayRows(netArray, 3)}；0 行→{GlobalVariableManagerViewModel.ShouldRebuildArrayRows(netArray, 0)}");

            Check("[G] 网络数组可写区：CanWriteNetworkArray=true（根行出现「写入数组…」）",
                root.CanWriteNetworkArray, $"CanWriteNetworkArray={root.CanWriteNetworkArray}");

            var readOnlyArray = new NetworkVariableModel
            {
                Name = "Flags",
                DataType = typeof(bool[]),
                ConnectionName = "PLC1",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.DiscreteInputs,
                    DataType = DataValueType.Boolean,
                    Offset = "0",
                    Length = 3
                },
                Value = new[] { true, false, true }
            };
            var readOnlyRoot = new VariableNode
            {
                Name = "Flags",
                DataType = typeof(bool[]),
                TypeName = "Boolean[]",
                Level = 0,
                IsNetwork = true,
                OriginalModel = readOnlyArray
            };
            Check("[G] 网络数组只读区：CanWriteNetworkArray=false（只显示「只读」）",
                !readOnlyRoot.CanWriteNetworkArray, $"CanWriteNetworkArray={readOnlyRoot.CanWriteNetworkArray}");

            var editor = new ArrayEditorViewModel();
            editor.OnDialogOpened(new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, netArray }
            });
            Check("[G] 网络数组写入弹窗：隐藏基准面板、固定 3 行、标题为「写入数组」",
                !editor.ShowBaselineControls && !editor.CanEditStructure
                && editor.Elements.Count == 3
                && editor.Title.Contains("写入数组", StringComparison.Ordinal)
                && editor.ScopeHint.Contains("整组写入设备", StringComparison.Ordinal),
                $"ShowBaseline={editor.ShowBaselineControls} CanEdit={editor.CanEditStructure} " +
                $"rows={editor.Elements.Count} title={editor.Title} hint={editor.ScopeHint}");

            Check("[G] 网络数组写入弹窗：当前值已到 → CanConfirm=true",
                editor.CanConfirm, $"CanConfirm={editor.CanConfirm}");

            var emptyModel = new NetworkVariableModel
            {
                Name = "Empty",
                DataType = typeof(int[]),
                ConnectionName = "PLC1",
                AddressConfig = new ModbusAddress
                {
                    Area = ModbusArea.HoldingRegisters,
                    DataType = DataValueType.Int32,
                    Offset = "0",
                    Length = 3
                },
                Value = null
            };
            var emptyEditor = new ArrayEditorViewModel();
            emptyEditor.OnDialogOpened(new DialogParameters
            {
                { ArrayEditorViewModel.VariableKey, emptyModel }
            });
            Check("[G] 网络数组写入弹窗：当前值未到 → CanConfirm=false（防空表写成 0）",
                !emptyEditor.CanConfirm && emptyEditor.Elements.Count == 3,
                $"CanConfirm={emptyEditor.CanConfirm} rows={emptyEditor.Elements.Count}");
        }

        // ==================================================================
        //  ⑦ 显示分组：方案级分类（唯一名 / 筛选 / 删组回落 / 持久化 / 扫描组入口）
        // ==================================================================

        private static void RunVariableGroupContract()
        {
            Section("[G] 变量管理：显示分组（方案级分类 / 唯一名 / 筛选 / 删组回落 / 持久化）");

            var monitor = new VariableGroupDescriptor { Id = Guid.NewGuid(), Name = "监控分组" };
            var control = new VariableGroupDescriptor { Id = Guid.NewGuid(), Name = "控制分组" };
            var groups = new List<VariableGroupDescriptor> { monitor, control };

            Check("[G] 分组名：空 / 重名（trim + 不区分大小写）/ 保留名 全部拒绝",
                VariableGroupEditorViewModel.ValidateGroupName("  ", groups, null) != null
                && VariableGroupEditorViewModel.ValidateGroupName(" 监控分组 ", groups, null) != null
                && VariableGroupEditorViewModel.ValidateGroupName("未分组", groups, null) != null,
                $"empty={VariableGroupEditorViewModel.ValidateGroupName("  ", groups, null)} " +
                $"dup={VariableGroupEditorViewModel.ValidateGroupName(" 监控分组 ", groups, null)} " +
                $"reserved={VariableGroupEditorViewModel.ValidateGroupName("未分组", groups, null)}");

            Check("[G] 分组名：不重名可用；改名排除自身（拿别人的名字才算重名）",
                VariableGroupEditorViewModel.ValidateGroupName("报警分组", groups, null) == null
                && VariableGroupEditorViewModel.ValidateGroupName("监控分组", groups, monitor.Id) == null
                && VariableGroupEditorViewModel.ValidateGroupName("控制分组", groups, monitor.Id) != null,
                "");

            var asciiGroups = new List<VariableGroupDescriptor>
            {
                new() { Id = Guid.NewGuid(), Name = "GroupA" }
            };
            Check("[G] 分组名：ASCII 大小写不敏感 + 20 字符上限",
                VariableGroupEditorViewModel.ValidateGroupName("groupa", asciiGroups, null) != null
                && VariableGroupEditorViewModel.ValidateGroupName("GroupB", asciiGroups, null) == null
                && VariableGroupEditorViewModel.ValidateGroupName(new string('x', 20), asciiGroups, null) == null
                && VariableGroupEditorViewModel.ValidateGroupName(new string('x', 21), asciiGroups, null) != null,
                "");

            var v1 = new VariableNode { Name = "Temp", Level = 0, IsNetwork = true, SourceLabel = "PLC1", GroupId = monitor.Id };
            var v2 = new VariableNode { Name = "Start", Level = 0, IsNetwork = true, SourceLabel = "PLC1", GroupId = control.Id };
            var v3 = new VariableNode { Name = "Spare", Level = 0, IsNetwork = true, SourceLabel = "PLC1", GroupId = Guid.Empty };
            var nodes = new List<VariableNode> { v1, v2, v3 };

            Check("[G] 分组筛选：监控分组只回监控点；全部（null）回全部",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", monitor.Id, "")) == "Temp"
                && Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", null, "")) == "Temp,Start,Spare",
                "monitor=" + Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", monitor.Id, "")));

            Check("[G] 未分组（Guid.Empty）只回未归类的点",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", Guid.Empty, "")) == "Spare",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", Guid.Empty, "")));

            Check("[G] ★搜索跨分组：在监控分组里搜控制分组的点也要搜到",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", monitor.Id, "Start")) == "Start",
                Names(GlobalVariableManagerViewModel.FilterDisplayNodes(nodes, "PLC1", monitor.Id, "Start")));

            var netVars = new List<NetworkVariableModel>
            {
                new() { Name = "Temp", ConnectionName = "PLC1", GroupId = monitor.Id },
                new() { Name = "Start", ConnectionName = "PLC1", GroupId = control.Id },
                new() { Name = "Spare", ConnectionName = "PLC1", GroupId = Guid.Empty },
            };
            var mutableGroups = new List<VariableGroupDescriptor> { monitor, control };
            int affected = VariableGroupEditorViewModel.DeleteGroupCore(mutableGroups, netVars, control.Id);
            Check("[G] ★删组：受影响变量回落到未分组，变量本身不删，组表只剩另一组",
                affected == 1 && netVars[1].GroupId == Guid.Empty && netVars.Count == 3
                && mutableGroups.Count == 1 && mutableGroups[0].Id == monitor.Id,
                $"affected={affected} groups={mutableGroups.Count} startGroup={netVars[1].GroupId}");

            Check("[G] 内置「未分组」不可删（DeleteGroupCore 空跑）",
                VariableGroupEditorViewModel.DeleteGroupCore(mutableGroups, netVars, Guid.Empty) == 0
                && mutableGroups.Count == 1,
                "");

            var renameTarget = new VariableGroupDescriptor { Id = Guid.NewGuid(), Name = "监控" };
            var renameGroups = new List<VariableGroupDescriptor> { renameTarget };
            var renamedVar = new NetworkVariableModel { Name = "Temp", ConnectionName = "PLC1", GroupId = renameTarget.Id };
            bool renamed = VariableGroupEditorViewModel.RenameGroupCore(renameGroups, renameTarget.Id, "监控分组");
            Check("[G] ★改名：只改显示名，变量仍指向同一个 Id（不需要级联）",
                renamed && renameTarget.Name == "监控分组" && renamedVar.GroupId == renameTarget.Id,
                $"name={renameTarget.Name} varGroup={renamedVar.GroupId}");

            var selModel = new NetworkVariableModel { Name = "Sel", ConnectionName = "PLC1", GroupId = Guid.Empty };
            var selNode = new VariableNode { Name = "Sel", Level = 0, IsNetwork = true, GroupId = Guid.Empty };
            var selOption = new VariableGroupOption { Id = monitor.Id, Name = monitor.Name };
            var applied = GlobalVariableManagerViewModel.ApplyGroupSelection(selModel, selNode, selOption);
            Check("[G] 分组下拉落账：合法选项写模型+节点；null 不覆盖模型",
                applied == monitor.Id && selModel.GroupId == monitor.Id && selNode.GroupId == monitor.Id
                && GlobalVariableManagerViewModel.ApplyGroupSelection(selModel, selNode, null) == monitor.Id
                && selModel.GroupId == monitor.Id,
                $"applied={applied} model={selModel.GroupId}");

            // —— 编辑器 VM：新增 / 改名 / 删除的命令行为（含 Changed 事件） ——
            var ws = new WorkspaceContext();
            ws.SwitchSolution(new SolutionModel());
            ws.GlobalVariables.Clear();
            var editGroup = new VariableGroupDescriptor { Id = Guid.NewGuid(), Name = "监控分组" };
            ws.CurrentSolution.VariableGroups.Add(editGroup);
            var editVar = new NetworkVariableModel { Name = "Temp", ConnectionName = "PLC1", GroupId = editGroup.Id };
            ws.GlobalVariables.Add(editVar);

            var editor = new VariableGroupEditorViewModel(ws);
            editor.Open();
            Check("[G] 分组编辑器打开：组行 + 内置未分组 + 变量数",
                editor.Rows.Count == 2
                && editor.Rows[0].Name == "监控分组" && editor.Rows[0].Count == 1
                && editor.Rows[1].IsBuiltIn && editor.Rows[1].Name == "未分组",
                $"rows={editor.Rows.Count} first={editor.Rows.FirstOrDefault()?.Name}");

            editor.NewGroupName = "监控分组";
            Check("[G] 分组编辑器新增：重名时 CanAddGroup=false 且给出原因",
                !editor.CanAddGroup && editor.NewGroupError?.Contains("已存在", StringComparison.Ordinal) == true,
                $"canAdd={editor.CanAddGroup} err={editor.NewGroupError}");

            int changedCount = 0;
            editor.Changed += () => changedCount++;
            editor.NewGroupName = "报警分组";
            editor.AddGroupCommand.Execute();
            Check("[G] 分组编辑器新增：合法名写入方案组表并触发 Changed",
                ws.CurrentSolution.VariableGroups.Count == 2
                && ws.CurrentSolution.VariableGroups.Any(g => g.Name == "报警分组")
                && changedCount == 1,
                $"groups={ws.CurrentSolution.VariableGroups.Count} changed={changedCount}");

            var renameRow = editor.Rows.First(r => r.Name == "监控分组");
            editor.BeginRenameCommand.Execute(renameRow);
            renameRow.EditName = "控制分组";
            editor.CommitRenameCommand.Execute(renameRow);
            Check("[G] 分组编辑器改名：方案组表改名，变量仍指向原 Id",
                ws.CurrentSolution.VariableGroups.First(g => g.Id == editGroup.Id).Name == "控制分组"
                && editVar.GroupId == editGroup.Id
                && changedCount == 2,
                $"name={ws.CurrentSolution.VariableGroups.First(g => g.Id == editGroup.Id).Name} changed={changedCount}");

            var dupRow = editor.Rows.First(r => r.Name == "控制分组");
            editor.BeginRenameCommand.Execute(dupRow);
            dupRow.EditName = "报警分组";
            editor.CommitRenameCommand.Execute(dupRow);
            Check("[G] 分组编辑器改名：重名被拒（组表名字不变）",
                ws.CurrentSolution.VariableGroups.First(g => g.Id == editGroup.Id).Name == "控制分组"
                && changedCount == 2,
                $"name={ws.CurrentSolution.VariableGroups.First(g => g.Id == editGroup.Id).Name} changed={changedCount}");

            var deleteRow = editor.Rows.First(r => r.Name == "控制分组");
            editor.BeginDeleteCommand.Execute(deleteRow);
            editor.ConfirmDeleteCommand.Execute(deleteRow);
            Check("[G] 分组编辑器删组：变量回落未分组、组表移除、Changed 触发",
                editVar.GroupId == Guid.Empty
                && ws.CurrentSolution.VariableGroups.Count == 1
                && changedCount == 3,
                $"varGroup={editVar.GroupId} groups={ws.CurrentSolution.VariableGroups.Count} changed={changedCount}");
            editor.Close();

            // 无方案：明确提示并禁用，不再"输完名字点一下什么都不发生"
            var noSolutionWs = new WorkspaceContext();
            var noSolutionEditor = new VariableGroupEditorViewModel(noSolutionWs);
            noSolutionEditor.Open();
            noSolutionEditor.NewGroupName = "报警分组";
            Check("[G] 无方案：添加分组明确提示并禁用（不再静默失效）",
                !noSolutionEditor.CanAddGroup
                && noSolutionEditor.NewGroupError?.Contains("没有打开的方案", StringComparison.Ordinal) == true,
                $"canAdd={noSolutionEditor.CanAddGroup} err={noSolutionEditor.NewGroupError}");
            noSolutionEditor.Close();

            var dto = VariableDto.FromNetwork(
                new NetworkVariableModel
                {
                    Name = "Temp",
                    ConnectionName = "PLC1",
                    DataType = typeof(double),
                    GroupId = monitor.Id,
                    AddressConfig = new ModbusAddress
                    {
                        Area = ModbusArea.HoldingRegisters,
                        DataType = DataValueType.Double,
                        Offset = "0"
                    }
                }, CommunicationType.ModbusTcp);

            var json = JsonConvert.SerializeObject(new
            {
                VariableGroups = new List<VariableGroupDescriptor> { monitor },
                VariableSnapshots = new List<VariableDto> { dto }
            });
            var back = JObject.Parse(json);
            var groupsBack = back["VariableGroups"]?.ToObject<List<VariableGroupDescriptor>>();
            var varsBack = back["VariableSnapshots"]?.ToObject<List<VariableDto>>();
            Check("[G] ★持久化往返：分组表与变量的 GroupId 原样还原",
                groupsBack is { Count: 1 } && groupsBack[0].Id == monitor.Id && groupsBack[0].Name == monitor.Name
                && varsBack is { Count: 1 } && varsBack[0].GroupId == monitor.Id,
                $"groups={groupsBack?.Count} varGroup={varsBack?.FirstOrDefault()?.GroupId}");

            var legacyDto = JsonConvert.DeserializeObject<VariableDto>(
                "{\"VarType\":\"Network\",\"Name\":\"Old\",\"ConnectionName\":\"PLC1\"}");
            var legacySolution = JsonConvert.DeserializeObject<SolutionModel>("{}");
            Check("[G] 旧方案：缺 VariableGroups / GroupId → 空表 + 未分组（零迁移）",
                legacyDto != null && legacyDto.GroupId == Guid.Empty
                && legacySolution != null && legacySolution.VariableGroups.Count == 0,
                legacyDto == null
                    ? "dto=null"
                    : $"varGroup={legacyDto.GroupId} groups={legacySolution?.VariableGroups.Count}");

            var persistencePath = FindRepoFile(@"VisionMaster\Services\VariablePersistenceService.cs");
            var dtoPath = FindRepoFile(@"Core\Models\Variable\VariableDto.cs");
            Check("[G] 持久化接线：DTO 捕获 GroupId + 恢复时写回 GroupId（悬空归一）",
                persistencePath != null && dtoPath != null
                && File.ReadAllText(dtoPath).Contains("GroupId = nv.GroupId", StringComparison.Ordinal)
                && File.ReadAllText(persistencePath).Contains("var groupId = dto.GroupId", StringComparison.Ordinal)
                && File.ReadAllText(persistencePath).Contains("GroupId = groupId", StringComparison.Ordinal)
                && File.ReadAllText(persistencePath).Contains(
                    "solution.VariableGroups.All(g => g.Id != groupId)", StringComparison.Ordinal),
                persistencePath == null || dtoPath == null ? "定位不到持久化文件" : "");

            var vmPath = FindRepoFile(@"VisionMaster\ViewModels\DialogViewModels\GlobalVariableManagerViewModel.cs");
            var xamlPath = FindRepoFile(@"VisionMaster\Views\DialogViews\GlobalVariableView.xaml");
            if (vmPath == null || xamlPath == null)
            {
                Check("[G] 显示分组结构扫描定位", false, "定位不到 VM / 视图（不允许跳过）");
                return;
            }

            string vm = File.ReadAllText(vmPath);
            string xaml = Regex.Replace(File.ReadAllText(xamlPath), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            Check("[G] ★扫描组：变量管理不再打开扫描组编辑器（入口只在通讯设置）",
                !vm.Contains("ScanGroupEditorViewModel") && !vm.Contains("ManageScanGroupsCommand")
                && !xaml.Contains("ManageScanGroupsCommand") && !xaml.Contains("ManageGroupsButton"),
                "");
            Check("[G] 显示分组：左栏模板选择器 / 分组列 / 管理分组入口 / 覆盖层在位",
                xaml.Contains("GvSourceNodeSelector") && xaml.Contains("GvGroupNodeTemplate")
                && xaml.Contains("x:Name=\"GroupColumn\"") && xaml.Contains("SelectedGroup")
                && xaml.Contains("OpenGroupManagerCommand") && xaml.Contains("GroupEditor.Rows")
                && xaml.Contains("GroupEditor.IsOpen"),
                "");
            Check("[G] 分组编辑器接线：管理器订阅/退订 Changed + 覆盖层内部命令齐备",
                vm.Contains("GroupEditor.Changed += OnVariableGroupsChanged")
                && vm.Contains("GroupEditor.Changed -= OnVariableGroupsChanged")
                && xaml.Contains("GroupEditor.AddGroupCommand")
                && xaml.Contains("GroupEditor.BeginRenameCommand")
                && xaml.Contains("GroupEditor.CommitRenameCommand")
                && xaml.Contains("GroupEditor.CancelRenameCommand")
                && xaml.Contains("GroupEditor.BeginDeleteCommand")
                && xaml.Contains("GroupEditor.ConfirmDeleteCommand")
                && xaml.Contains("GroupEditor.CancelDeleteCommand"),
                "");
        }

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
    }
}
