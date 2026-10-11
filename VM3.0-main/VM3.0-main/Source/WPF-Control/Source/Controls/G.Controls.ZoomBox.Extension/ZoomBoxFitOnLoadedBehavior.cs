using Microsoft.Xaml.Behaviors;

namespace G.Controls.ZoomBox.Extension
{
    public class ZoomBoxFitOnLoadedBehavior : Behavior<Zoombox>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            this.AssociatedObject.Loaded += this.AssociatedObject_Loaded;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            this.AssociatedObject.Loaded -= this.AssociatedObject_Loaded;
        }

        private void AssociatedObject_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            this.AssociatedObject.FitToBounds();
        }
    }
}