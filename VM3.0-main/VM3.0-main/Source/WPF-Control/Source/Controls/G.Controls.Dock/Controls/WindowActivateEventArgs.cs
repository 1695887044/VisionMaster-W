
using System;

namespace G.Controls.Dock.Controls
{
    internal class WindowActivateEventArgs : EventArgs
    {
        public WindowActivateEventArgs(IntPtr hwndActivating)
        {
            this.HwndActivating = hwndActivating;
        }

        public IntPtr HwndActivating { get; private set; }
    }
}