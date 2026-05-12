using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class SliderAttribute : Attribute
    {
        public double Min { get; set; } = 0;
        public double Max { get; set; } = 100;
        public SliderAttribute(double min, double max) { Min = min; Max = max; }
    }
}
