// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    [Flags]
    public enum AllowedSpecialValues
    {
        None = 0,
        NaN = 1,
        PositiveInfinity = 2,
        NegativeInfinity = 4,
        AnyInfinity = PositiveInfinity | NegativeInfinity,
        Any = NaN | AnyInfinity
    }
}

