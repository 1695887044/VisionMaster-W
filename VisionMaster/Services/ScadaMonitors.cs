using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace VisionMaster.Services
{
    /// <summary>
    /// 一块显示器。两个矩形都是<b>物理像素</b>、虚拟桌面坐标（原点 = 主屏左上角）。
    ///
    /// 为什么矩形是物理像素而不是 WPF 的 DIP
    /// ---------
    /// 物理像素是系统（<c>GetMonitorInfo</c>）的原生口径，拿到手不用做任何假设；
    /// 要交给 WPF 摆窗口时再乘一次缩放因子（见 <see cref="WorkDip"/>）。
    /// 反过来"枚举时就换算成 DIP"看着省事，但那样这个类型就悄悄绑死了"缩放因子是多少"，
    /// 而缩放因子是随机器变的——单位转换放在离使用点最近的地方，读的人才知道这里换算过。
    /// </summary>
    public sealed class ScadaMonitor
    {
        public ScadaMonitor(string deviceName, bool isPrimary, Rect bounds, Rect work)
        {
            DeviceName = deviceName ?? "";
            IsPrimary = isPrimary;
            Bounds = bounds;
            Work = work;
        }

        /// <summary>
        /// 设备名（如 <c>\\.\DISPLAY2</c>）。它是落屏配置里存的那个值——
        /// 只要线还插在同一个视频口上，插拔、重启、换分辨率都不变。
        /// </summary>
        public string DeviceName { get; }

        /// <summary>是不是系统认定的主屏（虚拟桌面原点所在的那一块）</summary>
        public bool IsPrimary { get; }

        /// <summary>显示器全幅（含任务栏占的那条）</summary>
        public Rect Bounds { get; }

        /// <summary>工作区（扣掉任务栏）。独立窗口落点用它，免得压在任务栏底下</summary>
        public Rect Work { get; }

        /// <summary>工作区换算成 WPF 的 DIP 坐标（窗口的 Left/Top/Width/Height 就是这个口径）</summary>
        public Rect WorkDip(double dpiScale) => ScadaMonitors.ToDip(Work, dpiScale);

        public override string ToString() =>
            $"{DeviceName}{(IsPrimary ? "（主屏）" : "")} {Bounds.Width:0}×{Bounds.Height:0}";
    }

    /// <summary>
    /// 显示器清单与落屏几何。全程序<b>只有这一处</b>调 Win32 问"有几块屏、各在哪"，
    /// 也只有这一处做"物理像素 ↔ WPF DIP"的换算。
    ///
    /// 为什么自己 P/Invoke 而不用 WinForms 的 Screen
    /// ---------
    /// 只为了拿一份显示器清单就给 VisionMaster / ScadaChecks 两个工程打开
    /// <c>UseWindowsForms</c>，等于为了一个查询把整套 WinForms 运行时和它的消息循环
    /// 拖进一个纯 WPF 程序——依赖代价远大于收益。两个 user32 函数就够了。
    ///
    /// 本进程的 DPI 前提（<see cref="DpiScale"/> 的推导依赖它）
    /// ---------
    /// 本程序没有 app.manifest、也没设任何 DPI 感知开关，.NET 桌面默认让进程
    /// <b>System-DPI-aware</b>：窗口只在创建时查一次主屏 DPI，之后所有显示器沿用同一个缩放因子。
    /// 所以"主屏物理工作区宽度 ÷ WPF 认的工作区宽度"就是那个因子，全局只有这一个数。
    /// </summary>
    public static class ScadaMonitors
    {
        private const int MonitorInfoPrimary = 0x1;

        /// <summary>独立窗口距目标屏工作区右下角的留白（DIP）。与 S13-e 之前逐字一致</summary>
        public const double IndependentMargin = 24;

        /// <summary>独立窗口尺寸相对工作区的比例上限（宽 / 高）</summary>
        private const double IndependentWidthRatio = 0.62;
        private const double IndependentHeightRatio = 0.72;

        /// <summary>独立窗口尺寸的下限（DIP）——再小就没法看画面了</summary>
        private const double MinWindowWidth = 480;
        private const double MinWindowHeight = 360;

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// <c>MONITORINFOEX</c>。<c>szDevice</c> 的 32 是 Win32 头文件里的 <c>CCHDEVICENAME</c>，
        /// 不能改小——缓冲区不够时 <c>GetMonitorInfo</c> 会直接失败，表现为"一块屏都枚举不出来"。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        #endregion

        /// <summary>
        /// 当前接在机器上的所有显示器。<b>主屏排第一</b>，其余按设备名排序——
        /// 下拉框里的次序必须每次一样，否则"第二项是副屏"这种肌肉记忆会漂。
        /// 枚举不出来时返回空清单（调用方各自退回历史路径，见 <see cref="ScadaRuntimeWindow"/>）。
        /// </summary>
        public static IReadOnlyList<ScadaMonitor> All()
        {
            var found = new List<ScadaMonitor>();

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data) =>
                {
                    var info = new MONITORINFOEX();
                    info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                    if (GetMonitorInfo(hMonitor, ref info))
                    {
                        found.Add(new ScadaMonitor(
                            info.szDevice ?? "",
                            (info.dwFlags & MonitorInfoPrimary) != 0,
                            ToRect(info.rcMonitor),
                            ToRect(info.rcWork)));
                    }
                    // 回调返回 false 会中断枚举，所以无论这一块成功与否都继续下一块
                    return true;
                }, IntPtr.Zero);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            return found
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.DeviceName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 把配置里的显示器设备名解成一块真实的屏。
        ///
        /// 三种结果
        /// ---------
        /// · 名字为空 → 主屏（= 历史行为，也是默认配置走的路）；
        /// · 名字匹配上 → 那一块；
        /// · 名字非空但找不到（拔线、换了视频口、在别的机器上打开同一份配置）→ <b>回落主屏</b>，
        ///   并把 <paramref name="fellBack"/> 置 true。调用方据此记一条日志——
        ///   现场"我明明配了副屏，怎么还跑在主屏上"这种情况，日志里得看得出来，否则只能靠猜。
        ///
        /// 为什么不做"离配置那块屏最近的替代屏"这种智能回落
        /// ---------
        /// 落屏这件事没有"差不多对"：摆到一块没人看的屏上，现场看到的就是"运行画面不见了"，
        /// 比老老实实回到主屏更难排查。回落只有一条规则，读的人不用推演。
        /// </summary>
        public static ScadaMonitor? Resolve(string deviceName, out bool fellBack)
        {
            var all = All();
            fellBack = false;
            if (all.Count == 0) return null;

            var wanted = (deviceName ?? "").Trim();
            if (wanted.Length > 0)
            {
                var hit = all.FirstOrDefault(m => string.Equals(m.DeviceName, wanted, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
                fellBack = true;
            }

            return all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
        }

        /// <summary>
        /// 本进程的全局 DPI 缩放因子（物理像素 ÷ 它 = DIP）。
        ///
        /// 怎么算出来的
        /// ---------
        /// 主屏的物理工作区宽度来自 <c>GetMonitorInfo</c>，WPF 认的工作区宽度来自
        /// <see cref="SystemParameters.WorkArea"/>——两者测的是<b>同一块屏</b>，商就是缩放因子。
        /// 这样不必再引 DPI API，也不会出现"两个来源各说各话"的口径冲突
        /// （前提见类型注释：本进程是 System-DPI-aware，全局只有一个因子）。
        ///
        /// 取不到时返回 1.0：宁可少缩放，也不要拿 0 去当除数。
        /// </summary>
        public static double DpiScale()
        {
            var primary = All().FirstOrDefault(m => m.IsPrimary);
            if (primary == null) return 1.0;
            return ComputeDpiScale(primary.Work.Width, SystemParameters.WorkArea.Width);
        }

        /// <summary>
        /// 纯函数：由「主屏物理工作区宽度」与「WPF 认的工作区宽度（DIP）」求缩放因子。
        ///
        /// 抽成纯函数是为了能被断言直接钉住：真机上 DPI 是 100% 还是 150% 不由我们决定，
        /// 但"怎么换算"必须是确定的、可复算的。
        /// </summary>
        public static double ComputeDpiScale(double primaryWorkPhysicalWidth, double primaryWorkDipWidth)
        {
            if (primaryWorkPhysicalWidth <= 0 || primaryWorkDipWidth <= 0) return 1.0;

            var scale = primaryWorkPhysicalWidth / primaryWorkDipWidth;

            // 合理区间之外说明两个来源说的不是同一块屏（例如将来把进程改成 Per-Monitor V2
            // 而这里没跟上）。那时按 1.0 处理，最坏是窗口尺寸不合预期；
            // 按一个错因子算，会把窗口摆到屏幕外面去——那在现场就是"运行画面不见了"。
            return scale < 0.5 || scale > 4.0 ? 1.0 : scale;
        }

        /// <summary>纯函数：物理像素矩形 → WPF DIP 矩形</summary>
        public static Rect ToDip(Rect physical, double dpiScale)
        {
            if (dpiScale <= 0) dpiScale = 1.0;
            return new Rect(
                physical.X / dpiScale,
                physical.Y / dpiScale,
                physical.Width / dpiScale,
                physical.Height / dpiScale);
        }

        /// <summary>
        /// 纯函数：算出独立形态窗口的 Left/Top/Width/Height（DIP）。
        ///
        /// 规则与 S13-e 之前逐字一致，只是把"哪块屏"从写死的主屏变成入参
        /// ---------
        /// · 尺寸按目标屏工作区的 62% / 72% 收一收，并夹在 480×360 以上；
        /// · 落在工作区<b>右下角</b>、留 <see cref="IndependentMargin"/> 的边。
        ///   为什么不居中：主界面通常是最大化的，居中的窗口会正好压在图像区上，
        ///   等于换个方式复现了本来要躲的那个问题（见 <see cref="Views.ScadaRuntimeWindow"/>）。
        /// </summary>
        public static Rect PlaceIndependent(Rect workDip, double desiredWidth, double desiredHeight)
        {
            var w = Math.Min(desiredWidth, Math.Max(MinWindowWidth, workDip.Width * IndependentWidthRatio));
            var h = Math.Min(desiredHeight, Math.Max(MinWindowHeight, workDip.Height * IndependentHeightRatio));
            return new Rect(
                workDip.Right - w - IndependentMargin,
                workDip.Bottom - h - IndependentMargin,
                w,
                h);
        }

        /// <summary>
        /// 纯函数：给下拉框用的一句话方位描述。
        ///
        /// 现场两块屏型号往往一样，光看"显示器 1 / 显示器 2"分不清哪块是哪块——
        /// 说清在主屏的哪一侧最省事。判定拿两块屏的中心点连线，横向差更大就算左右，否则算上下。
        /// </summary>
        public static string DescribePosition(ScadaMonitor monitor, ScadaMonitor? primary)
        {
            if (monitor == null) return "";
            if (monitor.IsPrimary) return "主显示器";

            // 没拿到主屏（枚举结果里一块都没标主屏）时不能跟着说"主显示器"——
            // 那会让下拉里出现两块都叫"主显示器"的屏，等于什么都没说。
            if (primary == null) return "附加显示器";

            var dx = monitor.Bounds.X + monitor.Bounds.Width / 2 - (primary.Bounds.X + primary.Bounds.Width / 2);
            var dy = monitor.Bounds.Y + monitor.Bounds.Height / 2 - (primary.Bounds.Y + primary.Bounds.Height / 2);

            if (Math.Abs(dx) >= Math.Abs(dy))
                return dx >= 0 ? "在主屏右侧" : "在主屏左侧";

            return dy >= 0 ? "在主屏下方" : "在主屏上方";
        }

        private static Rect ToRect(RECT r) => new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
