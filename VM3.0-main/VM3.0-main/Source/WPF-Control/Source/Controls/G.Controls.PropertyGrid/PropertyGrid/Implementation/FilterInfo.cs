// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    internal struct FilterInfo
    {
        public string InputString;
        public Predicate<object> Predicate;
    }
}

