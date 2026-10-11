using System.Windows;
using System.Windows.Controls;

namespace G.Controls.TransitionBox
{
    public class TransitionBox : Control
    {
        static TransitionBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TransitionBox), new FrameworkPropertyMetadata(typeof(TransitionBox)));
        }
    }

}
