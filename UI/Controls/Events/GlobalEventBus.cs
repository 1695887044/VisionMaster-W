using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace UI.Events
{
    /// <summary>
    /// 全局静态事件总线 (轻量级 Pub/Sub)
    /// </summary>
    public static class GlobalEventBus
    {
        // 核心存储器：<事件类型, 订阅者列表>
        // 使用 ConcurrentDictionary 保证并发注入时的绝对安全
        private static readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();

        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <typeparam name="TMessage">事件参数类型</typeparam>
        /// <param name="handler">处理逻辑</param>
        public static void Subscribe<TMessage>(Action<TMessage> handler)
        {
            var eventType = typeof(TMessage);

            // 确保字典中有这个类型的列表，并使用 lock 保证列表添加时的线程安全
            var handlers = _subscribers.GetOrAdd(eventType, _ => new List<Delegate>());
            lock (handlers)
            {
                if (!handlers.Contains(handler))
                {
                    handlers.Add(handler);
                }
            }
        }

        /// <summary>
        /// 取消订阅 (⚠️ WPF 必做：在 ViewModel 的 OnNavigatedFrom 或 Dispose 中调用，防止内存泄漏)
        /// </summary>
        public static void Unsubscribe<TMessage>(Action<TMessage> handler)
        {
            var eventType = typeof(TMessage);
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                }
            }
        }

        /// <summary>
        /// 同步发布事件 (在当前线程直接执行)
        /// </summary>
        public static void Publish<TMessage>(TMessage message)
        {
            var eventType = typeof(TMessage);
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                // 拷贝一份列表再执行，防止在遍历过程中有其他线程增删订阅者引发异常
                Action<TMessage>[] handlersCopy;
                lock (handlers)
                {
                    handlersCopy = handlers.Cast<Action<TMessage>>().ToArray();
                }

                foreach (var handler in handlersCopy)
                {
                    handler.Invoke(message);
                }
            }
        }

        /// <summary>
        /// 异步发布事件 (丢到线程池执行，不阻塞当前发布者)
        /// </summary>
        public static void PublishAsync<TMessage>(TMessage message)
        {
            Task.Run(() => Publish(message));
        }

        /// <summary>
        /// 🌟 WPF 专属：发布事件，并强制将接收者的执行逻辑调度到 UI 主线程！
        /// (极其适合后台视觉引擎算完数据后，通知前端刷新图表)
        /// </summary>
        public static void PublishOnUIThread<TMessage>(TMessage message)
        {
            var eventType = typeof(TMessage);
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                Action<TMessage>[] handlersCopy;
                lock (handlers)
                {
                    handlersCopy = handlers.Cast<Action<TMessage>>().ToArray();
                }

                foreach (var handler in handlersCopy)
                {
                    // 检查是否处于 WPF 环境中
                    if (Application.Current != null && Application.Current.Dispatcher != null)
                    {
                        // 切入 UI 线程执行
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => handler.Invoke(message)));
                    }
                    else
                    {
                        // 退化为普通执行
                        handler.Invoke(message);
                    }
                }
            }
        }
    }
}
