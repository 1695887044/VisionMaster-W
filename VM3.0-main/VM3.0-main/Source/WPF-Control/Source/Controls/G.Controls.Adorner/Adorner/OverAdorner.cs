namespace G.Controls.Adorner.Adorner;

public class OverAdorner : BorderAdorner
{
    public OverAdorner(UIElement adornedElement) : base(adornedElement)
    {
        this.Fill = new SolidColorBrush(Colors.LightSkyBlue) { Opacity = 0.1 };
        this.Pen = new Pen(new SolidColorBrush(Colors.SkyBlue), 1);
    }
}
