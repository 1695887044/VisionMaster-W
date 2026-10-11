// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    public class InvalidTemplateException : Exception
    {
        #region Constructors

        public InvalidTemplateException(string message)
          : base(message)
        {
        }

        public InvalidTemplateException(string message, Exception innerException)
          : base(message, innerException)
        {
        }

        #endregion
    }
}

