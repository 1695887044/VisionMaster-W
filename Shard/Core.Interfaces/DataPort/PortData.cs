using Core.Interfaces.Core;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public class OutputPort<T> : ObservableObject, IOutputPort
    {
        public string Name { get; }
        public Type DataType => typeof(T);
        public string Description { get; set; }

        public event EventHandler ValueChanged;
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        private T _typedValue;
        public object Value
        {
            get => _typedValue;
            set
            {
                SetProperty(ref _typedValue, (T)value);
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public OutputPort(string name, string description = "")
        {
            Name = name;
            Description = description;
        }
    }
    public class InputPort<T> : ObservableObject, IInputPort
    {
         T _cachedLinkedValue;
        public bool IsRequired { get; set; } = true;
        public string Name { get; }
        public Type DataType => typeof(T);
        public string Description { get; set; }

        private T _manualValue;
        public object Value
        {
            get => _manualValue;
            set => SetProperty(ref _manualValue, (T)value);
        }
        private IOutputPort _linkedSource;

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public IOutputPort LinkedSource
        {
            get => _linkedSource;
            set
            {
                if (_linkedSource != null)
                    _linkedSource.ValueChanged -= UpstreamValueChanged;
                SetProperty(ref _linkedSource, value);
                if (_linkedSource != null)
                    _linkedSource.ValueChanged += UpstreamValueChanged;
                OnPropertyChanged(nameof(ActualValue));
            }
        }

        // 当上游输出发生变化时触发
        private void UpstreamValueChanged(object sender, EventArgs e)
        {
            RefreshLinkedCache();
            OnPropertyChanged(nameof(ActualValue));
        }

        public InputPort(string name, T defaultValue = default, string description = "")
        {
            Name = name;
            _manualValue = defaultValue;
            Description = description;
        }

        public object GetActualValue()
        {
            return LinkedSource != null ? _cachedLinkedValue : _manualValue;
        }

        public T GetTypedValue() => (T)GetActualValue();

        public T ActualValue => GetTypedValue();
        private void RefreshLinkedCache()
        {
            if (_linkedSource == null) return;

            object rawValue = _linkedSource.Value;

            if (rawValue == null)
            {
                _cachedLinkedValue = default(T);
                return;
            }

            if (rawValue is T typedValue)
            {
                _cachedLinkedValue = typedValue;
                return;
            }

            try
            {
                Type targetType = typeof(T);
                if (Nullable.GetUnderlyingType(targetType) != null)
                    targetType = Nullable.GetUnderlyingType(targetType);

                if (targetType.IsEnum && rawValue is string strValue)
                    _cachedLinkedValue = (T)Enum.Parse(targetType, strValue);
                else
                    _cachedLinkedValue = (T)Convert.ChangeType(rawValue, targetType);
            }
            catch
            {
                throw new Exception("无法将上游输出值转换为指定类型");
            }
        }
        protected virtual T DefaultConvert(object rawValue)
        {
            Type targetType = typeof(T);
            if (Nullable.GetUnderlyingType(targetType) != null)
            {
                targetType = Nullable.GetUnderlyingType(targetType);
            }

            if (targetType.IsEnum && rawValue is string strValue)
            {
                return (T)Enum.Parse(targetType, strValue);
            }

            // 系统默认的兜底转换
            return (T)Convert.ChangeType(rawValue, targetType);
        }

    }
}
