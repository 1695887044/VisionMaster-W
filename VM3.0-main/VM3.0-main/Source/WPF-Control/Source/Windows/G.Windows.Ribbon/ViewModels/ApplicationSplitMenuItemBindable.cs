// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Windows.Ribbon
{
    public class ApplicationSplitMenuItemBindable : SplitMenuItemBindable
    {
        public ApplicationSplitMenuItemBindable()
            : this(false)
        {
        }

        public ApplicationSplitMenuItemBindable(bool isApplicationMenu)
            : base(isApplicationMenu)
        {
        }
    }
}

