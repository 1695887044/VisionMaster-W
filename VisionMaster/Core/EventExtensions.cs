using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    public static class EventExtensions
    {
        /// <summary>
        /// 订阅事件并返回可释放的订阅句柄
        /// </summary>
        /// <param name="eventSource">事件源</param>
        /// <param name="handler">事件处理器</param>
        /// <returns>IDisposable对象，调用Dispose取消订阅</returns>
        public static IDisposable Subscribe(this IOutputPort eventSource, EventHandler handler)
        {
            eventSource.ValueChanged += handler;
            return new EventSubscription(() => eventSource.ValueChanged -= handler);
        }

        /// <summary>
        /// 通用事件订阅扩展
        /// </summary>
        public static IDisposable Subscribe<TEventArgs>(
            this object eventSource,
            string eventName,
            EventHandler<TEventArgs> handler)
        {
            var eventInfo = eventSource.GetType().GetEvent(eventName);
            eventInfo.AddEventHandler(eventSource, handler);
            return new EventSubscription(() => eventInfo.RemoveEventHandler(eventSource, handler));
        }
    }

    /// <summary>
    /// 事件订阅句柄，释放时自动取消订阅
    /// </summary>
    internal sealed class EventSubscription : IDisposable
    {
        private Action _unsubscribeAction;

        public EventSubscription(Action unsubscribeAction)
        {
            _unsubscribeAction = unsubscribeAction ?? throw new ArgumentNullException(nameof(unsubscribeAction));
        }

        public void Dispose()
        {
            // 确保只取消一次
            var action = Interlocked.Exchange(ref _unsubscribeAction, null);
            action?.Invoke();
        }
    }
}
