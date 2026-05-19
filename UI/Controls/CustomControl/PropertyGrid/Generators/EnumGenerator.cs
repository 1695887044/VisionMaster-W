using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    #region ====== 1. 枚举生成器 (带高性能缓存与描述提取) ======

    /// <summary>
    /// 高性能强类型键值对，避免 WPF 绑定匿名类引发的反射性能灾难
    /// </summary>
    public class EnumItemCache
    {
        public object Key { get; set; } = null!;
        public string Value { get; set; } = string.Empty;
    }

    public class EnumGenerator : IControlGenerator
    {
        public int Priority => 100;

        // 🌟 性能优化：全局枚举解析缓存，拒绝重复反射
        private static readonly Dictionary<Type, List<EnumItemCache>> _enumCache = new();

        public bool CanProcess(PropertyInfo prop, Type targetType, bool isReadOnly) => targetType.IsEnum;

        public FrameworkElement Create(PropertyInfo prop, object bindingSource, bool isReadOnly)
        {
            var comboBox = new ComboBox
            {
                IsEnabled = !isReadOnly,
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var enumType = prop.PropertyType;

            // 查缓存，没有则解析并提取 Description
            if (!_enumCache.TryGetValue(enumType, out var items))
            {
                items = new List<EnumItemCache>();
                foreach (var value in Enum.GetValues(enumType))
                {
                    // 🌟 方案扩展：优先读取 [Description] 特性用于 UI 展示
                    var fieldInfo = enumType.GetField(value.ToString()!);
                    var descAttr = fieldInfo?.GetCustomAttribute<DescriptionAttribute>();
                    string displayValue = descAttr != null ? descAttr.Description : value.ToString()!;

                    items.Add(new EnumItemCache { Key = value, Value = displayValue });
                }
                _enumCache[enumType] = items;
            }

            comboBox.ItemsSource = items;
            comboBox.DisplayMemberPath = "Value";
            comboBox.SelectedValuePath = "Key";

            var binding = new Binding(prop.Name) { Source = bindingSource, Mode = BindingMode.TwoWay };
            comboBox.SetBinding(Selector.SelectedValueProperty, binding);

            return comboBox;
        }
    }

    #endregion





}