using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VirtualCameraClient
{
    /// <summary>
    /// 最小 MVVM 基类：只实现 INotifyPropertyChanged 这一个界面通知契约。
    ///
    /// 为什么不用 CommunityToolkit.Mvvm / Prism：
    /// 本工具是"零 NuGet 依赖"的独立小程序，需要的只是一个 SetProperty，
    /// 引框架不仅多一份包，还会让这个工具和主仓库生态产生没必要的耦合。
    ///
    /// 为什么 SetProperty 要先比较再赋值：
    /// WPF 的绑定收到"值没变"的通知也会走一遍刷新逻辑；先比较能挡掉大量无意义刷新，
    /// 也让"派生属性"（如 StartStopText）只在真正变化时才联动。
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
