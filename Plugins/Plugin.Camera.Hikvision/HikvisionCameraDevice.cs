using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Threading;
using Core.Interfaces;

namespace Plugin.Camera.Hikvision
{
    /// <summary>
    /// 海康威视相机驱动（GigE / USB3，走 MVS 的 .NET 包装器 MvCamCtrl.NET）。
    ///
    /// 在驱动族里的位置
    /// ---------
    /// 与网络相机（客户端推帧）不同，这里是**真机驱动**：自己打开 SDK 设备句柄、
    /// 启动采流，并在自己的取图线程上把 SDK 帧解成像素后 <see cref="CameraDeviceBase.PushFrame"/>
    /// 送进基类的环形队列 —— 契约的第一条硬约束（SDK 收图线程绝不阻塞）由
    /// "独立取图线程 + 拷贝入队"满足。
    ///
    /// SDK 运行时是**运行时探测**的（见 MvsRuntime）
    /// ---------
    /// 编译期不引用海康 DLL：没装 MVS 的机器上本驱动照样随插件加载，
    /// Open 时给出"去哪装、放哪"的中文指引，其余契约（状态机 / 队列 / 计数）照常可验证。
    ///
    /// 打开失败的原因**一律抛异常**而不是 SetDetail 后返回 false：
    /// 基类 Open 的收尾会用"打开失败：{异常消息}"覆盖状态说明 —— SetDetail 的文字会被顶掉。
    /// </summary>
    [Display(
        Name = "海康相机",
        GroupName = "相机驱动",
        Description = "海康威视 GigE/USB3 相机（需安装海康 MVS，并把 MvCameraControl.Net.dll 放进 Modules 目录）",
        ShortName = "\uf03e")]
    public sealed class HikvisionCameraDevice : CameraDeviceBase
    {
        private readonly object _sdkGate = new();

        private MvsRuntime? _sdk;
        private object? _camera;            // MvCamCtrl.NET.MyCamera 实例（句柄收在包装器内部）

        private volatile bool _grabbing;    // 取图线程的停止标志
        private Thread? _grabThread;

        /// <summary>像素格式转换的目标缓冲（按需扩容、常驻钉住；只有取图线程会碰它）</summary>
        private byte[] _convertBuffer = Array.Empty<byte>();
        private GCHandle _convertPin;
        private bool _convertPinned;

        public HikvisionCameraDevice(CameraDescriptor descriptor) : base(descriptor) { }

        // ==================================================================
        //  差异点实现（基类骨架要的四件事）
        // ==================================================================

        /// <summary>枚举（GigE + USB3）→ 按序列号对上方案里配置的那台 → 创建并打开设备</summary>
        protected override bool OpenCore()
        {
            var serial = (Descriptor.SerialNo ?? string.Empty).Trim();
            if (serial.Length == 0)
                throw new InvalidOperationException("尚未配置相机序列号：请在「系统 → 相机设置」为本步骤选择一台海康相机");

            _sdk = MvsRuntime.TryLoad();
            if (_sdk == null)
                throw new InvalidOperationException(MissingSdkDetail(MvsRuntime.LoadError));

            lock (_sdkGate)
            {
                _camera = _sdk.CreateCamera();

                var devList = _sdk.NewStruct("MV_CC_DEVICE_INFO_LIST");
                int ret = _sdk.Call(_camera, "MV_CC_EnumDevices_NET",
                    _sdk.Const("MV_GIGE_DEVICE") | _sdk.Const("MV_USB_DEVICE"), devList);
                if (ret != 0)
                    throw new InvalidOperationException($"枚举海康相机失败（错误码 0x{ret:X8}）");

                var deviceInfo = _sdk.FindDeviceBySerial(devList, serial);
                if (deviceInfo == null)
                    throw new InvalidOperationException(
                        $"未找到序列号为「{serial}」的海康相机（本次枚举 {_sdk.DeviceCount(devList)} 台）。"
                        + "请确认相机已接好、网络可达，且方案里的序列号与设备一致");

                ret = _sdk.Call(_camera, "MV_CC_CreateDevice_NET", deviceInfo);
                if (ret != 0)
                    throw new InvalidOperationException($"创建海康设备失败（错误码 0x{ret:X8}）");

                ret = _sdk.Call(_camera, "MV_CC_OpenDevice_NET");
                if (ret != 0)
                {
                    _sdk.Call(_camera, "MV_CC_DestroyDevice_NET");
                    throw new InvalidOperationException($"打开海康设备失败（错误码 0x{ret:X8}）");
                }

                // 连续采图（免触发）：本平台的节拍由流程控制，相机侧只管持续出图
                _sdk.Call(_camera, "MV_CC_SetEnumValue_NET", "TriggerMode", 0u);

                // 打开即应用方案里的曝光/增益（失败只记日志不阻断打开，参数可随后在相机设置里再调）
                try { ApplySettingsCore(ReadSettings()); }
                catch (Exception ex) { Log?.Warn($"[{Descriptor.Caption}] 打开后应用参数失败：{ex.Message}"); }

                return true;
            }
        }

        /// <summary>启动采流：SDK StartGrabbing + 拉起取图线程</summary>
        protected override bool StartStreamCore()
        {
            int ret = _sdk!.Call(_camera!, "MV_CC_StartGrabbing_NET");
            if (ret != 0)
                throw new InvalidOperationException($"启动采流失败（错误码 0x{ret:X8}）");

            _grabbing = true;
            _grabThread = new Thread(GrabLoop)
            {
                IsBackground = true,
                Name = $"HikGrab-{Descriptor.SerialNo}",
            };
            _grabThread.Start();
            return true;
        }

        /// <summary>停止采流：收线程 → SDK StopGrabbing</summary>
        protected override void StopStreamCore()
        {
            _grabbing = false;
            _grabThread?.Join(2000);
            _grabThread = null;

            _sdk?.Call(_camera!, "MV_CC_StopGrabbing_NET");
        }

        /// <summary>关闭设备：收线程 → StopGrabbing → Close → Destroy（每步都容忍失败）</summary>
        protected override void CloseCore()
        {
            _grabbing = false;
            _grabThread?.Join(2000);
            _grabThread = null;

            UnpinConvertBuffer();

            lock (_sdkGate)
            {
                if (_camera != null && _sdk != null)
                {
                    _sdk.Call(_camera, "MV_CC_StopGrabbing_NET");
                    _sdk.Call(_camera, "MV_CC_CloseDevice_NET");
                    _sdk.Call(_camera, "MV_CC_DestroyDevice_NET");
                    _camera = null;
                }
            }
        }

        /// <summary>应用采集参数：曝光（μs）/ 增益 —— 节点名与 MVS 客户端一致</summary>
        protected override bool ApplySettingsCore(CameraSettings settings)
        {
            int ret = _sdk!.Call(_camera!, "MV_CC_SetFloatValue_NET", "ExposureTime", (float)settings.ExposureTimeUs);
            if (ret != 0)
                throw new InvalidOperationException($"设置曝光失败（ExposureTime，错误码 0x{ret:X8}）");

            ret = _sdk.Call(_camera!, "MV_CC_SetFloatValue_NET", "Gain", (float)settings.Gain);
            if (ret != 0)
                throw new InvalidOperationException($"设置增益失败（Gain，错误码 0x{ret:X8}）");

            return true;
        }

        /// <summary>真机有 SDK 掉线回调兜底，不需要心跳轮询推断</summary>
        protected override bool RequiresHeartbeat => false;

        // ==================================================================
        //  取图线程：GetImageBuffer → 像素格式转换 → PushFrame → FreeImageBuffer
        // ==================================================================

        private void GrabLoop()
        {
            var sdk = _sdk!;
            var camera = _camera!;
            var frameOut = sdk.NewStruct("MV_FRAME_OUT");

            var mono8 = sdk.PixelType("PixelType_Gvsp_Mono8");
            var rgb8 = sdk.PixelType("PixelType_Gvsp_RGB8_Packed");

            while (_grabbing)
            {
                // 100ms 一轮：停止标志最多滞后一个超时周期生效
                int ret = sdk.Call(camera, "MV_CC_GetImageBuffer_NET", frameOut, 100);
                if (ret != 0)
                    continue;   // 超时（没来帧）或取图失败：回头先看停止标志，再继续等

                try
                {
                    int width = sdk.IntOf(sdk.GetField(frameOut, "nWidth"));
                    int height = sdk.IntOf(sdk.GetField(frameOut, "nHeight"));
                    var srcData = sdk.GetField(frameOut, "pBufData");
                    int srcLen = sdk.IntOf(sdk.GetField(frameOut, "nBufLen"));
                    var pixelName = sdk.PixelTypeName(sdk.GetField(frameOut, "enPixelType"));

                    var kind = ClassifyPixelFormat(pixelName);
                    if (kind == "Unknown")
                    {
                        Log?.Warn($"[{Descriptor.Caption}] 暂不支持的像素格式 {pixelName}，该帧被丢弃");
                        continue;
                    }

                    int channels = kind == "Mono" ? 1 : 3;
                    var dstPixel = kind == "Mono" ? mono8 : rgb8;
                    int need = width * height * channels;
                    EnsureConvertBuffer(need);

                    // SDK 出来的帧统一转成 Mono8 / RGB8_Packed（Bayer/YUV 都由它消化），
                    // 转换目标就是这片钉住的托管缓冲，转完立刻拷成独立 byte[] 再入队
                    var convert = sdk.NewStruct("MV_PIXEL_CONVERT_PARAM");
                    sdk.SetField(convert, "nWidth", width);
                    sdk.SetField(convert, "nHeight", height);
                    sdk.SetField(convert, "pSrcData", srcData);
                    sdk.SetField(convert, "nSrcDataLen", srcLen);
                    sdk.SetField(convert, "enSrcPixelType", sdk.GetField(frameOut, "enPixelType"));
                    sdk.SetField(convert, "enDstPixelType", dstPixel);
                    sdk.SetField(convert, "pDstBuffer", _convertPin.AddrOfPinnedObject());
                    sdk.SetField(convert, "nDstBufferSize", _convertBuffer.Length);

                    ret = sdk.Call(camera, "MV_CC_ConvertPixelType_NET", convert);
                    if (ret != 0)
                    {
                        Log?.Warn($"[{Descriptor.Caption}] 像素格式转换失败（{pixelName}，错误码 0x{ret:X8}），该帧被丢弃");
                        continue;
                    }

                    var data = new byte[need];
                    Marshal.Copy(_convertPin.AddrOfPinnedObject(), data, 0, need);

                    // PushFrame 内部会校验、回填身份、入队并唤醒等待者（基类负责溢出丢弃计数）
                    PushFrame(new CameraFrame
                    {
                        PixelData = data,
                        Width = width,
                        Height = height,
                        Channels = channels,
                        SourceName = $"hikvision://{Descriptor.SerialNo}",
                    }, out _);
                }
                catch (Exception ex)
                {
                    // 单帧解算失败绝不能带垮取图线程：记日志、下一帧再来
                    Log?.Warn($"[{Descriptor.Caption}] 取图解算异常：{ex.Message}");
                }
                finally
                {
                    sdk.Call(camera, "MV_CC_FreeImageBuffer_NET", frameOut);
                }
            }
        }

        /// <summary>像素格式分类（按 GenICam 命名约定；internal 供断言直接验）</summary>
        internal static string ClassifyPixelFormat(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "Unknown";
            if (typeName.Contains("Mono", StringComparison.OrdinalIgnoreCase)) return "Mono";
            if (typeName.Contains("Bayer", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("RGB", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("BGR", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("YUV", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("YCbCr", StringComparison.OrdinalIgnoreCase))
                return "Color";
            return "Unknown";
        }

        /// <summary>按需扩容转换缓冲并保持钉住（只有取图线程会碰它，无需加锁）</summary>
        private void EnsureConvertBuffer(int need)
        {
            if (_convertBuffer.Length >= need && _convertPinned) return;

            UnpinConvertBuffer();
            _convertBuffer = new byte[need];
            _convertPin = GCHandle.Alloc(_convertBuffer, GCHandleType.Pinned);
            _convertPinned = true;
        }

        private void UnpinConvertBuffer()
        {
            if (_convertPinned)
            {
                _convertPin.Free();
                _convertPinned = false;
            }
            _convertBuffer = Array.Empty<byte>();
        }

        /// <summary>SDK 缺位时的中文指引（去哪装、放哪）</summary>
        private static string MissingSdkDetail(string loadError)
            => "未找到海康 MVS 运行时（MvCameraControl.Net.dll）。请安装海康 MVS 客户端，"
             + "并把安装目录 Development\\DotNet\\ 下的 MvCameraControl.Net.dll 复制到本程序的 Modules 目录后重启软件。"
             + (string.IsNullOrEmpty(loadError) ? "" : $"（探测记录：{loadError}）");
    }
}
