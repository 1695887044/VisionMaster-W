
using Core.Halcon.Models;

namespace Core.Events
{
    /// <summary>
    /// 插件 → 主程序视图的图像显示事件
    /// 插件通过 GlobalEventBus.PublishOnUIThread 发布（自动切 UI 线程），
    /// ImageView 订阅后显示到 ViewIndex 对应的 ImageReadOnly 控件
    /// 生命周期约定（边界处拷贝，所有权单一）：
    /// Image 是发布方的原图引用，订阅方必须 CopyImage 后显示自己的副本，不得直接持有。
    /// ⚠ 这个引用**活不过发布方的下一轮**（基类会在下一轮开始时释放上一轮的输出图像），
    /// 所以异步订阅者（UI 线程回调）可能拿到已释放的图——需要留存帧的宿主请实现
    /// <see cref="IImagePreviewCapture"/>，在发布线程上同步拷贝（见 Captured）。
    /// </summary>
    public sealed class ImageDisplayEvent<T>
    {
        /// <summary>
        /// 目标控件索引（1~9）；&lt;=0 表示不显示
        /// </summary>
        public int ViewIndex { get; init; }

        /// <summary>
        /// 要显示的图像（发布方持有并管理原图）
        /// </summary>
        public T Image { get; init; }

        /// <summary>
        /// 测量标注层（线段/角度/文本），与图像同帧覆盖渲染；null 表示无标注
        /// </summary>
        public List<MeasureAnnotation> Annotations { get; init; }

        /// <summary>
        /// 标题（可选）。画布列表里显示这张图叫什么，例如"缺陷检测结果"。
        /// 为空时列表回退到"步骤.端口"。
        /// </summary>
        public string? Title { get; init; }

        /// <summary>
        /// 随图携带的键值信息（可选）。画布列表第二行按这些行显示；
        /// 为空表示只显示默认的流程/时间/尺寸。
        /// </summary>
        public IReadOnlyList<ImageInfoRow>? InfoRows { get; init; }

        /// <summary>
        /// 发布方（插件实例名，由 <c>PublishPreview</c> 自动填）。画布列表用它标注"这张图谁注入的"。
        /// </summary>
        public string? PluginName { get; init; }

        /// <summary>
        /// 已由发布线程上的捕获钩子（<see cref="IImagePreviewCapture"/>）拷贝留存。
        /// 采集侧据此跳过异步回调里的"再拷一次"——那时源图可能已被下一轮释放；
        /// 其它订阅者不受影响，照旧收到事件。
        /// </summary>
        public bool Captured { get; set; }
    }
}
