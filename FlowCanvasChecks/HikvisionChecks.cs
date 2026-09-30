using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using Plugin.Camera.Hikvision;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 海康相机驱动（Plugin.Camera.Hikvision）的断言。
    ///
    /// 这份断言刻意**不要求装海康 SDK、不要求接真相机**——它守的是驱动作为"扩展"的契约面：
    ///   ① 可被发现：DLL 落在 Modules、实现 ICameraDevice、带 [Display]（与 PluginService.LoadCameraDrivers
    ///      同一条判据，"写了驱动却选不到"全是栽在这里）；
    ///   ② 可被宿主构造：按 CameraProvider.CreateDevice 的形状 (CameraDescriptor) 构造，身份正确透传；
    ///   ③ 缺 SDK 时**优雅降级**：Open 失败但状态机干净地回 Closed，StateDetail 说清"去哪装、放哪"——
    ///      这台机器没装海康 MVS 也能全绿；装了 MVS 的机器上则退化为"枚举不到合成序列号"，同样成立；
    ///   ④ 基类状态机的既有约束不被破坏（未打开时 StartStream 拒绝、Dispose 幂等）；
    ///   ⑤ 像素格式分类（Mono / Color / Unknown）——它决定取图线程把 SDK 帧转成单通道还是三通道，
    ///      分错了类，彩色相机出的图会整帧错乱。
    /// </summary>
    internal static class HikvisionChecks
    {
        private const string DriverDll = "Plugin.Camera.Hikvision.dll";
        private const string DriverTypeName =
            "Plugin.Camera.Hikvision.HikvisionCameraDevice, Plugin.Camera.Hikvision, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public static void Run()
        {
            Section("[HK] 海康相机驱动：发现 → 构造 → 缺 SDK 优雅降级 → 像素格式分类");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var modules = Path.Combine(repoRoot, "Modules");

            // ---- ① 发现：模拟 PluginService.LoadCameraDrivers 的同一条判据 ----
            Check("驱动 DLL 在 Modules 目录里", File.Exists(Path.Combine(modules, DriverDll)), modules);

            Assembly? asm = null;
            try
            {
                asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Plugin.Camera.Hikvision")
                      ?? Assembly.LoadFrom(Path.Combine(modules, DriverDll));
            }
            catch (Exception ex) { Check("插件程序集可装载", false, ex.Message); return; }

            var driverType = asm.GetType("Plugin.Camera.Hikvision.HikvisionCameraDevice");
            Check("实现 ICameraDevice 的非抽象类型存在", driverType != null && typeof(ICameraDevice).IsAssignableFrom(driverType), "");

            var att = driverType?.GetCustomAttribute<DisplayAttribute>();
            Check("驱动带 [Display]（没有它 PluginService 会静默跳过，相机设置里选不到）",
                att?.Name == "海康相机", $"Name={att?.Name}");

            // ---- ② 构造：与 CameraProvider.CreateDevice 同一形状 ----
            var descriptor = new CameraDescriptor
            {
                SerialNo = "SYNTH-" + Guid.NewGuid().ToString("N")[..8],   // 合成序列号：任何环境都枚举不到
                DisplayName = "合成海康相机",
                DriverTypeKey = DriverTypeName,
                Settings = new CameraSettings(),
            };

            ICameraDevice? device = null;
            try
            {
                device = Activator.CreateInstance(driverType!, descriptor) as ICameraDevice;
            }
            catch (Exception ex)
            {
                Check("按 (CameraDescriptor) 构造失败", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            Check("按 (CameraDescriptor) 构造成功，身份正确透传",
                device != null
                && device.Descriptor.SerialNo == descriptor.SerialNo
                && device.Descriptor.Caption.Contains("合成海康相机"),
                device?.Descriptor.Caption ?? "(null)");

            Check("初始状态 Closed", device!.State == CameraConnectionState.Closed, $"{device.State}");

            // ---- ③ 基类状态机的既有约束不被破坏 ----
            Check("未打开时 StartStream 被拒绝",
                !device.StartStream() && device.State == CameraConnectionState.Closed, $"{device.State}");

            // ---- ④ 缺 SDK / 枚举不到相机：Open 失败但优雅降级 ----
            var opened = device.Open();
            var detail = device.StateDetail;
            Check("合成序列号 → Open 失败（真机枚举不到 / 缺 SDK 时同样失败）",
                !opened && device.State == CameraConnectionState.Closed,
                $"State={device.State} Detail={detail}");
            Check("失败原因说清「去哪装、怎么对」：要么缺 SDK 指路，要么报未找到序列号",
                detail.Contains("MvCameraControl") || detail.Contains("未找到序列号"), detail);

            // ---- ⑤ 释放幂等 ----
            var disposeThrew = false;
            try { device.Dispose(); device.Dispose(); }
            catch (Exception ex) { disposeThrew = true; }
            Check("Dispose 幂等（二次释放不抛）", !disposeThrew, "");

            // ---- ⑥ 像素格式分类：决定取图线程把 SDK 帧转成单通道还是三通道 ----
            Check("像素分类：Mono 系归 Mono",
                HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_Mono8") == "Mono"
                && HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_Mono10_Packed") == "Mono"
                && HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_Mono12") == "Mono", "");
            Check("像素分类：Bayer / RGB / YUV 系归 Color（统一转 RGB8 再进管线）",
                HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_BayerGR8") == "Color"
                && HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_RGB8_Packed") == "Color"
                && HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_BayerRG12_Packed") == "Color"
                && HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_YUV422_YUYV_Packed") == "Color", "");
            Check("像素分类：认不出的归 Unknown（宁可丢帧也不出花屏图）",
                HikvisionCameraDevice.ClassifyPixelFormat("") == "Unknown"
                && HikvisionCameraDevice.ClassifyPixelFormat("PixelType_Gvsp_SomethingNew") == "Unknown", "");
        }
    }
}
