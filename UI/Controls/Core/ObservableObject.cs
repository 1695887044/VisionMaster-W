

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI.Core
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        // WPF 必须要监听的事件
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
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="storage">后台字段引用</param>
        /// <param name="value">新传入的值</param>
        /// <param name="propertyName">属性名称(自动获取)</param>
        /// <returns>如果值发生了改变则返回 true</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            // 比较新值和旧值，如果一样就没必要触发通知了，提升性能
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            // 赋值
            storage = value;
            // 通知 UI 刷新
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
