using Microsoft.Xaml.Behaviors;

namespace G.Controls.ZoomBox.Extension
{
    public class ZoomBoxFitOnSizeChangedBehavior : Behavior<Zoombox>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            this.AssociatedObject.SizeChanged += this.AssociatedObject_SizeChanged;
        }
        private void AssociatedObject_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            this.AssociatedObject.FitToBounds();
        }
        protected override void OnDetaching()
        {
            base.OnDetaching();
            this.AssociatedObject.SizeChanged -= this.AssociatedObject_SizeChanged;
        }
    }
}