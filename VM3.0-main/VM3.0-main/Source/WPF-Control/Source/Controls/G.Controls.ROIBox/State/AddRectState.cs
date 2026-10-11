using System.Windows.Input;

namespace G.Controls.ROIBox.State
{
    [Obsolete]
    public class AddRectState : IState
    {
        private readonly ROIBox _box;
        public AddRectState(ROIBox box)
        {
            this._box = box;
        }
        private Point? _mouseDown;
        public void MouseLeave(object sender, MouseEventArgs e)
        {
            this.Clear();
        }

        public void MouseUp(object sender, MouseButtonEventArgs e)
        {
            this.Clear();
        }

        public void MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;
            var point = e.GetPosition(sender as FrameworkElement);
            this.UpdateRect(point);
        }

        public void MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;
            this._mouseDown = e.GetPosition(sender as FrameworkElement);
        }

        void UpdateRect(Point to)
        {
            if (this._mouseDown == null)
                return;
            this._box.Rect = new Rect(this._mouseDown.Value, to);
        }

        void Clear()
        {
            this._mouseDown = null;
        }
    }
}
