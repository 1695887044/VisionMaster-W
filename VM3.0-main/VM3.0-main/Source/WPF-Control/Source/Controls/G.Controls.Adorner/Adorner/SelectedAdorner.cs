namespace G.Controls.Adorner.Adorner;

public class SelectedAdorner : BorderAdorner
{
    public SelectedAdorner(UIElement adornedElement) : base(adornedElement)
    {
        //this.Fill = new SolidColorBrush(Colors.Blue) { Opacity = 0.5 };
        this.Pen = new Pen(new SolidColorBrush(Colors.Orange), 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
        this.ScaleLen = 0;
    }
}
