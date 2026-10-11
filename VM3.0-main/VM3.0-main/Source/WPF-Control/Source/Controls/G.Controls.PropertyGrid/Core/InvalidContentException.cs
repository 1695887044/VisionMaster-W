// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    public class InvalidContentException : Exception
    {
        #region Constructors

        public InvalidContentException(string message)
          : base(message)
        {
        }

        public InvalidContentException(string message, Exception innerException)
          : base(message, innerException)
        {
        }

        #endregion
    }
}

