using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Halcon;
using Core.Halcon.Controls;
using Core.Halcon.Models;
using HalconDotNet;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// Core.Halcon 画布 ROI 的「拖拽改大小」交互回归（2026-10-15）。
    ///
    /// 背景（真机反馈：创建 ROI 后"拖拽改大小很不方便 / 改不了大小"）：
    /// HSmartWindowControlWPF 的鼠标链路是「控件内部先 SendMouseDownEvent（交给 HALCON 做句柄交互）
    /// → 之后才触发 HMouseDown 事件」，而 HalconBase 只给 ActiveRoi 挂 HALCON 句柄。
    /// 于是在"未选中的 ROI 上按下并拖动"这一次手势里，HALCON 收到 down 时句柄还没挂上，
    /// 整段手势被浪费 —— 用户必须"点一次（选中）、再拖一次"才能改大小。
    ///
    /// 断言环境（本类自带，不依赖其它断言的线程/资源）：
    /// · STA 工作线程 + 一个**屏幕外的真实 WPF 窗口**。HALCON 的 drawing object 手柄只在
    ///   可见窗口上响应：实测 buffer 窗口（open_window "buffer"）只有"点中对象内部"能命中，
    ///   角点手柄与拖拽全部失效（2026-10-15 探针），所以这里必须开真窗口；
    /// · 主题字典挂在**窗口**上 —— 本工程不是 WPF Application，没有 Application 级资源，
    ///   而 ImageEdit 的模板在 /Core.Halcon;component/Generic.xaml（宿主 VisionMaster\App.xaml:66 同款合并）；
    /// · 鼠标用 HALCON 的 send_mouse_*_event + 反射调 HalconBase 的 HMouse* 处理器，
    ///   完整复刻控件内部的调用顺序（先 SendMouse*Event、再触发 HMouse* 事件）。
    ///
    /// 坐标系：控件尺寸 == 图像尺寸 == part 跨度，故 图像坐标 == 控件坐标。
    /// rectangle2 参数口径：length1 沿 **column** 轴、length2 沿 **row** 轴（HALCON 口径，容易记反）。
    /// </summary>
    internal static class RoiInteractionChecks
    {
        // 探针用的 ROI：中心 (300,400)、phi=0、l1=100、l2=60
        //   → rows[240,360]、cols[300,500]；左上角手柄 (240,300)、中心 (300,400)
        private static readonly double[] RoiParams = { 300.0, 400.0, 0.0, 100.0, 60.0 };
        private const double CornerRow = 240, CornerCol = 300;
        private const double DragToRow = 200, DragToCol = 260;      // 向左上拖 → l1/l2 增大
        private const double OutsideRow = 232, OutsideCol = 292;    // 距角点 ≈11.3px：在 8px 外、14px 内

        public static void Run()
        {
            Section("Core.Halcon ROI 画布交互（拖拽改大小）");

            if (!HalconRuntime.IsAvailable)
            {
                // 反证（R38）：不可用原因来自引擎探测（缺 halcon.dll / PATH 未配），与本次改动无关；
                // 引擎不在时 drawing object 根本创建不出来，交互断言没有被测对象。
                Check("[ROI] 画布交互断言（跳过：本机无 HALCON 引擎）", true,
                    "原因：" + (HalconRuntime.UnavailableReason ?? "未知"));
                return;
            }

            var results = new List<(string Name, bool Ok, string Detail)>();
            var sta = new Thread(() =>
            {
                try { RunProbe(results); }
                catch (Exception ex)
                {
                    lock (results) results.Add(("STA 运行", false, ex.GetType().Name + ": " + ex.Message));
                }
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();

            foreach (var r in results)
                Check("[ROI] " + r.Name, r.Ok, r.Detail);
        }

        private static void RunProbe(List<(string Name, bool Ok, string Detail)> results)
        {
            void R(string name, bool ok, string detail)
            {
                lock (results) results.Add((name, ok, detail));
            }

            var win = new Window
            {
                Width = 820,
                Height = 660,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                // 屏幕外：跑断言不打扰桌面。HwndHost 仍会创建真窗口，HALCON 手柄照常响应（已实测）
                Left = -4000,
                Top = 0,
            };
            win.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Core.Halcon;component/Generic.xaml", UriKind.Relative),
            });
            var edit = new ImageEdit { Width = 800, Height = 600 };
            win.Content = edit;
            win.Show();
            Pump(400);

            HObject probeImage = null!;   // 底图（显式释放，对齐 R37 的资源纪律）
            HImage? boundImage = null;
            try
            {
                var hswc = FindChild<HSmartWindowControlWPF>(edit);
                if (hswc?.HalconWindow == null)
                {
                    // 这不是"环境不支持"，而是模板/窗口没就绪——按失败报，别静默跳过
                    R("画布与 HALCON 窗口就绪", false,
                        hswc == null ? "视觉树里找不到 HSmartWindowControlWPF（模板未应用）" : "HalconWindow 为 null");
                    return;
                }
                var hw = hswc.HalconWindow;

                var mDown = typeof(HalconBase).GetMethod("HSmart_MouseDownForRoi",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var mUp = typeof(HalconBase).GetMethod("HSmart_MouseUpForRoi",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var argCtor = typeof(HSmartWindowControlWPF.HMouseEventArgsWPF)
                    .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
                if (mDown == null || mUp == null)
                {
                    R("鼠标处理器可达（反射）", false, "HSmart_MouseDownForRoi / HSmart_MouseUpForRoi 找不到");
                    return;
                }

                // 底图：控件尺寸 == 图像尺寸 == part，保证 图像坐标 == 控件坐标
                HOperatorSet.GenImageConst(out probeImage, "byte", 800, 600);
                boundImage = new HImage(probeImage);
                edit.HImage = boundImage;
                Pump(300);
                edit.FitToImage();
                Pump(200);

                var roi = new DrawingObjectInfo(DrawShapeType.Rectangle,
                    ToTuples(RoiParams), "ROI_0");
                edit.DrawObjectList.Add(roi);
                Pump(200);

                // ── 鼠标链路（严格按控件内部的顺序）─────────────────────────────
                object Args(double row, double col)
                    => argCtor.Invoke(new object?[] { col, row, row, col, 0, (MouseButton?)MouseButton.Left });

                void Down(double row, double col)
                {
                    HOperatorSet.SendMouseDownEvent(hw, row, col, 1, out HTuple _);
                    mDown!.Invoke(edit, new[] { (object)hswc, Args(row, col) });
                }
                void Drag(double row, double col)
                    => HOperatorSet.SendMouseDragEvent(hw, row, col, 1, out HTuple _);
                void Up(double row, double col)
                {
                    HOperatorSet.SendMouseUpEvent(hw, row, col, 1, out HTuple _);
                    mUp!.Invoke(edit, new[] { (object)hswc, Args(row, col) });
                }

                void Reset() => roi.HTuples = ToTuples(RoiParams);
                string Shape()
                    => "l1=" + roi.HTuples[3].D.ToString("F1") + " l2=" + roi.HTuples[4].D.ToString("F1");
                string Centre()
                    => "row=" + roi.HTuples[0].D.ToString("F1") + " col=" + roi.HTuples[1].D.ToString("F1");

                // ── 场景 1（基线）：已选中 → 一次手势拖角点 → 尺寸变化 ──
                // 这条同时是环境反证：它红了说明"本环境驱动不了手柄"，后面几条无从谈起（R38）。
                Reset();
                edit.ActiveRoi = roi;
                Pump(200);
                Down(CornerRow, CornerCol); Drag(220, 280); Drag(DragToRow, DragToCol); Up(DragToRow, DragToCol);
                R("① 已选中后拖角点改大小（基线，同时反证环境可驱动手柄）",
                    roi.HTuples[3].D > 101 && roi.HTuples[4].D > 61,
                    "拖前 " + "l1=100.0 l2=60.0" + " → 拖后 " + Shape());

                // ── 场景 2（核心）：冷启动（未选中）→ 一次手势直接拖角点 → 尺寸变化 ──
                Reset();
                edit.ActiveRoi = null;
                Pump(200);
                Down(CornerRow, CornerCol); Drag(220, 280); Drag(DragToRow, DragToCol); Up(DragToRow, DragToCol);
                R("② 冷启动一次手势直接拖角点即可改大小（本次修复目标）",
                    roi.HTuples[3].D > 101 && roi.HTuples[4].D > 61,
                    "拖后 " + Shape() + "（修复前：只被选中、尺寸不变）");

                // ── 场景 3（对照）：冷启动只"点一下角点"不拖 → 尺寸必须不变 ──
                // 防"随便点一下尺寸就变"的假通过：选中与改大小是两件事。
                Reset();
                edit.ActiveRoi = null;
                Pump(200);
                Down(CornerRow, CornerCol); Up(CornerRow, CornerCol);
                R("③ 冷启动只点角点（不拖）→ 只选中、尺寸不变（防假通过）",
                    Math.Abs(roi.HTuples[3].D - 100) < 0.5 && Math.Abs(roi.HTuples[4].D - 60) < 0.5
                    && edit.ActiveRoi != null,
                    Shape() + "，选中=" + (edit.ActiveRoi != null));

                // ── 场景 4（回归）：点空白 → 结束编辑（既有语义不许被本次改动吃掉）──
                Reset();
                edit.ActiveRoi = roi;
                Pump(200);
                Down(100, 700); Up(100, 700);
                R("④ 点空白结束编辑（既有语义回归）", edit.ActiveRoi == null,
                    "点空白后 ActiveRoi=" + (edit.ActiveRoi == null ? "null" : "仍在"));

                // ── 场景 5（回归）：从对象内部拖 → 移动中心，不是改大小 ──
                Reset();
                edit.ActiveRoi = roi;
                Pump(200);
                Down(300, 400); Drag(320, 420); Drag(340, 440); Up(340, 440);
                R("⑤ 从内部拖 = 移动（不是改大小，回归）",
                    Math.Abs(roi.HTuples[0].D - 300) > 5 && Math.Abs(roi.HTuples[3].D - 100) < 0.5,
                    Centre() + "，" + Shape());

                // ── 场景 6（多 ROI）：冷启动直接拖第二个 ROI 的角点 → 切换 + 改大小一次完成 ──
                Reset();
                var second = new DrawingObjectInfo(DrawShapeType.Rectangle,
                    ToTuples(new[] { 500.0, 650.0, 0.0, 60.0, 40.0 }), "ROI_1");
                edit.DrawObjectList.Add(second);
                edit.ActiveRoi = null;
                Pump(250);
                // ROI_1：rows[460,540] cols[590,710]，左上角手柄 (460,590)
                Down(460, 590); Drag(440, 570); Drag(420, 550); Up(420, 550);
                R("⑥ 多 ROI 下冷启动一次手势拖第二个的角点（切换 + 改大小）",
                    second.HTuples[3].D > 61 && ReferenceEquals(edit.ActiveRoi, second),
                    "ROI_1 " + "l1=" + second.HTuples[3].D.ToString("F1") + " l2=" + second.HTuples[4].D.ToString("F1")
                    + "，ActiveRoi=" + (edit.ActiveRoi as DrawingObjectInfo)?.RoiName);
                edit.DrawObjectList.Remove(second);
                Pump(150);

                // ── 场景 7：已选中后起手点偏出手柄 ~11px → 选中不许被摘掉 ──
                // HALCON 手柄约 8px 见方；旧容差正好 8px，用户瞄偏一点就丢选中（要重新点选才能再拖）。
                Reset();
                edit.ActiveRoi = roi;
                Pump(200);
                Down(OutsideRow, OutsideCol); Up(OutsideRow, OutsideCol);
                R("⑦ 起手点偏出手柄 ~11px 时不摘除选中（容差放宽）",
                    edit.ActiveRoi != null,
                    "偏 (" + OutsideRow + "," + OutsideCol + ") 后 ActiveRoi="
                    + (edit.ActiveRoi == null ? "被摘除" : "保留"));

                // ── 场景 8（行为变更钉住）：冷启动在对象**内部**按下并拖动 → 移动中心 ──
                // 补发 down 之后，"未选中时从内部拖"也会驱动 HALCON 的移动交互（旧行为只是选中）。
                // 这是修复的必然结果（HALCON 里内部拖本来就是移动），钉住它免得日后被当成新缺陷。
                Reset();
                edit.ActiveRoi = null;
                Pump(200);
                Down(300, 400); Drag(320, 420); Drag(340, 440); Up(340, 440);
                R("⑧ 冷启动从内部拖 = 移动（补发 down 带来的行为变更，钉住）",
                    Math.Abs(roi.HTuples[0].D - 300) > 5 && Math.Abs(roi.HTuples[3].D - 100) < 0.5,
                    Centre() + "，" + Shape());
            }
            finally
            {
                edit.HImage = null;      // 先解绑，免得 DisplayImageInfo 还持着句柄
                boundImage?.Dispose();
                probeImage?.Dispose();
                win.Close();
                Pump(150);
            }
        }

        /// <summary>参数数组 → HTuple[]（DrawingObjectInfo 的口径）</summary>
        private static HTuple[] ToTuples(double[] values)
        {
            var tuples = new HTuple[values.Length];
            for (int i = 0; i < values.Length; i++)
                tuples[i] = new HTuple(values[i]);
            return tuples;
        }

        /// <summary>
        /// 泵一段消息：本线程没有 Application.Run，窗口布局与 HwndHost（HALCON 窗口）创建
        /// 都要靠显式 PushFrame 才推进；DispatcherTimer 负责到点退出帧。
        /// </summary>
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds),
                DispatcherPriority.Background, (s, e) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) return typed;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
