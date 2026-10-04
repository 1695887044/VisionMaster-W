using HalconDotNet;

namespace Core.Events
{
    /// <summary>
    /// 预览帧的"发布时机"捕获钩子（可选实现，宿主注册）。
    ///
    /// 为什么需要它 —— 一句话：**图像的所有权活不过下一轮**。
    /// 插件发布的 HImage 通常就是它自己的输出端口值，而 <c>VisionPluginBase</c> 会在
    /// 下一轮开始时释放上一轮的输出（AutoDisposeRoundOutputs）。同时 PublishPreview 是
    /// 经事件总线 BeginInvoke 到 UI 线程派发的——UI 只要落后一轮（循环运行、大图、高节拍），
    /// 回调里再 CopyImage 拿到的就是已释放的图：
    ///   · 图已被完全释放 → IsInitialized() 为 false，静默丢弃（画布空白，无任何日志）；
    ///   · 释放进行到一半 → HALCON #4060 "access to deleted region in copy_image"。
    /// 现场表现就是"单次运行正常、循环一开图不会被收纳"。
    ///
    /// 所以需要留存帧的宿主必须在**图像还有效时**（= PublishPreview 调用返回之前）同步拷走。
    /// 本接口就是那个时机：<see cref="PluginPreviewExtensions.PublishPreview"/> 在派发事件前
    /// 先调一次 <see cref="ImagePreviewCaptureHub.Current"/>；实现方自行拷贝、自行管理副本，
    /// 并把事件的 Captured 置位，避免异步订阅者再收一次。
    /// </summary>
    public interface IImagePreviewCapture
    {
        /// <summary>
        /// 在**发布方线程**同步调用（不是 UI 线程）。
        /// 实现方需自行判空、自行拷贝；拷贝失败也必须置位 <c>e.Captured</c>（同一帧不重试）。
        /// </summary>
        void Capture(ImageDisplayEvent<HImage> e);
    }

    /// <summary>
    /// 捕获钩子的注册点。未注册（null）时 PublishPreview 走原路径：仅事件派发、不拷贝——
    /// 无宿主（纯测试进程）下行为与历史一致。
    /// </summary>
    public static class ImagePreviewCaptureHub
    {
        /// <summary>当前注册的捕获实现（宿主在启动时注册；一个进程一个，后注册者覆盖）</summary>
        public static IImagePreviewCapture? Current { get; set; }
    }
}
