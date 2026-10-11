// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Windows;

namespace G.Controls.PropertyGrid
{
    public class IndexChangedEventArgs : PropertyChangedEventArgs<int>
    {
        #region Constructors

        public IndexChangedEventArgs(RoutedEvent routedEvent, int oldIndex, int newIndex)
          : base(routedEvent, oldIndex, newIndex)
        {
        }

        #endregion

        protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        {
            ((IndexChangedEventHandler)genericHandler)(genericTarget, this);
        }
    }
}

