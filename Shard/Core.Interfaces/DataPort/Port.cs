using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public class Port<T> : IPort
    {
        public string Name { get; }

        public Type DataType => typeof(T);

        private T _typedValue;

        public T TypedValue
        {
            get => _typedValue;
            set => _typedValue = value;
        }
        public object Value
        {
            get => _typedValue;
            // 隐式装箱与显式拆箱
            set => _typedValue = (T)value;
        }

        public Port(string name, T defaultValue = default)
        {
            Name = name;
            _typedValue = defaultValue;
        }
        private string description;

        public string Description
        {
            get { return description; }
            set { description = value; }
        }

    }

}
