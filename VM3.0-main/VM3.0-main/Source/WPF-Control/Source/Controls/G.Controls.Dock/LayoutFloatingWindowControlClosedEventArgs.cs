using G.Controls.Dock.Controls;
using System;

namespace G.Controls.Dock
{
    public sealed class LayoutFloatingWindowControlClosedEventArgs : EventArgs
    {
        public LayoutFloatingWindowControlClosedEventArgs(LayoutFloatingWindowControl layoutFloatingWindowControl)
        {
            this.LayoutFloatingWindowControl = layoutFloatingWindowControl;
        }

        public LayoutFloatingWindowControl LayoutFloatingWindowControl { get; }
    }

}
