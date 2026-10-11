using System.Windows.Input;

namespace G.Controls.ROIBox.State;
public interface IState
{
    void MouseDown(object sender, MouseButtonEventArgs e);
    void MouseLeave(object sender, MouseEventArgs e);
    void MouseMove(object sender, MouseEventArgs e);
    void MouseUp(object sender, MouseButtonEventArgs e);
}