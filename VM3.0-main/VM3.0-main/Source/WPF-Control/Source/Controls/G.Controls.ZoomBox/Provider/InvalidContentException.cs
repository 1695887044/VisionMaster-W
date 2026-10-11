
using System;

namespace G.Controls.ZoomBox
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
