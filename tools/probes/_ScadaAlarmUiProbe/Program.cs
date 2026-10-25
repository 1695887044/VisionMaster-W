using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.Services.Hardware;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace ScadaAlarmUiProbe
{
    /// <summary>
    /// 报警配置弹窗出图探针（R105/R134/R129）：在屏幕外真窗口里装 <see cref="ScadaAlarmConfigDialogView"/>，
    /// 用真 <see cref="ScadaDocument"/> 喂出设计基线的九个画面态，逐态截图。
    ///
    /// 两条与"证据可信"直接相关的取舍：
    /// · 资源字典严格按 App.xaml 的顺序与份数合并（含 Fluent 之后重挂 Input.xaml 那一刀）。
    ///   少挂或换序，TextBox/CheckBox 拿到的就是另一份模板，截图就成了另一套界面的证据。
    /// · 每态打完先量一行状态指纹（选中行 / 计数 / 可见行数 / 像素哈希）。九张图里若有两张
    ///   哈希相同，说明这一态的视觉根本没跟着视图模型走，肉眼看图是看不出来的。
    ///
    /// 输出：artifacts\render\&lt;yyyyMMdd&gt;\scada-alarm-config\wpf-&lt;state&gt;.png（R166）。
    /// 基线 design-&lt;state&gt;.png 是只读对照物，本探针不往 docs/UI设计基线/ 写任何文件。
    /// </summary>
    internal static class Program
    {
        /// <summary>与 docs/UI设计基线/scada-alarm-config/design.html 的 body[data-state] 一一对应</summary>
        private static readonly string[] AllStates =
            { "filled", "stale", "filter", "nomatch", "error", "receipt", "new", "empty", "running" };

        [STAThread]
        private static int Main(string[] args)
        {
            // 重定向到日志文件时，.NET 默认按控制台代码页（本机 936）写中文，
            // gate 侧严格 UTF-8 回读会直接失败（R183），这里显式声明一次。
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app);
            ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            string dir = OutputDirectory(args);
            Directory.CreateDirectory(dir);

            var wanted = (args.FirstOrDefault(a => a.StartsWith("--states=", StringComparison.Ordinal))
                    ?.Substring("--states=".Length) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var states = wanted.Length > 0 ? wanted : AllStates;

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                foreach (var state in states)
                {
                    if (Array.IndexOf(AllStates, state) < 0)
                    {
                        Console.WriteLine($"RESULT: FAIL（未知画面态「{state}」，可选：{string.Join(" / ", AllStates)}）");
                        return 1;
                    }

                    hashes[state] = Shot(dir, state);
                }

                // 九态互不相同是本探针的最低可信度：相同 = 至少有一态根本没改变视觉。
                var dupes = hashes.GroupBy(kv => kv.Value)
                    .Where(g => g.Count() > 1)
                    .Select(g => string.Join(" = ", g.Select(kv => kv.Key)))
                    .ToArray();

                Console.WriteLine($"出图目录: {dir}");
                if (dupes.Length > 0)
                {
                    Console.WriteLine($"RESULT: FAIL（以下画面态像素完全相同，视觉未跟着状态走：{string.Join("；", dupes)}）");
                    return 1;
                }

                Console.WriteLine($"RESULT: OK（{hashes.Count} 态，像素指纹互不相同）");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static string Shot(string dir, string state)
        {
            ScadaEditHistory.Clear();

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "AlarmCfgProbe.vms" };
            solution.Flows.Clear();
            solution.Flows.Add(new FlowModel { FlowName = "Main", Role = FlowRole.Main });
            workspace.SwitchSolution(solution);
            workspace.GlobalVariables.Clear();

            if (state != "empty")
                SeedAlarms(solution.Scada, workspace);

            // 运行态只影响「运行中」chip 与底栏后果说明，其余九态一律按未运行显示。
            var runState = new StaticFlowRunState(state == "running");
            var vm = new ScadaAlarmConfigDialogViewModel(workspace, new NoopPicker(), null, () => "张工", runState);
            vm.OnDialogOpened(new DialogParameters());

            // 种子里 AddAlarm 会往全局撤销栈压记录；清掉，让截图落在"刚打开"这个中立态上。
            ScadaEditHistory.Clear();

            Arrange(vm, state);

            ViewModelLocationProvider.Register<ScadaAlarmConfigDialogView>(() => vm);
            var view = new ScadaAlarmConfigDialogView { DataContext = vm };

            // 与 prism:Dialog.WindowStyle 同三项：None / NoResize / SizeToContent，
            // 否则截到的是"套了系统标题栏的窗口"，跟基线根本不是同一屏。
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                ShowInTaskbar = false,
                Content = view,
            };

            window.Show();
            // running 态的 chip 靠 1 秒 DispatcherTimer 现读；泵够 1.3 秒它才真的亮起来。
            Pump(state == "running" ? 1300 : 700);
            view.UpdateLayout();

            // Loaded 之后才做的排布（新增行走视图的滚动/聚焦路径），做完再泵一次让
            // DispatcherPriority.Loaded 上的延迟动作真的跑完，否则截到的是"还没滚过去"的那一帧。
            ArrangeAfterShow(vm, state);
            Pump(300);
            view.UpdateLayout();

            var row = vm.SelectedRow;
            Console.WriteLine(
                $"  [{state}] rows={vm.Rows.Count} visible={vm.VisibleRowCount} " +
                $"total=\"{vm.TotalHint}\" breakdown={vm.ShowBreakdown} " +
                $"group=\"{vm.GroupFilter}\" search=\"{vm.SearchText}\" " +
                $"selected=\"{row?.Name}\" hint={row?.SetupHintTitle} " +
                $"inlineErr=\"{(row?.HasInlineError == true ? row.InlineError : "")}\" " +
                $"receipt={vm.HasReceipt} running={vm.IsRunning} " +
                $"undo=\"{vm.UndoButtonText}\" " +
                $"size={view.ActualWidth:0}×{view.ActualHeight:0}");

            if (Math.Abs(view.ActualWidth - 920) > 1 || Math.Abs(view.ActualHeight - 640) > 1)
                Console.WriteLine($"    [warn] 弹窗尺寸 {view.ActualWidth:0}×{view.ActualHeight:0} 与基线 920×640 不符");

            var bitmap = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight));
            // 指纹必须在 Save 之前算：位图一旦编过 PNG 再解回来，混入的就是编解码器的噪声，
            // "两图像素相同"这条判据会被稀释成"两张图碰巧没撞在一起"。
            string hash = PixelHash(bitmap);
            Console.WriteLine($"    [pixels] {hash}");

            string path = Path.Combine(dir, $"wpf-{state}.png");
            Save(bitmap, path);

            window.Close();
            Pump(120);
            vm.OnDialogClosed();
            return hash;
        }

        /// <summary>把九态各自的"这一屏长什么样"排出来；数据一律来自种子，不靠临时造值。</summary>
        private static void Arrange(ScadaAlarmConfigDialogViewModel vm, string state)
        {
            switch (state)
            {
                case "filled":
                case "running":
                    // 基线 filled/running 选中的是「1#电机温度」（高高限 / 严重 / 阈值 85 / 回差 3 / 延时 2），
                    // 且底栏带着「撤销「修改报警 [1#电机温度] 的阈值」」——那是一条真实编辑记录换来的。
                    // 这里先改 86 再改回 85：模型停在基线值，撤销栈里留下一条同名记录（等值写入不产记录，
                    // 所以不能直接写 85）。
                    vm.SelectedRow = vm.Rows.ElementAtOrDefault(3);
                    vm.SelectedRow.ThresholdText = "86";
                    vm.SelectedRow.ThresholdText = "85";
                    break;

                case "stale":
                    vm.SelectedRow = vm.Rows.ElementAtOrDefault(0);
                    break;

                case "filter":
                    vm.SelectedRow = vm.Rows.ElementAtOrDefault(3);
                    vm.SetGroupFilterCommand.Execute("一号线");
                    break;

                case "nomatch":
                    vm.SearchText = "不存在的报警";
                    break;

                case "error":
                    // 基线 error 态：把「视觉-单件缺陷超限」改成已存在的「冷却水流量」，重名报错落在这一行
                    vm.SelectedRow = vm.Rows.ElementAtOrDefault(2);
                    vm.SelectedRow.Name = "冷却水流量";
                    break;

                case "receipt":
                    vm.SelectedRow = vm.Rows.ElementAtOrDefault(5);
                    vm.RemoveCommand.Execute();
                    break;

                case "new":
                    // 不在这里加：视图的 RowAdded（滚到新行 + 聚焦名称格）挂在 Loaded 上，
                    // 窗口还没 Show 就 Execute 等于绕过这条路径，截图证明不了"详情没被裁"。
                    // 真正的动作在 ArrangeAfterShow。
                    break;

                case "empty":
                    break;
            }
        }

        /// <summary>
        /// 种子逐条抄自 design.html 的 tbody（行序、变量名、判定值、组名、三条长文本都对齐），
        /// 这样"同一屏同一状态"的对照才成立（R129）。
        /// </summary>
        private static void SeedAlarms(ScadaDocument doc, WorkspaceContext workspace)
        {
            Add(doc, workspace, "通讯-触发变量断线", "PLC_Trigger", ScadaAlarmKind.Stale, ScadaAlarmSeverity.Critical,
                enabled: true, group: "通讯", threshold: 0, deadband: 0, delay: 0, stale: 5,
                message: "PLC 触发变量超过 5 秒未刷新",
                cause: "Modbus 503 连接中断或 PLC 停止轮询",
                remedy: "检查 PLC_Modbus 连接与寄存器 0，确认 PLC 处于运行态",
                extra: "对应寄存器 0，心跳由 PLC 侧自增");

            Add(doc, workspace, "通讯-S7心跳丢失", "S7_Heartbeat", ScadaAlarmKind.BoolOff, ScadaAlarmSeverity.Warning,
                enabled: true, group: "通讯", threshold: 0, deadband: 0, delay: 3, stale: 10,
                message: "S7 心跳位丢失", cause: "网线松动或 PLC 断电", remedy: "检查网线指示灯与 PLC 供电");

            Add(doc, workspace, "视觉-单件缺陷超限", "DefectCount", ScadaAlarmKind.High, ScadaAlarmSeverity.Warning,
                enabled: true, group: "视觉", threshold: 10, deadband: 1, delay: 0, stale: 10,
                message: "单件缺陷数超过 10", cause: "来料不良或光源衰减", remedy: "抽检来料并清洁光源");

            Add(doc, workspace, "1#电机温度", "Pump1.Temp", ScadaAlarmKind.HighHigh, ScadaAlarmSeverity.Critical,
                enabled: true, group: "一号线", threshold: 85, deadband: 3, delay: 2, stale: 10,
                message: "1#电机绕组温度超过 85℃",
                cause: "冷却水泵停转、阀门被误关或环境温度过高",
                remedy: "检查冷却水回路与水泵电源，确认后复位热继电器");

            Add(doc, workspace, "冷却水流量", "Flow.Coolant", ScadaAlarmKind.Low, ScadaAlarmSeverity.Warning,
                enabled: true, group: "一号线", threshold: 12, deadband: 2, delay: 1, stale: 10,
                message: "冷却水流量低于 12", cause: "泵未启动或管路堵塞", remedy: "启动水泵并检查过滤器");

            Add(doc, workspace, "上料仓余量", "Hopper.Level", ScadaAlarmKind.Low, ScadaAlarmSeverity.Info,
                enabled: true, group: "上料段", threshold: 15, deadband: 5, delay: 0, stale: 10,
                message: "上料仓余量低于 15", cause: "补料不及时", remedy: "通知上料工位补料");

            Add(doc, workspace, "夹具气压", "Clamp.AirPressure", ScadaAlarmKind.BoolOff, ScadaAlarmSeverity.Warning,
                enabled: false, group: "二号工位", threshold: 0, deadband: 0, delay: 2, stale: 10,
                message: "夹具气压开关断开", cause: "气源阀门被关闭", remedy: "打开气源阀门并复检");
        }

        private static void Add(
            ScadaDocument doc, WorkspaceContext workspace, string name, string variable,
            ScadaAlarmKind kind, ScadaAlarmSeverity severity, bool enabled, string group,
            double threshold, double deadband, double delay, double stale,
            string message = null, string cause = null, string remedy = null, string extra = null)
        {
            var alarm = doc.AddAlarm(name, kind);
            alarm.Severity = severity;
            alarm.IsEnabled = enabled;
            alarm.AlarmGroup = group;
            alarm.Threshold = threshold;
            alarm.Deadband = deadband;
            alarm.DelaySeconds = delay;
            alarm.StaleSeconds = stale;
            alarm.Message = message;
            alarm.Cause = cause;
            alarm.Remedy = remedy;
            alarm.ExtraInfo = extra;

            workspace.GlobalVariables.Add(new LocalVariableModel
            {
                Name = variable,
                DataType = typeof(double),
                Value = 0.0,
            });

            alarm.Bind(StableId(variable), variable);
        }

        /// <summary>变量 Id 由名字现算，两次运行/两张图之间同一路变量拿到同一个 Id（图与图才可比）。</summary>
        private static Guid StableId(string name)
        {
            var bytes = System.Security.Cryptography.MD5
                .HashData(System.Text.Encoding.UTF8.GetBytes(name));
            return new Guid(bytes.AsSpan(0, 16));
        }

        /// <summary>
        /// 窗口已经 Show、视图已经 Loaded 之后再排一次：只有「新增行」这一态需要，
        /// 因为它的滚动/聚焦是视图在 Loaded 时挂上 <c>RowAdded</c> 才发生的。
        /// </summary>
        private static void ArrangeAfterShow(ScadaAlarmConfigDialogViewModel vm, string state)
        {
            if (state == "new")
                vm.AddCommand.Execute();
        }

        private sealed class StaticFlowRunState : IFlowRunState
        {
            private readonly bool _running;

            public StaticFlowRunState(bool running) => _running = running;

            public bool AnyFlowRunning => _running;
        }

        /// <summary>本探针不点「选择变量」，选择器只需存在并静默取消（回调一次都不触发）。</summary>
        private sealed class NoopPicker : IScadaVariablePicker
        {
            public void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked) { }
        }

        private static string OutputDirectory(string[] args)
        {
            var @out = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal));
            if (@out != null) return @out.Substring("--out=".Length);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                dir = dir.Parent;

            string root = dir?.FullName ?? AppContext.BaseDirectory;
            return Path.Combine(root, "artifacts", "render", DateTime.Now.ToString("yyyyMMdd"), "scada-alarm-config");
        }

        /// <summary>
        /// 与 VisionMaster/App.xaml 的 MergedDictionaries 同序同份数（含 Icon 字体键）。
        /// Fluent 必须排在 UI/Generic.xaml 之后，Input.xaml 必须在 Fluent 之后重挂一次——
        /// 少了后者，输入框拿到的是 Fluent 模板，截图就不是运行期那套长相。
        /// </summary>
        private static void MergeAppResources(Application app)
        {
            // 与 App.xaml 同一个键：视图里的图标字符都吃 {StaticResource Icon}
            app.Resources["Icon"] = new FontFamily(
                "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");

            string[] sources =
            {
                "/Core.Halcon;component/Generic.xaml",
                "pack://application:,,,/UI;component/Themes/Generic.xaml",
                "/VM.Scada.Controls;component/Themes/ScadaIcons.xaml",
                "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
                "/UI;component/Themes/Controls/Input.xaml",
            };

            foreach (var source in sources)
            {
                try
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.RelativeOrAbsolute) });
                }
                catch (Exception ex)
                {
                    // 少挂一册就出图 = 拿"另一套资源环境下的样子"当运行期证据，必须红着停。
                    throw new InvalidOperationException($"资源字典「{source}」合并失败，出图不可信：{ex.Message}", ex);
                }
            }

            if (app.Resources.MergedDictionaries.Count != sources.Length)
                throw new InvalidOperationException($"资源字典应为 {sources.Length} 份，实际 {app.Resources.MergedDictionaries.Count} 份");
        }

        private static RenderTargetBitmap Render(Visual visual, int width, int height)
        {
            var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>64×64 采样后取 MD5 前 16 位：回答"画出来的像素到底一不一样"。</summary>
        private static string PixelHash(BitmapSource source)
        {
            const int size = 64;
            var scaled = new TransformedBitmap(source,
                new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);

            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            using var md5 = System.Security.Cryptography.MD5.Create();
            return Convert.ToHexString(md5.ComputeHash(pixels))[..16];
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var stopper = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            stopper.Tick += (_, _) =>
            {
                stopper.Stop();
                frame.Continue = false;
            };
            stopper.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
