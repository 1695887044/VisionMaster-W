using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class IconAttribute : Attribute
    {
        public string IconCode { get; }
        public IconAttribute(string iconCode) { IconCode = iconCode; }
    }
}
