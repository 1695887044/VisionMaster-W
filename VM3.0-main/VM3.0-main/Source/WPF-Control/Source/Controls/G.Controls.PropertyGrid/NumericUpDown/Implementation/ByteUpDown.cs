// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    public class ByteUpDown : CommonNumericUpDown<byte>
    {
        #region Constructors

        static ByteUpDown()
        {
            UpdateMetadata(typeof(ByteUpDown), 1, byte.MinValue, byte.MaxValue);
        }

        public ByteUpDown()
          : base(Byte.TryParse, Decimal.ToByte, (v1, v2) => v1 < v2, (v1, v2) => v1 > v2)
        {
        }

        #endregion //Constructors

        #region Base Class Overrides

        protected override byte IncrementValue(byte value, byte increment)
        {
            return (byte)(value + increment);
        }

        protected override byte DecrementValue(byte value, byte increment)
        {
            return (byte)(value - increment);
        }

        #endregion //Base Class Overrides
    }
}

