// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class MenuItemBindable : SplitButtonBindable
    {
        public MenuItemBindable()
            : this(false)
        {
        }

        public MenuItemBindable(bool isApplicationMenu)
            : base(isApplicationMenu)
        {
        }
    }
}

