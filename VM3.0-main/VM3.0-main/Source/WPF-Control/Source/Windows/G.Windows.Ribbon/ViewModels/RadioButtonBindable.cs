// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class RadioButtonBindable : ControlBindableBase
    {
        public bool IsChecked
        {
            get
            {
                return _isChecked;
            }

            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    RaisePropertyChanged();
                }
            }
        }
        private bool _isChecked;
    }
}

