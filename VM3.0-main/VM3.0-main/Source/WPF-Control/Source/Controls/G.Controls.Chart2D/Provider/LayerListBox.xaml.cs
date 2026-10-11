// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows;
using System.Windows.Controls;

namespace G.Controls.Chart2D
{
    /// <summary> 曲线视图 </summary>
    public class LayerListBox : ListBox
    {
        public static ComponentResourceKey DefaultKey => new ComponentResourceKey(typeof(LayerListBox), "S.LayerListBox.Default");
        public static ComponentResourceKey PointKey => new ComponentResourceKey(typeof(LayerListBox), "S.LayerListBox.Point.Default");

        static LayerListBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LayerListBox), new FrameworkPropertyMetadata(typeof(LayerListBox)));
        }
    }
}

