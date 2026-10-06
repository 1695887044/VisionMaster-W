using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Core.Interfaces;
using HalconDotNet;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图像采集插件（Plugin.ImageAcquisition）的契约断言。
    ///
    /// 守的是三类"改坏了却很难被发现"的面：
    ///   ① 端口面：相机模式的精确帧号端口（FrameId / ReceivedCount）在，且旧端口
    ///      （Image/CurrentPath/CurrentIndex/TotalFiles）没被改名 —— 下游接线全靠端口名；
    ///   ② 原始像素 → HImage 的唯一转换核心：灰度/彩色通道数、BGR 逐字节顺序、BGRA 去 alpha，
    ///      以及"长度不足 / 通道数不支持 / 尺寸超大"必须在校验层拦下（不能等进了非托管世界才炸）；
    ///   ③ 文件夹枚举：扩展名过滤 + 自然序（img2 排在 img10 前）—— 文件索引是直接喂给生产用的。
    ///
    /// 走反射调用插件的私有静态实现：不为测试在产品代码里放宽可见性
    /// （与 Harness.PushUndoReflection 同一约定）。
    /// </summary>
    internal static class ImageAcquisitionChecks
    {
        private const string PluginDll = "Plugin.ImageAcquisition.dll";
        private const string PluginTypeName = "Plugin.ImageAcquisition.ImageAcquisitionPlugin";
        private const string Extensions = ".bmp,.jpg,.jpeg,.png,.tif,.tiff";

        public static void Run()
        {
            Section("[IA] 图像采集：端口面 + 原始像素→HImage + 文件夹自然序");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var modules = Path.Combine(repoRoot, "Modules");

            Check("插件 DLL 在 Modules 目录里", File.Exists(Path.Combine(modules, PluginDll)), modules);

            Assembly? asm = null;
            try
            {
                asm = AppDomain.CurrentDomain.GetAssemblies()
                          .FirstOrDefault(a => a.GetName().Name == "Plugin.ImageAcquisition")
                      ?? Assembly.LoadFrom(Path.Combine(modules, PluginDll));
            }
            catch (Exception ex)
            {
                Check("插件程序集可装载", false, ex.Message);
                return;
            }

            var type = asm.GetType(PluginTypeName);
            Check("插件类型存在且实现 IVisionPlugin",
                type != null && typeof(IVisionPlugin).IsAssignableFrom(type), PluginTypeName);
            if (type == null)
                return;

            // 构造期不得碰 HALCON 原生库：这类插件会在启动扫描端口时被实例化，
            // 构造函数里 new HImage() 会让"无 HALCON 的机器"上整个插件 DLL 被判成加载失败。
            // 两条判据：① 构造不抛；② 构造后 PreviewImage 仍是 null（没在构造期建图）。
            object instance;
            try
            {
                instance = Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                Check("插件可实例化（构造期不碰 HALCON 原生库）", false, $"{ex.GetType().Name}: {ex.Message}");
                return;
            }
            Check("插件可实例化（构造期不碰 HALCON 原生库）", true, "");

            var previewProp = type.GetProperty("PreviewImage");
            var previewValue = previewProp?.GetValue(instance);
            Check("构造后 PreviewImage 为 null（未在构造期创建 HImage）",
                previewProp != null && previewValue == null,
                previewProp == null
                    ? "PreviewImage 属性未找到"
                    : $"PreviewImage = {(previewValue == null ? "null" : previewValue.GetType().Name)}");

            // ---- ① 端口面 ----
            var plugin = (IVisionPlugin)instance;
            var outputs = plugin.Outputs.Keys.ToList();
            Check("输出端口含 FrameId / ReceivedCount（相机模式精确帧号，不靠语义重载）",
                outputs.Contains("FrameId") && outputs.Contains("ReceivedCount"),
                string.Join(",", outputs));
            Check("旧输出端口未被改名（Image / CurrentPath / CurrentIndex / TotalFiles）",
                new[] { "Image", "CurrentPath", "CurrentIndex", "TotalFiles" }.All(outputs.Contains),
                string.Join(",", outputs));
            // 多相机身份：SourceSerial 是"这份标定/这张图属于哪台相机"的机器可读来源（接标定/坐标变换自动核对）
            // 默认值即"空"（string 端口的 default 是 null，空串与 null 都算未提供——下游按 Trim 后非空才校验）
            Check("输出端口含 SourceSerial（多相机身份；相机模式=配置序列号，其余模式为空）",
                outputs.Contains("SourceSerial")
                && string.IsNullOrEmpty(plugin.Outputs["SourceSerial"].Value as string),
                string.Join(",", outputs));
            Check("输入端口未被改名（FilePath / FolderPath / FileIndex / FrameTimeoutMs）",
                new[] { "FilePath", "FolderPath", "FileIndex", "FrameTimeoutMs" }.All(plugin.Inputs.ContainsKey),
                string.Join(",", plugin.Inputs.Keys));

            // ---- ② DisplayViewIndex 写入即夹取（越界窗口号会静默丢图：消费端按等值筛选） ----
            var viewProp = type.GetProperty("DisplayViewIndex");
            if (viewProp == null)
            {
                Check("DisplayViewIndex 属性存在", false, "");
            }
            else
            {
                viewProp.SetValue(instance, 42);
                var clampedHigh = (int)viewProp.GetValue(instance)!;
                viewProp.SetValue(instance, -5);
                var clampedLow = (int)viewProp.GetValue(instance)!;
                viewProp.SetValue(instance, 3);
                var normal = (int)viewProp.GetValue(instance)!;
                Check("DisplayViewIndex 写入即夹取到 0~9",
                    clampedHigh == 9 && clampedLow == 0 && normal == 3,
                    $"42→{clampedHigh}, -5→{clampedLow}, 3→{normal}");
            }

            // ---- ③ ToHImage：原始像素 → HImage 的唯一转换核心 ----
            var toHImage = type.GetMethod("ToHImage", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(byte[]), typeof(int), typeof(int), typeof(int) }, null);
            Check("ToHImage(byte[],width,height,channels) 存在", toHImage != null, "");
            if (toHImage == null)
                return;

            CheckGray(toHImage);
            CheckBgr(toHImage);
            CheckBgra(toHImage);
            CheckToHImageGuards(toHImage);

            // ---- ④ 文件夹枚举：过滤 + 自然序 ----
            CheckFolderListing(type);
        }

        private static void CheckGray(MethodInfo toHImage)
        {
            // 4x3 灰度：值按行优先原样搬进单通道（(row,col) ↔ buffer[row*width+col]）
            var pixels = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 };
            using var image = (HImage)toHImage.Invoke(null, new object[] { pixels, 4, 3, 1 })!;
            image.GetImageSize(out int width, out int height);
            HOperatorSet.CountChannels(image, out HTuple channels);
            var at00 = GrayAt(image, 0, 0);
            var at12 = GrayAt(image, 1, 2);
            var at23 = GrayAt(image, 2, 3);

            Check("灰度 4x3x1 → 单通道、尺寸与行列不转置",
                width == 4 && height == 3 && (int)channels.D == 1
                && Math.Abs(at00 - 10) < 0.001 && Math.Abs(at12 - 16) < 0.001 && Math.Abs(at23 - 21) < 0.001,
                $"{width}x{height}x{channels.D}, (0,0)={at00}, (1,2)={at12}, (2,3)={at23}");
        }

        private static void CheckBgr(MethodInfo toHImage)
        {
            // 2x1 BGR 交错。像素0 = 纯红（内存序 B=0,G=0,R=255），像素1 = 纯蓝（B=255,G=0,R=0）。
            // 约定：HALCON 图的通道 1/2/3 就是 R/G/B（见 ColorSampler："channels[0/1/2] 分别是 R/G/B"），
            // 所以纯红必须落在 R 通道上——这条断言就是"彩色不反色、像素不错位"的判据。
            var pixels = new byte[] { 0, 0, 255, 255, 0, 0 };
            using var image = (HImage)toHImage.Invoke(null, new object[] { pixels, 2, 1, 3 })!;
            HOperatorSet.CountChannels(image, out HTuple channels);
            var r0 = ChannelValue(image, 0, 0, 0);
            var g0 = ChannelValue(image, 1, 0, 0);
            var b0 = ChannelValue(image, 2, 0, 0);
            var r1 = ChannelValue(image, 0, 0, 1);
            var b1 = ChannelValue(image, 2, 0, 1);

            Check("BGR 2x1x3 → 三通道；纯红/纯蓝落在 R/B 通道（不反色、不串像素）",
                (int)channels.D == 3
                && Math.Abs(r0 - 255) < 0.001 && Math.Abs(g0 - 0) < 0.001 && Math.Abs(b0 - 0) < 0.001
                && Math.Abs(r1 - 0) < 0.001 && Math.Abs(b1 - 255) < 0.001,
                $"像0 R={r0} G={g0} B={b0}；像1 R={r1} B={b1}");
        }

        private static void CheckBgra(MethodInfo toHImage)
        {
            // 2x1 BGRA 交错：4 通道必须先压成紧凑 BGR，否则 GenImageInterleaved 整帧错位。
            // 像素0 = 纯红（B=0,G=0,R=255,A=9），像素1 = 纯绿（B=0,G=255,R=0,A=9）
            var pixels = new byte[] { 0, 0, 255, 9, 0, 255, 0, 9 };
            using var image = (HImage)toHImage.Invoke(null, new object[] { pixels, 2, 1, 4 })!;
            HOperatorSet.CountChannels(image, out HTuple channels);
            var r0 = ChannelValue(image, 0, 0, 0);
            var g0 = ChannelValue(image, 1, 0, 0);
            var g1 = ChannelValue(image, 1, 0, 1);

            Check("BGRA 2x1x4 → 抽出 alpha 压成三通道 BGR（不是按 3 字节/像素错位读）",
                (int)channels.D == 3
                && Math.Abs(r0 - 255) < 0.001 && Math.Abs(g0 - 0) < 0.001 && Math.Abs(g1 - 255) < 0.001,
                $"像0 R={r0} G={g0}；像1 G={g1}, ch={channels.D}");
        }

        private static void CheckToHImageGuards(MethodInfo toHImage)
        {
            var shortData = InvokeExpectError(toHImage, new object[] { new byte[10], 4, 4, 1 });
            Check("像素数据不足 → ArgumentException（绝不进非托管世界越界读）",
                shortData is ArgumentException, Describe(shortData));

            var twoChannel = InvokeExpectError(toHImage, new object[] { new byte[8], 2, 2, 2 });
            Check("2 通道 → ArgumentException（没有约定好的解释方式，不许静默按灰度读）",
                twoChannel is ArgumentException, Describe(twoChannel));

            // 100000x100000x3 = 3e10 字节：int 乘法会溢出成负数、让"长度不足"校验反面通过，
            // 必须用 long 计算并在入口以 ArgumentException 拦下（而不是 AllocHGlobal 抛看不懂的错）
            var huge = InvokeExpectError(toHImage, new object[] { new byte[16], 100000, 100000, 3 });
            Check("超大尺寸 → ArgumentException（long 计算，int 溢出不会反放行）",
                huge is ArgumentException, Describe(huge));
        }

        private static void CheckFolderListing(Type pluginType)
        {
            var listMethod = pluginType.GetMethod("ListFolderImages", BindingFlags.NonPublic | BindingFlags.Static);
            if (listMethod == null)
            {
                Check("ListFolderImages 存在", false, "");
                return;
            }

            var dir = Path.Combine(Path.GetTempPath(), "ia_checks_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var name in new[] { "img10.bmp", "img2.bmp", "img1.bmp", "IMG3.BMP", "note.txt" })
                    File.WriteAllBytes(Path.Combine(dir, name), new byte[] { 0 });

                var files = (List<string>)listMethod.Invoke(null, new object[] { dir, Extensions })!;
                var names = files.Select(Path.GetFileName).ToList();

                Check("扩展名过滤：只收图像、忽略 .txt",
                    names.Count == 4 && !names.Contains("note.txt"), string.Join(",", names));
                Check("自然序：img1 < img2 < IMG3 < img10（纯字符串序会把 10 排到 2 前面）",
                    names.SequenceEqual(new[] { "img1.bmp", "img2.bmp", "IMG3.BMP", "img10.bmp" }),
                    string.Join(",", names));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* 清理失败不影响断言结论 */ }
            }
        }

        // ------------------------------------------------------------------
        //  小工具
        // ------------------------------------------------------------------

        private static double GrayAt(HImage image, int row, int col)
        {
            HOperatorSet.GetGrayval(image, row, col, out HTuple value);
            return value.D;
        }

        /// <summary>取第 channel 个通道（0 基）在 (row,col) 的灰阶</summary>
        private static double ChannelValue(HImage image, int channel, int row, int col)
        {
            HOperatorSet.AccessChannel(image, out HObject channelObject, channel + 1);
            try
            {
                HOperatorSet.GetGrayval(channelObject, row, col, out HTuple value);
                return value.D;
            }
            finally
            {
                channelObject?.Dispose();
            }
        }

        private static Exception? InvokeExpectError(MethodInfo method, object[] args)
        {
            try
            {
                var result = method.Invoke(null, args);
                (result as IDisposable)?.Dispose();
                return null;
            }
            catch (TargetInvocationException tie)
            {
                return tie.InnerException ?? tie;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static string Describe(Exception? ex)
            => ex == null ? "未抛异常" : $"{ex.GetType().Name}: {ex.Message}";
    }
}
