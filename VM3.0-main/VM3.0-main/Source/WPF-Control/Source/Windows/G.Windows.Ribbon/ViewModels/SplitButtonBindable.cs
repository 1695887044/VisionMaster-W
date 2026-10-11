// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class SplitButtonBindable : MenuButtonBindable
    {
        public SplitButtonBindable()
            : this(false)
        {
        }

        public SplitButtonBindable(bool isApplicationMenu)
            : base(isApplicationMenu)
        {
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get { return _isChecked; }
            set
            {
                _isChecked = value;
                RaisePropertyChanged();
            }
        }


        private bool _isCheckable;
        public bool IsCheckable
        {
            get { return _isCheckable; }
            set
            {
                _isCheckable = value;
                RaisePropertyChanged();
            }
        }

        private ButtonBindable _dropDownButtonBindable = new ButtonBindable();
        public ButtonBindable DropDownButtonBindable
        {
            get { return _dropDownButtonBindable; }
            set
            {
                _dropDownButtonBindable = value;
                RaisePropertyChanged();
            }
        }
    }
}

