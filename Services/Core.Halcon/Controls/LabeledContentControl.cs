using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Core.Halcon.Controls
{
    [ContentProperty("Content")]
    public class LabeledContentControl:ContentControl
    {
        
        public string LeftText
        {
            get { return (string)GetValue(LeftTextProperty); }
            set { SetValue(LeftTextProperty, value); }
        }
        public static readonly DependencyProperty LeftTextProperty =
            DependencyProperty.Register("LeftText", typeof(string), typeof(LabeledContentControl), new PropertyMetadata(string.Empty));



        public string RightText
        {
            get { return (string)GetValue(RightTextProperty); }
            set { SetValue(RightTextProperty, value); }
        }

        public static readonly DependencyProperty RightTextProperty =
            DependencyProperty.Register("RightText", typeof(string), typeof(LabeledContentControl), new PropertyMetadata(string.Empty));




     





    }
}
