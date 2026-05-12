using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace UI.CustomControl
{
    public class Popover : ContentControl
    {
        static Popover()
        {
            // 告诉 WPF 引擎：请去 Themes/Generic.xaml 里找我的默认样式
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Popover), new FrameworkPropertyMetadata(typeof(Popover)));
        }

        // 1. 气泡里面装什么内容
        public object FlyoutContent
        {
            get { return GetValue(FlyoutContentProperty); }
            set { SetValue(FlyoutContentProperty, value); }
        }
        public static readonly DependencyProperty FlyoutContentProperty =
            DependencyProperty.Register("FlyoutContent", typeof(object), typeof(Popover), new PropertyMetadata(null));

        // 2. 控制气泡是否打开 (支持双向绑定)
        public bool IsOpen
        {
            get { return (bool)GetValue(IsOpenProperty); }
            set { SetValue(IsOpenProperty, value); }
        }
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register("IsOpen", typeof(bool), typeof(Popover), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        // 3. 气泡弹出的方向 (默认在下方)
        public PlacementMode Placement
        {
            get { return (PlacementMode)GetValue(PlacementProperty); }
            set { SetValue(PlacementProperty, value); }
        }
        public static readonly DependencyProperty PlacementProperty =
            DependencyProperty.Register("Placement", typeof(PlacementMode), typeof(Popover), new PropertyMetadata(PlacementMode.Bottom));
    }
}
