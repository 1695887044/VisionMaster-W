








using System;

namespace G.Controls.Dock.Themes
{
    /// <inheritdoc/>
    public class GenericTheme : Theme
    {
        /// <inheritdoc/>
        public override Uri GetResourceUri()
        {
            return new Uri("/G.Controls.Dock;component/Themes/Generic.xaml", UriKind.Relative);
        }
    }
}