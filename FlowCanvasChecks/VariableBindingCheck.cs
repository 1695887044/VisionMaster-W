using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Interfaces;
using HalconDotNet;
using Prism.Dialogs;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「变量绑定」窗口（VariableBindingView / VariableBindingViewModel）的断言。
    ///
    /// 为什么值得单独钉住
    /// ---------
    /// 这个窗口的确定逻辑是「常量框非空就写常量」，而它**不管用户有没有换过端口**。
    /// 于是只要换端口时不重置常量框，就会出现：
    ///   选中 A 端口填了值 → 又去点 B 端口看看 → 点确定 → A 的值被写到 B 上。
    /// 界面上一切正常，被写坏的是另一个端口的绑定 —— 事后极难定位。
    /// 同时反过来也危险：端口绑的是上游连线时，若把连线文本回显进常量框，
    /// 点确定就会把连线改写成常量，等于悄悄把线拆了。
    ///
    /// 这两条都只有真跑一遍 VM 才看得出来，所以这里直接驱动 ViewModel（不碰 WPF 控件）。
    /// </summary>
    internal static class VariableBindingCheck
    {
        /// <summary>断言用的「变量赋值」类型名（只在本用例里注册，避免污染画布断言用的桩插件）</summary>
        private const string AssignType = "VM.BindingCheck.VariableAssignment";

        public static void Run()
        {
            Section("[V] 变量绑定窗口：回显当前值 / 换端口不串值 / 不把连线当常量");

            var provider = new StubPluginProvider();
            provider.RegisterModule(new ToolItemModel
            {
                Name = "变量赋值",
                ModuleTypeName = AssignType,
                Category = "变量操作",
                Description = "断言用变量赋值",
                InputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "Name", Description = "目标变量名称", DataTypeName = "System.String" },
                    new() { Name = "Value", Description = "要赋的值", DataTypeName = "System.Object" },
                    new() { Name = "CreateIfNotExists", Description = "变量不存在时是否自动创建", DataTypeName = "System.Boolean" },
                    // 图像端口：给"候选列表按类型过滤"的断言用（HImage 是强类型，最能看出过滤生效）
                    new() { Name = "SrcImage", Description = "输入图像", DataTypeName = typeof(HImage).AssemblyQualifiedName },
                },
            });

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "绑定窗口断言" };
            var flow = new FlowModel { FlowName = "绑定断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            // 三个端口各代表一种"值从哪来"，这正是最容易搞错的地方：
            //   Name              → InputValues（手填常量，插件 OnConfirm 写回的那份 —— 真实方案就是这个）
            //   Value             → LinkedSources 里的常量连线（在绑定窗口里填过常量）
            //   CreateIfNotExists → LinkedSources 里的上游端口连线（必须清空，不能回显）
            var step = new ActionStep("", "变量赋值", AssignType, "赋值_Expected");
            step.SetInputValue("Name", "Expected");
            step.LinkedSources["Value"] = Constant("黑,棕,玫红,红,黄");
            step.LinkedSources["CreateIfNotExists"] = new LinkReference(
                LinkKind.StepPort, Guid.NewGuid(), "Out", "上游_0.Out");
            flow.Steps.Add(step);

            // 前提断言：Name 走的必须是 InputValues 这条路。
            // 上一版用例把三个端口都造成"常量连线"，于是绕开了真实路径 —— 代码明明不对却全绿。
            Check("用例前提：Name 的值存在 InputValues 里（不是 LinkedSources）",
                !step.LinkedSources.ContainsKey("Name") && step.InputValues.ContainsKey("Name"),
                $"LinkedSources 有 Name = {step.LinkedSources.ContainsKey("Name")}；"
                + $"InputValues 有 Name = {step.InputValues.ContainsKey("Name")}");

            var vm = new VariableBindingViewModel(workspace, provider);
            vm.OnDialogOpened(new DialogParameters
            {
                { "IsSingleBindMode", false },
                { "TargetStep", step },
            });

            Check("弹窗打开后，默认选中端口的手填值被回显出来",
                vm.ConstantValue == "Expected", $"ConstantValue=[{vm.ConstantValue}]");

            vm.SelectedInputPort = vm.DisplayDataPort.FirstOrDefault(p => p.Definition.Name == "Value");
            Check("换到「常量连线」的端口：回显连线里的常量",
                vm.ConstantValue == "黑,棕,玫红,红,黄", $"ConstantValue=[{vm.ConstantValue}]");

            vm.SelectedInputPort = vm.DisplayDataPort.FirstOrDefault(p => p.Definition.Name == "CreateIfNotExists");
            Check("换到「绑的是上游端口」的端口：常量框必须清空（否则点确定会把连线改写成常量）",
                vm.ConstantValue == "", $"ConstantValue=[{vm.ConstantValue}]");

            vm.SelectedInputPort = vm.DisplayDataPort.FirstOrDefault(p => p.Definition.Name == "Name");
            Check("再换回来值还在（回显可重复，不是一次性）",
                vm.ConstantValue == "Expected", $"ConstantValue=[{vm.ConstantValue}]");

            RunTypeFilterContract(vm);
            RunTypeResolveContract();
            RunViewStyleContract();
        }

        // ==================================================================
        //  候选列表按类型过滤 + 绑定判据
        // ==================================================================

        /// <summary>
        /// 候选列表的类型过滤。
        ///
        /// 为什么值得钉死：过滤的失败方式是**静默**的 ——
        ///   · 过滤太松：把 HRegion 摆进 HImage 端口的候选里，用户双击才被拦下（回到改前的老样子）；
        ///   · 过滤太紧：把本来能绑的项藏起来，用户以为"上游没这个变量"，然后去查插件、查流程，
        ///     永远查不到这张列表框上。
        /// 两条都只有真跑一遍 VM 才看得出来，所以这里直接驱动 ViewModel。
        /// </summary>
        private static void RunTypeFilterContract(VariableBindingViewModel vm)
        {
            Section("[V] 候选端口列表：按目标端口类型过滤 / 显示全部 / 换端口重算");

            // 造一个"上游算子"：三个输出端口，只有第一个能被 HImage 端口接住
            var upstream = new ToolItemModel
            {
                Name = "图像采集_0",
                Id = Guid.NewGuid(),
                OutputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "Image", Description = "采集图像", DataTypeName = typeof(HImage).AssemblyQualifiedName },
                    new() { Name = "Region", Description = "缺陷区域", DataTypeName = typeof(HRegion).AssemblyQualifiedName },
                    new() { Name = "Row", Description = "行坐标", DataTypeName = typeof(double).AssemblyQualifiedName },
                },
            };
            vm.SelectedNode = upstream;

            vm.SelectedInputPort = vm.DisplayDataPort.First(p => p.Definition.Name == "SrcImage");
            var imageCandidates = vm.DisplayPorts.Select(p => p.Name).ToList();
            Check("选中 HImage 端口：候选里只剩 HImage 输出（HRegion / double 被过滤掉）",
                imageCandidates.Count == 1 && imageCandidates[0] == "Image",
                "实际候选：" + string.Join(", ", imageCandidates));

            vm.ShowAllPorts = true;
            var allCandidates = vm.DisplayPorts.ToList();
            var disabled = allCandidates.Where(p => !p.IsBindable).Select(p => p.Name).ToList();
            Check("勾选「显示全部」：三条都列出来，且不兼容的两条被标为不可绑定",
                allCandidates.Count == 3 && disabled.Count == 2
                && disabled.Contains("Region") && disabled.Contains("Row"),
                $"候选 {allCandidates.Count} 条，不可绑定：" + string.Join(", ", disabled));
            Check("不可绑定项带得出原因（否则置灰了也说不清为什么）",
                allCandidates.Where(p => !p.IsBindable).All(p => !string.IsNullOrWhiteSpace(p.IncompatibleReason))
                && allCandidates.Where(p => p.IsBindable).All(p => string.IsNullOrEmpty(p.IncompatibleReason)),
                string.Join(" | ", allCandidates.Select(p => $"{p.Name}:{p.IncompatibleReason}")));

            vm.ShowAllPorts = false;
            Check("取消「显示全部」：恢复成只剩能绑的",
                vm.DisplayPorts.Count == 1 && vm.DisplayPorts[0].Name == "Image",
                "实际候选：" + string.Join(", ", vm.DisplayPorts.Select(p => p.Name)));

            // 换输入端口必须重算 —— 早先只在换上游节点时算，
            // 换端口不重算就会把"上一个端口的过滤结果"带过来
            vm.SelectedInputPort = vm.DisplayDataPort.First(p => p.Definition.Name == "Value");
            Check("换到 object 端口：三条全部可绑（object 什么都接得住）",
                vm.DisplayPorts.Count == 3,
                "实际候选：" + string.Join(", ", vm.DisplayPorts.Select(p => p.Name)));

            vm.SelectedInputPort = vm.DisplayDataPort.First(p => p.Definition.Name == "SrcImage");
            Check("换回 HImage 端口：过滤结果跟着重算（不是残留上一次的）",
                vm.DisplayPorts.Count == 1 && vm.DisplayPorts[0].Name == "Image",
                "实际候选：" + string.Join(", ", vm.DisplayPorts.Select(p => p.Name)));

            // 判据必须是纯函数、可空安全 —— 弹窗在无选中项时也会走这条路
            Check("判据 null 安全（无选中端口时不抛、按全兼容处理）",
                TypeHelper.CanBindTo(typeof(HImage), typeof(object)), "");
        }

        /// <summary>
        /// 端口类型名解析。
        ///
        /// 为什么单列一条：过滤能不能生效**全看这一步**。端口声明的 DataTypeName 是
        /// AssemblyQualifiedName（PluginService / FlowQueryHelper / 各插件 DynamicPortInfo 都是），
        /// Type.GetType 直接命中；但插件自己手写的固定端口可能只写短名，
        /// 那种情况下解析失败就退化成 object ⇒ 过滤静默失效（界面看起来"正常"，只是不筛了）。
        /// </summary>
        private static void RunTypeResolveContract()
        {
            Section("[V] 端口类型名解析（过滤是否生效的前提）");

            Check("程序集限定名可解析",
                TypeHelper.ResolveType(typeof(HImage).AssemblyQualifiedName) == typeof(HImage), "");

            Check("只写短名也能解析（逐程序集兜底）",
                TypeHelper.ResolveType("HalconDotNet.HImage") == typeof(HImage),
                $"解析结果={TypeHelper.ResolveType("HalconDotNet.HImage").Name}");

            Check("未知类型名退回 object（= 放行，不误杀）",
                TypeHelper.ResolveType("不存在.这样的.类型") == typeof(object), "");

            Check("空类型名退回 object（不抛异常）",
                TypeHelper.ResolveType(null) == typeof(object) && TypeHelper.ResolveType("") == typeof(object), "");
        }

        // ==================================================================
        //  视图样式收编契约（UI 库 Dialog* 公共令牌）
        // ==================================================================

        /// <summary>本视图必须能解析到的公共样式键（键名即跨程序集契约）</summary>
        private static readonly string[] RequiredDialogStyleKeys =
        {
            "DialogColumnCard", "DialogColumnHeader", "DialogColumnHeaderAccent",
            "DialogColumnTitle", "DialogColumnTitleIcon", "DialogOptionItem",
            "DialogChip", "DialogChipText", "DialogCheckBox", "DialogFooter",
            "DialogFooterPrimaryButton", "DialogFooterSecondaryButton",
            "DialogInput", "DialogCombo", "DialogFormLabel",
        };

        /// <summary>已经收编到 UI 库、不允许视图再本地定义的键</summary>
        private static readonly string[] ForbiddenLocalDialogKeys =
        {
            "NoWarningItemStyle", "InputPortItemStyle",
            "PresetOptionButtonStyle", "SelectedPresetOptionButtonStyle",
        };

        /// <summary>
        /// 静态扫描（不碰 WPF 运行时），理由与 BlobDetectChecks 同：
        /// 控制台进程里 new Application 会让后续投射类断言失效、且退出时挂住。
        /// 这里扫"用到但没定义"与"本地又抄了一份"，等价于运行期那句"找不到资源"。
        /// </summary>
        private static void RunViewStyleContract()
        {
            Section("[V] 视图样式收编契约（UI 库 Dialog* 令牌）");

            string? xamlPath = ResolveRepoFile(@"VisionMaster\Views\DialogViews\VariableBindingView.xaml");
            string? stylesPath = ResolveRepoFile(@"UI\Controls\Themes\DialogStyles.xaml");
            string? colorsPath = ResolveRepoFile(@"UI\Controls\Themes\Colors.xaml");
            string? genericPath = ResolveRepoFile(@"UI\Controls\Themes\Generic.xaml");

            if (xamlPath == null || stylesPath == null || colorsPath == null || genericPath == null)
            {
                Check("变量绑定视图样式收编（静态扫描）", true, "跳过：定位不到视图或 UI 库主题文件");
                return;
            }

            string xaml = File.ReadAllText(xamlPath);
            string stylesText = File.ReadAllText(stylesPath);
            string genericText = File.ReadAllText(genericPath);
            // 视图用的 Dialog* 键一半是样式（DialogStyles.xaml）、一半是令牌（Colors.xaml），
            // 只扫样式那一本会把"DialogAccentBrush 等一律报缺失"——那是断言自己漏了，不是视图错了
            string tokenText = File.ReadAllText(colorsPath);

            // ---- ① 视图侧：颜色不许硬编码、样式不许再抄一遍 ----
            var colors = Regex.Matches(xaml, @"#[0-9A-Fa-f]{3}\b|#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b");
            Check("【收编】视图里已无硬编码颜色（含原来那套 #409EFF / #F5F7FA，一律走令牌）",
                colors.Count == 0,
                colors.Count == 0 ? "" : string.Join(", ", colors.Select(m => m.Value).Distinct().Take(10)));

            var localDefs = ForbiddenLocalDialogKeys.Where(k => xaml.Contains($"x:Key=\"{k}\"")).ToList();
            Check("【收编】视图不再本地定义 NoWarningItemStyle / InputPortItemStyle / PresetOptionButtonStyle",
                localDefs.Count == 0,
                localDefs.Count == 0 ? "" : "仍在本地定义：" + string.Join(", ", localDefs));

            Check("【收编】视图改用 Dialog* 公共样式（栏卡片 / 选项行 / 底部栏）",
                xaml.Contains("StaticResource DialogColumnCard")
                && xaml.Contains("StaticResource DialogOptionItem")
                && xaml.Contains("StaticResource DialogFooter"), "");

            // 候选行必须显示数据类型：绑定是类型敏感的动作，"这一项能不能接住当前端口"本来就靠类型判断，
            // 列表里不显示类型就只能靠名字猜（勾了「显示全部」时更明显：不兼容项被置灰，
            // 而"为什么不行"恰恰要看类型）。这条是回归防线 —— 整栏重写时最容易把它漏掉。
            bool showsDataType = xaml.Contains("DataTypeName, Converter={ui:TypeNameToFriendlyNameConverter}");
            Check("【绑定】候选行显示数据类型（DataTypeName 走友好名转换器）",
                showsDataType,
                showsDataType
                    ? "候选行会在名称右侧显示「文本 (String)」「小数 (Double)」这类类型胶囊"
                    : "候选行模板里没有绑定 DataTypeName —— 用户将看不到类型信息");

            // ---- ② 弹窗尺寸：改前是 900×580 + NoResize（用户拉不大，三栏必然挤） ----
            Check("【尺寸】视图不再写死 900×580（改为下限 + 可缩放，窗口尺寸交给宿主 Window）",
                !xaml.Contains("Width=\"900\"") && !xaml.Contains("Height=\"580\""), "");
            Check("【尺寸】允许缩放且不再 SizeToContent（否则拖边框看不到变化）",
                xaml.Contains("ResizeMode\" Value=\"CanResizeWithGrip\"")
                && xaml.Contains("SizeToContent\" Value=\"Manual\""), "");

            // ---- ③ UI 库侧：用到的每个键都必须真有定义 ----
            var definedKeys = Regex.Matches(stylesText + tokenText + genericText, @"x:Key=""([A-Za-z0-9]+)""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            var usedKeys = Regex.Matches(xaml, @"StaticResource\s+(Dialog[A-Za-z0-9]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            var missing = usedKeys.Where(k => !definedKeys.Contains(k)).ToList();
            Check("【核心】视图用到的每个 Dialog* 键都在 UI 库里有定义（否则运行期抛\"找不到资源\"）",
                missing.Count == 0,
                missing.Count == 0 ? $"共 {usedKeys.Count} 个键全部命中" : "缺失：" + string.Join(", ", missing));

            var missingRequired = RequiredDialogStyleKeys.Where(k => !definedKeys.Contains(k)).ToList();
            Check("【核心】本次收编的公共键全部落在 DialogStyles.xaml（换主题只改 Colors.xaml）",
                missingRequired.Count == 0,
                missingRequired.Count == 0 ? "" : "缺失：" + string.Join(", ", missingRequired));

            Check("【核心】DialogStyles.xaml 已在 Generic.xaml 合并链上",
                genericText.Contains("Themes/DialogStyles.xaml"), "");

            RunWindowStyleSetterContract();
            RunMissingResourceKeyContract();
            RunDialogChromeContract();
            RunCollectedViewStyleContract();
        }

        /// <summary>
        /// 已收编弹窗的样式纪律：不许再有颜色字面量、不许再本地定义 Style。
        ///
        /// 为什么只列这几个而不是全目录：这些是**逐个改造过**的（本次整体收编 2 个 +
        /// 前几轮收编 4 个），对它们提这个要求是承诺；其余弹窗的收编状态各有历史，
        /// 一并纳入会变成"改一处报十处"，反而让人不敢碰断言。
        /// 每收编一个弹窗，就往这个名单里加一个。
        /// </summary>
        private static readonly string[] CollectedDialogViews =
        {
            "VariableBindingView.xaml", "GlobalVariableView.xaml",
            "CameraSettingsView.xaml", "CommunicationSettingsView.xaml",
            "FlowManagerView.xaml", "ConditionEditorView.xaml",
            
        };

        /// <summary>
        /// 允许保留的颜色字面量（是"确定不走令牌"，不是"待修"）。
        /// 键为文件名，值为允许出现的色值。
        /// </summary>
        private static readonly Dictionary<string, string[]> AllowedColorLiterals = new()
        {
            // 采集质量色点（绿=采集正常 / 黄=最近一次读取失败 / 灰=断线）：全项目统一约定，
            // SCADA 图层面板、流程画布、属性面板都用同一组，换肤时应整体一起动，不能只让弹窗变。
            ["GlobalVariableView.xaml"] = new[] { "#FF67C23A", "#FFE6A23C", "#FFC0C4CC", "#FF909399" },
        };

        private static void RunCollectedViewStyleContract()
        {
            var dir = ResolveRepoDir(@"VisionMaster\Views\DialogViews");
            if (dir == null)
            {
                Check("【收编】弹窗样式纪律", true, "跳过：定位不到 DialogViews");
                return;
            }

            var colorOffenders = new List<string>();
            var styleOffenders = new List<string>();
            int checkedCount = 0;

            foreach (var name in CollectedDialogViews)
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path))
                {
                    colorOffenders.Add($"{name}（文件不存在）");
                    continue;
                }
                checkedCount++;

                string raw = File.ReadAllText(path);
                var allowed = AllowedColorLiterals.TryGetValue(name, out var a) ? a : Array.Empty<string>();

                // 先剥掉 XML 注释再扫。
                // 注释里写色值是**有价值的文档**（"改前是 #409EFF / #34495e 那套 Bootstrap 配色"
                // 是后人判断"这处为什么改"的唯一线索），不该被这条纪律连带禁掉 ——
                // 而这一条断言已经因为这个理由误报过两次（上一轮 VariableBindingView、本轮四个视图）。
                string text = Regex.Replace(raw, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

                var colors = Regex.Matches(text, @"#[0-9A-Fa-f]{3}\b|#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b")
                    .Select(m => m.Value)
                    .Where(c => !allowed.Contains(c))
                    .Distinct()
                    .ToList();
                if (colors.Count > 0) colorOffenders.Add($"{name} → {string.Join(", ", colors)}");

                // 本地 <Style x:Key=...> ：形状/颜色都该取自 UI 库（DataTemplate 不算，它绑的是本视图的 VM 契约）
                var localStyles = Regex.Matches(text, @"<Style\b[^>]*x:Key=""([A-Za-z0-9]+)""")
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .ToList();
                if (localStyles.Count > 0) styleOffenders.Add($"{name} → {string.Join(", ", localStyles)}");
            }

            Check("【收编】已收编弹窗里不再有硬编码颜色（白名单里的采集质量色点除外）",
                colorOffenders.Count == 0,
                colorOffenders.Count == 0
                    ? $"已扫 {checkedCount} 个弹窗，颜色全部走 Dialog* 令牌"
                    : string.Join("；", colorOffenders));

            Check("【收编】已收编弹窗不再本地定义 Style（形状与颜色一律取自 UI 库）",
                styleOffenders.Count == 0,
                styleOffenders.Count == 0 ? "" : string.Join("；", styleOffenders));
        }

        /// <summary>
        /// 所有弹窗都必须自绘标题栏：去系统边框 + 可拖 + 有关闭绑定。
        ///
        /// 为什么值得钉：系统原生窗框与这套青蓝界面格格不入（同屏切换像两个软件），
        /// 而"漏一个"太容易了 —— 本次就是在变量管理上发现的，随后全仓一扫又找出 4 个
        /// （相机设置 / 通讯设置 / 条件逻辑配置中心 / 流程管理）。
        ///
        /// 只检查**带 prism:Dialog.WindowStyle 的文件**：这个目录里还放着嵌在 Shell 中的普通视图
        /// （如 SolutionListView），它们没有窗口样式、也不该有标题栏。
        /// </summary>
        private static void RunDialogChromeContract()
        {
            var dir = ResolveRepoDir(@"VisionMaster\Views\DialogViews");
            if (dir == null)
            {
                Check("【窗框】弹窗目录定位", true, "跳过：定位不到 DialogViews");
                return;
            }

            var missingNone = new List<string>();
            var missingDrag = new List<string>();
            var missingClose = new List<string>();
            int dialogCount = 0;

            foreach (var file in Directory.GetFiles(dir, "*.xaml"))
            {
                string text = File.ReadAllText(file);
                if (!text.Contains("prism:Dialog.WindowStyle")) continue;   // 不是弹窗
                dialogCount++;

                string name = Path.GetFileNameWithoutExtension(file);
                if (!text.Contains("Property=\"WindowStyle\" Value=\"None\"")) missingNone.Add(name);
                if (!text.Contains("IsWindowDraggable")) missingDrag.Add(name);

                // "关闭入口"有两种合法形态：标题栏右上角的 ×（CloseCommand），
                // 或底部的「取消 / 确定」按钮（CancelCommand / ConfirmCommand）。
                // 去掉系统标题栏时至少要有其中之一，否则这个弹窗关不掉。
                if (!text.Contains("CloseCommand")
                    && !text.Contains("CancelCommand")
                    && !text.Contains("ConfirmCommand"))
                {
                    missingClose.Add(name);
                }
            }

            Check("【窗框】所有弹窗都已去掉系统标题栏（改自绘，与青蓝界面统一）",
                missingNone.Count == 0,
                $"共找到 {dialogCount} 个带窗口样式的弹窗；"
                + (missingNone.Count == 0 ? "全部自绘" : "仍用系统窗框：" + string.Join(", ", missingNone)));

            Check("【窗框】每个弹窗的自绘标题栏都可拖动（无边框窗口没有系统拖动区）",
                missingDrag.Count == 0,
                missingDrag.Count == 0 ? "" : "缺 ui:WindowDragBehavior：" + string.Join(", ", missingDrag));

            Check("【窗框】每个弹窗都有关闭入口（标题栏 × 或底部取消/确定，二选一）",
                missingClose.Count == 0,
                missingClose.Count == 0
                    ? "全部具备（无边框窗口没有系统关闭按钮，这一条是关不关得掉的底线）"
                    : "两个都没有：" + string.Join(", ", missingClose));
        }

        /// <summary>
        /// 视图里不许出现 <c>{StaticResource BooleanToVisibilityConverter}</c>。
        ///
        /// 【为什么】这个键在 App.xaml（只有 Icon / ExpandToggleButtonTemplate / LinkButtonStyle）
        /// 和 UI 库的任何合并字典里都**没有定义**。UI 库里只有
        /// <c>UI/Controls/Converters/BooleanToVisibilityConverter .cs</c> 这个**类**，
        /// 通过 ui 命名空间映射使用，正确写法是 <c>{ui:BooleanToVisibilityConverter}</c>。
        ///
        /// 写错的后果是运行期抛「无法找到名为"BooleanToVisibilityConverter"的资源」——
        /// 而它在**模板内部**时更阴：StaticResource 延迟到模板实例化才解析，
        /// 列表为空就一直不报错，等哪天列表有行了几何级地突然炸（本次实测两处：
        /// VariableBindingView 的 InputPortTemplate 内、PluginConfigShellView 第 117 行）。
        /// </summary>
        private static void RunMissingResourceKeyContract()
        {
            var offenders = new List<string>();
            foreach (var root in new[] { ResolveRepoDir(@"VisionMaster\Views"), ResolveRepoDir(@"Plugins") })
            {
                if (root == null) continue;
                foreach (var file in Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories))
                {
                    if (file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
                    if (File.ReadAllText(file).Contains("{StaticResource BooleanToVisibilityConverter}"))
                        offenders.Add(Path.GetFileName(file));
                }
            }

            Check("【资源】没有视图引用不存在的资源键（{StaticResource BooleanToVisibilityConverter} → 应为 {ui:...}）",
                offenders.Count == 0,
                offenders.Count == 0
                    ? "已扫 VisionMaster\\Views 与 Plugins 全部 xaml"
                    : "会炸：运行期抛「找不到资源」——" + string.Join(", ", offenders.Distinct()));
        }

        /// <summary>
        /// 宿主窗口样式（<c>prism:Dialog.WindowStyle</c>）里只准写依赖属性。
        ///
        /// 【为什么单列一条】Style 的 Setter 只能设 DependencyProperty，而
        /// <c>Window.WindowStartupLocation</c> 是个普通 CLR 属性（没有 WindowStartupLocationProperty）。
        /// 把它写进 Setter 的后果是**打开弹窗时**抛：
        ///     XamlParseException「设置属性 System.Windows.Setter.Property 时引发了异常」
        ///     内层 ArgumentNullException: Value cannot be null. (Parameter 'property')
        /// 行号指向那一行，看着像"这行写法不对"，其实这个属性压根不能出现在 Style 里。
        ///
        /// 而且它**三道关卡都拦不住**：XAML 编译（BAML）能过（BAML 不校验 Setter.Property）、
        /// 资源字典加载能过（压根不碰视图 BAML）、键名静态扫描也能过 ——
        /// 只有真的构造视图才会炸（本轮实测：探针 new VariableBindingView() 一步定位）。
        /// </summary>
        private static void RunWindowStyleSetterContract()
        {
            var dir = ResolveRepoDir(@"VisionMaster\Views\DialogViews");
            if (dir == null)
            {
                Check("【窗口样式】弹窗目录定位", true, "跳过：定位不到 DialogViews");
                return;
            }

            // 已知会炸的非依赖属性（实测结论，不是推测）
            string[] notDependencyProperties = { "WindowStartupLocation" };

            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(dir, "*.xaml"))
            {
                string text = File.ReadAllText(file);
                foreach (var prop in notDependencyProperties)
                {
                    if (text.Contains($"Property=\"{prop}\""))
                        offenders.Add($"{Path.GetFileName(file)} → {prop}");
                }
            }

            Check("【窗口样式】没有任何弹窗把非依赖属性（WindowStartupLocation）写进 Style 的 Setter",
                offenders.Count == 0,
                offenders.Count == 0
                    ? "已扫全部弹窗：只有 WindowStyle / ResizeMode / SizeToContent / Width / Height / MinWidth / MinHeight 这类 DP"
                    : "会炸：打开弹窗时抛 ArgumentNullException('property') —— " + string.Join(", ", offenders));
        }

        /// <summary>从输出目录往上找仓库根，再拼相对路径（定位不到返回 null，由调用方跳过）</summary>
        private static string? ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>同 ResolveRepoFile，但定位目录</summary>
        private static string? ResolveRepoDir(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>造一个常量连线 —— 与 Confirm 写入时的构造方式保持一致</summary>
        private static LinkReference Constant(string value)
            => new(LinkKind.Constant, Guid.Empty, value, $"{LinkProtocol.ConstantDisplayPrefix}{value}");
    }
}
