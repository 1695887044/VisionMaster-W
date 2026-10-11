// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace G.Windows.Ribbon
{
    public class MenuButtonBindable : ControlBindableBase
    {
        public MenuButtonBindable()
            : this(false)
        {
        }

        public MenuButtonBindable(bool isApplicationMenu)
        {
            _isApplicationMenu = isApplicationMenu;
        }

        public bool IsVerticallyResizable
        {
            get
            {
                return _isVerticallyResizable;
            }

            set
            {
                if (_isVerticallyResizable != value)
                {
                    _isVerticallyResizable = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsHorizontallyResizable
        {
            get
            {
                return _isHorizontallyResizable;
            }

            set
            {
                if (_isHorizontallyResizable != value)
                {
                    _isHorizontallyResizable = value;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsDropDownOpen
        {
            get
            {
                return _isDropDownOpen;
            }

            set
            {
                if (_isDropDownOpen != value)
                {
                    _isDropDownOpen = value;
                    RaisePropertyChanged();
                }
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public ObservableCollection<ControlBindableBase> ControlDataCollection
        {
            get
            {
                if (_controlDataCollection == null)
                {
                    _controlDataCollection = new ObservableCollection<ControlBindableBase>();
                }
                return _controlDataCollection;
            }
        }
        private ObservableCollection<ControlBindableBase> _controlDataCollection;

        private bool _isVerticallyResizable, _isHorizontallyResizable;
        private bool _isApplicationMenu;
        private bool _isDropDownOpen;
    }
}

