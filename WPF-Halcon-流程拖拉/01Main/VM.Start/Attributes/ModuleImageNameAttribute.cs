using System;
using System.ComponentModel;

namespace VM.Start.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ModuleImageNameAttribute : Attribute
    {
        public ModuleImageNameAttribute(string imageName) 
        {
            _imageName = imageName;
        }
        private string _imageName;

        public string ImageName
        {
            get { return _imageName; }
            set { _imageName = value; }
        }

    }

}
