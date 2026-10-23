using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Scada.Snapshots;
using VisionMaster.Services;

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
            public static void Run() => RunBackgroundAssetChecks();
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
    }
}
