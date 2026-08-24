using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces.Core
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 手动触发属性更改通知
        /// [CallerMemberName] 会在编译时自动填入调用它的属性名称，极大地减少了硬编码字符串的错误
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 完美复刻 Prism 的 SetProperty 方法
        /// 支持数组内容比较、可空类型处理、枚举比较等特殊情况
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="storage">后台字段引用</param>
        /// <param name="value">新传入的值</param>
        /// <param name="propertyName">属性名称(自动获取)</param>
        /// <returns>如果值发生了改变则返回 true</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            // 比较新值和旧值，如果一样就没必要触发通知了，提升性能
            if (ValuesAreEqual(storage, value))
            {
                return false;
            }

            // 赋值
            storage = value;
            // 通知 UI 刷新
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 判断两个值是否相等
        /// 支持数组内容比较、可空类型处理、枚举比较等特殊情况
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="oldValue">旧值</param>
        /// <param name="newValue">新值</param>
        /// <returns>如果相等返回 true，否则返回 false</returns>
        private bool ValuesAreEqual<T>(T oldValue, T newValue)
        {
            // 处理 null 值情况
            if (oldValue == null && newValue == null)
                return true;
            
            if (oldValue == null || newValue == null)
                return false;

            // 处理数组类型 - 比较内容而不是引用
            Type type = typeof(T);
            if (type.IsArray)
            {
                return ArraysAreEqual(oldValue as Array, newValue as Array);
            }

            // 处理枚举类型 - 使用 Enum.Equals
            if (type.IsEnum)
            {
                return Enum.Equals(oldValue, newValue);
            }

            // 处理字符串类型 - 使用 String.Equals 确保一致性
            if (typeof(string).IsAssignableFrom(type))
            {
                return string.Equals(oldValue as string, newValue as string);
            }

            // 默认使用 EqualityComparer
            return EqualityComparer<T>.Default.Equals(oldValue, newValue);
        }

        /// <summary>
        /// 比较两个数组的内容是否相等
        /// </summary>
        /// <param name="array1">第一个数组</param>
        /// <param name="array2">第二个数组</param>
        /// <returns>如果内容相等返回 true，否则返回 false</returns>
        private bool ArraysAreEqual(Array array1, Array array2)
        {
            if (array1 == null && array2 == null)
                return true;
            
            if (array1 == null || array2 == null)
                return false;

            if (array1.Length != array2.Length)
                return false;

            for (int i = 0; i < array1.Length; i++)
            {
                object val1 = array1.GetValue(i);
                object val2 = array2.GetValue(i);

                if (val1 == null && val2 == null)
                    continue;
                
                if (val1 == null || val2 == null)
                    return false;

                if (!val1.Equals(val2))
                    return false;
            }

            return true;
        }
    }
}
