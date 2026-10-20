using System.Windows;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 视图是否以"嵌入页"方式显示（硬件管理外壳）。视图用它隐藏自己的标题栏与
    /// 底部关闭/确定按钮；独立弹窗路径不设置它，行为与原来完全一致。
    ///
    /// 用附加属性而不是给三个视图各加一个 DP：宿主只需要 SetIsEmbedded(view, true)，
    /// 子视图 XAML 用 RelativeSource 找到 UserControl 上的这个属性即可。
    /// </summary>
    public static class EmbeddedMode
    {
        public static readonly DependencyProperty IsEmbeddedProperty = DependencyProperty.RegisterAttached(
            "IsEmbedded",
            typeof(bool),
            typeof(EmbeddedMode),
            new PropertyMetadata(false));

        public static bool GetIsEmbedded(DependencyObject d) => (bool)d.GetValue(IsEmbeddedProperty);

        public static void SetIsEmbedded(DependencyObject d, bool value) => d.SetValue(IsEmbeddedProperty, value);
    }
}
