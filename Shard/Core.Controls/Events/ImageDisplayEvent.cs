

namespace Core.Events
{
    /// <summary>
    /// 插件 → 主程序视图的图像显示事件
    /// 插件通过 GlobalEventBus.PublishOnUIThread 发布（自动切 UI 线程），
    /// ImageView 订阅后显示到 ViewIndex 对应的 ImageReadOnly 控件
    /// 生命周期约定（边界处拷贝，所有权单一）：
    /// Image 是发布方的原图引用，订阅方必须 CopyImage 后显示自己的副本，不得直接持有
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
    }
}
