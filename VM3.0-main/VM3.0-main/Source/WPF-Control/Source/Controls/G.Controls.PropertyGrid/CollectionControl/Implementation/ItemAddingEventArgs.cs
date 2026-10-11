// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows;

namespace G.Controls.PropertyGrid
{
    public class ItemAddingEventArgs : CancelRoutedEventArgs
    {
        #region Constructor

        public ItemAddingEventArgs(RoutedEvent itemAddingEvent, object itemAdding)
          : base(itemAddingEvent)
        {
            this.Item = itemAdding;
        }

        #endregion

        #region Properties

        #region Item Property

        public object Item
        {
            get;
            set;
        }

        #endregion

        #endregion //Properties
    }
}

