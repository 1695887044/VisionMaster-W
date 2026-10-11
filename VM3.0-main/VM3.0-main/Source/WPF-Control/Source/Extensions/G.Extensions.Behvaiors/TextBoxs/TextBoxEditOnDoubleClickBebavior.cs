using System.Xml.Serialization;

namespace G.Extensions.Behvaiors.TextBoxs;

[Obsolete("输入中文有输入法是会自动LostFocus，需要再测试一下")]
public class TextBoxEditOnDoubleClickBebavior : Behavior<TextBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        this.AssociatedObject.Focusable = false;
        this.AssociatedObject.LostFocus += this.AssociatedObject_LostFocus;
        this.AssociatedObject.MouseDown += this.AssociatedObject_MouseDown;
        this.AssociatedObject.MouseLeave += this.AssociatedObject_MouseLeave;
    }

    private void AssociatedObject_MouseLeave(object sender, MouseEventArgs e)
    {
        this.AssociatedObject.Focusable = false;
    }

    private void AssociatedObject_MouseDown(object sender, MouseButtonEventArgs e)
    {
        this.AssociatedObject.Focusable = true;
    }

    private void AssociatedObject_LostFocus(object sender, RoutedEventArgs e)
    {
        this.AssociatedObject.Focusable = false;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        this.AssociatedObject.LostFocus -= this.AssociatedObject_LostFocus;
        this.AssociatedObject.MouseDown -= this.AssociatedObject_MouseDown;
        this.AssociatedObject.MouseLeave -= this.AssociatedObject_MouseLeave;
    }
}
