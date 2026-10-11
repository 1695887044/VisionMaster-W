// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    internal class UShortUpDown : CommonNumericUpDown<ushort>
    {
        #region Constructors

        static UShortUpDown()
        {
            UpdateMetadata(typeof(UShortUpDown), 1, ushort.MinValue, ushort.MaxValue);
        }

        public UShortUpDown()
          : base(ushort.TryParse, Decimal.ToUInt16, (v1, v2) => v1 < v2, (v1, v2) => v1 > v2)
        {
        }

        #endregion //Constructors

        #region Base Class Overrides

        protected override ushort IncrementValue(ushort value, ushort increment)
        {
            return (ushort)(value + increment);
        }

        protected override ushort DecrementValue(ushort value, ushort increment)
        {
            return (ushort)(value - increment);
        }

        #endregion //Base Class Overrides
    }
}

