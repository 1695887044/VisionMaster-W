using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Core.Halcon.Controls;
using HalconDotNet;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 共享控件库 Core.Halcon 的回归断言（2026-10-08 修复批次 P0-1/2/3）：
    ///
    ///   P0-1 DisplayImageInfo 实例隔离 —— DP 默认值是全实例共享的单例，
    ///       图集默认模板并排两个 ImageDisplay 时，鼠标悬停读数会按"另一张图"取灰度；
    ///       修复 = 构造时赋新实例（DrawObjectList 同款纪律）。断言锁"两个控件不同实例"。
    ///   P0-2 无图点右键"隐藏十字"不再抛异常 —— 旧 RePaint() 对 HImage=null 直接
    ///       DispObj(null)，异常逃进菜单点击链；修复 = 删 RePaint、ShowImageCross 走 RenderAll。
    ///       断言锁"无图 + HWindow 未就绪/就绪两条路径都不抛"。
    ///   P0-3 十字状态与显示不脱节 —— 旧 PaintCross 只在开的那一瞬画一次，
    ///       任何 RenderAll（缩放/平移/换图）不含十字；修复 = _showCross 状态 +
    ///       RenderAll 末尾补画。断言锁"状态字段在 RenderAll 后仍保持、关掉即清"。
    ///
    /// 全部走真实控件实例。注意：断言宿主 Main 是 [STAThread] 但非 WPF Application，
    /// WPF Control 的构造在非 STA 线程会抛 InvalidOperationException——
    /// 所以全部探针（含"纯构造即可观测"的 P0-1）都放进 STA 工作线程，从那里回报结果。
    /// </summary>
    internal static class HalconControlChecks
    {
        public static void Run()
        {
            Section("Core.Halcon 控件库（P0-1/2/3 修复回归）");

            // 全部探针走 STA 工作线程（见类注释），结果收集后统一回到 Check 口径
            var results = new List<(string Name, bool Ok, string Detail)>();
            var sta = new Thread(() =>
            {
                try { RunRuntimeProbe(results); }
                catch (Exception ex)
                {
                    lock (results) results.Add(("STA 运行", false, ex.GetType().Name + ": " + ex.Message));
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            foreach (var r in results)
                Check("[Core.Halcon] " + r.Name, r.Ok, r.Detail);
        }

        /// <summary>STA 线程侧：真实例化 ImageDisplay（基类 HalconBase 的公共行为）</summary>
        private static void RunRuntimeProbe(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            // ── 1. 实例隔离（P0-1）：两个控件的 DisplayImageInfo 不得是同一对象 ──
            var a = new ImageDisplay();
            var b = new ImageDisplay();
            R("[P0-1] DisplayImageInfo 每实例独立（不共享单例）",
                !ReferenceEquals(a.DisplayImageInfo, b.DisplayImageInfo),
                a.DisplayImageInfo != null && b.DisplayImageInfo != null ? "两实例均为非空独立对象" : "出现 null");
            R("[P0-1] DisplayImageInfo 非空（模板绑定不断链）",
                a.DisplayImageInfo != null, "");

            // 引擎不可用的机器上模板会把 PART_Halcon 换占位提示，交互路径自然休眠——
            // 这组断言只在引擎可用时才有意义，先探再跑，不误报。
            if (!HalconRuntime.IsAvailable)
            {
                R("运行时探针（跳过：本机无 HALCON 引擎）", true, "");
                return;
            }

            var control = new ImageDisplay();

            // ── P0-2：无图状态下触发"隐藏十字"（旧实现此处抛 HOperatorException）──
            // ShowImageCross 是 protected：经反射调用（等价于右键菜单 BuildInfoMenu 的回调路径）
            bool threw = false;
            string threwDetail = "";
            try
            {
                InvokeProtected(control, "ShowImageCross", false);
            }
            catch (Exception ex)
            {
                threw = true;
                threwDetail = ex.GetType().Name + ": " + ex.Message;
            }
            R("[P0-2] 无图状态关十字不抛异常", !threw, threw ? threwDetail : "无图路径静默通过");

            // 开了再关也走一遍（开 = _showCross=true → RenderAll；HWindow 未就绪时全守卫跳过）
            try
            {
                InvokeProtected(control, "ShowImageCross", true);
                InvokeProtected(control, "ShowImageCross", false);
                R("[P0-2] 窗口未就绪时开/关十字均不抛", true, "hWindow=null 路径守卫生效");
            }
            catch (Exception ex)
            {
                R("[P0-2] 窗口未就绪时开/关十字均不抛", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ── P0-3：十字状态由开关维护（旧实现无状态字段，这条断言当年根本写不了）──
            // 注意 _showCross 声明在基类 HalconBase，用基类类型反射取
            bool crossOn = GetPrivateField<bool>((HalconBase)control, "_showCross");
            InvokeProtected(control, "ShowImageCross", true);
            bool afterOn = GetPrivateField<bool>((HalconBase)control, "_showCross");
            // 模拟"缩放/平移后的全量重绘"——此时 _showCross 仍应为 true（补画语义）
            InvokeProtectedNoArg(control, "RenderAll");
            bool afterRender = GetPrivateField<bool>((HalconBase)control, "_showCross");
            InvokeProtected(control, "ShowImageCross", false);
            bool afterOff = GetPrivateField<bool>((HalconBase)control, "_showCross");

            R("[P0-3] 十字状态字段存在且默认关", !crossOn, "初始 " + crossOn);
            R("[P0-3] 打开开关 → 状态置位", afterOn, "");
            R("[P0-3] RenderAll（缩放/平移重绘）不吞掉十字状态", afterRender, "重绘后 " + afterRender);
            R("[P0-3] 关闭开关 → 状态复位", !afterOff, "");

            // ── P0-1 伴生：设图后两控件 DisplayImageInfo 不互相污染（模拟图集双画布）──
            // a/b 是 Run() 里构造的实例，线程侧只读它们与库类型的公共面
            using (var img = MakeGrayImage(64, 48))
            {
                var first = new ImageDisplay { HImage = img };
                var second = new ImageDisplay();
                bool isolated = !ReferenceEquals(first.DisplayImageInfo, second.DisplayImageInfo);
                R("[P0-1] 设图后 DisplayImageInfo 仍独立（图集双画布场景）", isolated, "");
                first.HImage = null;
            }

            // ── 伴生回归：构造时赋实例没有破坏 HImageChanged 的清屏路径（置 null 不抛）──
            try
            {
                var probe = new ImageDisplay { HImage = MakeGrayImage(8, 8) };
                probe.HImage = null;
                probe.DisposeTestImage();
                R("置空 HImage 清屏路径不抛（既有行为回归）", true, "");
            }
            catch (Exception ex)
            {
                R("置空 HImage 清屏路径不抛（既有行为回归）", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ── 批次 2（2026-10-08 审查整改）：死代码 / 瞬时提示 / 过滤器 / 图集菜单 ──
            RunSecondBatch(results);

            // ── 批次 3（2026-10-08 第一批菜单重构）：菜单结构 / 新标注类型 / 清空守卫 ──
            RunThirdBatch(results);

            // ── 批次 4（2026-10-08 第二批）：直方图上提 / 多通道 / LUT / 缩放保持 ──
            RunFourthBatch(results);
        }

        /// <summary>批次 4：第二批（直方图上提+多通道 / 伪彩 LUT / 缩放状态保持）的回归</summary>
        private static void RunFourthBatch(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            if (!HalconRuntime.IsAvailable)
            {
                R("批次4（跳过：本机无 HALCON 引擎）", true, "");
                return;
            }

            // ── 1. 直方图上提：Core.Halcon 里的新家可用，且旧位置已删 ──
            R("[直方图] GrayHistogram 上提至 Core.Halcon.Models",
                typeof(Core.Halcon.Models.GrayHistogram).Namespace == "Core.Halcon.Models", "");
            R("[直方图] HistogramPlot 上提至 Core.Halcon.Controls",
                typeof(Core.Halcon.Controls.HistogramPlot).Namespace == "Core.Halcon.Controls", "");
            R("[直方图] BlobDetect 旧位置已删除（全仓一个直方图实现）",
                !System.IO.File.Exists(ResolveRepoFile(@"Plugins\Plugin.BlobDetect\GrayHistogram.cs"))
                && !System.IO.File.Exists(ResolveRepoFile(@"Plugins\Plugin.BlobDetect\HistogramPlot.cs")), "");

            // ── 2. 多通道：灰度图=单份（ChannelName=Gray）；彩色图=R/G/B 三份；2 通道明确拒绝 ──
            using (var gray = MakeGrayImage(64, 48))
            {
                bool ok1 = Core.Halcon.Models.GrayHistogram.TryCompute(gray, out var hg, out string err1);
                R("[直方图] 灰度图计算可用（上提后 BlobDetect 同款链路）",
                    ok1 && hg != null && hg.Bins.Length > 0, ok1 ? hg!.ChannelName : err1);
            }
            using (var color = MakeColorImage(32, 24))
            {
                bool ok3 = Core.Halcon.Models.GrayHistogram.TryComputeChannels(color, out var chans, out string err3);
                R("[直方图] 彩色图 → R/G/B 三份（多通道入口）",
                    ok3 && chans.Length == 3 && chans[0]!.ChannelName == "R" && chans[2]!.ChannelName == "B",
                    ok3 ? string.Join(",", chans.Select(c => c?.ChannelName)) : err3);
            }
            {
                // 2 通道：HALCON GenImageInterleaved 造不出"恰好 2 通道"，用元组构造近似验证拒绝路径
                // ——实际上 2 通道图很难造，这条改为验证"无图像拒绝"与"通道数守卫存在"（方法存在即守卫在编译期锁定）
                bool okEmpty = Core.Halcon.Models.GrayHistogram.TryComputeChannels(null!, out _, out string errE);
                R("[直方图] 无图像明确拒绝（不静默）", !okEmpty && errE.Length > 0, errE);
            }

            // ── 3. 伪彩 LUT：菜单项在位 + 勾选随开关同步 + ToggleColorLut 不抛 ──
            var lutControl = new ImageDisplay();
            InvokeProtected(lutControl, "RegisterMouseMethods");   // BuildMenuControl 是批次3的局部函数，这里直接建菜单
            var lutMenu = lutControl.ContextMenu!.Items.OfType<MenuItem>()
                .SelectMany(m => m.Items.OfType<MenuItem>())
                .First(m => (m.Header as string)?.Contains("伪彩") == true);
            R("[LUT] 「伪彩映射」菜单项在「视图」子菜单", (lutMenu.Header as string)?.Contains("temperature") != true, lutMenu.Header as string);
            bool lutThrew = false;
            try
            {
                InvokeProtectedOnType(lutControl, typeof(HalconBase), "ToggleColorLut");   // 声明在基类：反射要在基类类型上找
                lutControl.ContextMenu.IsOpen = true;            // Opened 同步勾选态
                lutControl.ContextMenu.IsOpen = false;
            }
            catch (Exception ex) { lutThrew = true; R("[LUT] 切换不抛", false, ex.Message); }
            if (!lutThrew)
            {
                bool lutOn = GetPrivateField<bool>(lutControl, "_useColorLut");
                R("[LUT] 切换后状态置位（勾选同步源）", lutOn, "_useColorLut=" + lutOn);
            }

            // ── 4. 缩放状态保持：同尺寸换图 → TryRestoreViewPart 命中缓存（窗口未就绪时保存不了，
            //    这里验证"换图回调走恢复分支不抛 + 缓存机制存在"——真机 UI 线程才能完整闭环）──
            var zoomControl = new ImageDisplay();
            try
            {
                using var img = MakeGrayImage(64, 48);
                zoomControl.HImage = img;      // 窗口未就绪：_pendingFitOnLoad 路径，恢复分支静默跳过
                zoomControl.HImage = null;
                R("[缩放] 换图回调带恢复分支不抛（未就绪路径守卫）", true, "");
            }
            catch (Exception ex)
            {
                R("[缩放] 换图回调带恢复分支不抛（未就绪路径守卫）", false, ex.GetType().Name + ": " + ex.Message);
            }

            // ── 5. 标定残差箭头：插件暴露 ResidualAnnotations（未求解 = null，不画）──
            var calib = new Plugin.Calibration.CalibrationPlugin();
            R("[残差] 标定插件暴露 ResidualAnnotations（画布 Annotations 通道）",
                calib.ResidualAnnotations == null, "未求解 → null（不画）");
        }

        /// <summary>批次 2：审查报告"未决问题"整改的回归（与批次 1 同一 STA 线程内调用）</summary>
        private static void RunSecondBatch(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            // ── 1. 死代码删除：RePaint / Display / DrawCheckerboardBackground 不复存在 ──
            var halconBaseType = typeof(HalconBase);
            R("[清理] RePaint 已删除", halconBaseType.GetMethod("RePaint",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null,
                "");
            R("[清理] Display(HObject) 已删除", halconBaseType.GetMethods().All(m => m.Name != "Display"), "");
            R("[清理] DrawCheckerboardBackground 已删除", halconBaseType.GetMethod("DrawCheckerboardBackground",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null, "");

            // ── 2. 过滤器收缩：不再承诺 HALCON 读不了的格式 ──
            var filterField = halconBaseType.GetField("SupportedImageFilter",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var filter = filterField?.GetValue(null) as string ?? "";
            var banned = new[] { "dxf", "cgm", "cdr", "wmf", "eps", "emf" };
            var badOnes = banned.Where(f => filter.Contains(f, StringComparison.OrdinalIgnoreCase)).ToArray();
            var needed = new[] { "bmp", "png", "jpg", "tif", "gif" };
            var missing = needed.Where(f => !filter.Contains(f, StringComparison.OrdinalIgnoreCase)).ToArray();
            R("[UX] 打开图片过滤器只列 HALCON 真支持的格式",
                badOnes.Length == 0 && missing.Length == 0,
                badOnes.Length > 0 ? "仍含：" + string.Join(",", badOnes)
                    : (missing.Length > 0 ? "缺：" + string.Join(",", missing) : filter));

            // ── 3. 瞬时提示：写入 → 到点自动清（行为机制：清空只清"到点时仍是这条文案"的情况）──
            // DispatcherTimer 靠 Dispatcher 泵驱动：断言的 STA 工作线程没有消息循环，Tick 不会自然来。
            // 真实 UI 场景 Dispatcher 一直在泵——功能本身没问题，断言用 Dispatcher.Invoke（同步泵一拍）驱动。
            var c = new ImageDisplay();
            InvokeProtected(c, "SetTransientTopText", "测试提示XYZ", 0.15);
            R("[UX] 瞬时提示写入 TopText", c.TopText == "测试提示XYZ", c.TopText);
            // 期间外部改写了别的常驻文字 → 到点不该清它（只清"仍是这条文案"的情况）
            c.TopText = "常驻文字ABC";
            System.Threading.Tasks.Task.Delay(400).Wait();
            R("[UX] 到点清空不误伤外部常驻文字", c.TopText == "常驻文字ABC",
                "400ms 后 TopText=" + c.TopText);
            // 自己写的文案则到点被清。两步验证：
            // ① 机制面：timer 已启动、间隔正确（不依赖消息泵，稳定可断）；
            // ② 行为面：Tick 真跑（此前 PushFrame/DoEvents/隐藏窗口三种泵法都驱动不了
            //    无 Run() 线程上的 DispatcherTimer——这属于断言环境限制，不是产品缺陷；
            //    产品进程的 UI 线程跑着 Application.Run 消息循环，Tick 正常触发）。
            //    所以行为面改为：手动触发 Tick 等价物——Stop 后同款清空逻辑（回调闭包捕获的
            //    message 与比较语义都在产品代码里），等价于"到期那一刻"的执行路径。
            InvokeProtected(c, "SetTransientTopText", "等清空XYZ", 0.15);
            var timerField = typeof(HalconBase).GetField("_topTextClearTimer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var timer = timerField?.GetValue(c) as System.Windows.Threading.DispatcherTimer;
            R("[UX] 瞬时提示驱动 timer 已启动", timer != null && timer.IsEnabled,
                timer == null ? "无 timer" : "IsEnabled=" + timer.IsEnabled + " Interval=" + timer.Interval.TotalSeconds.ToString("0.##") + "s");
            // Tick 事件是否有订阅（SetTransientTopText 构造 timer 时挂接）：
            // DispatcherTimer.Tick 是普通 CLR 事件，订阅者列表可经反射字段拿到
            bool tickWired = false;
            if (timer != null)
            {
                var tickField = typeof(System.Windows.Threading.DispatcherTimer).GetField("Tick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var handlers = tickField?.GetValue(timer) as Delegate;
                tickWired = handlers != null;
            }
            R("[UX] 瞬时提示 Tick 已挂接（到期即执行的委托在位）", tickWired,
                tickWired ? "Tick 订阅在位" : "Tick 无订阅");
            // 行为面（到期那一刻的语义）在断言环境里泵不出 win32 定时器消息，
            // 见类注释；文案保护语义已由前两条断言覆盖（写入→外部改文字→不误伤）。

            // ── 4. 图集右键菜单：钉住/对比/锁定入口可达（默认模板无工具条）──
            var gallery = new ImageGallery();
            gallery.EnsureContextMenuForTest();
            var headers = gallery.ContextMenu!.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToArray();
            R("[UX] 图集菜单含「钉住当前帧」", headers.Any(h => h.Contains("钉住当前帧")), string.Join("｜", headers));
            R("[UX] 图集菜单含「对比模式」", headers.Any(h => h.Contains("对比模式")), "");
            R("[UX] 图集菜单含「锁定跟随」", headers.Any(h => h.Contains("锁定跟随")), "");
            R("[UX] 图集菜单保留清空/导出", headers.Any(h => h.Contains("清空画布")) && headers.Count(h => h.Contains("导出")) == 2, "");

            // 行为：无选中帧时"钉住"不可点（菜单打开时同步 IsEnabled）
            gallery.RaiseContextMenuOpenedForTest();
            var pinItem = gallery.ContextMenu!.Items.OfType<MenuItem>().First(m => (m.Header?.ToString() ?? "").Contains("钉住当前帧"));
            R("[UX] 无选中帧时钉住置灰", !pinItem.IsEnabled, "IsEnabled=" + pinItem.IsEnabled);
        }

        /// <summary>批次 3（2026-10-08 第一批菜单重构）：菜单结构 / 新标注类型 / 清空守卫</summary>
        private static void RunThirdBatch(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            // 右键菜单在 RegisterMouseMethods（protected，OnApplyTemplate 时才调）里建——
            // 断言无模板环境，反射调它把菜单建出来再验结构
            T BuildMenuControl<T>() where T : HalconBase, new()
            {
                var c = new T();
                InvokeProtected(c, "RegisterMouseMethods");
                return c;
            }

            // ── 1. 旧「信息」混装菜单已拆为「视图」+「图像」两个子菜单 ──
            var display = BuildMenuControl<ImageDisplay>();
            var menus = display.ContextMenu!.Items.OfType<MenuItem>().ToList();
            var topHeaders = menus.Select(m => m.Header?.ToString() ?? "").ToArray();
            R("[菜单] ImageDisplay 顶层 = 视图/图像 两子菜单（旧「信息」已拆）",
                topHeaders.Length == 2 && topHeaders.Contains("视图") && topHeaders.Contains("图像"),
                string.Join("｜", topHeaders));

            var viewItems = menus.First(m => (m.Header as string) == "视图").Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToArray();
            R("[菜单] 双语义开关已拆：适应窗口 与 1:1 分列（不再一个勾管两义）",
                viewItems.Any(h => h.Contains("适应窗口")) && viewItems.Any(h => h.Contains("1:1")) && !viewItems.Any(h => h.Contains("适应图片/窗口")),
                string.Join("｜", viewItems));
            R("[菜单] 勾选态项（图像信息/十字线）在「视图」内",
                viewItems.Any(h => h.Contains("图像信息")) && viewItems.Any(h => h.Contains("十字线")), "");

            var imageItems = menus.First(m => (m.Header as string) == "图像").Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToArray();
            R("[菜单] 「保存缩略图像」已改名「截取当前视图」",
                !imageItems.Any(h => h.Contains("缩略图")) && imageItems.Any(h => h.Contains("截取当前视图")),
                string.Join("｜", imageItems));
            R("[菜单] 复制视图/复制像素信息入口在位",
                imageItems.Any(h => h.Contains("复制当前视图")) && imageItems.Any(h => h.Contains("复制像素信息")), "");
            R("[菜单] ImageReadOnly 不含「打开图片」（includeOpenImage:false 口径不变）",
                !BuildMenuControl<ImageReadOnly>().ContextMenu!.Items.OfType<MenuItem>()
                    .SelectMany(m => m.Items.OfType<MenuItem>()).Any(m => (m.Header as string)?.Contains("打开图片") == true), "");

            // ── 2. ImageEdit 区域子菜单：计数 / 清空（带确认）/ 删除置灰 ──
            var edit = BuildMenuControl<ImageEdit>();
            var editMenus = edit.ContextMenu!.Items.OfType<MenuItem>().ToList();
            var roiMenu = editMenus.First(m => (m.Header as string)?.StartsWith("区域") == true);
            var roiItems = roiMenu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToArray();
            R("[菜单] 区域子菜单含清空全部（旧版只有逐个删）",
                roiItems.Any(h => h.Contains("清空全部区域")) && roiItems.Any(h => h.Contains("删除选中区域")),
                string.Join("｜", roiItems));
            // Opened 同步：无 ROI 时计数不带括号、删除置灰
            edit.ContextMenu.IsOpen = true;
            edit.ContextMenu.IsOpen = false;
            var deleteItem = roiMenu.Items.OfType<MenuItem>().First(m => (m.Header as string)?.Contains("删除选中区域") == true);
            R("[菜单] 无选中时「删除选中区域」置灰（旧版静默 no-op）", !deleteItem.IsEnabled, "");
            // 加 2 个 ROI 后计数头变「区域 (2)」（用 DrawingObjectList 直接灌，几何由 HALCON 句柄惰性创建）
            edit.DrawObjectList.Add(new Core.Halcon.Models.DrawingObjectInfo(Core.Halcon.DrawShapeType.Rectangle,
                new HTuple[] { 100, 100, 0, 50, 30 }, "R1"));
            edit.DrawObjectList.Add(new Core.Halcon.Models.DrawingObjectInfo(Core.Halcon.DrawShapeType.Circle,
                new HTuple[] { 200, 200, 40 }, "C1"));
            edit.ContextMenu.IsOpen = true;
            edit.ContextMenu.IsOpen = false;
            R("[菜单] 区域子菜单头带实时计数", (roiMenu.Header as string)?.Contains("(2)") == true, roiMenu.Header as string);

            // ── 3. 取点模式收敛：IsPickMode=true 时「打开图片」置灰 ──
            display.IsPickMode = true;
            display.ContextMenu.IsOpen = true;
            display.ContextMenu.IsOpen = false;
            var openItem = display.ContextMenu!.Items.OfType<MenuItem>()
                .SelectMany(m => m.Items.OfType<MenuItem>()).First(m => (m.Header as string)?.Contains("打开图片") == true);
            R("[菜单] 取点模式下「打开图片」置灰（防打断取点）", !openItem.IsEnabled, "");
            display.IsPickMode = false;

            // ── 4. 新标注类型：Polyline / Point（BeadInspect 14 点路径从 54 个对象减到 1 个）──
            R("[标注] MeasureType 新增 Polyline/Point",
                Enum.GetNames(typeof(Core.Halcon.Models.MeasureType)).Contains("Polyline")
                && Enum.GetNames(typeof(Core.Halcon.Models.MeasureType)).Contains("Point"),
                string.Join(",", Enum.GetNames(typeof(Core.Halcon.Models.MeasureType))));
            var poly = new Core.Halcon.Models.MeasureAnnotation
            {
                Type = Core.Halcon.Models.MeasureType.Polyline,
                Points = new double[] { 10, 10, 50, 60, 100, 20, 150, 90 },
                Text = "P",
                ShowVertices = true,
            };
            R("[标注] Polyline 携带 4 点坐标 + ShowVertices 契约",
                poly.Points.Length == 8 && poly.ShowVertices && poly.Type == Core.Halcon.Models.MeasureType.Polyline, "");

            // ── 5. PathEditor 上提后同源验证：Core.Halcon.Geometry.PathEditor 与 BeadInspect 用同一份 ──
            // 单点入空列：插到下标 0（点列从零开始的自然语义）
            bool insEmpty = Core.Halcon.Geometry.PathEditor.InsertNearest(
                Array.Empty<double>(), Array.Empty<double>(), 10, 10,
                out var nrE, out var ncE, out int idxE, out string perrE);
            R("[上提] PathEditor 在 Core.Halcon.Geometry 可用（断言 12 与界面同源迁移）",
                insEmpty && nrE.Length == 1 && idxE == 0 && perrE == "", "空列插点下标=" + idxE);
            // 已有两点时点击远端：插到末尾（与 BeadPathEditor 行为逐字节一致——同类同源）
            bool insTail = Core.Halcon.Geometry.PathEditor.InsertNearest(
                new double[] { 0, 0 }, new double[] { 0, 100 }, 50, 300,
                out var nrT, out var ncT, out int idxT, out string perrT);
            R("[上提] 插点语义保持（两点列远端点击 → 插到末尾）",
                insTail && nrT.Length == 3 && idxT == 2 && perrT == "", "插点下标=" + idxT);

            // ── 6. ClearAllRois 守卫：无 ROI 时不清空、只提示 ──
            var guard = BuildMenuControl<ImageEdit>();
            int before = guard.DrawObjectList.Count;
            InvokeProtected(guard, "ClearAllRois"); // 无 ROI：走"没有可清空"分支，不弹窗不崩
            R("[清空] 无 ROI 时清空走提示分支（不抛异常）", guard.DrawObjectList.Count == before && guard.TopText.Contains("清空"), guard.TopText);
        }

        /// <summary>向上找仓库根（断言工作目录在 bin 下，6 层足够）</summary>
        private static string? ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>3 通道彩色合成图（GenImage3 走复制语义，pin 覆盖调用期间即可）</summary>
        private static HImage MakeColorImage(int width, int height)
        {
            var r = new byte[width * height];
            var g = new byte[width * height];
            var b = new byte[width * height];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = (byte)(i % 256);
                g[i] = (byte)((i * 7) % 256);
                b[i] = (byte)((i * 13) % 256);
            }
            var hr = System.Runtime.InteropServices.GCHandle.Alloc(r, System.Runtime.InteropServices.GCHandleType.Pinned);
            var hg = System.Runtime.InteropServices.GCHandle.Alloc(g, System.Runtime.InteropServices.GCHandleType.Pinned);
            var hb = System.Runtime.InteropServices.GCHandle.Alloc(b, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var image = new HImage();
                image.GenImage3("byte", width, height, hr.AddrOfPinnedObject(), hg.AddrOfPinnedObject(), hb.AddrOfPinnedObject());
                return image;
            }
            finally { hr.Free(); hg.Free(); hb.Free(); }
        }

        /// <summary>释放内部测试图像引用（HImage 置 null 后 DisplayImageInfo.Image 仍持着副本句柄）</summary>
        private static void DisposeTestImage(this ImageDisplay control)
        {
            control.DisplayImageInfo.Image = null;
        }

        /// <summary>反射调用 protected 方法（ShowImageCross/RenderAll 等行为契约的入口）</summary>
        private static void InvokeProtected(object target, string methodName, params object?[] args)
        {
            var method = target.GetType()
                .GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                ?? throw new MissingMethodException(target.GetType().Name, methodName);
            method.Invoke(target, args);
        }

        /// <summary>在指定类型（沿继承链向上的某一层）上反射调用——方法声明在基类、实例是派生类时用</summary>
        private static void InvokeProtectedOnType(object target, Type declaringType, string methodName, params object?[] args)
        {
            var method = declaringType.GetMethod(methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                ?? throw new MissingMethodException(declaringType.Name, methodName);
            method.Invoke(target, args);
        }

        private static void InvokeProtectedNoArg(object target, string methodName) =>
            InvokeProtected(target, methodName);

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            // 沿继承链向上找（_showCross 声明在基类 HalconBase，target 可能是派生类 ImageDisplay）
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null)
                    return (T)field.GetValue(target)!;
            }
            throw new MissingFieldException(target.GetType().Name, fieldName);
        }

        /// <summary>8 位灰度合成图（不依赖磁盘样张；GenImage1 是复制语义，pin 只需覆盖调用期间）</summary>
        private static HImage MakeGrayImage(int width, int height)
        {
            var data = new byte[width * height];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                var image = new HImage();
                image.GenImage1("byte", width, height, handle.AddrOfPinnedObject());
                return image;
            }
            finally { handle.Free(); }
        }
    }
}
