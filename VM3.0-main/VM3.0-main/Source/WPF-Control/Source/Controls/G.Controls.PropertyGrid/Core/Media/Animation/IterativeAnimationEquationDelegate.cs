// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid.Media.Animation
{
    public delegate T IterativeAnimationEquationDelegate<T>(TimeSpan currentTime, T from, T to, TimeSpan duration);
}

