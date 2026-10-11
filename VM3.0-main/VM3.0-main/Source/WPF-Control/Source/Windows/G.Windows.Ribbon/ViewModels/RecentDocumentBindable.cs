// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class RecentDocumentBindable : ToggleButtonBindable
    {
        public int Index
        {
            get
            {
                return _index;
            }

            set
            {
                if (_index != value)
                {
                    _index = value;
                    RaisePropertyChanged();
                }
            }
        }
        private int _index;
    }
}

