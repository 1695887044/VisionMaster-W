// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class TextBoxBindable : ControlBindableBase
    {
        public string Text
        {
            get
            {
                return _text;
            }

            set
            {
                if (_text != value)
                {
                    _text = value;
                    RaisePropertyChanged();
                }
            }
        }
        private string _text;
    }
}

