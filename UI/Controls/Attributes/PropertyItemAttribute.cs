using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class PropertyItemAttribute : Attribute
    {
        public PropertyItemAttribute(Type type)
        {
            Type = type;
        }
        public Type Type { get; private set; }
    }
}
