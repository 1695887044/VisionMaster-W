// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace G.Windows.Ribbon
{

    public class GalleryData<T> : ControlBindableBase
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ObservableCollection<GalleryCategoryBindable<T>> CategoryBindableCollection
        {
            get
            {
                if (_controlDataCollection == null)
                {
                    _controlDataCollection = new ObservableCollection<GalleryCategoryBindable<T>>();
                }
                return _controlDataCollection;
            }
        }
        private ObservableCollection<GalleryCategoryBindable<T>> _controlDataCollection;

        public T SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (!Object.Equals(value, _selectedItem))
                {
                    _selectedItem = value;
                    RaisePropertyChanged();
                }
            }
        }

        private T _selectedItem;

        public bool CanUserFilter
        {
            get
            {
                return _canUserFilter;
            }

            set
            {
                if (_canUserFilter != value)
                {
                    _canUserFilter = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool _canUserFilter;
    }
}

