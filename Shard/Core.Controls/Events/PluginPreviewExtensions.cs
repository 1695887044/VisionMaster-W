using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;

namespace Core.Events
{
    /// <summary>
    /// 插件图像预览发布辅助（显示标准化）
    /// 一行代码把图像发布到主界面指定视图窗口，替代手写 GlobalEventBus + ImageDisplayEvent 的 5 行样板
    /// 用法：this.PublishPreview(PreviewImage, DisplayViewIndex + 1);
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
        {
            GlobalEventBus.PublishOnUIThread(new ImageDisplayEvent<HImage>
            {
                ViewIndex = viewIndex,
                Image = image
            });
        }

        /// <summary>
        /// 发布图像预览 + 测量标注到主界面指定视图窗口（标注与图像同帧渲染）
        /// </summary>
        /// <param name="plugin">插件实例（扩展方法接收者）</param>
        /// <param name="image">要显示的图像</param>
        /// <param name="viewIndex">目标视图窗口索引（1~9）</param>
        /// <param name="annotations">测量标注（线段/角度/文本）</param>
        public static void PublishPreview(this IVisionPlugin plugin, HImage image, int viewIndex, IEnumerable<MeasureAnnotation> annotations)
        {
            GlobalEventBus.PublishOnUIThread(new ImageDisplayEvent<HImage>
            {
                ViewIndex = viewIndex,
                Image = image,
                Annotations = annotations?.ToList()
            });
        }
    }
}
