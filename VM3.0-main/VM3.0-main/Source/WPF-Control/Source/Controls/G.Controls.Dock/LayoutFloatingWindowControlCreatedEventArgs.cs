using G.Controls.Dock.Controls;
using System;

namespace G.Controls.Dock
{
    public sealed class LayoutFloatingWindowControlCreatedEventArgs : EventArgs
    {
        public LayoutFloatingWindowControlCreatedEventArgs(LayoutFloatingWindowControl layoutFloatingWindowControl)
        {
            this.LayoutFloatingWindowControl = layoutFloatingWindowControl;
        }

        public LayoutFloatingWindowControl LayoutFloatingWindowControl { get; }
    }

}
