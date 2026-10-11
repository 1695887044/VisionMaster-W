// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    public class LongUpDown : CommonNumericUpDown<long>
    {
        #region Constructors

        static LongUpDown()
        {
            UpdateMetadata(typeof(LongUpDown), 1L, long.MinValue, long.MaxValue);
        }

        public LongUpDown()
          : base(Int64.TryParse, Decimal.ToInt64, (v1, v2) => v1 < v2, (v1, v2) => v1 > v2)
        {
        }

        #endregion //Constructors

        #region Base Class Overrides

        protected override long IncrementValue(long value, long increment)
        {
            return value + increment;
        }

        protected override long DecrementValue(long value, long increment)
        {
            return value - increment;
        }

        #endregion //Base Class Overrides
    }
}

