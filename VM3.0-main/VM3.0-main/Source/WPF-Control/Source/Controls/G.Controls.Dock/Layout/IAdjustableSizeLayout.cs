








using System.Windows;

namespace G.Controls.Dock.Layout
{
    public interface IAdjustableSizeLayout
    {
        void AdjustFixedChildrenPanelSizes(Size? parentSize = null);
    }
}