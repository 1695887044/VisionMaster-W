// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class SplitMenuItemBindable : MenuItemBindable
    {
        public SplitMenuItemBindable()
            : this(false)
        {
        }

        public SplitMenuItemBindable(bool isApplicationMenu)
            : base(isApplicationMenu)
        {
        }
    }
}

