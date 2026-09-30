using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Plugin.Camera.Hikvision
{
    /// <summary>
    /// 海康 MVS .NET 运行时（MvCamCtrl.NET / MvCameraControl.Net.dll）的反射绑定。
    ///
    /// 为什么用反射而不是直接引用 SDK
    /// ---------
    /// MvCameraControl.Net.dll 是海康 MVS 客户端安装包里的托管包装器，仓库里没有、也不该替用户
    /// 分发（版本要与相机固件/MVS 对齐）。编译期不依赖它，本插件在任何机器上都能构建；
    /// 打开设备时再探测：装了 MVS 就绑定使用，没装则由驱动给出"去哪装、放哪"的中文指引。
    /// 绑定面收敛在本类一处 —— SDK 换版本只需要改这一个文件。
    ///
    /// 绑定的成员（均为 MvCamCtrl.NET.MyCamera 的公开成员，名字与参考实现逐一对应）
    /// ---------
    ///   常量 : MV_GIGE_DEVICE / MV_USB_DEVICE（传输层接口类型）
    ///   枚举 : MvGvspPixelType
    ///   方法 : MV_CC_EnumDevices_NET / MV_CC_CreateDevice_NET / MV_CC_OpenDevice_NET /
    ///          MV_CC_CloseDevice_NET / MV_CC_DestroyDevice_NET / MV_CC_StartGrabbing_NET /
    ///          MV_CC_StopGrabbing_NET / MV_CC_GetImageBuffer_NET / MV_CC_FreeImageBuffer_NET /
    ///          MV_CC_ConvertPixelType_NET / MV_CC_SetFloatValue_NET / MV_CC_SetEnumValue_NET
    ///   结构 : MV_CC_DEVICE_INFO_LIST / MV_CC_DEVICE_INFO / MV_GIGE_DEVICE_INFO /
    ///          MV_USB3_DEVICE_INFO / MV_FRAME_OUT / MV_FRAME_OUT_INFO_EX / MV_PIXEL_CONVERT_PARAM
    /// </summary>
    internal sealed class MvsRuntime
    {
        private static readonly Lazy<MvsRuntime?> _instance =
            new(TryLoadCore, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>探测并绑定 MVS 运行时；SDK 不在位时返回 null（原因看 <see cref="LoadError"/>）</summary>
        public static MvsRuntime? TryLoad() => _instance.Value;

        /// <summary>最后一次探测失败的说明（SDK 在位时为 null）</summary>
        public static string LoadError { get; private set; } = string.Empty;

        private readonly Assembly _assembly;
        private readonly Type _cameraType;
        private readonly Dictionary<string, MethodInfo> _methods = new();
        private readonly Dictionary<string, Type> _types = new();

        private MvsRuntime(Assembly assembly, Type cameraType)
        {
            _assembly = assembly;
            _cameraType = cameraType;
        }

        private static MvsRuntime? TryLoadCore()
        {
            var errors = new List<string>();

            // 已加载过的直接用（宿主可能先于我们装进默认 ALC）
            try
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MvCameraControl.Net");
                if (loaded != null)
                    return Bind(loaded, errors);
            }
            catch (Exception ex) { errors.Add($"已加载程序集探测失败: {ex.Message}"); }

            foreach (var path in CandidatePaths())
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var asm = Assembly.LoadFrom(path);
                    return Bind(asm, errors);
                }
                catch (Exception ex)
                {
                    errors.Add($"{path}: {ex.Message}");
                }
            }

            LoadError = string.Join("；", errors.DefaultIfEmpty("未找到 MvCameraControl.Net.dll"));
            return null;
        }

        private static MvsRuntime Bind(Assembly asm, List<string> errors)
        {
            var cameraType = asm.GetType("MvCamCtrl.NET.MyCamera");
            if (cameraType == null)
            {
                errors.Add($"{asm.Location}: 没有 MvCamCtrl.NET.MyCamera 类型（不是海康 .NET 包装器？）");
                return null;
            }
            return new MvsRuntime(asm, cameraType);
        }

        /// <summary>SDK 探测路径：程序目录 → Modules → 逐级向上的 Modules → MVS 安装目录（环境变量）</summary>
        private static IEnumerable<string> CandidatePaths()
        {
            var baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, "MvCameraControl.Net.dll");
            yield return Path.Combine(baseDir, "Modules", "MvCameraControl.Net.dll");

            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                yield return Path.Combine(dir.FullName, "Modules", "MvCameraControl.Net.dll");

            // MVS 安装器会写 MVCAM_SDK_PATH，指向安装根目录
            var sdkRoot = Environment.GetEnvironmentVariable("MVCAM_SDK_PATH");
            if (!string.IsNullOrWhiteSpace(sdkRoot))
                yield return Path.Combine(sdkRoot, "Development", "DotNet", "MvCameraControl.Net.dll");
        }

        /// <summary>new 一个 MyCamera 实例（包装器把设备句柄收在实例内部，后续调用都走它）</summary>
        public object CreateCamera() => Activator.CreateInstance(_cameraType)
            ?? throw new InvalidOperationException("无法创建 MvCamCtrl.NET.MyCamera 实例");

        /// <summary>读 SDK 常量（MV_GIGE_DEVICE / MV_USB_DEVICE 等）</summary>
        public uint Const(string name)
        {
            var field = _cameraType.GetField(name)
                ?? throw new MissingFieldException($"MVS SDK 缺少常量 {name} —— MvCameraControl.Net.dll 版本不匹配");
            return Convert.ToUInt32(field.GetValue(null));
        }

        /// <summary>取嵌套类型（MV_CC_DEVICE_INFO / MV_FRAME_OUT 等）</summary>
        public Type Nested(string name)
        {
            var key = "T:" + name;
            if (!_types.TryGetValue(key, out var type))
            {
                type = _cameraType.GetNestedType(name)
                    ?? throw new MissingMemberException($"MVS SDK 缺少类型 MyCamera.{name} —— MvCameraControl.Net.dll 版本不匹配");
                _types[key] = type;
            }
            return type;
        }

        /// <summary>调用 MyCamera 的方法（返回值约定为 int 错误码；ref 结构体参数在原地更新）</summary>
        public int Call(object camera, string name, params object[] args)
        {
            var key = name + "#" + args.Length;
            if (!_methods.TryGetValue(key, out var method))
            {
                method = _cameraType.GetMethods().FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length)
                    ?? throw new MissingMethodException($"MVS SDK 缺少方法 {name}（{args.Length} 参数）—— MvCameraControl.Net.dll 版本不匹配");
                _methods[key] = method;
            }

            var result = method.Invoke(camera, args);
            return result is int code ? code : -1;
        }

        /// <summary>new 一个 SDK 结构体</summary>
        public object NewStruct(string name) => Activator.CreateInstance(Nested(name))
            ?? throw new InvalidOperationException($"无法创建 MyCamera.{name} 实例");

        /// <summary>读 SDK 结构体的字段（struct 装箱语义：只读用）</summary>
        public object GetField(object box, string name)
            => FieldOf(box.GetType(), name).GetValue(box);

        /// <summary>写 SDK 结构体的字段（struct 装箱语义：改完必须写回原槽位）</summary>
        public void SetField(object box, string name, object value)
            => FieldOf(box.GetType(), name).SetValue(box, value);

        /// <summary>结构体字段转 int（字段可能是 ushort/uint/int/枚举，统一兜）</summary>
        public int IntOf(object value) => Convert.ToInt32(value);

        /// <summary>枚举值（按名字），如 PixelType_Gvsp_Mono8</summary>
        public object PixelType(string name) => Enum.Parse(Nested("MvGvspPixelType"), name);

        /// <summary>枚举成员的名字（用于像素格式分类）</summary>
        public string PixelTypeName(object value) => Enum.GetName(Nested("MvGvspPixelType"), value) ?? string.Empty;

        /// <summary>设备列表里的台数（MV_CC_DEVICE_INFO_LIST.nDeviceNum）</summary>
        public uint DeviceCount(object devList) => Convert.ToUInt32(GetField(devList, "nDeviceNum"));

        /// <summary>
        /// 在枚举结果里按序列号找设备。命中返回该设备的 MV_CC_DEVICE_INFO（装箱结构体，
        /// 可直接喂给 MV_CC_CreateDevice_NET）；没找到返回 null。
        /// </summary>
        public object? FindDeviceBySerial(object devList, string serial)
        {
            var infoType = Nested("MV_CC_DEVICE_INFO");
            uint count = DeviceCount(devList);
            var raw = GetField(devList, "pDeviceInfo");

            for (uint i = 0; i < count; i++)
            {
                IntPtr entryPtr;
                if (raw is IntPtr[] entries)
                {
                    if (i >= (uint)entries.Length) break;
                    entryPtr = entries[i];
                }
                else if (raw is IntPtr basePtr)
                {
                    entryPtr = Marshal.ReadIntPtr(basePtr, (int)i * IntPtr.Size);
                }
                else
                {
                    break;   // 未知形态：交由上层按"未找到"处理
                }

                var box = Marshal.PtrToStructure(entryPtr, infoType);
                if (box == null) continue;

                uint layerType = Convert.ToUInt32(GetField(box, "nTLayerType"));
                string? sn = null;

                if (layerType == Const("MV_GIGE_DEVICE"))
                {
                    sn = TextFieldOfNestedStruct(GetField(box, "SpecialInfo"), "stGigEInfo", "MV_GIGE_DEVICE_INFO", "chSerialNumber");
                }
                else if (layerType == Const("MV_USB_DEVICE"))
                {
                    sn = TextFieldOfNestedStruct(GetField(box, "SpecialInfo"), "stUsb3VInfo", "MV_USB3_DEVICE_INFO", "chSerialNumber");
                }

                if (sn != null && string.Equals(sn.Trim(), serial.Trim(), StringComparison.OrdinalIgnoreCase))
                    return box;
            }

            return null;
        }

        /// <summary>
        /// 从"内嵌字节数组形式的子结构"里读一个文本字段。
        /// 包装器把 GigE/USB3 的设备信息存成 byte[]（SpecialInfo.stGigEInfo 等），
        /// 要按子结构布局解一遍才能拿到序列号 / 用户名。
        /// </summary>
        private string? TextFieldOfNestedStruct(object special, string arrayField, string structName, string textField)
        {
            if (special == null) return null;

            var bytes = GetField(special, arrayField) as byte[];
            if (bytes == null || bytes.Length == 0) return null;

            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var sub = Marshal.PtrToStructure(handle.AddrOfPinnedObject(), Nested(structName));
                if (sub == null) return null;
                return TextField(sub, textField);
            }
            finally { handle.Free(); }
        }

        /// <summary>结构体的文本字段：包装器不同版本是 byte[] 或 string，两种都兜住</summary>
        public string? TextField(object box, string name)
        {
            var value = FieldOf(box.GetType(), name).GetValue(box);
            return value switch
            {
                byte[] bytes => DecodeAscii(bytes),
                string s => s,
                null => null,
                _ => value.ToString(),
            };
        }

        private static string DecodeAscii(byte[] bytes)
        {
            int end = Array.IndexOf(bytes, (byte)0);
            return end < 0
                ? System.Text.Encoding.ASCII.GetString(bytes)
                : System.Text.Encoding.ASCII.GetString(bytes, 0, end);
        }

        private FieldInfo FieldOf(Type type, string name)
            => type.GetField(name)
                ?? throw new MissingFieldException($"MVS SDK 缺少字段 {type.Name}.{name} —— MvCameraControl.Net.dll 版本不匹配");
    }
}
