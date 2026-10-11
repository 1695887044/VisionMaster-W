// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace G.Windows.Ribbon
{
    public class GalleryCategoryBindable<T> : ControlBindableBase
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ObservableCollection<T> GalleryItemDataCollection
        {
            get
            {
                if (_controlDataCollection == null)
                {
                    _controlDataCollection = new ObservableCollection<T>();
                }
                return _controlDataCollection;
            }
        }
        private ObservableCollection<T> _controlDataCollection;
    }
}

