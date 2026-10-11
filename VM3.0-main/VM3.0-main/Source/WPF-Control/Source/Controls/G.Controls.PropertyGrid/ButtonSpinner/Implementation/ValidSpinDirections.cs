// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    /// <summary>
    /// Represents spin directions that are valid.
    /// </summary>
    [Flags]
    public enum ValidSpinDirections
    {
        /// <summary>
        /// Can not increase nor decrease.
        /// </summary>
        None = 0,

        /// <summary>
        /// Can increase.
        /// </summary>
        Increase = 1,

        /// <summary>
        /// Can decrease.
        /// </summary>
        Decrease = 2
    }
}

