// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class ApplicationMenuItemBindable : MenuItemBindable
    {
        public ApplicationMenuItemBindable()
            : this(false)
        {
        }

        public ApplicationMenuItemBindable(bool isApplicationMenu)
            : base(isApplicationMenu)
        {
        }
    }
}

