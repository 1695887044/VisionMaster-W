// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

using System;

namespace G.Controls.PropertyGrid
{
    public class DecimalUpDown : CommonNumericUpDown<decimal>
    {
        #region Constructors

        static DecimalUpDown()
        {
            UpdateMetadata(typeof(DecimalUpDown), 1m, decimal.MinValue, decimal.MaxValue);
        }

        public DecimalUpDown()
          : base(Decimal.TryParse, (d) => d, (v1, v2) => v1 < v2, (v1, v2) => v1 > v2)
        {
        }

        #endregion //Constructors

        #region Base Class Overrides

        protected override decimal IncrementValue(decimal value, decimal increment)
        {
            return value + increment;
        }

        protected override decimal DecrementValue(decimal value, decimal increment)
        {
            return value - increment;
        }

        #endregion //Base Class Overrides
    }
}

