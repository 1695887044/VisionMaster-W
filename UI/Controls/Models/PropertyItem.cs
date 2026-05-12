
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace UI.Models
{
    public class PropertyItem : INotifyPropertyChanged
    {
        private readonly object _targetObject;
        public PropertyInfo Property { get; }

        public string DisplayName { get; set; }
        public string GroupName { get; set; }
        public string Description { get; set; }
        public bool IsReadOnly { get; set; }
        public Type PropertyType => Property.PropertyType;

        public BrowsePathAttribute BrowseAttribute { get; set; }

        public string IconCode { get; set; }
        public bool HasIcon => !string.IsNullOrEmpty(IconCode);

        // 核心：智能路由的 Key
        public string EditorKey { get; set; }

        public PropertyItem(object targetObject, PropertyInfo property)
        {
            _targetObject = targetObject;
            Property = property;

            var displayAttr = property.GetCustomAttribute<DisplayAttribute>();
            DisplayName = displayAttr?.Name ?? property.Name;
            GroupName = displayAttr?.GroupName ?? "基本参数";
            Description = displayAttr?.Description;

            var readOnlyAttr = property.GetCustomAttribute<ReadOnlyAttribute>();
            IsReadOnly = readOnlyAttr != null && readOnlyAttr.IsReadOnly;

            BrowseAttribute = property.GetCustomAttribute<BrowsePathAttribute>();

            var iconAttr = property.GetCustomAttribute<IconAttribute>();
            IconCode = iconAttr?.IconCode;

            DetermineEditorKey();
        }

        private void DetermineEditorKey()
        {
            if (BrowseAttribute != null) { EditorKey = "Editor_FileBrowse"; return; }
            if (typeof(ICommand).IsAssignableFrom(PropertyType)) { EditorKey = "Editor_Command"; return; }
            if (PropertyType == typeof(bool)) { EditorKey = "Editor_Boolean"; return; }
            if (PropertyType.IsEnum) { EditorKey = "Editor_Enum"; return; }

            EditorKey = "Editor_String";
        }

        public object Value
        {
            get => Property.GetValue(_targetObject);
            set
            {
                var currentValue = Property.GetValue(_targetObject);
                if (!Equals(currentValue, value))
                {
                    try
                    {
                        var convertedValue = value == null ? null : Convert.ChangeType(value, PropertyType);
                        Property.SetValue(_targetObject, convertedValue);
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                    }
                    catch { /* 转换失败静默处理 */ }
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
