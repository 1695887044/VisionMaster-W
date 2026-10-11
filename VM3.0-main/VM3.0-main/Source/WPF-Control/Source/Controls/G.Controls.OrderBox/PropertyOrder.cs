global using G.Mvvm.ViewModels.Base;

namespace G.Controls.OrderBox
{
    public class PropertyOrder : BindableBase, IProperty
    {
        public PropertyOrder()
        {

        }
        public PropertyOrder(PropertyInfo propertyInfo)
        {
            this.PropertyInfo = propertyInfo;
            this.PropertyName = propertyInfo.Name;
        }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                _isSelected = value;
                RaisePropertyChanged();
            }
        }

        private bool _useDesc;
        public bool UseDesc
        {
            get { return _useDesc; }
            set
            {
                _useDesc = value;
                RaisePropertyChanged();
            }
        }

        public string PropertyName { get; set; }
        private PropertyInfo _propertyInfo;
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Xml.Serialization.XmlIgnore]
        public PropertyInfo PropertyInfo
        {
            get { return _propertyInfo; }
            set
            {
                _propertyInfo = value;
                RaisePropertyChanged();
                this.PropertyName = value.Name;
            }
        }
    }
}
