using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class VariableNode : BindableBase
    {
        public GlobalVariableModel OriginalModel { get; set; }
        public string Name { get; set; }
        public Type DataType { get; set; }
        public string TypeName { get; set; }
        public string Description { get; set; }

        // 数据展示相关属性
        public object RawValue { get; set; }
        public string DisplayValue
        {
            get
            {
                if (RawValue is Array arr) return $"Array [{arr.Length}]";
                return RawValue?.ToString() ?? "Null";
            }
        }

        // 数组子节点值属性
        public object ChildDefaultValue { get; set; }
        public object ChildValue { get; set; }

        public ObservableCollection<VariableNode> Children { get; } = new();
        public bool IsContainer => Children.Count > 0;
        public int Level { get; set; }

        // UI显隐控制属性
        public bool IsRootNode => Level == 0;
        public bool IsChildNode => Level == 1;
        public bool IsEditableNode => Level == 0 && DataType != null && !DataType.IsArray;
        public bool IsArrayRootNode => Level == 0 && DataType != null && DataType.IsArray;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
    }
}
