// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class GalleryBindable : ControlBindableBase
    {
        public GalleryItemBindable SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    RaisePropertyChanged();
                }
            }
        }

        private GalleryItemBindable _selectedItem;

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

