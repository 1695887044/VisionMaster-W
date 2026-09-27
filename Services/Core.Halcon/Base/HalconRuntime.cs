using System;
using System.Runtime.InteropServices;

namespace Core.Halcon.Controls
{
    /// <summary>
    /// HALCON 原生运行时可用性探测（进程内只探一次，结果全局缓存）。
    ///
    /// 为什么需要它
    /// ---------
    /// halcondotnet.dll 只是 .NET 托管外壳，真正干活的 halcon.dll 来自本机安装的 HALCON
    /// （%HALCONROOT%\bin\x64-win64 挂在 PATH 上）。没装的机器上，HSmartWindowControlWPF
    /// 会在 OnRender 里创建 HWindow 时抛 DllNotFoundException —— 异常发生在 WPF 渲染回调里，
    /// 就地捕获了下一帧重排还会再炸，等于永远接不住。所以必须在控件进入视觉树**之前**就
    /// 知道原生库能不能加载：能 → 走正常模板；不能 → 换占位提示，视觉功能整体降级，
    /// 方案编辑/流程编排/通讯/组态照常可用（与启动自检 HalconCheck 的"只警告不阻断"同口径）。
    ///
    /// 为什么用 NativeLibrary.TryLoad 而不是真调一个算子
    /// ---------
    /// TryLoad("halcon") 与 halcondotnet 的 DllImport 同名、走同一套 Windows 搜索顺序（含 PATH），
    /// 且连依赖一起解析——加载成功句柄就留在进程内，后续 P/Invoke 直接命中；失败也不会触发
    /// 任何 HalconDotNet 类型的静态初始化，不产生副作用。许可（license）是否有效这里探不出来，
    /// 仍由启动自检的真调用判定 —— 本类只回答"原生库在不在"，不回答"引擎能不能干活"。
    /// </summary>
    public static class HalconRuntime
    {
        private static readonly Lazy<(bool Available, string? Reason)> _probe =
            new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>原生 HALCON 运行时在本机是否可用（首次访问时探测，之后读缓存）</summary>
        public static bool IsAvailable => _probe.Value.Available;

        /// <summary>不可用原因（用于界面占位提示与日志）；可用时为 null</summary>
        public static string? UnavailableReason => _probe.Value.Reason;

        private static (bool, string?) Probe()
        {
            try
            {
                if (NativeLibrary.TryLoad("halcon", out _))
                    return (true, null);
                return (false,
                    "本机没有可用的 HALCON 运行时（找不到 halcon.dll）：请安装 HALCON（64 位），"
                    + "或把安装目录下的 bin\\x64-win64 加入系统 PATH 环境变量后重启本软件。");
            }
            catch (Exception ex)
            {
                return (false, $"探测 HALCON 运行时失败：{ex.Message}");
            }
        }
    }
}
