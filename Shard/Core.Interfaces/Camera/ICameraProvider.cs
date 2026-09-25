using System;
using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 相机仓库契约：宿主实现，插件通过 <see cref="IExecutionContext.Cameras"/> 拿到。
    ///
    /// 为什么必须走执行上下文递送
    /// ---------
    /// 插件 DLL 只引用 Core.Interfaces，物理上够不到宿主程序集里的 CameraProvider；
    /// 而插件是由 PluginService 以 Assembly.LoadFrom 装进默认 AssemblyLoadContext 的，
    /// 它引用的 Core.Interfaces 会命中宿主已加载的那一份（同标识 → 同一程序集实例）。
    /// 所以"能力"只能挂在执行上下文上传过去——与 Logger / GlobalVariables / ImageHub 完全同一范式。
    /// </summary>
    public interface ICameraProvider
    {
        /// <summary>当前方案已配置的相机（配置态；不含运行态队列/状态）</summary>
        IReadOnlyList<CameraDescriptor> Cameras { get; }

        /// <summary>按内部 Id 取运行态设备</summary>
        bool TryGetDevice(Guid cameraId, out ICameraDevice device);

        /// <summary>按序列号取运行态设备（收图服务按 URL 里的序列号路由时用）</summary>
        bool TryGetDeviceBySerial(string serialNo, out ICameraDevice device);
    }

    /// <summary>
    /// 空实现：没有工作区 / 未注册相机服务时的兜底（容器注册用的简化执行上下文走这条）。
    ///
    /// 为什么要有它而不是让属性为 null：让插件侧只判 <c>TryGet</c> 的返回值即可，
    /// 不必再为"这个能力可能不存在"写一层判空——与 IGlobalVariableWriter
    /// "非空但写入即失败并说明原因"同一口径。
    /// </summary>
    public sealed class NullCameraProvider : ICameraProvider
    {
        /// <summary>全局唯一实例（无状态，可安全共享）</summary>
        public static readonly NullCameraProvider Instance = new();

        private NullCameraProvider() { }

        /// <inheritdoc />
        public IReadOnlyList<CameraDescriptor> Cameras => Array.Empty<CameraDescriptor>();

        /// <inheritdoc />
        public bool TryGetDevice(Guid cameraId, out ICameraDevice device)
        {
            device = null!;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetDeviceBySerial(string serialNo, out ICameraDevice device)
        {
            device = null!;
            return false;
        }
    }
}
