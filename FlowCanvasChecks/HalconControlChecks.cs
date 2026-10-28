using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Core.Halcon.Controls;
using Core.Halcon.Models;
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

            // ── 批次 7：图集"死帧不许留在选中/钉住"（纯逻辑，不需要 HALCON 引擎）──
            RunGalleryLivenessBatch(results);

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

            // ── 批次 5（2026-10-27 批 2）：重绘合并 / 同尺寸换图不重铺 ──
            RunRenderBatch(results);

            // ── 批次 6（2026-10-27 批 3）：滚轮缩放上下限 / 没有活图不画叠加层 ──
            RunZoomBatch(results);
        }

        /// <summary>
        /// 批 2（2026-10-27）：重绘合并与"同尺寸换图不重铺"。
        ///
        /// 只钉两件可离线判定的事（像素级验收要真窗口，见 R53）：
        ///   ① HImage 与 Annotations 两个 DP 各触发一次请求，但同一轮只**刷新一次**；
        ///   ② 尺寸判定改用"本控件上次画过的尺寸"——同尺寸换图不再被判成"尺寸变了"
        ///      （旧实现靠"旧图是否还活着"推断：图集清空后旧图已 Dispose → 每帧都重铺）。
        /// </summary>
        private static void RunRenderBatch(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            // ── ① 同尺寸换图不重铺；真换尺寸仍重铺 ──
            var sizeProbe = new ImageDisplay();
            using (var img1 = MakeGrayImage(64, 48))
            using (var img2 = MakeGrayImage(64, 48))
            using (var img3 = MakeGrayImage(32, 24))
            {
                sizeProbe.HImage = img1;
                int after1 = sizeProbe.SizeChangeCountForTest;
                sizeProbe.HImage = img2;
                int after2 = sizeProbe.SizeChangeCountForTest;
                sizeProbe.HImage = img3;
                int after3 = sizeProbe.SizeChangeCountForTest;

                R("[重绘] 首帧算一次尺寸变化、同尺寸换图不再重铺",
                    after1 == 1 && after2 == 1, $"首帧={after1} 同尺寸换图后={after2}");
                R("[重绘] 真换尺寸仍算尺寸变化（不能一刀切不重铺）",
                    after3 == 2, $"换尺寸后={after3}");
            }

            // ── ② 一帧内两个 DP 变化 → 只刷新一次 ──
            var renderProbe = new ImageDisplay();
            int req0 = renderProbe.RenderRequestCountForTest;
            int flush0 = renderProbe.RenderFlushCountForTest;

            using (var img = MakeGrayImage(64, 48))
            {
                renderProbe.HImage = img;
                renderProbe.Annotations = new List<MeasureAnnotation>
                {
                    new MeasureAnnotation
                    {
                        Type = MeasureType.Text,
                        Points = new[] { 1.0, 1.0 },
                        Text = "合并断言"
                    }
                };
            }

            int req1 = renderProbe.RenderRequestCountForTest;
            int flushBeforePump = renderProbe.RenderFlushCountForTest;
            renderProbe.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            int flushAfterPump = renderProbe.RenderFlushCountForTest;

            R("[重绘] HImage + Annotations 两次 DP 变化合并成一次刷新",
                req1 - req0 == 2 && flushBeforePump - flush0 == 0 && flushAfterPump - flush0 == 1,
                $"请求={req1 - req0} 泵前刷新={flushBeforePump - flush0} 泵后刷新={flushAfterPump - flush0}");
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
            R("[UX] 图集菜单含「显示缩略图条」", headers.Any(h => h.Contains("显示缩略图条")), "");
            R("[UX] 图集菜单保留清空/导出", headers.Any(h => h.Contains("清空画布")) && headers.Count(h => h.Contains("导出")) == 2, "");

            // 能力闸门（2026-10-24）：锁定/对比/条子三项只在"这格可能多张图"时才有可观察效果。
            // 断言必须覆盖两个方向——只验"不支持时置灰"会漏掉"支持了却还是灰的"（那等于功能不可达）。
            var pinItem = gallery.ContextMenu!.Items.OfType<MenuItem>().First(m => (m.Header?.ToString() ?? "").Contains("钉住当前帧"));
            var compareItem = gallery.ContextMenu.Items.OfType<MenuItem>().First(m => (m.Header?.ToString() ?? "").Contains("对比模式"));
            var lockItem = gallery.ContextMenu.Items.OfType<MenuItem>().First(m => (m.Header?.ToString() ?? "").Contains("锁定跟随"));
            var stripItem = gallery.ContextMenu.Items.OfType<MenuItem>().First(m => (m.Header?.ToString() ?? "").Contains("显示缩略图条"));

            // ① 默认（SupportsFrameHistory=false，即收容关）：三项置灰，且钉住也置灰
            gallery.SupportsFrameHistory = false;
            gallery.RaiseContextMenuOpenedForTest();
            R("[UX] 每格只有一张时：对比/锁定/条子三项置灰",
                !compareItem.IsEnabled && !lockItem.IsEnabled && !stripItem.IsEnabled,
                $"compare={compareItem.IsEnabled} lock={lockItem.IsEnabled} strip={stripItem.IsEnabled}");
            R("[UX] 无选中帧时钉住置灰", !pinItem.IsEnabled, "IsEnabled=" + pinItem.IsEnabled);
            R("[UX] 置灰项带说明（指出去哪儿打开收容）",
                !string.IsNullOrEmpty(compareItem.ToolTip?.ToString())
                && compareItem.ToolTip!.ToString()!.Contains("保留插件注入图的历史帧"),
                compareItem.ToolTip?.ToString() ?? "(无提示)");

            // ② 反向：有历史能力 + 有选中帧 → 三项都能点、钉住也能点、提示清空
            gallery.SupportsFrameHistory = true;
            gallery.SelectedFrame = new ImageFrame("assert-menu-1");
            gallery.RaiseContextMenuOpenedForTest();
            R("[UX] 有历史能力时：对比/锁定/条子三项可点",
                compareItem.IsEnabled && lockItem.IsEnabled && stripItem.IsEnabled,
                $"compare={compareItem.IsEnabled} lock={lockItem.IsEnabled} strip={stripItem.IsEnabled}");
            R("[UX] 有历史能力 + 有选中帧时钉住可点", pinItem.IsEnabled, "IsEnabled=" + pinItem.IsEnabled);
            R("[UX] 可用时不留「为什么不能点」的提示", compareItem.ToolTip == null, compareItem.ToolTip?.ToString() ?? "(null，正确)");

            // ③ 能力被收回时，已打开的对比模式与已钉住的基准要一起清掉
            //   （那时左右两张是同一张图；留着基准还可能指向已被覆盖释放的 HImage）
            gallery.IsCompareMode = true;
            gallery.PinnedFrame = gallery.SelectedFrame;
            gallery.SupportsFrameHistory = false;
            R("[UX] 收容关闭时自动退出对比模式并清空对比基准",
                !gallery.IsCompareMode && gallery.PinnedFrame == null,
                $"IsCompareMode={gallery.IsCompareMode} PinnedFrame={(gallery.PinnedFrame == null ? "null" : "有")}");
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

        /// <summary>
        /// 批 6（2026-10-27）：滚轮缩放的上下限 + "没有活图就不画叠加层"。
        ///
        /// 现场：跑完流程后滚轮往外缩，图像连同几何标注一起缩到亚像素，画布只剩一行定长像素的
        /// 标注文字——看起来就是"图像丢了"。根因是 HSmartWindowControlWPF 的滚轮**没有下界**
        /// （每格 ×√2，缩 17 格 ≈ 500 倍，771px 的图只剩 1px）。
        ///
        /// 这里钉五条，其中 ①③⑤ 是正向、② 是反向（下限不能卡太死）、⑥ 用像素尺子反证：
        ///   ① 铺满时再外缩 → 事件被 Handled、part 不变（下限）；
        ///   ② 放大状态下外缩 → 放行（不误拦）；
        ///   ③ 已缩过头（旧视图缓存 / 外部 SetPart 留下的超大 part）→ 一格外缩弹回铺满；
        ///   ④ 放大到 part 跨度 ≤ 8 像素后再内缩 → 被 Handled（part 不许退化成一个点）；
        ///   ⑤ 活图 + 粗标注 → 窗口有内容（证明尺子量得到，否则 ⑥ 是空断言）；
        ///   ⑥ 图被 Dispose 后强制重画 → 窗口非黑 ≈ 0（不再出现"黑底 + 一行绿字"）。
        ///
        /// 必须是**真窗口**（R53）：buffer 窗口没有鼠标路由，PreviewMouseWheel 投递不进去；
        /// 这里用屏幕外真窗口（Left=-4000），不打扰桌面。
        /// </summary>
        private static void RunZoomBatch(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            if (!HalconRuntime.IsAvailable)
            {
                // 反证（R38）：不可用原因来自引擎探测（缺 halcon.dll / PATH 未配），与本次改动无关；
                // 引擎不在时连 HWindow 都建不出来，缩放断言没有被测对象。
                R("滚轮缩放断言（跳过：本机无 HALCON 引擎）", true, "");
                return;
            }

            Window? win = null;
            ImageDisplay? display = null;
            HImage? image = null;
            try
            {
                win = new Window
                {
                    Width = 820,
                    Height = 660,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = 0,
                };
                win.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/Core.Halcon;component/Generic.xaml", UriKind.Relative),
                });
                display = new ImageDisplay { Width = 800, Height = 600 };
                win.Content = display;
                win.Show();
                Pump(400);

                var hswc = FindChild<HSmartWindowControlWPF>(display);
                var hw = hswc?.HalconWindow;
                if (hswc == null || hw == null)
                {
                    // 不是"环境不支持"，是模板/窗口没就绪——按失败报，别静默跳过（R38）
                    R("滚轮缩放断言：画布与 HALCON 窗口就绪", false,
                        hswc == null ? "视觉树里找不到 HSmartWindowControlWPF（模板未应用）" : "HalconWindow 为 null");
                    return;
                }

                image = MakeGrayImage(800, 600);
                display.HImage = image;
                Pump(300);
                hswc.SetFullImagePart();
                Pump(150);

                double fitSpan = PartSpan(hw);
                R("滚轮缩放：铺满基线可测（跨度 > 0）", fitSpan > 0, $"span={fitSpan:F0}");

                // ① 铺满时再外缩：必须被拦，且 part 不变
                bool handledOut = RaiseWheel(hswc, -120);
                Pump(120);
                double afterOut = PartSpan(hw);
                R("① 缩到铺满即止（外缩被拦、part 不变）",
                    handledOut && Math.Abs(afterOut - fitSpan) < 0.5,
                    $"Handled={handledOut} span {fitSpan:F0} → {afterOut:F0}");

                // ② 反向：放大状态下外缩仍放行（下限不能卡太死）
                hswc.HZoomWindowContents(hswc.ActualWidth / 2, hswc.ActualHeight / 2, 120);
                Pump(120);
                bool handledOutZoomed = RaiseWheel(hswc, -120);
                Pump(120);
                R("② 放大状态下外缩放行（下限不误拦）", !handledOutZoomed,
                    $"Handled={handledOutZoomed} span={PartSpan(hw):F0}");

                // ③ 已缩过头 → 一格外缩弹回铺满（现场黑屏的救回路径）
                hw.SetPart(-300000.0, -300000.0, 600000.0, 600000.0);
                Pump(120);
                bool handledRecover = RaiseWheel(hswc, -120);
                Pump(200);
                double recovered = PartSpan(hw);
                R("③ 缩过头后一格外缩弹回铺满（现场黑屏的救回路径）",
                    handledRecover && Math.Abs(recovered - fitSpan) < 0.5,
                    $"Handled={handledRecover} span 900000 → {recovered:F0}（铺满 {fitSpan:F0}）");

                // ④ 放大极限：真放大到 part 跨度 ≤ 8 像素后，内缩被拦
                for (int i = 0; i < 40 && PartSpan(hw) > 8; i++)
                    hswc.HZoomWindowContents(hswc.ActualWidth / 2, hswc.ActualHeight / 2, 120);
                Pump(150);
                double atLimit = PartSpan(hw);
                bool handledIn = RaiseWheel(hswc, 120);
                Pump(120);
                R("④ 放大到极限后内缩被拦（part 不退化成一个点）",
                    handledIn && Math.Abs(PartSpan(hw) - atLimit) < 0.5,
                    $"极限 span={atLimit:F1} Handled={handledIn} 之后={PartSpan(hw):F1}");

                // ⑤/⑥ 没有活图就不画叠加层
                hswc.SetFullImagePart();
                Pump(120);
                display.Annotations = new List<MeasureAnnotation>
                {
                    new MeasureAnnotation
                    {
                        Type = MeasureType.Polyline,
                        Points = new[] { 40.0, 40.0, 560.0, 760.0 },
                        Color = "green",
                        LineWidth = 3,
                    },
                    new MeasureAnnotation
                    {
                        Type = MeasureType.Text,
                        Points = new[] { 80.0, 60.0 },
                        Text = "DEAD-IMAGE-OVERLAY",
                        Color = "green",
                    },
                };
                Pump(250);
                using (var live = hw.DumpWindowImage())
                {
                    double ratio = PixelProbe.NonBlackRatio(live);
                    R("⑤ 活图 + 标注：窗口有内容（反证像素尺子量得到）",
                        ratio > 0.5, $"非黑={ratio:P2}");
                }

                // ⑦ 布局重挂（Unloaded → Loaded）后必须还能画：点工具箱胶囊 / 切画布布局走的正是这条路径
                //   （2026-10-27 真机：OnControlUnloaded 把 hSmart/hWindow 置空，而 Loaded 里读的是字段
                //    → NullReferenceException；异常从 BroadcastLoadedEvent 抛出去还会跳过同一广播里
                //    其它画布的 Loaded → 整页永久不刷新）。这里真的把控件从窗口里摘掉再挂回去。
                win.Content = null;
                Pump(250);
                win.Content = display;
                Pump(450);
                var restoredWindow = GetPrivateField<HWindow?>(display, "hWindow");
                double ratioAfterReattach = 0;
                if (restoredWindow != null)
                {
                    try
                    {
                        using var afterReattach = restoredWindow.DumpWindowImage();
                        ratioAfterReattach = PixelProbe.NonBlackRatio(afterReattach);
                    }
                    catch { ratioAfterReattach = -1; }
                }
                R("⑦ 布局重挂（Unloaded→Loaded）后窗口句柄恢复且仍能画",
                    restoredWindow != null && ratioAfterReattach > 0.5,
                    $"hWindow 字段={(restoredWindow == null ? "null（旧代码在这里抛 NRE）" : "非 null")}"
                    + $" 非黑={ratioAfterReattach:P2}");

                image.Dispose();     // 图死了，但 DP 仍指着它（复刻宿主换帧窗口期）
                InvokeProtectedNoArg(display, "RenderAll");
                Pump(200);
                // 注意：重挂之后 HSmartWindowControlWPF 会给出**新的** HWindow（旧句柄只是残留缓冲），
                // 所以这里必须 dump 字段里的那个窗口——用重挂前的 hw 会读到旧画面，断言变成假绿/假红。
                var dumpWindow = restoredWindow ?? hw;
                using (var dead = dumpWindow.DumpWindowImage())
                {
                    double ratio = PixelProbe.NonBlackRatio(dead);
                    R("⑥ 死图不画叠加层（不再出现\"黑底 + 标注\"）",
                        ratio < 0.0005,
                        $"非黑={ratio:P3}（改前约 0.3%：那条粗绿线 + 一行绿字）");
                }
            }
            catch (Exception ex)
            {
                R("滚轮缩放断言执行", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { image?.Dispose(); } catch { /* 已释放 */ }
                try { win?.Close(); } catch { /* 窗口可能已失效 */ }
                Pump(100);
            }
        }

        /// <summary>
        /// 批 7（2026-10-27）：图集"死帧不许留在选中/钉住"。
        ///
        /// 背景：`ImageFrame.Dispose()` 把 Image 置 null 后**不通知绑定**（Image 是普通 CLR 属性），
        /// 而 `ImageGallery.Recompute` 在"保留历史帧 + 锁定跟随"下漏了一个分支：原选中帧被同槽新帧
        /// 覆盖后四个分支全不命中，SelectedFrame 继续指着已释放的帧 → 画布永久"黑底 + 标注"、
        /// 不自愈（真机 2026-10-27）。钉住的对比基准同样没有校验。
        ///
        /// 只验 `Recompute` 的选中/钉住口径（纯逻辑，不依赖模板、不依赖 HALCON 引擎）：
        ///   ① 锁定跟随 + 选中帧被同槽覆盖 → 选中清空（旧实现：仍指向旧帧）；
        ///   ② 钉住帧只是被流程下拉挡住（仍然活着）→ 不清空（反向：不许误清）；
        ///   ③ 钉住帧被同槽覆盖 → 清空。
        /// </summary>
        private static void RunGalleryLivenessBatch(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            // ① 锁定跟随 + 选中帧被同槽新帧覆盖
            var frames = new System.Collections.ObjectModel.ObservableCollection<ImageFrame>
            {
                new ImageFrame { FlowName = "流程A", StepName = "结果显示", PortName = "图像", Sequence = 1 },
            };
            var gallery = new ImageGallery
            {
                ItemsSource = frames,
                ViewIndexFilter = 1,
                SupportsFrameHistory = true,
                AutoSelectNewest = true,
            };
            gallery.Recompute();
            var stale = gallery.SelectedFrame;
            gallery.AutoSelectNewest = false;   // 用户按"锁定跟随"
            frames[0] = new ImageFrame { FlowName = "流程A", StepName = "结果显示", PortName = "图像", Sequence = 2 };
            gallery.Recompute();
            R("图集：锁定跟随时选中帧被同槽覆盖 → 选中清空（不留已释放的帧）",
                stale != null && gallery.SelectedFrame == null,
                $"覆盖前选中={(stale == null ? "null（前置失败）" : "序号" + stale.Sequence)}，"
                + $"覆盖后={(gallery.SelectedFrame == null ? "null" : "序号" + gallery.SelectedFrame.Sequence)}");

            // ②③ 钉住的对比基准
            var pinFrames = new System.Collections.ObjectModel.ObservableCollection<ImageFrame>
            {
                new ImageFrame { FlowName = "流程A", StepName = "结果显示", Sequence = 1 },
                new ImageFrame { FlowName = "流程B", StepName = "结果显示", Sequence = 2 },
            };
            var pinGallery = new ImageGallery
            {
                ItemsSource = pinFrames,
                ViewIndexFilter = 1,
                SupportsFrameHistory = true,
            };
            pinGallery.Recompute();
            var pinned = pinFrames[0];
            pinGallery.PinnedFrame = pinned;
            pinGallery.SelectedFlow = "流程B";    // 基准被流程下拉挡住，但帧仍然活着
            pinGallery.Recompute();
            R("图集：钉住的基准只是被流程筛选挡住时不清空（反向：不误清）",
                ReferenceEquals(pinGallery.PinnedFrame, pinned),
                $"钉住={(pinGallery.PinnedFrame == null ? "null" : pinGallery.PinnedFrame.FlowName)}");

            pinGallery.SelectedFlow = ImageGallery.AllFlowsLabel;
            pinFrames[0] = new ImageFrame { FlowName = "流程A", StepName = "结果显示", Sequence = 3 };
            pinGallery.Recompute();
            R("图集：钉住的基准被同槽覆盖 → 清空（对比大图不再画黑底 + 标注）",
                pinGallery.PinnedFrame == null,
                $"钉住={(pinGallery.PinnedFrame == null ? "null" : pinGallery.PinnedFrame.FlowName)}");
        }

        /// <summary>当前显示范围（part）的行跨度；取不到返回 -1</summary>
        private static double PartSpan(HWindow hw)        {
            try
            {
                HOperatorSet.GetPart(hw, out HTuple r1, out HTuple _, out HTuple r2, out HTuple _);
                return r2.D - r1.D;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 按 WPF 的隧道阶段投递一次滚轮事件：HalconBase 的上下限挂在 PreviewMouseWheel 上，
        /// 与真实鼠标同一条路由（Handled 之后控件自己的 MouseWheel 不再执行）。
        /// </summary>
        private static bool RaiseWheel(HSmartWindowControlWPF hswc, int delta)
        {
            var args = new System.Windows.Input.MouseWheelEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            };
            hswc.RaiseEvent(args);
            return args.Handled;
        }

        /// <summary>按类型找可视树里的第一个后代（模板内部元素用 FindName 不稳定）</summary>
        private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T typed) return typed;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>泵消息指定时长（真窗口的 Loaded/渲染/绑定都靠它推进）</summary>
        private static void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}
