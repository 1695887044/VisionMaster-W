using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Core.Interfaces;
using Plugin.Motion.Virtual;
using Plugin.Motion.ZMotion;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 正运动驱动 + 运动调试面板的断言。
    ///
    /// 这批全是**不需要真卡**也能验、而一旦写错只有真机才炸的那些：
    ///   · 回零模式码（填错一个数字，轴会朝反方向撞过去）；
    ///   · 状态位域（把报警读成到位，安全停机就不触发）；
    ///   · 机型能力（轴数/IO 数报大了，界面会允许操作不存在的轴）；
    ///   · 故障分级与建议（决定现场"复位一下"还是"叫维修"）；
    ///   · native 库投递位置（投错地方 = 现场一连接就找不到 zauxdll.dll）。
    /// </summary>
    internal static class MotionZMotionChecks
    {
        /// <summary>
        /// 在可视树里按 x:Name 递归找元素。
        ///
        /// 控件模板（ControlTemplate）内部的元素用 FrameworkTemplate.FindName 才找得到，
        /// 但跨 ToggleButton 的嵌套模板（ComboBox → ToggleButton → Border）层次太深，
        /// 直接递归可视树反而最直接 —— 这只服务于"模板是否真的被应用"这一类探测。
        /// </summary>
        private static System.Windows.FrameworkElement? FindByName(
            System.Windows.DependencyObject root, string name)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.FrameworkElement fe && fe.Name == name) return fe;

                var hit = FindByName(child, name);
                if (hit != null) return hit;
            }

            return null;
        }

        public static void Run()
        {
            Section("[Z] 正运动驱动与调试面板：回零码 / 机型 / 位域 / 故障分级 / 部署 / 点动");

            RunHomeModeMapping();
            RunModelCapabilities();
            RunStatusBits();
            RunFaultInterpretation();
            RunDeploymentContract();
            RunJogEndToEnd();
            RunJogWatchdog();
            RunDebugPanelContract();
            RunThemeKeyUniquenessContract();
            RunProviderRecursionGuardContract();
            RunInterruptingCommandContract();
            RunStepConfigOptionContract();
        }

        // ==================================================================
        //  ⑫ 轴 / 卡参数的可选化（动态候选下拉）
        // ==================================================================

        /// <summary>
        /// 运动插件的"轴名 / 卡地址"必须标注成**动态候选**，且属性面板认得它。
        ///
        /// 【为什么单列一条】这两类参数是纯 string，默认渲染成文本框 ——
        /// 用户得先去别处翻配置、记住逻辑名、再回来敲，敲错要到运行期才报
        /// "没有名为 X 的轴"（反馈原话："不够直观，难以直观地选择指定的轴"）。
        /// 标注是"约定"：漏标一个插件不会报错，只会静默退回成文本框，所以要用断言扫一遍。
        /// </summary>
        private static void RunStepConfigOptionContract()
        {
            Section("[Z] 轴 / 卡参数的可选化（动态候选下拉）");

            var dir = ResolveRepoDir(@"Plugins\Plugin.Motion.Steps");
            if (dir == null)
            {
                Check("轴/卡参数标注", true, "跳过：定位不到运动插件目录");
                return;
            }

            var files = Directory.GetFiles(dir, "Motion*Plugin.cs");
            var missingAxis = new List<string>();

            // ★ 卡地址已彻底废弃：流程里寻址只认轴名（轴级）与卡名（IO 等卡级操作）。
            //   任何"按地址寻址"的痕迹回到代码里，都会把物理接线重新写进工艺 ——
            //   换网段/换卡就要改流程，这正是当初要消灭的东西。这里做反向断言守住它。
            var addressLeftovers = new List<string>();

            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);

                // IO 插件没有轴概念（它是卡级操作，按卡名选卡），所以按"是否有该参数"判断，不是按文件
                if (text.Contains("InputPort<string> Axis")
                    && !text.Contains("StepConfigOptionKind.MotionAxisName"))
                {
                    missingAxis.Add(name);
                }

                if (text.Contains("MotionCardAddress") || text.Contains("CardKey"))
                    addressLeftovers.Add(name);
            }

            Check($"★全部 {files.Length} 个运动插件的「轴」参数都标了动态候选",
                missingAxis.Count == 0,
                missingAxis.Count == 0
                    ? ""
                    : "漏标：" + string.Join("、", missingAxis) + "（漏了不会报错，只会静默退回成手打文本框）");

            Check("★运动插件里不再出现「卡地址」寻址（MotionCardAddress / CardKey 已删除）",
                addressLeftovers.Count == 0,
                addressLeftovers.Count == 0 ? "" : "仍有残留：" + string.Join("、", addressLeftovers));

            var panelPaths = new[]
            {
                @"UI\Controls\CustomControl\PropertyGrid\CardPropertyGrid.cs",
                @"UI\Controls\CustomControl\PropertyGrid\FlatPropertyGrid.cs",
            };
            bool registered = panelPaths.All(path =>
            {
                var f = ResolveRepoFile(path);
                return f != null && File.ReadAllText(f).Contains("new OptionSourceGenerator()");
            });
            Check("★两个属性面板都注册了动态候选生成器（漏一个就有一半场景仍是文本框）",
                registered, "");

            var boardView = ResolveRepoFile(@"VisionMaster\Views\DialogViews\MotionBoardView.xaml");
            Check("板卡窗口最小高 680（否则默认尺寸下页签内容显示不全）",
                boardView != null && File.ReadAllText(boardView).Contains("Value=\"680\""),
                "用户反馈：旧版轴映射区域在默认尺寸下无法显示 —— 合并窗口用最小高度保底");

            // ★ 下面一组是用户指出"标注了特性也没法被赋值"之后补的。
            //   [StepConfig] 按框架设计**不暴露为端口、不可变量链接**（见 StepConfigAttribute 的注释），
            //   所以只有常量的话，"轴由上游决定"（配方切换、上位机指定轴）根本落不了地 ——
            //   光在属性面板加个下拉，解决的只是"填常量更方便"，不是"能赋值"。
            //   必须是**输入端口**才能被链接赋值（InputPort.ActualValue 的取值优先级是链接值 > 手填值）。
            var missingPort = new List<string>();
            var unusedPort = new List<string>();

            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);

                // IO 插件是卡级操作：它必须有"运动卡"端口（按卡名），否则无法配置
                if (text.Contains("MotionIoPlugin") && !text.Contains("InputPort<string> Card"))
                    missingPort.Add(name + "（Card）");
                if (text.Contains("InputPort<string> Axis") == false && name != "MotionIoPlugin.cs")
                    missingPort.Add(name + "（Axis）");

                // 有端口却从不读 ActualValue = 摆设：链接上去也不生效
                if (text.Contains("InputPort<string> Card") && !text.Contains("Card.ActualValue"))
                    unusedPort.Add(name + "（Card）");
                if (text.Contains("InputPort<string> Axis") && !text.Contains("Axis.ActualValue"))
                    unusedPort.Add(name + "（Axis）");
            }

            Check("★每个插件的「轴 / 卡」都有可链接的输入端口"
                  + "（只靠 [StepConfig] 常量的话，编辑流程时赋不了值——它不暴露为端口）",
                missingPort.Count == 0,
                missingPort.Count == 0 ? "" : "缺端口：" + string.Join("、", missingPort));

            Check("★端口在运行期真的被读取（有端口却只读常量 = 摆设，链接上去也不生效）",
                unusedPort.Count == 0,
                unusedPort.Count == 0 ? "" : "未读取 ActualValue：" + string.Join("、", unusedPort));

            // ★ 设备名（显示名）与"候选值必须是地址"这一组。
            //   背景：多张卡时列表/候选里只有 IP（127.0.0.1 / 192.168.0.11）认不出谁是谁，
            //   所以显示带设备名；但**写进流程的值必须仍是地址** ——
            //   否则用户改一次设备名，已经配好的流程就全断了（与 MotionDescriptor 那句
            //   "引用一律用 Id，不要用显示名"是同一个坑）。
            var boardSettings = ResolveRepoFile(@"VisionMaster\Views\DialogViews\MotionBoard\MotionBoardSettingsView.xaml");
            string boardSettingsText = boardSettings != null ? File.ReadAllText(boardSettings) : string.Empty;
            Check("★已添加的卡可以改设备名（新增对话框里能填、加完就没入口了，而现场往往接线后才想起改名）",
                boardSettingsText.Contains("Binding DeviceName"),
                boardSettings == null ? "定位不到 MotionBoardSettingsView.xaml" : "");

            var motionModule = ResolveRepoFile(@"VisionMaster\Modules\MotionModule.cs");
            string moduleText = motionModule != null ? File.ReadAllText(motionModule) : string.Empty;
            Check("★卡候选的**值**仍是地址（显示可以带设备名，但写进流程的必须是稳定的地址键）",
                moduleText.Contains("new StepConfigOption(") && moduleText.Contains("c.Address"),
                "值一旦改成显示文本，用户改设备名就会让已配流程全部失效");

            Check("候选的显示名只在设备名非空时才加括号（否则显示成「127.0.0.1（127.0.0.1）」更乱）",
                moduleText.Contains("string.IsNullOrWhiteSpace(c.DisplayName)"), "");

            var bindingView = ResolveRepoFile(@"VisionMaster\Views\DialogViews\VariableBindingView.xaml");
            string bindingViewText = bindingView != null ? File.ReadAllText(bindingView) : string.Empty;
            Check("预设下拉显示 Display、写入 Option（值与显示分离）",
                bindingViewText.Contains("{Binding Display}") && bindingViewText.Contains("SelectedValuePath=\"Option\""),
                bindingView == null ? "定位不到 VariableBindingView.xaml" : "");

            // ★ "框架再包一层"：没有自定义视图的插件，由宿主自动生成参数面板。
            //   此前是回退到「变量绑定」窗口 —— 那是个两步式交互（先选左边端口、再在右边选上游），
            //   只想填个常量也得绕一圈。改成参数清单后一步到位，与有视图插件共用同一个外壳。
            var processVm = ResolveRepoFile(@"VisionMaster\ViewModels\ProcessViewModel.cs");
            string pvText = processVm != null ? File.ReadAllText(processVm) : string.Empty;

            // 注意：不能断言"整个文件没有 DataBindView" —— 那里另外两处是**合法**的，
            // 是插件自定义视图里点 🔗 时的"单绑定模式"回调，与这条路径无关。
            Check("★无自定义视图的插件由框架自动包一层参数面板（不再回退到两步式的变量绑定窗口）",
                pvText.Contains("new AutoPortConfigView(") && pvText.Contains("if (view == null)"),
                "回退到 DataBindView 的话，填个常量要绕一圈（先选端口、再选上游）");

            // ★ 这条是"真"验证：拿一个确实没有自定义视图的插件，看它的输入端口能否被拿到。
            //   用户实测到的现象是"弹窗建出来了、里面一片空白" —— 根因就是插件实例解析返回了 null
            //   （旧逻辑对无视图插件直接 return null），而光靠读源码的文本断言发现不了这种问题。
            var delayPlugin = new VisionMaster.Plugins.Util.DelayPlugin();
            Check("★没有自定义视图的插件也能拿到输入端口（拿不到的话参数面板就是一片空白）",
                delayPlugin is not IPluginCustomViewProvider && delayPlugin.Inputs.Count > 0,
                $"自定义视图={delayPlugin is IPluginCustomViewProvider} 输入端口数={delayPlugin.Inputs.Count}");

            Check("★建界面前先把已保存的配置灌进端口（否则界面显示默认值，看着像「改完没保存」）",
                pvText.Contains("(pluginInstance as VisionPluginBase)?.Initialize(stepData)"), "");

            Check("只在「框架生成」这条分支里灌值（插件自带视图在 GetConfigView 里已灌过，重复灌是多余动作）",
                pvText.Contains("if (view == null)")
                && pvText.IndexOf("(pluginInstance as VisionPluginBase)?.Initialize(stepData)", StringComparison.Ordinal)
                   > pvText.IndexOf("if (view == null)", StringComparison.Ordinal),
                "");

            Check("★插件实例解析不再要求「必须实现 IPluginCustomViewProvider」"
                  + "（那样无视图插件会拿到 null，面板就是一片空白）",
                pvText.Contains("return Activator.CreateInstance(type);")
                && !pvText.Contains("IsAssignableFrom(type))") ,
                "解析里若还有 IsAssignableFrom 的提前 return，无视图插件就又拿不到实例了");

            var shellVm = ResolveRepoFile(@"VisionMaster\ViewModels\DialogViewModels\PluginConfigShellViewModel.cs");
            string shellText = shellVm != null ? File.ReadAllText(shellVm) : string.Empty;
            Check("★外壳认两种视图形态（接口在 DataContext 上 / 直接实现在控件自身上）",
                shellText.Contains("viewObj as IPluginConfigView") && shellText.Contains("viewObj.DataContext as IPluginConfigView"),
                "只认一种的话，框架生成的参数面板 Initialize 不会被调到，弹窗里是空白");

            Check("两种插件共用同一个外壳（有视图用插件的，没视图用框架生成的）",
                pvText.Contains("GetConfigView(stepData) as FrameworkElement") && pvText.Contains("new AutoPortConfigView("), "");

            var autoView = ResolveRepoFile(@"VisionMaster\Views\DialogViews\AutoPortConfigView.xaml.cs");
            string autoText = autoView != null ? File.ReadAllText(autoView) : string.Empty;
            Check("自动参数层一行一个可链接编辑器（标签 + 值 + 🔗 + ✕），且标签优先用端口描述",
                autoText.Contains("LinkableValueEditor") && autoText.Contains("port.Description"),
                autoView == null ? "定位不到 AutoPortConfigView.xaml.cs" : "");

            Check("自动参数层幂等重建（外壳与视图都可能调 Initialize，不幂等会重复出行）",
                autoText.Contains("PortPanel.Children.Clear()"), "");

            // ★ 枚举候选。用户在「轴运动」的参数面板里看不到"绝对/相对"的候选值，只看到一个空文本框。
            //   根因有两条：
            //     ① 自动参数面板用的 LinkableValueEditor 对枚举也只给 TextBox（要手打 Absolute/Relative）；
            //     ② 绑定窗口的候选走 PresetOptions 快照，而快照是插件扫描期生成的，枚举候选没被带过来。
            //   枚举候选其实**随时能从端口类型算出来**，根本不需要快照 —— 所以两处都改成现场推。
            var linkable = ResolveRepoFile(@"Shard\Core.Controls\LinkableValueEditor.xaml");
            string linkableXaml = linkable != null ? File.ReadAllText(linkable) : string.Empty;
            var linkableCs = ResolveRepoFile(@"Shard\Core.Controls\LinkableValueEditor.xaml.cs");
            string linkableText = linkableCs != null ? File.ReadAllText(linkableCs) : string.Empty;

            Check("★枚举端口用下拉（手打 Absolute/Relative 既易错、也看不出有哪些合法值）",
                linkableXaml.Contains("PART_EnumBox") && linkableText.Contains("IsOptionPort"),
                linkable == null ? "定位不到 LinkableValueEditor.xaml" : "");

            Check("枚举下拉与固定值框互斥（两者同在 Column 1，不互斥会叠在一起）",
                linkableXaml.Contains("IsOptionPort") && linkableXaml.Contains("Visibility\" Value=\"Collapsed\""), "");

            // ★ 枚举是**封闭集合**：给了候选就不该再允许绑定或手输。
            //   绑定的语义是"值由上游运行时决定"，而上游的值未必是合法枚举；
            //   "清除"更无处可去 —— 没有一个"空枚举"可以表示。
            //   需要"由上游决定走哪个分支"时应当用普通端口（string/int），那是另一种表达能力。
            Check("★枚举端口不提供链接按钮（上游值未必是合法枚举，绑上去等于把非法值塞进封闭集合）",
                linkableText.Contains("enumLocked") && linkableText.Contains("PART_LinkBtn.Visibility = enumLocked"),
                "枚举还能绑的话，运行时会出现非法枚举值");

            Check("★枚举端口不提供清除按钮（清掉之后没有任何「空枚举」可以表示）",
                linkableText.Contains("enumLocked || (!IsTextEditable && !IsLinked)"), "");

            Check("输入框按规格做成圆角浅灰描边（TextBox 无 CornerRadius，必须走模板）",
                linkableXaml.Contains("CornerRadius=\"8\"") && linkableXaml.Contains("0EA5E9"), "");

            Check("参数标签固定宽 112 且右对齐（左对齐+宽度随文字伸缩会让每行输入框起点不齐）",
                linkableXaml.Contains("Width=\"112\"") && linkableXaml.Contains("TextAlignment=\"Right\""), "");

            var bindingVmFile = ResolveRepoFile(@"VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs");
            string bindingVmText = bindingVmFile != null ? File.ReadAllText(bindingVmFile) : string.Empty;
            Check("★枚举候选从端口**类型**现场推（不依赖 PresetOptions 快照，那条路会拿到空）",
                bindingVmText.Contains("type.IsEnum") && bindingVmText.Contains("Enum.GetNames(type)"), "");

            // 真验证：这条链的前提是"能从类型名解析回枚举类型"，解析失败的话候选永远是空的。
            var enumType = VisionMaster.Helpers.TypeHelper.ResolveType(typeof(StringComparison).AssemblyQualifiedName);
            Check("★类型名能解析回枚举类型（解析不回来，枚举候选就永远是空的）",
                enumType != null && enumType.IsEnum && Enum.GetNames(enumType).Length > 0,
                $"解析结果={enumType?.Name ?? "null"}");

            Check("自动参数面板的行有宽度与行距（不限宽时输入框会一路铺到屏幕最右边）",
                autoText.Contains("Width = 520") && autoText.Contains("Thickness(0, 0, 0, 6)"), "");

            // ★ 参数面板窗口尺寸：一条参数不该出现「一屏空白」。
            //   外壳默认尺寸是给"插件自带视图"准备的固定大画布（创建 ROI 那类需要足够绘制区），
            //   而框架生成的参数面板只有几行 —— 所以按内容类型分流：只有后者自适应高度。
            var shellView = ResolveRepoFile(@"VisionMaster\Views\DialogViews\PluginConfigShellView.xaml");
            string shellXaml = shellView != null ? File.ReadAllText(shellView) : string.Empty;
            var shellCs = ResolveRepoFile(@"VisionMaster\Views\DialogViews\PluginConfigShellView.xaml.cs");
            string shellCsText = shellCs != null ? File.ReadAllText(shellCs) : string.Empty;

            Check("★参数区行高为 *（外壳窗口固定高，行给 Auto 时插件自带视图只画自然高、窗口下半截全空；自动面板靠 SizeToContent=Height 时 * 行照样缩到内容高）",
                shellXaml.Contains("Height=\"*\""), "");

            // 自适应由**参数面板自己**做：外壳去猜内容类型会踩时序
            // （它的 Loaded 触发时那段 Content 绑定可能还没把内容挂上，判断落空 → 尺寸没改 →
            //   表现就是"只有一条参数、窗口照样一大块空白"）。
            string autoViewCs = autoView != null ? File.ReadAllText(autoView) : string.Empty;
            Check("★参数面板自己在 Loaded 里改窗口尺寸（外壳猜内容类型会踩时序，上一版就没生效）",
                autoViewCs.Contains("Window.GetWindow(this)") && autoViewCs.Contains("SizeToContent"),
                autoView == null ? "定位不到 AutoPortConfigView.xaml.cs" : "");

            Check("★自适应时必须同时调小 MinHeight（外壳那个 420 是给插件大视图的保底，留着就还是空白）",
                autoViewCs.Contains("MinHeight = 180"), "");

            Check("★按钮圆角走模板（Button 没有 CornerRadius，改默认模板里的 Border 是无效的——上一版就是直角）",
                shellXaml.Contains("ControlTemplate TargetType=\"Button\"") && shellXaml.Contains("CornerRadius=\"8\""), "");

            Check("外壳不再自己判断窗口尺寸（职责在参数面板，避免两处逻辑打架）",
                !shellCsText.Contains("SizeToContent"), "");

            Check("★外壳配色全部走令牌（不再散落 #409EFF / #303133 / #909399 这类硬编码）",
                !shellXaml.Contains("#409EFF") && !shellXaml.Contains("#303133")
                && shellXaml.Contains("DialogAccentBrush") && shellXaml.Contains("DialogShadowColor"),
                "硬编码色值换主题时会被漏掉；DialogShadowColor 是 Color 类型（供 DropShadowEffect）");

            Check("头部按规格：图标徽章（40px 圆角 12）+ 标题 16 SemiBold + 副标题 12",
                shellXaml.Contains("CornerRadius=\"12\"") && shellXaml.Contains("FontSize=\"16\"")
                && shellXaml.Contains("FontWeight=\"SemiBold\""), "");

            Check("头部有关闭按钮（复用 CancelCommand —— 取消逻辑里含「正在执行先停」的处理）",
                shellXaml.Contains("ToolTip=\"关闭\"") && shellXaml.Contains("CancelCommand"), "");

            Check("底部：状态胶囊 + 耗时等宽字体 + 执行按钮带 ▶ 图标",
                shellXaml.Contains("StatusChipStyle") && shellXaml.Contains("Consolas")
                && shellXaml.Contains("▶"), "");

            Check("参数面板里的提示条是浅主色圆角条（不是一段裸文字，否则像界面出故障）",
                autoText.Contains("BuildHintBar") && autoText.Contains("DialogAccentLightBrush"), "");

            // ★ "方案提供的常量值不能被绑定"：轴名（以及卡名）的合法值由方案决定，
            //   绑上游等于允许流程填一个方案里不存在的轴 —— 那不是灵活，是把编译期能发现的问题
            //   推到运行期（"找不到轴"）。判据用 IsFunctionalEnum（其原意即"不需要链接上游变量"）。
            // ★ 实测「只能选」端口：轴名端口（IsFunctionalEnum=true）必须被识别为只能选，
            //   且候选按 OptionKind 现取 —— 这是"几根轴认不出谁是谁"的直接验证。
            //   这里注册一个模拟宿主的提供器（真宿主在 MotionModule 里注册的是方案实时查询）。
            //
            // 注意：这里用**轴名**做探针而不是卡地址 —— 运动卡地址已在上线前彻底废弃，
            // 流程里寻址只认轴名（轴级）与卡名（IO 等卡级操作），地址只活在「运动卡设置」里。
            Core.Interfaces.StepConfigOptionSource.Register(kind =>
                kind == Core.Interfaces.StepConfigOptionKind.MotionAxisName
                    ? new[] { new Core.Interfaces.StepConfigOption("X", "X") }
                    : Array.Empty<Core.Interfaces.StepConfigOption>());

            string optionProbe = "未执行";
            var optionThread = new System.Threading.Thread(() =>
            {
                var plugin = new Plugin.Motion.Steps.MotionMovePlugin();
                var editor = new Core.Controls.LinkableValueEditor { Port = plugin.Axis };
                editor.Measure(new System.Windows.Size(520, 40));
                editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                editor.UpdateLayout();

                optionProbe = $"只能选={editor.IsOptionPort} 候选数={editor.EnumOptions.Count}"
                              + $" 显示={(editor.EnumOptions.Count > 0 ? editor.EnumOptions[0].Display : "-")}"
                              + $" 值={(editor.EnumOptions.Count > 0 ? editor.EnumOptions[0].Value : "-")}";
            });
            optionThread.SetApartmentState(System.Threading.ApartmentState.STA);
            optionThread.Start();
            optionThread.Join(8000);

            Check("★轴名端口被识别为「只能选」且候选来自方案（写进流程的就是轴名本身）",
                optionProbe.Contains("只能选=True") && optionProbe.Contains("候选数=1")
                && optionProbe.Contains("X"),
                optionProbe);

            Check("★下拉候选与枚举共用一套机制（IsOptionPort = 枚举 ∪ 方案候选），不再只认枚举",
                linkableText.Contains("IsOptionPort") && linkableText.Contains("port.IsFunctionalEnum"), "");

            Check("下拉候选值/显示分离（DisplayMemberPath=Display、SelectedValuePath=Value）",
                linkableXaml.Contains("DisplayMemberPath=\"Display\"") && linkableXaml.Contains("SelectedValuePath=\"Value\""),
                "不分离的话，选了「运动卡2（192.168.0.11）」会把整串文字写进流程");

            // ★ 选中项的显示必须用 Binding 取 SelectionBoxItemTemplate：
            //   DisplayMemberPath 生成的模板是"第一次选中"时才有值的，
            //   TemplateBinding 在模板应用那一刻取值（当时 null）且不跟随变化 ——
            //   结果就是收起状态下直接调 StepConfigOption.ToString()，显示成类型全名。
            Check("★选中项显示用 Binding 取 SelectionBoxItemTemplate（TemplateBinding 不跟随后期变化）",
                linkableXaml.Contains("SelectionBoxItemTemplate, RelativeSource={RelativeSource TemplatedParent}")
                && !linkableXaml.Contains("TemplateBinding SelectionBoxItemTemplate"),
                "回退成 TemplateBinding 的现象：下拉框收起时显示 Core.Interfaces.StepConfigOption");

            // ★ 试运行/正式运行都读 InputValues，而"必填"的判据此前**只看有没有链接** ——
            //   于是界面里明明填了 500，一执行仍报「必填参数 'Delay' 未配置」。
            var compiler = ResolveRepoFile(@"Engine\FlowCompiler.cs");
            string compilerText = compiler != null ? File.ReadAllText(compiler) : string.Empty;
            Check("★必填参数判据含「界面填过常量」（只看 LinkedSource 会把填了常量的步骤判成未配置）",
                compilerText.Contains("hasConstant") && compilerText.Contains("InputValues")
                && compilerText.Contains("input.IsRequired && input.LinkedSource == null && !hasConstant"),
                "不修的话现象是「输入常量没反应，执行仍报参数缺失」");

            // 居中用行为实现。两条弯路都别再走：
            //   ① 写进 XAML Style 的 Setter —— WindowStartupLocation 不是依赖属性，开窗即抛异常；
            //   ② 在 View 的 Loaded 里设 WindowStartupLocation —— 位置在"显示那一刻"就算完了，
            //      此时设已经晚了（现象：弹窗留在系统给的默认位置，偏左上或被侧栏压住）。
            // 直接改 Left/Top 才是立即生效的，所以行为里就是这么做的。
            var centering = ResolveRepoFile(@"UI\Controls\Behaviors\WindowCenteringBehavior.cs");
            string centeringText = centering != null ? File.ReadAllText(centering) : string.Empty;
            Check("★弹窗居中用附加行为（设 Left/Top 立即生效；WindowStartupLocation 时机已过）",
                centeringText.Contains("window.Left =") && centeringText.Contains("window.Top ="),
                centering == null ? "定位不到 WindowCenteringBehavior.cs" : "");

            Check("按工作区居中而不是整屏（任务栏在下方时，按整屏居中看着像没居中）",
                centeringText.Contains("SystemParameters.WorkArea"), "");

            Check("★参数面板与变量绑定窗口都挂了居中行为",
                shellXaml.Contains("WindowCenteringBehavior") && bindingViewText.Contains("WindowCenteringBehavior"),
                "");

            Check("枚举下拉自带圆角模板（默认是系统灰底方角样式，混在圆角输入框里很突兀）",
                linkableXaml.Contains("ComboBox.Template") && linkableXaml.Contains("PART_Popup"), "");

            // ★ 实测：枚举下拉到底有没有套上自定义模板。
            //   只读 XAML 证明不了 —— 模板可能被显式 Style 或别处的隐式样式盖掉，
            //   而现象（箭头在文字左侧、无边框、宽度不撑满）恰恰是"系统默认模板"的样子。
            //   判据用自定义模板里独有的 `Bd`（系统 ComboBox 模板里没有这个名字）。
            string probe = "未执行";
            try
            {
                var t = new System.Threading.Thread(() =>
                {
                    var editor = new Core.Controls.LinkableValueEditor
                    {
                        Port = new InputPort<System.StringComparison>(
                            "T", System.StringComparison.Ordinal, "探测"),
                    };
                    editor.Measure(new System.Windows.Size(520, 40));
                    editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                    editor.UpdateLayout();

                    var box = editor.FindName("PART_EnumBox") as System.Windows.Controls.ComboBox;
                    bool hasCustomBd = box != null && FindByName(box, "Bd") != null;
                    probe = $"下拉实例={box != null} 自定义模板命中={hasCustomBd} 宽={box?.ActualWidth:F0}";
                });
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.Start();
                t.Join(8000);
            }
            catch (Exception ex)
            {
                probe = "探测异常：" + ex.Message;
            }

            Check("★枚举下拉套用的是自定义模板（命中 Bd 圆角边框；系统模板里没有这个名字）",
                probe.Contains("自定义模板命中=True"), probe);

            // ★ 执行上下文必须注入 IMotionProvider。
            //   此前全工程没有任何一处给 ExecutionContext.Motions 赋值 —— 它永远是 NullMotionProvider，
            //   正式运行和试运行都会在"取卡"这一步失败，现象是：
            //   界面里明明选了「正运动运动控制卡（127.0.0.1）」，一执行就报「找不到运动卡 127.0.0.1」。
            //   （相机当年就踩过并修了同样的坑 —— Cameras 有注入而 Motions 没有，对照即可发现。）
            var engineService = ResolveRepoFile(@"Engine\FlowEngineService.cs");
            string engineText = engineService != null ? File.ReadAllText(engineService) : string.Empty;
            Check("★正式运行的执行上下文注入了 IMotionProvider（此前永远是 NullMotionProvider）",
                engineText.Contains("Motions = _motions") && engineText.Contains("IMotionProvider motions"),
                "不注入的话，运动步骤永远报「找不到运动卡」");

            var testRunner = ResolveRepoFile(@"Engine\PluginTestRunner.cs");
            string runnerText = testRunner != null ? File.ReadAllText(testRunner) : string.Empty;
            Check("★试运行的执行上下文同样注入（与正式运行共用同一条组装规则）",
                runnerText.Contains("Motions = motions ?? NullMotionProvider.Instance")
                && runnerText.Contains("IMotionProvider motions = null"),
                testRunner == null ? "定位不到 PluginTestRunner.cs" : "");

            // ★ 轴点位表（每轴 16 点：位置/速度/加减速/曲线 + 走此点）。
            //   模型随方案 JSON 落盘（与轴映射同一体系，不引入 SQLite）；
            //   曲线映射到正运动 SetSramp（梯形=0 / S曲线=200ms）；匀速不提供 —— 真卡没有对应物。
            var pointModel = ResolveRepoFile(@"Shard\Core.Interfaces\Motion\MotionPoint.cs");
            Check("★每轴 16 点点位模型随方案落盘（固定行数让「点位号」有稳定指代）",
                pointModel != null && File.ReadAllText(pointModel).Contains("PointsPerAxis = 16"),
                pointModel == null ? "定位不到 MotionPoint.cs" : "");

            var zmotionCard = ResolveRepoFile(@"Plugins\Plugin.Motion.ZMotion\ZMotionCard.cs");
            string cardText = zmotionCard != null ? File.ReadAllText(zmotionCard) : string.Empty;
            Check("★曲线映射到正运动 SetSramp（梯形=0 / S曲线=200ms；Accel/Decel≤0 不覆盖现场参数）",
                cardText.Contains("ApplyAccelAndCurve") && cardText.Contains("SetSramp")
                && cardText.Contains("200f : 0f"),
                zmotionCard == null ? "定位不到 ZMotionCard.cs" : "");

            var withAxisPort = files.Where(f => File.ReadAllText(f).Contains("InputPort<string> Axis")).ToList();
            Check("轴端口 IsRequired=false（老流程只填常量，不能因为没链接就判编译失败）",
                withAxisPort.All(f => File.ReadAllText(f).Contains("IsRequired = false")), "");

            Check("轴端口启用 IsFunctionalEnum（绑定界面上显示为下拉，候选来自当前方案）",
                withAxisPort.All(f => File.ReadAllText(f).Contains("IsFunctionalEnum = true")), "");

            // ★ 这一组盯的是"候选为什么会是空的"。
            //   PresetOptions 是插件**扫描期**（宿主反射 new 实例读端口）算出来的，
            //   那一刻方案还没配卡，取到的是空列表 —— 空值会被复制进 PortDefinition 带到界面上，
            //   现象就是"绑定窗口里轴名/卡地址没有下拉"。
            //   所以端口必须声明 OptionKind（要哪一类候选），由绑定界面**每次打开时现取**。
            Check("★运动端口声明了 OptionKind（候选项随方案变化，不能在扫描期取一次就存下来）",
                files.All(f => !File.ReadAllText(f).Contains("InputPort<string> Axis")
                               || File.ReadAllText(f).Contains("OptionKind = StepConfigOptionKind.")),
                "缺 OptionKind 的端口在绑定界面上会没有下拉候选");

            var bindingVm = ResolveRepoFile(@"VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs");
            string vmText = bindingVm != null ? File.ReadAllText(bindingVm) : string.Empty;
            Check("★绑定窗口按 OptionKind **现取**候选（不是直接用 PresetOptions 快照）",
                vmText.Contains("portDef.OptionKind") && vmText.Contains("StepConfigOptionSource.GetOptionItems"),
                bindingVm == null ? "定位不到 VariableBindingViewModel.cs" : "");

            Check("候选类别能一路传到界面（IPort.OptionKind → PortDefinition.OptionKind → PluginService 赋值）",
                File.ReadAllText(ResolveRepoFile(@"Shard\Core.Interfaces\DataPort\IPort.cs") ?? "").Contains("OptionKind")
                && File.ReadAllText(ResolveRepoFile(@"Core\Models\PortDefinition.cs") ?? "").Contains("OptionKind")
                && File.ReadAllText(ResolveRepoFile(@"Engine\PluginService.cs") ?? "").Contains("OptionKind = inputPort?.OptionKind"),
                "链路断在任何一段，绑定界面都拿不到候选类别");
        }

        // ==================================================================
        //  ⑪ 打断性命令：停止 / 失能不再排在回零后面等
        // ==================================================================

        /// <summary>
        /// "停止 / 失能"必须能打断正在执行的长命令，且调试面板不得在 UI 线程上同步等命令。
        ///
        /// 【为什么单列一条】用户实测反馈：在调试面板点「开始回零」（仿真器没接原点开关，
        /// 会一直跑到 120 秒超时），再点「失能」→ **整个界面卡死**。两个原因叠加：
        ///   ① 失能老老实实排在回零后面，命令线程被回零占着；
        ///   ② 界面用 <c>Completion.Wait()</c> 同步等它，于是 UI 线程被冻住最长 30 秒。
        /// 前者修在 <c>MotionDeviceBase</c>（打断性命令），后者修在调试面板（改异步）。
        /// </summary>
        private static void RunInterruptingCommandContract()
        {
            Section("[Z] 打断性命令：停止 / 失能不再排在回零后面等");

            var file = ResolveRepoFile(@"Shard\Core.Interfaces\Motion\MotionDeviceBase.cs");
            if (file == null)
            {
                Check("打断性命令契约", true, "跳过：定位不到 MotionDeviceBase");
                return;
            }

            string text = File.ReadAllText(file);

            Check("★Stop 与 Disable 被登记为打断性命令",
                Regex.IsMatch(text, @"IsInterrupting\(MotionCommandKind kind\)\s*=>\s*kind is MotionCommandKind\.Stop or MotionCommandKind\.Disable"),
                "没有它，「停止」会老实排在回零（几十秒）后面 —— 轮到它时轴早走完了");

            Check("★入队时对打断性命令触发取消（打断正在执行的那条）",
                text.Contains("interruptRequired = IsInterrupting(command.Kind)")
                && text.Contains("_emergencyCts?.Cancel()"),
                "缺了它，长命令会一直占着命令线程，停止/失能排不进去");

            // 必须**限定在 Enqueue 方法体内**比较：文件里别的方法也有 `if (gate != ...)`，
            // 用全文 IndexOf 会拿到更靠前的那一个，断言就永远失败（或永远通过）——
            // 这个坑在 §12 的 Provider 顺序断言上刚踩过一次，这里不再犯。
            const string enqueueHead = "public MotionCommandResult Enqueue(MotionCommand? command)";
            int enqueueAt = text.IndexOf(enqueueHead, StringComparison.Ordinal);
            string enqueueBody = enqueueAt > 0
                ? text.Substring(enqueueAt, Math.Min(3000, text.Length - enqueueAt))
                : string.Empty;

            int cancelAt = enqueueBody.IndexOf("if (interruptRequired)", StringComparison.Ordinal);
            int gateAfterLock = enqueueBody.LastIndexOf("if (gate != MotionCommandResult.Accepted)", StringComparison.Ordinal);
            Check("打断在入队锁**之外**执行（锁内 Cancel 会与命令线程抢同一把锁，等于自己等自己）",
                cancelAt > 0 && gateAfterLock > 0 && cancelAt < gateAfterLock,
                cancelAt < 0 || gateAfterLock < 0
                    ? "方法体内没找到关键代码"
                    : $"取消@{cancelAt} / 锁外拒绝处理@{gateAfterLock}");

            var debugTab = ResolveRepoFile(@"VisionMaster\ViewModels\DialogViewModels\MotionBoardDebugTabViewModel.cs");
            string debugTabText = debugTab != null ? File.ReadAllText(debugTab) : string.Empty;

            Check("调试面板不在 UI 线程上同步等命令（否则队列里有回零时界面会冻住）",
                debugTabText.Contains("await Task.Run(() => command.Completion.Wait("),
                "同步 Wait 会把 UI 线程冻住最长 CommandTimeoutMs（实测反馈的「点一下卡死」）——新版页签用 Task.Run 异步等命令完成");

            Check("命令处理器有异常兜底（async void 的异常没有接收者，会直接崩进程）",
                debugTabText.Contains("RunGuardedAsync"), "");
        }

        // ==================================================================
        //  ⑩ 设备表同步器：防重入 + 事件顺序（违反即 StackOverflow）
        // ==================================================================

        /// <summary>
        /// 设备表同步器必须防重入，且"先记状态、再广播事件"。
        ///
        /// 【为什么单列一条】违反这两条的后果是 **StackOverflowException** ——
        /// 它是 .NET 里**唯一无法捕获**的异常：进程直接消失、没有堆栈、没有日志，
        /// 现场只能描述"点一下就没了"。根因要顺着
        ///   MarkConfigDirty → EnsureSynced → SyncFromSolution → DevicesChanged
        ///   → 事件处理读设备（WPF 绑定是**同步**求值的）→ TryGetDevice → EnsureSynced → …
        /// 一路读代码才看得出来（本轮实测：在「运动卡设置」里改轴参数必崩，
        /// 因为每次按键都 MarkConfigDirty，而界面又绑定了 Device 上的轴数等属性）。
        ///
        /// 静态约束是唯一能在无人值守下守住它的手段：真跑一次会把测试进程也杀掉。
        /// </summary>
        private static void RunProviderRecursionGuardContract()
        {
            Section("[Z] 设备表同步器：防重入 + 事件顺序（违反即 StackOverflow）");

            var targets = new (string Label, string Path)[]
            {
                ("运动卡", @"VisionMaster\Services\Motion\MotionProvider.cs"),
                ("相机", @"VisionMaster\Services\Camera\CameraProvider.cs"),
            };

            foreach (var (label, relative) in targets)
            {
                var file = ResolveRepoFile(relative);
                if (file == null)
                {
                    Check($"{label} Provider 递归防护", true, "跳过：定位不到文件");
                    continue;
                }

                string text = File.ReadAllText(file);

                Check($"★{label} Provider 的 EnsureSynced 有重入闸（同步期间再进来直接返回）",
                    text.Contains("Interlocked.CompareExchange(ref _syncing, 1, 0)"),
                    "缺了它会：同步 → 广播事件 → 事件里读设备 → 再同步 …… 栈无限加深，"
                    + "最终 StackOverflowException（进程直接消失、没有堆栈）");

                // 顺序检查必须限定在**方法体内**：早退分支里也有一句 RememberSynced，
                // 用全文 IndexOf 比会永远成立（断言就白写了）。
                const string methodHead = "public IReadOnlyList<string> SyncFromSolution()";
                int head = text.IndexOf(methodHead, StringComparison.Ordinal);
                if (head < 0)
                {
                    Check($"{label} 同步方法定位", false, "定位不到 SyncFromSolution 方法体");
                    continue;
                }

                int nextMember = text.IndexOf("\n        ///", head + methodHead.Length, StringComparison.Ordinal);
                string body = nextMember > 0 ? text.Substring(head, nextMember - head) : text.Substring(head);

                int remember = body.IndexOf("RememberSynced(_workspace?.CurrentSolution, configs.Count);", StringComparison.Ordinal);
                int broadcast = body.IndexOf("DevicesChanged?.Invoke", StringComparison.Ordinal);

                Check($"★{label} Provider 先记同步状态、再广播 DevicesChanged"
                      + "（事件处理里读设备会回到 EnsureSynced，状态必须已就绪）",
                    remember >= 0 && broadcast >= 0 && remember < broadcast,
                    remember < 0 || broadcast < 0
                        ? $"方法体内没找到关键代码（记状态={remember >= 0} / 广播={broadcast >= 0}）"
                        : broadcast < remember
                            ? "顺序反了：广播在记状态之前 → 事件里的 EnsureSynced 会再进一次同步 → 递归"
                            : "顺序正确（记状态在广播之前）");
            }
        }

        // ==================================================================
        //  ⑨ 主题字典键唯一性（编译不报、启动即崩的那一类）
        // ==================================================================

        /// <summary>
        /// UI 库主题字典里的 x:Key 不得重复。
        ///
        /// 【为什么单列一条】重复键是典型的"编译能过、加载即炸"：
        ///   · XAML 编译（BAML）**不校验**资源字典的键唯一性；
        ///   · 运行时一旦撞车，加载那一本字典就抛
        ///       XamlParseException → ArgumentException: Item has already been added.
        ///     而 Generic.xaml 是**启动就合并**的，后果是**整个程序起不来**。
        ///
        /// 本轮实测：新加的 DialogStatusDot 与文件里早已存在的同名键撞车 ——
        /// 编译 0 错、冒烟全绿，探针一构造视图立刻炸。
        /// 跨文件也要查（多本字典合并时撞车同样会炸）。
        ///
        /// （放在本类里只是因为它是本轮引入的；这其实是全局样式契约，
        ///   若日后样式契约检查独立成文件，这条应一并迁过去。）
        /// </summary>
        private static void RunThemeKeyUniquenessContract()
        {
            Section("[Z] 主题字典键唯一性（重复键 = 启动即崩）");

            var themesDir = ResolveRepoDir(@"UI\Controls\Themes");
            if (themesDir == null)
            {
                Check("主题字典键唯一性", true, "跳过：定位不到 UI 主题目录");
                return;
            }

            var seen = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(themesDir, "*.xaml"))
            {
                string name = Path.GetFileName(file);
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"x:Key=""([A-Za-z0-9_]+)"""))
                {
                    string key = m.Groups[1].Value;
                    if (!seen.TryGetValue(key, out var list)) seen[key] = list = new List<string>();
                    list.Add(name);
                }
            }

            var duplicated = seen.Where(kv => kv.Value.Count > 1)
                .Select(kv => $"{kv.Key}（{string.Join(" + ", kv.Value)}）")
                .ToList();

            Check("★主题字典里没有重复的 x:Key（重复会让程序启动即抛「Item has already been added」）",
                duplicated.Count == 0,
                duplicated.Count == 0
                    ? $"已扫 {seen.Count} 个键，全部唯一"
                    : "重复：" + string.Join("；", duplicated));

            int dialogKeys = seen.Keys.Count(k => k.StartsWith("Dialog", StringComparison.Ordinal));
            Check("Dialog* 公共键已成体系（界面只需引用，不再各写一份）",
                dialogKeys >= 40, $"共 {dialogKeys} 个 Dialog* 键");
        }

        // ==================================================================
        //  ① 回零模式码（填错后果最重，逐个钉死）
        // ==================================================================

        private static void RunHomeModeMapping()
        {
            Section("[Z] 回零模式码映射（正运动 homemode）");

            // 这组数字取自仓库参考工程里已投产的映射。
            // 逐个断言而不是"抽查两个"：回零方式是现场按机构选的，
            // 用户选什么就发什么码 —— 表里错一行，就等于给某个机构配了错误的寻零路径。
            var expected = new (HomeMode Mode, uint Code, string Name)[]
            {
                (HomeMode.NegativeLimitIndex, 1, "负限位+Index"),
                (HomeMode.PositiveLimitIndex, 2, "正限位+Index"),
                (HomeMode.NegativeLimit, 17, "负限位"),
                (HomeMode.PositiveLimit, 18, "正限位"),
                (HomeMode.Origin, 19, "原点"),
                (HomeMode.PositiveLimitOrigin, 23, "正限位+原点"),
                (HomeMode.NegativeLimitOrigin, 27, "负限位+原点"),
                (HomeMode.PresetZero, 37, "零位置预设"),
            };

            foreach (var (mode, code, name) in expected)
            {
                Check($"回零方式「{name}」→ {code}",
                    ZMotionHomeModes.ToSdk(mode) == code,
                    $"实际={ZMotionHomeModes.ToSdk(mode)?.ToString() ?? "null"}");
            }

            Check("契约里定义的回零方式全部有映射（没有漏网的枚举值）",
                Enum.GetValues<HomeMode>().All(m => ZMotionHomeModes.ToSdk(m) != null),
                "缺失：" + string.Join(", ", Enum.GetValues<HomeMode>().Where(m => ZMotionHomeModes.ToSdk(m) == null)));

            Check("零位置预设被标记为「不需要找开关」（界面据此提示）",
                ZMotionHomeModes.IsPreset(HomeMode.PresetZero) && !ZMotionHomeModes.IsPreset(HomeMode.Origin), "");
        }

        // ==================================================================
        //  ② 机型能力
        // ==================================================================

        private static void RunModelCapabilities()
        {
            Section("[Z] 机型 → 能力（轴数 / IO 数）");

            var eci3428 = ZMotionModels.Resolve("ECI3428", string.Empty);
            Check("ECI3428 → 4 轴 / 24 入 / 16 出（已识别）",
                eci3428 is { AxisCount: 4, InputCount: 24, OutputCount: 16, Recognized: true },
                $"{eci3428.AxisCount} 轴 / {eci3428.InputCount} 入 / {eci3428.OutputCount} 出 / 识别={eci3428.Recognized}");

            var eci3828 = ZMotionModels.Resolve("ECI3828", string.Empty);
            Check("ECI3828 → 8 轴 / 24 入 / 20 出（同一驱动的不同机型，能力不同）",
                eci3828 is { AxisCount: 8, InputCount: 24, OutputCount: 20, Recognized: true },
                $"{eci3828.AxisCount} 轴 / {eci3828.InputCount} 入 / {eci3828.OutputCount} 出");

            var zmc = ZMotionModels.Resolve("ZMC408SCAN", string.Empty);
            Check("ZMC408SCAN → 4 轴（ZMC 系列也在表内）",
                zmc is { AxisCount: 4, Recognized: true }, $"{zmc.AxisCount} 轴");

            // 虚拟 PLC：RTSys 自带的仿真器就是它（实测机型名 VPLC532R，控制器状态窗口显示 RealAxes: 32）
            var vplc = ZMotionModels.Resolve("VPLC532R", string.Empty);
            Check("VPLC532R（虚拟 PLC / RTSys 仿真器）→ 32 轴，且如实标记「IO 点数未核实」",
                vplc is { AxisCount: 32, Recognized: true, IoCountApproximate: true },
                $"{vplc.AxisCount} 轴｜IO {vplc.InputCount}/{vplc.OutputCount}（未核实={vplc.IoCountApproximate}）"
                + "——VPLC 的点位取决于组态，报一个看起来确定的数字反而误导");

            var byReport = ZMotionModels.Resolve(string.Empty, "ECI3828");
            Check("配置留空时用控制器上报的机型",
                byReport.AxisCount == 8 && byReport.Recognized, $"{byReport.AxisCount} 轴");

            var withSuffix = ZMotionModels.Resolve(string.Empty, "ECI3828_V2");
            Check("上报机型带版本后缀也能匹配（前缀匹配）",
                withSuffix.AxisCount == 8 && withSuffix.Recognized, $"{withSuffix.AxisCount} 轴");

            Check("配置机型优先于上报机型（用户知道的可能更准）",
                ZMotionModels.Resolve("ECI3828", "ECI3428").AxisCount == 8, "");

            var unknown = ZMotionModels.Resolve("某个不认识的型号", string.Empty);
            Check("★未识别机型不猜：标记 Recognized=false 并按最小配置兜底",
                !unknown.Recognized && unknown.AxisCount == 4,
                $"识别={unknown.Recognized} 轴数={unknown.AxisCount}"
                + "（报小的后果是「某些轴不能操作」，可见可改；报大的后果是朝着不存在的轴下发命令）");
        }

        // ==================================================================
        //  ③ 状态位域
        // ==================================================================

        private static void RunStatusBits()
        {
            Section("[Z] 轴状态位域解析");

            Check("位定义：报警=bit3 / 正限位=bit4 / 负限位=bit5 / 到位=bit11 / 急停=bit12",
                ZMotionAxisBits.Alarm == 0x08
                && ZMotionAxisBits.PositiveLimit == 0x10
                && ZMotionAxisBits.NegativeLimit == 0x20
                && ZMotionAxisBits.InPosition == 0x800
                && ZMotionAxisBits.EmergencyStop == 0x1000,
                $"Alm={ZMotionAxisBits.Alarm:X} Pot={ZMotionAxisBits.PositiveLimit:X} "
                + $"Net={ZMotionAxisBits.NegativeLimit:X} Inp={ZMotionAxisBits.InPosition:X} Emg={ZMotionAxisBits.EmergencyStop:X}");

            var idle = ZMotionStatusParser.Parse(0x800, 3);
            Check("状态字 0x800（仅到位）→ 到位=true、其余全 false",
                idle is { InPosition: true, Alarm: false, PositiveLimit: false, NegativeLimit: false, EmergencyStop: false }
                && idle.PhysicalIndex == 3,
                idle.Describe());

            var alarm = ZMotionStatusParser.Parse(0x08);
            Check("★状态字 0x08（报警位）→ 报警=true 且有说明（读成到位就等于安全停机不触发）",
                alarm.Alarm && !alarm.InPosition && alarm.HasCriticalSignal && alarm.AlarmMessage.Length > 0,
                alarm.Describe());

            var limits = ZMotionStatusParser.Parse(0x30);
            Check("状态字 0x30 → 正负限位同时触发（两个位互不干扰）",
                limits is { PositiveLimit: true, NegativeLimit: true } && limits.HasCriticalSignal,
                limits.Describe());

            var emergency = ZMotionStatusParser.Parse(0x1000);
            Check("状态字 0x1000 → 急停输入触发（属临界信号）",
                emergency.EmergencyStop && emergency.HasCriticalSignal, emergency.Describe());

            // 全位为 1：注意 0xFFFFFFFF 在 C# 里是 uint 字面量（超出 int 范围），必须显式转换
            var allBits = ZMotionStatusParser.Parse(unchecked((int)0xFFFFFFFF));
            Check("全位都置位时五项全为 true（位解析没有互相覆盖）",
                allBits is { Alarm: true, PositiveLimit: true, NegativeLimit: true, InPosition: true, EmergencyStop: true },
                allBits.Describe());
        }

        // ==================================================================
        //  ④ 故障分级与建议
        // ==================================================================

        private static void RunFaultInterpretation()
        {
            Section("[Z] 故障分级与处置建议");

            var home = ZMotionFaults.Interpret("回零", -7, "Y");
            Check("回零失败 → 需人工复位（可能正卡在原点开关上，不能自动重试）",
                home.Severity == MotionFaultSeverity.RequiresReset,
                home.Severity.ToString());
            Check("故障里带**原始错误码**（现场据此对厂商手册）",
                home.Code == -7 && home.Message.Contains("-7"), home.Message);
            Check("故障里带出错的操作上下文（「回零」比「操作失败」有用得多）",
                home.Message.Contains("回零"), home.Message);
            Check("有处置建议（现场少打一次电话）",
                !string.IsNullOrWhiteSpace(home.Suggestion), home.Suggestion);
            Check("带上出错轴名",
                home.Axis == "Y", home.Axis);

            var connect = ZMotionFaults.Interpret("连接控制器", 3);
            Check("连接失败 → 需现场处理（检查 IP / 网线 / 供电 / 占用），并给出对应建议",
                connect.Severity == MotionFaultSeverity.RequiresService
                && connect.Suggestion.Contains("IP"), connect.Suggestion);

            var read = ZMotionFaults.Interpret("读取轴状态", 5);
            Check("读状态失败 → 可恢复级（单次通信抖动不该把设备判死，看门狗会兜连续失败）",
                read.Severity == MotionFaultSeverity.Recoverable, read.Severity.ToString());

            var motion = ZMotionFaults.Interpret("绝对运动", 12, "X");
            Check("运动被拒 → 需人工复位（卡侧状态不对：未使能/报警/限位）",
                motion.Severity == MotionFaultSeverity.RequiresReset, motion.Severity.ToString());

            Check("★不逐码编造文案：说明里保留原始码而不是伪装成已知含义",
                !home.Message.Contains("参数错误") && !home.Message.Contains("超时"),
                home.Message);
        }

        // ==================================================================
        //  ⑤ 部署契约（native 库放对位置）
        // ==================================================================

        private static void RunDeploymentContract()
        {
            Section("[Z] 部署：SDK 与 native 库");

            var csproj = ResolveRepoFile(@"Plugins\Plugin.Motion.ZMotion\Plugin.Motion.ZMotion.csproj");
            var sdkDecl = ResolveRepoFile(@"Plugins\Plugin.Motion.ZMotion\Sdk\Zmcaux.cs");
            var sdkNative = ResolveRepoFile(@"Plugins\Plugin.Motion.ZMotion\Sdk\zauxdll.dll");
            var sln = ResolveRepoFile("VisionMaster.sln");

            if (csproj == null)
            {
                Check("正运动驱动部署契约", true, "跳过：定位不到驱动工程");
                return;
            }

            string text = File.ReadAllText(csproj);

            Check("★native 库用通配投递到**宿主 bin**：DllImport 只在 exe 目录/系统目录/PATH 里找，"
                  + "不会去 Modules\\ 找（插件托管 dll 投递在那里，native 的不行）",
                text.Contains(@"Sdk\*.dll") && text.Contains("HostBinDir"),
                "逐个列文件名的话，SDK 升级时漏加一个文件就会在运行期炸；"
                + "而缺 native 依赖时的报错不会告诉你是缺了哪一个");

            Check("SDK 声明文件与 native 库同在 Sdk\\（升级时必须成对替换，分开放早晚版本错配）",
                sdkDecl != null && sdkNative != null,
                $"Zmcaux.cs={(sdkDecl != null)} zauxdll.dll={(sdkNative != null)}");

            // ★ 这条是本轮的真实故障钉成的断言：
            //   zauxdll.dll 的导入表里静态依赖 zmotion.dll（核心层），两者缺一不可。
            //   只放 zauxdll.dll 时的现象是 DllNotFoundException「找不到指定的模块」(0x7E) ——
            //   那条消息不会说缺的是哪个文件，看上去完全像"路径没放对"。
            var sdkDir = sdkNative != null ? Path.GetDirectoryName(sdkNative) : null;
            bool hasCore = sdkDir != null && File.Exists(Path.Combine(sdkDir, "zmotion.dll"));
            Check("★SDK 完整：zauxdll.dll（辅助层）与 zmotion.dll（核心层）必须同时存在",
                hasCore,
                hasCore
                    ? "两个 native 文件都在"
                    : "缺 zmotion.dll —— zauxdll.dll 在导入表里静态依赖它。"
                      + "请把 SDK 里的 zmotion.dll 一并放进 Plugins\\Plugin.Motion.ZMotion\\Sdk\\"
                      + "（缺它时连接报 DllNotFoundException(0x7E)，错误消息指不到缺哪个文件）");

            var driver = ResolveRepoFile(@"Plugins\Plugin.Motion.ZMotion\ZMotionCard.cs");
            string driverText = driver != null ? File.ReadAllText(driver) : string.Empty;
            Check("★驱动自带诊断：捕获 DllNotFoundException 并**点名**缺失的 native 文件"
                  + "（否则现场只能看到「找不到指定的模块」）",
                driverText.Contains("BuildMissingSdkMessage") && driverText.Contains("zmotion.dll"),
                driver == null ? "定位不到 ZMotionCard.cs" : "");

            Check("驱动对位数不符给出单独提示（32 位 SDK 装到 64 位进程同样报 0x7E，极易误判为路径问题）",
                driverText.Contains("BadImageFormatException"), "");

            Check("sln 已登记运动插件工程（否则 sln 全量构建不产出，Modules\\ 里会缺 dll）",
                sln != null && File.ReadAllText(sln).Contains("Plugin.Motion.ZMotion"), "");
        }

        // ==================================================================
        //  ⑥ 点动端到端（虚拟卡）
        // ==================================================================

        private static void RunJogEndToEnd()
        {
            Section("[Z] 点动端到端（虚拟卡）：持续运动 / 停止能终止");

            using var card = NewCard();
            card.Connect();
            EnableAxis(card, 0);

            using var jog = new MotionCommand
            {
                Kind = MotionCommandKind.Jog,
                PhysicalAxis = 0,
                LogicalAxis = "X",
                JogDirection = 1,
                VelocityMmPerS = 50,
                Timeout = TimeSpan.FromSeconds(2),
            };

            var accepted = card.Enqueue(jog);
            Check("点动命令被接受", accepted == MotionCommandResult.Accepted, "返回=" + accepted);

            var moved = WaitFor(() => card.GetAxisStatus(0) is { PositionMm: > 0.5 }, 2000);
            Check("★点动下发后轴**持续**运动（没有终点，直到被叫停）",
                moved, $"位置={(card.GetAxisStatus(0)?.PositionMm ?? double.NaN):F3} mm");

            // 立即叫停（mode=3 立即停）
            using var stop = new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,
                StopMode = 3,
                Retryable = false,
                Timeout = TimeSpan.FromSeconds(2),
            };
            card.Enqueue(stop);
            Thread.Sleep(80);

            var first = card.GetAxisStatus(0)?.PositionMm ?? 0;
            Thread.Sleep(250);
            var second = card.GetAxisStatus(0)?.PositionMm ?? 0;

            Check("★停止命令能终止点动（位置不再变化）",
                Math.Abs(second - first) < 0.05,
                $"停止后 {first:F3} mm → {second:F3} mm");
        }

        // ==================================================================
        //  ⑦ 点动心跳兜底（松手事件丢了也不能一直动）
        // ==================================================================

        private static void RunJogWatchdog()
        {
            Section("[Z] 点动心跳兜底（界面事件失效时的最后一道闸）");

            var now = DateTime.UtcNow;
            int timeout = MotionJogGuard.PulseTimeoutMs;

            Check("阈值取 500ms：按住期间 UI 每 150ms 一次脉冲，留 3 倍余量",
                timeout >= 300 && timeout <= 1000, $"实际 {timeout}ms");

            Check("心跳新鲜（100ms 前刚按过）→ 不干预点动",
                !MotionJogGuard.IsPulseTimedOut(now, now.AddMilliseconds(-100), timeout), "");

            Check("★心跳超时（900ms 没脉冲）→ 判定必须停",
                MotionJogGuard.IsPulseTimedOut(now, now.AddMilliseconds(-900), timeout), "");

            Check("从未点动过（MinValue）不触发停止（否则一开面板就会去停设备）",
                !MotionJogGuard.IsPulseTimedOut(now, DateTime.MinValue, timeout), "");

            Check("刚好等于阈值时不判超时（避免临界抖动导致按住时反复起停）",
                !MotionJogGuard.IsPulseTimedOut(now, now.AddMilliseconds(-timeout), timeout), "");
        }

        // ==================================================================
        //  ⑧ 调试面板契约（用户要的"控制 + 定位"）
        // ==================================================================

        private static void RunDebugPanelContract()
        {
            Section("[Z] 调试面板契约：控制 / 定位 / 回零 / IO / 急停");

            var path = ResolveRepoFile(@"VisionMaster\Views\DialogViews\MotionDebugView.xaml");
            if (path == null)
            {
                Check("调试面板契约", true, "跳过：定位不到 MotionDebugView.xaml");
                return;
            }

            string xaml = File.ReadAllText(path);

            Check("★点动用「按住才动」行为（HoldCommand 与 ReleaseCommand 成对，不是普通 Click）",
                xaml.Contains("HoldCommandBehavior.HoldCommand")
                && xaml.Contains("HoldCommandBehavior.ReleaseCommand"),
                "只有 Click 的话，点一下就是「持续运动到被别的操作打断」，对位根本没法用");

            Check("★有独立的急停入口（DialogDangerButton，实心红、不藏在子菜单里）",
                xaml.Contains("DialogDangerButton") && xaml.Contains("EmergencyStopCommand"), "");

            Check("覆盖用户要的四项：控制（使能/停止）、定位、回零、IO",
                xaml.Contains("EnableCommand") && xaml.Contains("StopCommand")
                && xaml.Contains("GoToAndWaitCommand") && xaml.Contains("HomeCommand")
                && xaml.Contains("ToggleOutputCommand"), "");

            Check("★体现一带多：轴列表按轴逐行列出（位置与状态并排），而不是只显示一张卡",
                xaml.Contains("ItemsSource=\"{Binding Axes}\"") && xaml.Contains("SelectedAxis"), "");

            Check("定位支持「取当前位置」（对位时最常用的起点）",
                xaml.Contains("UseCurrentPositionCommand"), "");

            Check("回零方式下拉显示中文（DisplayMemberPath=DisplayName，不是枚举成员名）",
                xaml.Contains("DisplayMemberPath=\"DisplayName\"")
                && xaml.Contains("ItemsSource=\"{Binding HomeModes}\""), "");

            // ★ Run.Text 的默认绑定模式是 TwoWay（它是 FlowDocument 元素，为 RichTextBox 场景设计），
            //   而状态标签这类属性在 VM 里是只读的 —— 不显式写 Mode=OneWay 会在**加载面板时**抛
            //   InvalidOperationException「无法对只读属性进行 TwoWay 绑定」（探针实测）。
            //   这与 TextBlock.Text（默认 OneWay）不同，是很容易踩且报错位置很远的坑。
            var badRuns = Regex.Matches(xaml, @"<Run[^>]*Text=""\{Binding[^}]*\}""")
                .Select(m => m.Value)
                .Where(s => !s.Contains("Mode=OneWay"))
                .ToList();
            Check("★Run 的绑定显式写了 Mode=OneWay（Run.Text 默认 TwoWay，绑只读属性会抛异常）",
                badRuns.Count == 0,
                badRuns.Count == 0 ? "" : "缺 Mode=OneWay：" + string.Join("；", badRuns));
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        private static MotionDescriptor NewDescriptor() => new()
        {
            Id = Guid.NewGuid(),
            DriverTypeKey = typeof(VirtualMotionCard).AssemblyQualifiedName ?? nameof(VirtualMotionCard),
            DisplayName = "点动断言用虚拟卡",
            Address = "127.0.0.1:" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Params = new MotionParams
            {
                PollIntervalMs = 5,
                WatchdogTimeoutMs = 200,
                CommandTimeoutMs = 3000,
                DefaultVelocityMmPerS = 50,
                MaxQueueDepth = 16,
            },
            Axes = new List<AxisMapping>
            {
                new() { LogicalName = "X", PhysicalIndex = 0, UnitsPerMm = 1000, SoftLimitMinMm = -100, SoftLimitMaxMm = 100 },
            },
        };

        private static VirtualMotionCard NewCard()
            => new(NewDescriptor()) { Log = new StubLog() };

        private static void EnableAxis(VirtualMotionCard card, int axis)
        {
            using var enable = new MotionCommand
            {
                Kind = MotionCommandKind.Enable,
                PhysicalAxis = axis,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromSeconds(2),
            };
            card.Enqueue(enable);
            enable.Completion.Wait(2000);
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

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
    }
}
