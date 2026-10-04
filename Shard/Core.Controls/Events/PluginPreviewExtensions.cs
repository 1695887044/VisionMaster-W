using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;

namespace Core.Events
{
    /// <summary>
    /// 插件图像预览发布辅助（显示标准化）
    /// 一行代码把图像发布到主界面指定视图窗口，替代手写 GlobalEventBus + ImageDisplayEvent 的 5 行样板
    /// 用法：this.PublishPreview(PreviewImage, DisplayViewIndex);
    /// ★ viewIndex 就是界面上的窗口号（1~9），直传即可，**不要 +1**：
    ///   消费端按"ViewIndex == 窗口号"等值筛选（见 ImageView 的九宫格）；
    ///   历史上这里写过 +1 的示例，被部分插件照抄，导致"选窗口1图进第2格"的错位。
    ///
    /// ⚠ 图像所有权：Image 只是发布方的原图引用，**发布方的下一轮就会释放它**。
    /// 需要留存帧的宿主通过 <see cref="IImagePreviewCapture"/>（<see cref="ImagePreviewCaptureHub"/>）
    /// 在发布线程上先同步拷走——本方法在派发事件前会调用该钩子。
    /// </summary>
    public static class PluginPreviewExtensions
    {
        /// <summary>
        /// 发布图像预览到主界面指定视图窗口
        /// </summary>
        /// <param name="plugin">插件实例（扩展方法接收者）</param>
        /// <param name="image">要显示的图像</param>
        /// <param name="viewIndex">目标视图窗口索引（1~9）</param>
        public static void PublishPreview(this IVisionPlugin plugin, HImage image, int viewIndex)
            => PublishPreview(plugin, image, viewIndex, null, null, null);

        /// <summary>
        /// 发布图像预览 + 测量标注到主界面指定视图窗口（标注与图像同帧渲染）
        /// </summary>
        /// <param name="plugin">插件实例（扩展方法接收者）</param>
        /// <param name="image">要显示的图像</param>
        /// <param name="viewIndex">目标视图窗口索引（1~9）</param>
        /// <param name="annotations">测量标注（线段/角度/文本）</param>
        public static void PublishPreview(this IVisionPlugin plugin, HImage image, int viewIndex, IEnumerable<MeasureAnnotation> annotations)
            => PublishPreview(plugin, image, viewIndex, null, null, annotations);

        /// <summary>
        /// 发布图像预览 + 标题 + 键值信息（画布列表按这些信息显示这张图）。
        /// 用法：this.PublishPreview(img, "缺陷检测结果", new("缺陷数", 51), new("判定", "NG"));
        /// </summary>
        /// <param name="plugin">插件实例（扩展方法接收者）</param>
        /// <param name="image">要显示的图像</param>
        /// <param name="title">标题（画布列表主标题）</param>
        /// <param name="info">随图携带的键值信息（可空）</param>
        public static void PublishPreview(this IVisionPlugin plugin, HImage image, string title, params ImageInfoRow[] info)
            => PublishPreview(plugin, image, 1, title, info, null);

        /// <summary>
        /// 发布图像预览 + 标题 + 键值信息 + 标注（完整版；标注与图像同帧渲染）
        /// </summary>
        /// <param name="plugin">插件实例（扩展方法接收者）</param>
        /// <param name="image">要显示的图像</param>
        /// <param name="viewIndex">目标视图窗口索引（画布列表模式下不参与显示，仅保留给宫格模式）</param>
        /// <param name="title">标题（画布列表主标题；可空）</param>
        /// <param name="info">随图携带的键值信息（可空）</param>
        /// <param name="annotations">测量标注（可空）</param>
        public static void PublishPreview(
            this IVisionPlugin plugin,
            HImage image,
            int viewIndex,
            string? title,
            IEnumerable<ImageInfoRow>? info,
            IEnumerable<MeasureAnnotation>? annotations)
        {
            var e = new ImageDisplayEvent<HImage>
            {
                ViewIndex = viewIndex,
                Image = image,
                PluginName = plugin?.InstanceName,
                Title = title,
                InfoRows = info?.ToList(),
                Annotations = annotations?.ToList()
            };

            // 先让"要留存帧"的宿主在**发布线程**上同步拷走（见 IImagePreviewCapture 的说明：
            // 这张图活不过发布方的下一轮，异步到 UI 线程再拷就晚了）；
            // 随后照旧派发事件——未被捕获时空订阅者就是 no-op，其它订阅者也不受捕获影响。
            ImagePreviewCaptureHub.Current?.Capture(e);

            GlobalEventBus.PublishOnUIThread(e);
        }
    }
}
