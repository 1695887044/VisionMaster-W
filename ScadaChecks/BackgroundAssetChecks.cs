using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Scada.Snapshots;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace ScadaChecks
{
    /// <summary>
    /// [BA] 页面背景图与方案图片资产：模型字段、页面属性声明、导入查重/重命名/越界、快照搬运。
    ///
    /// 与 [PS] 同一纪律：整段另起 STA 线程跑（图片解码与 WPF 位图要求 STA），
    /// 只读数据侧与资产服务，不建窗口、不建 Application。
    /// </summary>
    internal partial class Program
    {
        private static class BackgroundAssetChecks
        {
            public static void Run()
            {
                RunBackgroundAssetChecks();
                RunSoundAlarmChecks();
                RunHistoryChecks();
            }
        }

        private static void RunBackgroundAssetChecks()
        {
            Section("[BA] 页面背景图与方案资产：模型、属性声明、导入查重/重命名/越界、快照");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunBackgroundAssetChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("页面背景/资产断言全程未抛异常", false, failure.ToString());
        }

        private static void RunBackgroundAssetChecksCore()
        {
            // ---------------------------------------------------------------
            // ① 模型：默认值、版本号、不透明度夹紧、序列化往返
            // ---------------------------------------------------------------
            var page = new ScadaPage { Name = "P1" };
            Check("[BA1] 背景图默认空 / 填充默认 Uniform / 不透明度默认 100",
                page.BackgroundImage == string.Empty
                && page.BackgroundStretch == "Uniform"
                && Math.Abs(page.BackgroundOpacity - 100) < 0.001,
                $"image='{page.BackgroundImage}' stretch='{page.BackgroundStretch}' opacity={page.BackgroundOpacity}");

            var versionBefore = page.Version;
            page.BackgroundImage = "Image\\pid.png";
            Check("[BA1b] 改背景图会推高画面版本号（Web 快照按它失效）",
                page.Version > versionBefore,
                $"{versionBefore} → {page.Version}");

            page.BackgroundOpacity = 5;
            Check("[BA1c] 不透明度下限夹到 10", Math.Abs(page.BackgroundOpacity - 10) < 0.001, page.BackgroundOpacity.ToString("0.##"));
            page.BackgroundOpacity = 500;
            Check("[BA1d] 不透明度上限夹到 100", Math.Abs(page.BackgroundOpacity - 100) < 0.001, page.BackgroundOpacity.ToString("0.##"));

            page.BackgroundStretch = "Tile";
            page.BackgroundOpacity = 35;
            var json = JsonConvert.SerializeObject(page);
            var reloaded = JsonConvert.DeserializeObject<ScadaPage>(json);
            Check("[BA1e] 三个字段随 .vms 往返（JSON 键名与模型一致）",
                reloaded != null
                && reloaded.BackgroundImage == "Image\\pid.png"
                && reloaded.BackgroundStretch == "Tile"
                && Math.Abs(reloaded.BackgroundOpacity - 35) < 0.001,
                reloaded == null ? "反序列化为 null" : $"{reloaded.BackgroundImage}|{reloaded.BackgroundStretch}|{reloaded.BackgroundOpacity}");

            // ---------------------------------------------------------------
            // ② 页面属性声明：三条都在「背景」组，类型正确
            // ---------------------------------------------------------------
            var imageSpec = ScadaPageProperties.All.FirstOrDefault(s => s.Key == "BackgroundImage");
            var stretchSpec = ScadaPageProperties.All.FirstOrDefault(s => s.Key == "BackgroundStretch");
            var opacitySpec = ScadaPageProperties.All.FirstOrDefault(s => s.Key == "BackgroundOpacity");

            Check("[BA2] 背景图/填充/不透明度三条声明存在且归「背景」组",
                imageSpec != null && stretchSpec != null && opacitySpec != null
                && imageSpec.Group == ScadaPropertyGroups.PageBackground
                && stretchSpec.Group == ScadaPropertyGroups.PageBackground
                && opacitySpec.Group == ScadaPropertyGroups.PageBackground,
                $"{imageSpec?.Group}|{stretchSpec?.Group}|{opacitySpec?.Group}");

            Check("[BA2b] 背景图用 ImageFile（文件选择器），不是 Image（变量绑定）",
                imageSpec?.Kind == ElementPropertyKind.ImageFile,
                imageSpec?.Kind.ToString() ?? "missing");

            Check("[BA2c] 填充四种取值齐全、不透明度范围 10~100",
                stretchSpec != null
                && new[] { "Uniform", "UniformToFill", "Fill", "Tile" }.All(v => stretchSpec.Choices.Contains(v))
                && opacitySpec != null
                && Math.Abs(opacitySpec.Min - 10) < 0.001
                && Math.Abs(opacitySpec.Max - 100) < 0.001,
                $"choices={string.Join(",", stretchSpec?.Choices ?? Array.Empty<string>())} min={opacitySpec?.Min} max={opacitySpec?.Max}");

            // ---------------------------------------------------------------
            // ③ 资产服务：导入 / 同内容复用 / 同名不同内容重命名 / 越界 / 缺失
            // ---------------------------------------------------------------
            var root = Path.Combine(Path.GetTempPath(), "vm-scada-ba-" + Guid.NewGuid().ToString("N"));
            var solutionPath = Path.Combine(root, "Line1.vms");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(solutionPath, "{}");

                var sourceA = Path.Combine(root, "src-a.png");
                var sourceB = Path.Combine(root, "src-b.png");
                WritePng(sourceA, Colors.Red);
                WritePng(sourceB, Colors.Blue);

                Check("[BA3] 方案未保存时拒绝导入",
                    !SolutionAssetService.TryImport(null, sourceA, out _, out var noSolutionError)
                    && noSolutionError.Contains("方案还没保存"),
                    noSolutionError);

                Check("[BA3b] 首次导入复制进 Image 并返回相对路径",
                    SolutionAssetService.TryImport(solutionPath, sourceA, out var first, out var importError)
                    && first == "Image\\src-a.png"
                    && File.Exists(Path.Combine(root, "Image", "src-a.png")),
                    importError + " | " + first);

                Check("[BA3c] 不同名不同内容 → 新文件（不误判为重复）",
                    SolutionAssetService.TryImport(solutionPath, sourceB, out var newCopy, out _)
                    && newCopy == "Image\\src-b.png",
                    newCopy);

                // 同内容复制成另一个名字：必须复用第一份
                var sourceACopy = Path.Combine(root, "copy-of-a.png");
                File.Copy(sourceA, sourceACopy);
                Check("[BA3d] 同内容换名字导入 → 复用已有文件（不重复占空间）",
                    SolutionAssetService.TryImport(solutionPath, sourceACopy, out var reused, out _)
                    && reused == "Image\\src-a.png",
                    reused);

                // 同名不同内容：src-a.png 换成蓝色再导入 → src-a (2).png
                var sourceADifferent = Path.Combine(root, "other", "src-a.png");
                Directory.CreateDirectory(Path.GetDirectoryName(sourceADifferent)!);
                WritePng(sourceADifferent, Colors.Green);
                Check("[BA3e] 同名不同内容 → 重命名 (2)，原文件保留",
                    SolutionAssetService.TryImport(solutionPath, sourceADifferent, out var renamed, out var renameError)
                    && renamed == "Image\\src-a (2).png"
                    && File.Exists(Path.Combine(root, "Image", "src-a.png"))
                    && File.Exists(Path.Combine(root, "Image", "src-a (2).png")),
                    renameError + " | " + renamed);

                Check("[BA3f] 越界相对路径（..\\）被拒绝",
                    !SolutionAssetService.TryResolve(solutionPath, "..\\evil.png", out _, out var traversalError),
                    traversalError);

                Check("[BA3g] 绝对路径被拒绝",
                    !SolutionAssetService.TryResolve(solutionPath, @"C:\Windows\win.ini", out _, out var rootedError),
                    rootedError);

                Check("[BA3h] 有效相对路径解析到方案 Image 目录",
                    SolutionAssetService.TryResolve(solutionPath, "Image\\src-a.png", out var absolute, out var resolveError)
                    && string.Equals(absolute, Path.Combine(root, "Image", "src-a.png"), StringComparison.OrdinalIgnoreCase),
                    resolveError + " | " + absolute);

                var loadedOk = SolutionAssetService.TryLoad(solutionPath, "Image\\src-a.png", out var loaded, out var loadError);
                var missingOk = SolutionAssetService.TryLoad(solutionPath, "Image\\missing.png", out _, out var missingError);
                Check("[BA3i] 有效 PNG 可解码、缺失文件回退失败",
                    loadedOk && loaded != null && !missingOk && missingError.Contains("不存在"),
                    loadError + " | " + missingError);

                // -----------------------------------------------------------
                // ④ 快照：背景三字段随页面搬运（Web 渲染器要用）
                // -----------------------------------------------------------
                page.BackgroundImage = "Image\\src-a.png";
                page.BackgroundStretch = "UniformToFill";
                page.BackgroundOpacity = 40;

                var document = new ScadaDocument();
                document.Pages.Add(page);
                var runtime = new ScadaRuntime(document);
                runtime.Start();
                var snapshot = new ScadaPageSnapshotBuilder().Build(runtime);
                Check("[BA4] 快照携带背景图三字段",
                    snapshot != null
                    && snapshot.BackgroundImage == "Image\\src-a.png"
                    && snapshot.BackgroundStretch == "UniformToFill"
                    && Math.Abs(snapshot.BackgroundOpacity - 40) < 0.001,
                    snapshot == null ? "快照为 null" : $"{snapshot.BackgroundImage}|{snapshot.BackgroundStretch}|{snapshot.BackgroundOpacity}");

                // -----------------------------------------------------------
                // ⑤ 控件层换算：成员名 → Stretch / 平铺 / 不透明度
                // -----------------------------------------------------------
                Check("[BA5] 填充成员名换算到 WPF Stretch",
                    ScadaBackgroundStretch.Parse("Uniform") == Stretch.Uniform
                    && ScadaBackgroundStretch.Parse("UniformToFill") == Stretch.UniformToFill
                    && ScadaBackgroundStretch.Parse("Fill") == Stretch.Fill
                    && ScadaBackgroundStretch.Parse("Tile") == Stretch.None
                    && ScadaBackgroundStretch.Parse("未知") == Stretch.Uniform,
                    "Uniform/UniformToFill/Fill/Tile/未知");

                Check("[BA5b] 平铺与不透明度换算",
                    ScadaBackgroundStretch.IsTile("Tile")
                    && !ScadaBackgroundStretch.IsTile("Uniform")
                    && Math.Abs(ScadaBackgroundStretch.ToOpacity(40) - 0.4) < 0.001
                    && Math.Abs(ScadaBackgroundStretch.ToOpacity(0) - 0.1) < 0.001
                    && Math.Abs(ScadaBackgroundStretch.ToOpacity(999) - 1.0) < 0.001,
                    "Tile/40/0/999");

                // -----------------------------------------------------------
                // ⑥ 新图元：Hmi.Picture / Hmi.Line 注册与属性
                // -----------------------------------------------------------
                var picture = ElementRegistry.Find("Hmi.Picture");
                var line = ElementRegistry.Find("Hmi.Line");

                Check("[BB1] Hmi.Picture / Hmi.Line 已注册，默认尺寸正确",
                    picture != null && line != null
                    && Math.Abs(picture.DefaultWidth - 200) < 0.001
                    && Math.Abs(picture.DefaultHeight - 160) < 0.001
                    && Math.Abs(line.DefaultWidth - 160) < 0.001
                    && Math.Abs(line.DefaultHeight - 24) < 0.001,
                    $"picture={picture?.DefaultWidth}×{picture?.DefaultHeight} line={line?.DefaultWidth}×{line?.DefaultHeight}");

                Check("[BB2] Picture.SourcePath 用 ImageFile（选方案文件，不是变量绑定）",
                    ElementRegistry.FindProperty("Hmi.Picture", "SourcePath")?.Kind == ElementPropertyKind.ImageFile,
                    ElementRegistry.FindProperty("Hmi.Picture", "SourcePath")?.Kind.ToString() ?? "missing");

                Check("[BB3] Line 的 Points / Dash / Arrow 三条属性在",
                    ElementRegistry.FindProperty("Hmi.Line", "Points") != null
                    && ElementRegistry.FindProperty("Hmi.Line", "Dash") != null
                    && ElementRegistry.FindProperty("Hmi.Line", "Arrow") != null,
                    "Points/Dash/Arrow");

                // -----------------------------------------------------------
                // ⑦ 增量/减量动作：枚举、描述、执行与夹紧
                // -----------------------------------------------------------
                Check("[BB4] 增量/减量有展示名且不标“未接通”",
                    ScadaActionType.Increment.DisplayName() == "增量"
                    && ScadaActionType.Decrement.DisplayName() == "减量"
                    && ScadaActionType.Increment.PendingReason() == null
                    && ScadaActionType.Decrement.PendingReason() == null,
                    $"{ScadaActionType.Increment.DisplayName()}/{ScadaActionType.Decrement.DisplayName()}");

                var speedId = Guid.NewGuid();
                var speed = new FakeValueHandle
                {
                    VariableId = speedId,
                    Name = "Speed",
                    DataType = typeof(int),
                    Value = 100,
                };
                var valueSource = new FakeValueSource();
                valueSource.ById[speedId] = speed;
                valueSource.ByName["Speed"] = speed;

                var dispatcher = new ScadaActionDispatcher(new LoggerStub(), valueSource, new FakeNavigator());
                var stepAction = new ScadaAction
                {
                    Type = ScadaActionType.Increment,
                    Step = 10,
                    Min = 0,
                    Max = 150,
                };
                stepAction.BindVariable(speedId, "Speed");

                var stepHook = new ScadaEventHook();
                stepHook.Actions.Add(stepAction);

                dispatcher.Dispatch(stepHook, "测试");
                Check("[BB5] 增量：100 + 10 → 110", Convert.ToInt32(speed.Value) == 110, Convert.ToString(speed.Value));

                dispatcher.Dispatch(stepHook, "测试");
                dispatcher.Dispatch(stepHook, "测试");
                dispatcher.Dispatch(stepHook, "测试");
                dispatcher.Dispatch(stepHook, "测试");
                dispatcher.Dispatch(stepHook, "测试");
                Check("[BB6] 增量夹紧到上限 150（越限不再加）",
                    Convert.ToInt32(speed.Value) == 150, Convert.ToString(speed.Value));

                stepAction.Type = ScadaActionType.Decrement;
                stepAction.Step = 25;
                dispatcher.Dispatch(stepHook, "测试");
                Check("[BB7] 减量：150 − 25 → 125", Convert.ToInt32(speed.Value) == 125, Convert.ToString(speed.Value));

                var textId = Guid.NewGuid();
                var textHandle = new FakeValueHandle
                {
                    VariableId = textId,
                    Name = "Batch",
                    DataType = typeof(string),
                    Value = "abc",
                };
                valueSource.ById[textId] = textHandle;
                stepAction.Type = ScadaActionType.Increment;
                stepAction.Step = 1;
                stepAction.BindVariable(textId, "Batch");

                var textResult = dispatcher.DispatchWithResult(stepHook, "测试");
                Check("[BB8] 非数值变量拒绝增量且不改值",
                    textResult.Failed == 1 && Equals(textHandle.Value, "abc"),
                    $"failed={textResult.Failed} value={textHandle.Value}");

                stepAction.Step = 0;
                stepAction.BindVariable(speedId, "Speed");
                var zeroStep = dispatcher.DispatchWithResult(stepHook, "测试");
                Check("[BB9] 步长 0 拒绝（不静默写原值）",
                    zeroStep.Failed == 1 && Convert.ToInt32(speed.Value) == 125,
                    $"failed={zeroStep.Failed} value={speed.Value}");

                // -----------------------------------------------------------
                // ⑧ 启用对象动画：0 禁用、非 0 启用、反转、复位
                // -----------------------------------------------------------
                Check("[BC1] 启用对象有展示名",
                    ScadaAnimationType.Enable.DisplayName() == "启用对象",
                    ScadaAnimationType.Enable.DisplayName());

                var enableAnimation = new ScadaAnimation
                {
                    Type = ScadaAnimationType.Enable,
                    DisableReason = "联锁未满足：液位低",
                };

                Check("[BC2] 启用对象求值：0=禁用、1=启用、反转后反过来",
                    enableAnimation.TryEvaluateEnable(0, out var off, out _) && !off
                    && enableAnimation.TryEvaluateEnable(1, out var on, out _) && on
                    && new ScadaAnimation { Type = ScadaAnimationType.Enable, Invert = true }
                        .TryEvaluateEnable(1, out var invertedOff, out _) && !invertedOff,
                    "0/1/反转1");

                var buttonElement = new ScadaElement { TypeKey = "Hmi.Button" };
                var buttonControl = ElementRegistry.CreateControl(buttonElement);

                var appliedOff = buttonControl.TryApplyAnimationValue(enableAnimation, 0, out var enableError);
                Check("[BC3] 禁用 = 交互闸门（不可命中/不可用）+ 原因提示",
                    appliedOff
                    && !buttonControl.IsHitTestVisible
                    && !buttonControl.IsEnabled
                    && Equals(buttonControl.ToolTip, "联锁未满足：液位低")
                    && Math.Abs(buttonControl.Opacity - 0.55) < 0.001,
                    $"hit={buttonControl.IsHitTestVisible} enabled={buttonControl.IsEnabled} tip={buttonControl.ToolTip} opacity={buttonControl.Opacity} err={enableError}");

                var appliedOn = buttonControl.TryApplyAnimationValue(enableAnimation, 1, out _);
                Check("[BC4] 启用恢复交互与不透明度",
                    appliedOn
                    && buttonControl.IsHitTestVisible
                    && buttonControl.IsEnabled
                    && buttonControl.ToolTip == null
                    && Math.Abs(buttonControl.Opacity - 1d) < 0.001,
                    $"hit={buttonControl.IsHitTestVisible} enabled={buttonControl.IsEnabled} opacity={buttonControl.Opacity}");

                buttonControl.TryApplyAnimationValue(enableAnimation, 0, out _);
                buttonControl.Refresh();
                Check("[BC5] Refresh（停止运行）把禁用痕迹清干净",
                    buttonControl.IsHitTestVisible
                    && buttonControl.IsEnabled
                    && buttonControl.ToolTip == null
                    && Math.Abs(buttonControl.Opacity - 1d) < 0.001,
                    $"hit={buttonControl.IsHitTestVisible} enabled={buttonControl.IsEnabled} opacity={buttonControl.Opacity}");

                // -----------------------------------------------------------
                // ⑨ 文字图标库：名字 → 字形、原始码点、未知名字、字体真的带字形
                // -----------------------------------------------------------
                var iconText = new TextBlock();
                ScadaIconHelper.SetGlyph(iconText, "Gear");

                Check("[BD1] 图标名字解析为字形（Gear → U+F013）",
                    iconText.Text == "\uF013",
                    $"text=U+{(iconText.Text.Length > 0 ? ((int)iconText.Text[0]).ToString("X4") : "----")}");

                Check("[BD2] 原始码点四种写法都认（f013 / \\uf013 / &#xf013; / 0xf013）",
                    ScadaIconGlyphs.TryResolve("f013", out var g1) && g1 == "\uF013"
                    && ScadaIconGlyphs.TryResolve("\\uf013", out var g2) && g2 == "\uF013"
                    && ScadaIconGlyphs.TryResolve("&#xf013;", out var g3) && g3 == "\uF013"
                    && ScadaIconGlyphs.TryResolve("0xf013", out var g4) && g4 == "\uF013",
                    "f013/\\uf013/&#xf013;/0xf013");

                Check("[BD3] 未知名字不显示字母（返回空串）",
                    !ScadaIconGlyphs.TryResolve("NoSuchIcon", out _)
                    && ScadaIconGlyphs.Resolve("NoSuchIcon") == string.Empty,
                    ScadaIconGlyphs.Resolve("NoSuchIcon"));

                var iconTypeface = new Typeface(
                    iconText.FontFamily,
                    FontStyles.Normal,
                    FontWeights.Normal,
                    FontStretches.Normal);

                var hasGlyph = iconTypeface.TryGetGlyphTypeface(out var glyphTypeface)
                    && glyphTypeface.CharacterToGlyphMap.ContainsKey(0xF013);

                Check("[BD4] 内置字体真的带这个字形（不是只改了 FontFamily 名字）",
                    hasGlyph,
                    $"family={iconText.FontFamily.Source}");

                // -----------------------------------------------------------
                // ⑩ 图片行命令：图元行（Hmi.Picture）与页面行都必须有选择/清除
                //    （曾经只给页面行加了命令，图元行的按钮绑定为 null，点了没反应）
                // -----------------------------------------------------------
                var pictureProperty = ElementRegistry.FindProperty("Hmi.Picture", "SourcePath")!;
                var pictureElement = new ScadaElement { TypeKey = "Hmi.Picture" };
                var elementRow = new ScadaPropertyRow(pictureElement, pictureProperty, () => null);

                Check("[BE1] Hmi.Picture 图片行有选择/清除命令（按钮不再点了没反应）",
                    elementRow.IsImageFile
                    && ((ICommand)elementRow.PickImageCommand).CanExecute(null)
                    && !((ICommand)elementRow.ClearImageCommand).CanExecute(null)
                    && elementRow.ImageSummary.Contains("未设置"),
                    $"imageFile={elementRow.IsImageFile} pick={((ICommand)elementRow.PickImageCommand).CanExecute(null)} summary={elementRow.ImageSummary}");

                var sourceElement = new ScadaElement { TypeKey = "Hmi.Picture" };
                sourceElement.SetProperty("SourcePath", "Image\\a.png");
                var sourceControl = (PictureElement)ElementRegistry.CreateControl(sourceElement);
                sourceControl.Refresh();

                Check("[BE3] 图元控件能从模型读到 SourcePath（描述符 TargetProperty 没漏）",
                    pictureProperty.TargetProperty != null
                    && sourceControl.SourcePath == "Image\\a.png",
                    $"target={pictureProperty.TargetProperty?.Name ?? "null"} control='{sourceControl.SourcePath}'");

                var backgroundSpec = ScadaPageProperties.All.First(s => s.Key == "BackgroundImage");
                var pageRow = new ScadaPagePropertyRow(new ScadaPage(), new ScadaDocument(), backgroundSpec, () => null);

                Check("[BE2] 页面背景图行同样有选择/清除命令",
                    pageRow.IsImageFile
                    && ((ICommand)pageRow.PickImageCommand).CanExecute(null)
                    && pageRow.ImageSummary.Contains("未设置"),
                    $"imageFile={pageRow.IsImageFile} summary={pageRow.ImageSummary}");

                // -----------------------------------------------------------
                // ⑪ 对象更改/打开/关闭：枚举、描述符、控件真的发（真交互入口）
                // -----------------------------------------------------------
                Check("[BF1] 对象事件有中文展示名",
                    ScadaEventType.ObjectChanged.DisplayName() == "对象状态改变"
                    && ScadaEventType.Opened.DisplayName() == "打开"
                    && ScadaEventType.Closed.DisplayName() == "关闭",
                    $"{ScadaEventType.ObjectChanged.DisplayName()}/{ScadaEventType.Opened.DisplayName()}/{ScadaEventType.Closed.DisplayName()}");

                var bitDescriptor = ElementRegistry.Find("Hmi.BitButton")!;
                var selectorDescriptor = ElementRegistry.Find("Hmi.SelectorSwitch")!;
                Check("[BF2] 位按钮/多档旋钮/滑块/下拉都声明了对象事件",
                    bitDescriptor.Events.Contains(ScadaEventType.ObjectChanged)
                    && bitDescriptor.Events.Contains(ScadaEventType.Opened)
                    && bitDescriptor.Events.Contains(ScadaEventType.Closed)
                    && selectorDescriptor.Events.Contains(ScadaEventType.ObjectChanged)
                    && selectorDescriptor.Events.Contains(ScadaEventType.Opened)
                    && selectorDescriptor.Events.Contains(ScadaEventType.Closed)
                    && ElementRegistry.Find("Hmi.Slider")!.Events.Contains(ScadaEventType.ObjectChanged)
                    && ElementRegistry.Find("Hmi.ComboBox")!.Events.Contains(ScadaEventType.ObjectChanged),
                    "BitButton/SelectorSwitch/Slider/ComboBox");

                var eventDocument = new ScadaDocument();
                var eventEngine = new ScadaAlarmEngine(eventDocument, null);
                var eventBeat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var eventContext = new ScadaRuntimeContext(eventEngine, eventBeat, new FakeScadaWriter());

                var eventElement = new ScadaElement
                {
                    TypeKey = "Hmi.SelectorSwitch",
                    Name = "手自动",
                    Width = 120,
                    Height = 36,
                };
                eventElement.SetProperty("OptionCount", "2");
                eventElement.SetProperty("Option0Text", "手动");
                eventElement.SetProperty("Option1Text", "自动");
                eventElement.SetProperty("Mode", "InputOutput");
                eventElement.SetProperty("Value", "0");
                eventElement.Bindings.Add(new ScadaBinding
                {
                    TargetProperty = "Value",
                    VariableId = Guid.NewGuid(),
                    VariableName = "Auto",
                });

                var eventSelector = (SelectorSwitchElement)ElementRegistry.CreateControl(eventElement);
                eventSelector.RuntimeContext = eventContext;
                eventSelector.Refresh();

                var raised = new List<ScadaEventType>();
                eventSelector.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args)
                        raised.Add(args.ScadaEvent);
                }));

                eventSelector.Select(1);
                var opened = raised.SequenceEqual(new[] { ScadaEventType.ObjectChanged, ScadaEventType.Opened });

                raised.Clear();
                eventSelector.Select(0);
                var closed = raised.SequenceEqual(new[] { ScadaEventType.ObjectChanged, ScadaEventType.Closed });

                Check("[BF3] 多档旋钮真交互：拨到 1 发「更改+打开」，拨回 0 发「更改+关闭」",
                    opened && closed,
                    $"opened={opened} closed={closed}");

                // -----------------------------------------------------------
                // ⑫ 二维码 / 条码：真实编码并解回（不是画了个像码的图案）
                // -----------------------------------------------------------
                var qrRendered = ScadaCodeRenderer.TryRender(
                    "SN-2026-0042", ScadaCodeRenderer.FormatQr, 200, 200, 10, "M",
                    out var qrBitmap, out var qrError);
                var qrDecoded = qrRendered
                    && ScadaCodeRenderer.TryDecode(qrBitmap, out var qrText)
                    && qrText == "SN-2026-0042";

                var barRendered = ScadaCodeRenderer.TryRender(
                    "LOT-20261023-A", ScadaCodeRenderer.FormatCode128, 320, 100, 10, null,
                    out var barBitmap, out var barError);
                var barDecoded = barRendered
                    && ScadaCodeRenderer.TryDecode(barBitmap, out var barText)
                    && barText == "LOT-20261023-A";

                Check("[BG1] 二维码/条码真实编码并可解回",
                    qrDecoded && barDecoded,
                    $"qr={qrDecoded} bar={barDecoded} qrErr={qrError} barErr={barError}");

                var emptyRejected = !ScadaCodeRenderer.TryRender("", ScadaCodeRenderer.FormatQr, 200, 200, 10, "M", out _, out _);
                var longRejected = !ScadaCodeRenderer.TryRender(new string('X', 3000), ScadaCodeRenderer.FormatQr, 200, 200, 10, "M", out _, out var longError);

                Check("[BG2] 空内容/超长拒绝并给原因",
                    emptyRejected && longRejected && longError != null && longError.Contains("2953"),
                    longError ?? "无原因");

                Check("[BG3] Hmi.QRCode / Hmi.Barcode 已注册且内容可绑",
                    ElementRegistry.Find("Hmi.QRCode") != null
                    && ElementRegistry.Find("Hmi.Barcode") != null
                    && ElementRegistry.FindProperty("Hmi.QRCode", "CodeContent")?.IsBindable == true
                    && ElementRegistry.FindProperty("Hmi.Barcode", "CodeContent")?.IsBindable == true,
                    "QRCode/Barcode/CodeContent");

                // -----------------------------------------------------------
                // ⑬ 多曲线趋势：四条值都可绑、时间窗/量程/图例在
                // -----------------------------------------------------------
                var trendDescriptor = ElementRegistry.Find("Hmi.MultiTrend");
                var valuesBindable = trendDescriptor != null
                    && Enumerable.Range(1, 4).All(i =>
                        ElementRegistry.FindProperty("Hmi.MultiTrend", $"Value{i}")?.IsBindable == true);

                Check("[BH1] Hmi.MultiTrend 已注册，Value1~Value4 四条曲线都可绑",
                    trendDescriptor != null && valuesBindable,
                    $"valuesBindable={valuesBindable}");

                Check("[BH2] 趋势的时间窗/量程模式/图例三项在",
                    ElementRegistry.FindProperty("Hmi.MultiTrend", "TimeWindow") != null
                    && ElementRegistry.FindProperty("Hmi.MultiTrend", "RangeMode") != null
                    && ElementRegistry.FindProperty("Hmi.MultiTrend", "ShowLegend") != null,
                    "TimeWindow/RangeMode/ShowLegend");
            }
            finally
            {
                SolutionAssetService.ClearCache();
                try { Directory.Delete(root, recursive: true); } catch { /* 临时目录清理失败不影响断言结论 */ }
            }
        }

        /// <summary>写一张指定颜色的 4×4 PNG（真实可解码，用于验证 TryLoad）</summary>
        private static void WritePng(string path, Color color)
        {
            var bitmap = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[4 * 4 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i + 0] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 0xFF;
            }

            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 4, 4), pixels, 4 * 4, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        // ==================================================================
        // [BJ] 历史数据：CSV 账本查询 + 数据表格历史源三态
        // ==================================================================

        private static void RunHistoryChecks()
        {
            Section("[BJ] 历史数据：CSV 账本（范围/分页/关键字/坏文件）+ 表格历史源三态");

            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { RunHistoryChecksCore(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (failure != null)
                Check("历史数据断言全程未抛异常", false, failure.ToString());
        }

        private static void RunHistoryChecksCore()
        {
            var root = Path.Combine(Path.GetTempPath(), "scada-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var utf8Bom = new System.Text.UTF8Encoding(true);

                File.WriteAllText(Path.Combine(root, "A.csv"),
                    "时间,SN,结果,尺寸\r\n" +
                    "2026-10-23 08:00:00,SN-1,OK,10.1\r\n" +
                    "2026-10-23 09:00:00,SN-2,NG,10.2\r\n" +
                    "2026-10-22 23:30:00,SN-3,OK,10.3\r\n", utf8Bom);

                File.WriteAllText(Path.Combine(root, "B.csv"),
                    "时间,SN,结果,尺寸\r\n" +
                    "2026-10-21 10:00:00,SN-4,OK,10.4\r\n" +
                    "not-a-time,SN-5,OK,10.5\r\n", utf8Bom);

                // 表头不一致的旁路文件：必须显式跳过并计数，不能把它当同一张表读。
                File.WriteAllText(Path.Combine(root, "C.csv"),
                    "Time,Value\r\n2026-10-23 07:00:00,42\r\n", utf8Bom);

                var source = new CsvFolderHistorySource(root);
                var range = new ScadaHistoryQuery
                {
                    FromLocal = new DateTime(2026, 10, 21),
                    ToLocal = new DateTime(2026, 10, 24),
                    PageIndex = 0,
                    PageSize = 2,
                };

                Check("[BJ1] 目录存在 → 数据源可用",
                    source.IsAvailable && source.UnavailableReason == null,
                    source.UnavailableReason ?? "可用");

                var page0 = source.QueryAsync(range, CancellationToken.None).GetAwaiter().GetResult();
                Check("[BJ2] 按行时间过滤 + 最新在前 + 总数/页数",
                    page0.Status == ScadaHistoryStatus.Ok
                    && page0.TotalCount == 4
                    && page0.PageCount == 2
                    && page0.Rows.Count == 2
                    && page0.Rows[0][1] == "SN-2"
                    && page0.Rows[1][1] == "SN-1",
                    $"{page0.Status} / total={page0.TotalCount} / 首行={page0.Rows.FirstOrDefault()?[1]}");

                Check("[BJ3] 坏文件与坏行显式警告（跳 1 个表头不一致、1 行时间无法解析）",
                    page0.Message != null
                    && page0.Message.Contains("跳过 1 个")
                    && page0.Message.Contains("1 行时间无法解析"),
                    page0.Message ?? "(null)");

                var page1Query = new ScadaHistoryQuery
                {
                    FromLocal = range.FromLocal,
                    ToLocal = range.ToLocal,
                    PageIndex = 1,
                    PageSize = 2,
                };
                var page1 = source.QueryAsync(page1Query, CancellationToken.None).GetAwaiter().GetResult();
                Check("[BJ4] 第二页取到剩余两条（页大小只影响本页，不改变总数）",
                    page1.Status == ScadaHistoryStatus.Ok
                    && page1.TotalCount == 4
                    && page1.Rows.Count == 2
                    && page1.Rows[0][1] == "SN-3"
                    && page1.Rows[1][1] == "SN-4",
                    $"total={page1.TotalCount} / 首行={page1.Rows.FirstOrDefault()?[1]}");

                var keywordQuery = new ScadaHistoryQuery
                {
                    FromLocal = range.FromLocal,
                    ToLocal = range.ToLocal,
                    PageIndex = 0,
                    PageSize = 50,
                    Keyword = "ng",
                };
                var keyword = source.QueryAsync(keywordQuery, CancellationToken.None).GetAwaiter().GetResult();
                Check("[BJ5] 关键字匹配任意列、忽略大小写",
                    keyword.Status == ScadaHistoryStatus.Ok
                    && keyword.TotalCount == 1
                    && keyword.Rows[0][1] == "SN-2",
                    $"total={keyword.TotalCount}");

                var emptyQuery = new ScadaHistoryQuery
                {
                    FromLocal = new DateTime(2020, 1, 1),
                    ToLocal = new DateTime(2020, 1, 2),
                };
                var empty = source.QueryAsync(emptyQuery, CancellationToken.None).GetAwaiter().GetResult();
                Check("[BJ6] 范围合法但没记录 → Empty（不是 Unavailable）",
                    empty.Status == ScadaHistoryStatus.Empty && empty.TotalCount == 0,
                    empty.Status.ToString());

                var invalidQuery = new ScadaHistoryQuery
                {
                    FromLocal = new DateTime(2026, 10, 24),
                    ToLocal = new DateTime(2026, 10, 21),
                };
                var invalid = source.QueryAsync(invalidQuery, CancellationToken.None).GetAwaiter().GetResult();
                Check("[BJ7] 起点晚于终点 → InvalidRange",
                    invalid.Status == ScadaHistoryStatus.InvalidRange,
                    invalid.Message ?? "(null)");

                var missing = new CsvFolderHistorySource(Path.Combine(root, "nope"));
                Check("[BJ8] 目录不存在 → 不可用并给原因",
                    !missing.IsAvailable && !string.IsNullOrWhiteSpace(missing.UnavailableReason),
                    missing.UnavailableReason ?? "(null)");

                var noTimeDir = Path.Combine(root, "notime");
                Directory.CreateDirectory(noTimeDir);
                File.WriteAllText(Path.Combine(noTimeDir, "X.csv"),
                    "SN,结果\r\nSN-1,OK\r\n", utf8Bom);

                var noTime = new CsvFolderHistorySource(noTimeDir)
                    .QueryAsync(range, CancellationToken.None).GetAwaiter().GetResult();
                Check("[BJ9] 表头没有时间列 → InvalidRange，不拿别的列硬当时间",
                    noTime.Status == ScadaHistoryStatus.InvalidRange
                    && noTime.Message != null
                    && noTime.Message.Contains("找不到时间列"),
                    noTime.Message ?? "(null)");

                var quoted = CsvFolderHistorySource.ParseCsvLine("\"a,b\",c,\"d\"\"e\"");
                Check("[BJ10] CSV 解析：引号包裹与双引号转义",
                    quoted.Count == 3 && quoted[0] == "a,b" && quoted[1] == "c" && quoted[2] == "d\"e",
                    string.Join(" | ", quoted));

                // ---------------- 表格历史源模式 ----------------
                var columns = new[] { "时间", "SN", "结果", "尺寸" };
                var rowsPage0 = new List<IReadOnlyList<string>>
                {
                    new[] { "2026-10-23 09:00:00", "SN-2", "NG", "10.2" },
                    new[] { "2026-10-23 08:00:00", "SN-1", "OK", "10.1" },
                };
                var rowsPage1 = new List<IReadOnlyList<string>>
                {
                    new[] { "2026-10-22 23:30:00", "SN-3", "OK", "10.3" },
                    new[] { "2026-10-21 10:00:00", "SN-4", "OK", "10.4" },
                };

                var fake = new FakeHistorySource
                {
                    Handler = q => q.PageIndex == 0
                        ? ScadaHistoryResult.Ok(columns, rowsPage0, 4, 0, q.PageSize, "断言历史源")
                        : ScadaHistoryResult.Ok(columns, rowsPage1, 4, 1, q.PageSize, "断言历史源"),
                };

                var engine = new ScadaAlarmEngine(new ScadaDocument(), null);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(engine, beat, history: fake);

                var grid = new DataGridElement
                {
                    Width = 720,
                    Height = 300,
                    Source = "History",
                    HistoryRange = "Today",
                    HistoryPageSize = 2,
                };

                grid.RuntimeContext = context;

                Check("[BJ11] 切到历史源：工具栏出现、首查结果进表",
                    grid.IsHistoryMode
                    && grid.HistoryToolbarVisibility == Visibility.Visible
                    && grid.Rows.Count == 2,
                    $"mode={grid.IsHistoryMode} / rows={grid.Rows.Count}");

                Check("[BJ12] 结果列替换表格列（时间列等宽）",
                    grid.HeaderCells.Count == 4
                    && grid.Rows[0].Cells[0].Monospace
                    && grid.Rows[0].Cells[1].Text == "SN-2",
                    $"cols={grid.HeaderCells.Count} / 首行={grid.Rows.FirstOrDefault()?.Cells[1].Text} / 时间列等宽={grid.Rows.FirstOrDefault()?.Cells[0].Monospace}");

                Check("[BJ13] 分页读数：第几页/共几页/总数",
                    grid.HistoryPageText.Contains("1 / 2")
                    && grid.HistoryPageText.Contains("共 4 条"),
                    grid.HistoryPageText);

                grid.NextHistoryPageCommand.Execute(null);
                Check("[BJ14] 下一页 → 第 2 页数据",
                    grid.HistoryPageIndex == 1
                    && grid.Rows.Count == 2
                    && grid.Rows[0].Cells[1].Text == "SN-3"
                    && grid.Rows[1].Cells[1].Text == "SN-4"
                    && grid.HistoryPageText.Contains("2 / 2"),
                    $"page={grid.HistoryPageIndex} / 首行={grid.Rows.FirstOrDefault()?.Cells[1].Text}");

                grid.PreviousHistoryPageCommand.Execute(null);
                Check("[BJ15] 上一页回到第 1 页",
                    grid.HistoryPageIndex == 0 && grid.Rows[0].Cells[1].Text == "SN-2",
                    $"page={grid.HistoryPageIndex}");

                grid.HistoryKeyword = "NG";
                grid.QueryHistoryCommand.Execute(null);
                var lastQuery = fake.Queries.Last();
                Check("[BJ16] 关键字进入查询；改筛选后回到第一页",
                    lastQuery.Keyword == "NG" && lastQuery.PageIndex == 0,
                    $"keyword={lastQuery.Keyword} / page={lastQuery.PageIndex}");

                grid.HistoryRange = "Yesterday";
                grid.QueryHistoryCommand.Execute(null);
                lastQuery = fake.Queries.Last();
                Check("[BJ17] 范围映射到本地日界：昨天 = [昨天 00:00, 今天 00:00)",
                    lastQuery.FromLocal == DateTime.Today.AddDays(-1)
                    && lastQuery.ToLocal == DateTime.Today,
                    $"{lastQuery.FromLocal:O} ~ {lastQuery.ToLocal:O}");

                var queriesBeforeCycle = fake.Queries.Count;
                grid.CycleHistoryRangeCommand.Execute(null);
                Check("[BJ17a] 范围按钮循环到下一档并立即查询",
                    grid.HistoryRange == "Last7Days"
                    && grid.HistoryRangeLabel == "最近 7 天"
                    && fake.Queries.Count == queriesBeforeCycle + 1,
                    $"{grid.HistoryRange} / {grid.HistoryRangeLabel} / queries={fake.Queries.Count}");

                Check("[BJ17b] 标题计数在历史模式显示总数（不是本页行数）",
                    grid.TitleCountText == "4 条",
                    grid.TitleCountText);

                fake.Handler = q => ScadaHistoryResult.Empty(
                    columns, q.PageIndex, q.PageSize, "断言历史源", "该范围没有记录");
                grid.QueryHistoryCommand.Execute(null);
                Check("[BJ18] 空结果：标题说「该范围没有记录」、表体清空、不弹失败横幅",
                    grid.Rows.Count == 0
                    && grid.EmptyVisibility == Visibility.Visible
                    && grid.EmptyTitle.Contains("没有记录")
                    && grid.HistoryBannerVisibility == Visibility.Collapsed,
                    $"{grid.EmptyTitle} / banner={grid.HistoryBannerVisibility}");

                fake.Handler = q => ScadaHistoryResult.Ok(columns, rowsPage0, 4, 0, q.PageSize, "断言历史源");
                grid.QueryHistoryCommand.Execute(null);
                fake.Handler = q => ScadaHistoryResult.Unavailable("断言：目录不存在", "断言历史源");
                grid.QueryHistoryCommand.Execute(null);
                Check("[BJ19] 不可用不清空上一次结果：旧行保留、原因进横幅与脚注",
                    grid.Rows.Count == 2
                    && grid.StatusText.Contains("目录不存在")
                    && grid.EmptyVisibility == Visibility.Collapsed
                    && grid.HistoryBannerVisibility == Visibility.Visible
                    && grid.HistoryBannerText.Contains("目录不存在"),
                    $"rows={grid.Rows.Count} / banner={grid.HistoryBannerText}");

                var emptyGrid = new DataGridElement { Source = "History", HistoryPageSize = 2 };
                emptyGrid.RuntimeContext = new ScadaRuntimeContext(
                    engine,
                    beat,
                    history: new FakeHistorySource
                    {
                        IsAvailable = false,
                        UnavailableReason = "断言：没有历史目录",
                    });

                Check("[BJ20] 数据源不可用且无旧行：空态直接说明原因",
                    emptyGrid.EmptyVisibility == Visibility.Visible
                    && emptyGrid.EmptyTitle.Contains("不可用")
                    && emptyGrid.EmptyHint.Contains("没有历史目录"),
                    $"{emptyGrid.EmptyTitle} / {emptyGrid.EmptyHint}");

                grid.Source = "Live";
                Check("[BJ21] 切回实时：工具栏收起、行区回到实时空态",
                    !grid.IsHistoryMode
                    && grid.HistoryToolbarVisibility == Visibility.Collapsed
                    && grid.EmptyTitle == "还没有配置采集"
                    && grid.TitleCountText == "0 条",
                    $"{grid.IsHistoryMode} / {grid.EmptyTitle} / {grid.TitleCountText}");
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { /* 临时目录删不掉不影响结论 */ }
            }
        }

        /// <summary>可控的历史源：Handler 决定每次查询返回什么，Queries 记录入参。</summary>
        private sealed class FakeHistorySource : IScadaHistorySource
        {
            public string Name => "断言历史源";

            public bool IsAvailable { get; set; } = true;

            public string? UnavailableReason { get; set; }

            public List<ScadaHistoryQuery> Queries { get; } = new();

            public Func<ScadaHistoryQuery, ScadaHistoryResult> Handler { get; set; } =
                q => ScadaHistoryResult.Empty(Array.Empty<string>(), q.PageIndex, q.PageSize, "断言历史源");

            public Task<ScadaHistoryResult> QueryAsync(ScadaHistoryQuery query, CancellationToken cancellationToken)
            {
                Queries.Add(query);

                return Task.FromResult(
                    IsAvailable
                        ? Handler(query)
                        : ScadaHistoryResult.Unavailable(UnavailableReason ?? "不可用", Name));
            }
        }

        /// <summary>对象事件断言用的写通道：只回成功，不碰变量。</summary>
        private sealed class FakeScadaWriter : IScadaValueWriter
        {
            public bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error)
            {
                error = null;
                return true;
            }
        }

        // ==================================================================
        // [BI] 报警声音：状态机（开关/分级/静音/确认即停）+ WAV 结构
        // ==================================================================

        /// <summary>
        /// 声音是"时间 + 报警态"的状态机，最容易错的三处都在这里钉住：
        /// ① 确认最后一条之后必须停（否则操作员确认完还在叫）；
        /// ② 静音到期必须自己恢复（停掉定时器等它，就永远不会醒）；
        /// ③ 紧急级不可静音（静音只拦警告与提示）。
        ///
        /// 用假播放器数调用，不碰声卡：断言环境里没有音频设备也能跑，
        /// 而"真能听见"由临时探针在真机上放一遍（见 code-changes 记录）。
        /// </summary>
        private static void RunSoundAlarmChecks()
        {
            Section("[BI] 报警声音：开关、分级音型、静音到期、确认即停、设备不可用、WAV 结构");

            var now = new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc);
            var doc = new ScadaDocument();

            var criticalId = Guid.NewGuid();
            var criticalHandle = new FakeValueHandle
            {
                VariableId = criticalId,
                Name = "炉温",
                DataType = typeof(double),
                Value = 0d,
            };
            var warningId = Guid.NewGuid();
            var warningHandle = new FakeValueHandle
            {
                VariableId = warningId,
                Name = "压力",
                DataType = typeof(double),
                Value = 0d,
            };
            var infoId = Guid.NewGuid();
            var infoHandle = new FakeValueHandle
            {
                VariableId = infoId,
                Name = "安全门",
                DataType = typeof(bool),
                Value = false,
            };

            var source = new FakeValueSource();
            source.ById[criticalId] = criticalHandle;
            source.ById[warningId] = warningHandle;
            source.ById[infoId] = infoHandle;

            var criticalDef = doc.AddAlarm("炉温高高", ScadaAlarmKind.HighHigh);
            criticalDef.Bind(criticalId, "炉温");
            criticalDef.Threshold = 80;
            criticalDef.Severity = ScadaAlarmSeverity.Critical;

            var warningDef = doc.AddAlarm("压力高", ScadaAlarmKind.High);
            warningDef.Bind(warningId, "压力");
            warningDef.Threshold = 50;
            warningDef.Severity = ScadaAlarmSeverity.Warning;

            var infoDef = doc.AddAlarm("安全门开", ScadaAlarmKind.BoolOn);
            infoDef.Bind(infoId, "安全门");
            infoDef.Severity = ScadaAlarmSeverity.Info;

            var engine = new ScadaAlarmEngine(doc, source, () => now);
            engine.Attach();

            var player = new FakeSoundPlayer();
            var diagnostics = new List<string>();
            var service = new ScadaSoundAlarmService(
                engine,
                player,
                (_, message) => diagnostics.Add(message),
                () => now,
                autoTick: false);

            // 按「还等确认的那条」确认，不按列表第一条：已确认但条件仍成立的记录
            // 还挂在 ActiveAlarms 上（状态 Acknowledged），按索引会确认到它、返回 0。
            int AckUnacknowledged()
            {
                var record = engine.ActiveAlarms.FirstOrDefault(r => r.NeedsAcknowledge);
                return record == null ? 0 : engine.Acknowledge(record.AlarmId);
            }

            // ---- ① 默认关闭 ----
            criticalHandle.Raise(90d);
            Check("[BI1] 默认关闭：报警照常激活，但一声不响",
                engine.UnacknowledgedCount == 1 && player.Plays.Count == 0 && !service.IsSounding,
                $"未确认={engine.UnacknowledgedCount} / 响={player.Plays.Count}");

            // ---- ② 打开声音时已有未确认报警 → 立即响 ----
            service.Enabled = true;
            Check("[BI2] 打开声音时已有未确认报警 → 立即响一遍（紧急音）",
                player.Plays.Count == 1
                && player.Plays[0] == ScadaAlarmSeverity.Critical
                && service.IsSounding,
                $"响={player.Plays.Count} / 最后={player.Plays.LastOrDefault()}");

            now = now.Add(ScadaSoundAlarmService.CriticalRepeat);
            service.Tick();
            Check("[BI3] 紧急音按 950ms 重复：未确认就一直响",
                player.Plays.Count == 2 && service.IsSounding,
                $"响={player.Plays.Count}");

            // ---- ③ 紧急不可静音 ----
            var criticalMuted = service.Mute();
            Check("[BI4] 紧急级不可静音：Mute 返回 false，声音不停",
                !criticalMuted && !service.IsMuted && service.IsSounding,
                $"Mute={criticalMuted} / IsMuted={service.IsMuted}");

            // ---- ④ 确认最后一条 → 停 ----
            AckUnacknowledged();
            Check("[BI5] 确认最后一条未确认报警 → 立即停声",
                !service.IsSounding && player.Stops > 0 && engine.UnacknowledgedCount == 0,
                $"IsSounding={service.IsSounding} / Stop={player.Stops}");

            // ---- ⑤ 警告级：间歇 2s ----
            warningHandle.Raise(60d);
            Check("[BI6] 警告级响一遍并排下一拍",
                player.Plays.Count == 3
                && player.Plays[2] == ScadaAlarmSeverity.Warning
                && service.IsSounding,
                $"响={player.Plays.Count} / 最后={player.Plays.LastOrDefault()}");

            now = now.Add(ScadaSoundAlarmService.WarningRepeat);
            service.Tick();
            Check("[BI7] 警告音到点重复（间歇 2s）",
                player.Plays.Count == 4,
                $"响={player.Plays.Count}");

            // ---- ⑥ 静音与到期恢复 ----
            var warningMuted = service.Mute();
            Check("[BI8] 警告可静音：立即停声",
                warningMuted && service.IsMuted && !service.IsSounding,
                $"Mute={warningMuted} / IsMuted={service.IsMuted}");

            var playsBeforeMute = player.Plays.Count;
            now = now.Add(TimeSpan.FromMinutes(1));
            service.Tick();
            Check("[BI9] 静音期内节拍不发声",
                player.Plays.Count == playsBeforeMute,
                $"响={player.Plays.Count}（静音前 {playsBeforeMute}）");

            now = now.Add(TimeSpan.FromMinutes(4));
            service.Tick();
            Check("[BI10] 静音 5 分钟到期自动恢复发声（定时器必须一直在跑）",
                player.Plays.Count == playsBeforeMute + 1
                && !service.IsMuted
                && service.IsSounding,
                $"响={player.Plays.Count} / IsMuted={service.IsMuted}");

            AckUnacknowledged();
            Check("[BI11] 确认警告后同样停声",
                !service.IsSounding && engine.UnacknowledgedCount == 0,
                $"IsSounding={service.IsSounding}");

            // ---- ⑦ 提示级只响一次 ----
            var playsBeforeInfo = player.Plays.Count;
            infoHandle.Raise(true);
            var infoPlays = player.Plays.Count;
            Check("[BI12] 提示级每次新报警只响一次，且不算「持续发声」",
                infoPlays == playsBeforeInfo + 1
                && player.Plays[^1] == ScadaAlarmSeverity.Info
                && !service.IsSounding,
                $"响={infoPlays} / 最后={player.Plays.LastOrDefault()} / IsSounding={service.IsSounding}");

            now = now.Add(TimeSpan.FromSeconds(10));
            service.Tick();
            Check("[BI13] 提示级不重复（不因为「还挂着未确认」每 2 秒叫）",
                player.Plays.Count == infoPlays,
                $"响={player.Plays.Count}");

            AckUnacknowledged();

            // ---- ⑧ 关闭开关立即停声；试听是显式动作 ----
            // 先让条件恢复再重新越限：已确认但条件仍成立的记录不会因为"再写一次同一个值"重生，
            // 那是引擎的既有语义（同一值不重复评估），不是声音服务该绕过去的东西。
            warningHandle.Raise(40d);
            warningHandle.Raise(60d);
            Check("[BI14] 重新越限 → 声音恢复",
                service.IsSounding && player.Plays.Count == infoPlays + 1,
                $"IsSounding={service.IsSounding} / 响={player.Plays.Count}");

            service.Enabled = false;
            Check("[BI15] 关闭开关立即停声，且之后不再响",
                !service.IsSounding && player.Plays.Count == infoPlays + 1,
                $"IsSounding={service.IsSounding}");

            var beforeTest = player.Plays.Count;
            var testOk = service.Test();
            Check("[BI16] 试听是用户显式动作：关闭状态下也能响",
                testOk && player.Plays.Count == beforeTest + 1,
                $"Test={testOk} / 响={player.Plays.Count}");

            AckUnacknowledged();
            service.Dispose();

            // ---- ⑨ 设备不可用：不抛、不响、留一行诊断 ----
            var unavailablePlayer = new FakeSoundPlayer
            {
                IsAvailable = false,
                UnavailableReason = "断言：无音频设备",
            };
            var unavailableDiagnostics = new List<string>();
            var unavailableService = new ScadaSoundAlarmService(
                engine,
                unavailablePlayer,
                (_, message) => unavailableDiagnostics.Add(message),
                () => now,
                autoTick: false);

            unavailableService.Enabled = true;
            warningHandle.Raise(0d);
            warningHandle.Raise(70d);
            Check("[BI17] 音频设备不可用：不抛异常、不尝试发声、留一行诊断",
                unavailablePlayer.Plays.Count == 0
                && unavailableDiagnostics.Any(m => m.Contains("声音不可用")),
                $"响={unavailablePlayer.Plays.Count} / 诊断={string.Join(" | ", unavailableDiagnostics)}");

            unavailableService.Dispose();
            engine.Detach();

            // ---- ⑩ WAV 结构：时长、位深、振幅、双音 ----
            var criticalWav = ScadaAlarmTone.BuildWav(ScadaAlarmSeverity.Critical, 100);
            var warningWav = ScadaAlarmTone.BuildWav(ScadaAlarmSeverity.Warning, 100);
            var infoWav = ScadaAlarmTone.BuildWav(ScadaAlarmSeverity.Info, 100);

            var criticalParsed = ParseWav(criticalWav);
            Check("[BI18] WAV 头：RIFF/WAVE、PCM、单声道、44100Hz、16 位",
                criticalParsed.Riff == "RIFF"
                && criticalParsed.Wave == "WAVE"
                && criticalParsed.Format == 1
                && criticalParsed.Channels == 1
                && criticalParsed.SampleRate == ScadaAlarmTone.SampleRate
                && criticalParsed.Bits == 16,
                $"format={criticalParsed.Format} / channels={criticalParsed.Channels} / rate={criticalParsed.SampleRate} / bits={criticalParsed.Bits}");

            var warningParsed = ParseWav(warningWav);
            var infoParsed = ParseWav(infoWav);
            Check("[BI19] 时长按级别：紧急 900ms / 警告 320ms / 提示 130ms",
                criticalParsed.Samples.Length == ScadaAlarmTone.SampleRate * 900 / 1000
                && warningParsed.Samples.Length == ScadaAlarmTone.SampleRate * 320 / 1000
                && infoParsed.Samples.Length == ScadaAlarmTone.SampleRate * 130 / 1000,
                $"紧急={criticalParsed.Samples.Length} / 警告={warningParsed.Samples.Length} / 提示={infoParsed.Samples.Length}");

            var silent = ParseWav(ScadaAlarmTone.BuildWav(ScadaAlarmSeverity.Warning, 0));
            var quiet = ParseWav(ScadaAlarmTone.BuildWav(ScadaAlarmSeverity.Warning, 20));
            var peakFull = Peak(criticalParsed.Samples);
            var peakQuiet = Peak(quiet.Samples);
            var expectedPeak = short.MaxValue * ScadaAlarmTone.PeakAtFullVolume;
            Check("[BI20] 音量烘进振幅：0 全零、100 峰值≈0.6 满量程、20 明显更低",
                Peak(silent.Samples) == 0
                && Math.Abs(peakFull - expectedPeak) <= expectedPeak * 0.02
                && peakQuiet > 0
                && peakQuiet < peakFull * 0.35,
                $"0={Peak(silent.Samples)} / 20={peakQuiet} / 100={peakFull} / 期望≈{expectedPeak:0}");

            var critical880 = Magnitude(criticalParsed.Samples, 880);
            var critical660 = Magnitude(criticalParsed.Samples, 660);
            var critical1000 = Magnitude(criticalParsed.Samples, 1000);
            var warning880 = Magnitude(warningParsed.Samples, 880);
            var warning660 = Magnitude(warningParsed.Samples, 660);
            var info1175 = Magnitude(infoParsed.Samples, 1175);
            var info880 = Magnitude(infoParsed.Samples, 880);
            Check("[BI21] 音型可辨：紧急是 880/660 双音，警告单 880，提示单 1175",
                critical880 > critical1000 * 10
                && critical660 > critical1000 * 10
                && warning880 > warning660 * 10
                && info1175 > info880 * 10,
                $"紧急 880={critical880:0} 660={critical660:0} 1000={critical1000:0} / 警告 880={warning880:0} 660={warning660:0} / 提示 1175={info1175:0} 880={info880:0}");
        }

        /// <summary>数"响了几次、停了几次"的假播放器；真机播放由临时探针验。</summary>
        private sealed class FakeSoundPlayer : IScadaSoundPlayer
        {
            public bool IsAvailable { get; set; } = true;

            public string? UnavailableReason { get; set; }

            public List<ScadaAlarmSeverity> Plays { get; } = new();

            public int Stops { get; private set; }

            public void Play(ScadaAlarmSeverity severity, int volume) => Plays.Add(severity);

            public void Stop() => Stops++;
        }

        /// <summary>把 WAV 解成断言要的几项；只认本仓自己生成的 44 字节标准头。</summary>
        private static (string Riff, string Wave, short Format, short Channels, int SampleRate, short Bits, short[] Samples) ParseWav(byte[] wav)
        {
            using var stream = new MemoryStream(wav);
            using var reader = new BinaryReader(stream);

            var riff = new string(reader.ReadChars(4));
            reader.ReadInt32();                       // 文件长度
            var wave = new string(reader.ReadChars(4));

            var fmt = new string(reader.ReadChars(4));
            var fmtSize = reader.ReadInt32();
            var format = reader.ReadInt16();
            var channels = reader.ReadInt16();
            var sampleRate = reader.ReadInt32();
            reader.ReadInt32();                       // 每秒字节数
            reader.ReadInt16();                       // 块对齐
            var bits = reader.ReadInt16();
            if (fmtSize > 16)
                reader.ReadBytes(fmtSize - 16);

            var data = new string(reader.ReadChars(4));
            var dataSize = reader.ReadInt32();
            var samples = new short[dataSize / 2];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = reader.ReadInt16();

            return (riff, wave, format, channels, sampleRate, bits, samples);
        }

        private static int Peak(short[] samples)
        {
            var peak = 0;
            foreach (var sample in samples)
            {
                var magnitude = Math.Abs((int)sample);
                if (magnitude > peak)
                    peak = magnitude;
            }

            return peak;
        }

        /// <summary>单频能量（Goertzel 的直白版）：用来证明"这段音里确实有这个频率"。</summary>
        private static double Magnitude(short[] samples, double frequency)
        {
            double real = 0, imaginary = 0;

            for (var i = 0; i < samples.Length; i++)
            {
                var angle = 2 * Math.PI * frequency * i / ScadaAlarmTone.SampleRate;
                real += samples[i] * Math.Cos(angle);
                imaginary -= samples[i] * Math.Sin(angle);
            }

            return Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
        }
    }
}
