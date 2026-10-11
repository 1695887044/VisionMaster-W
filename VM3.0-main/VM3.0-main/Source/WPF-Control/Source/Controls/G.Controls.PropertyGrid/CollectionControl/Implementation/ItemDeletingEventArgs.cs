// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Windows;

namespace G.Controls.PropertyGrid
{
    public class ItemDeletingEventArgs : CancelRoutedEventArgs
    {
        #region Private Members

        private object _item;

        #endregion

        #region Constructor

        public ItemDeletingEventArgs(RoutedEvent itemDeletingEvent, object itemDeleting)
          : base(itemDeletingEvent)
        {
            _item = itemDeleting;
        }

        #region Property Item

        public object Item
        {
            get
            {
                return _item;
            }
        }

        #endregion

        #endregion
    }
}

