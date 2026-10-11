namespace G.Controls.Adorner.Adorner;

public class ErrorAdorner : BorderAdorner
{
    public ErrorAdorner(UIElement adornedElement) : base(adornedElement)
    {
        this.Pen = new Pen(Brushes.Red, 1);
        this.ScaleLen = 3;
        this.Fill = new SolidColorBrush(Colors.Red) { Opacity = 0.5 };
    }
}
