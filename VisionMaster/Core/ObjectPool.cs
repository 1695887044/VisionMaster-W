using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 高性能对象池（支持自动扩容、对象初始化、最大容量限制）
    /// </summary>
    /// <typeparam name="T">池化对象类型</typeparam>
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly ConcurrentQueue<T> _pool = new();
        private readonly Func<T> _factory;
        private readonly Action<T> _resetAction;
        private readonly int _maxCapacity;
        private int _totalCreated;
        private bool _disposed;

        /// <summary>
        /// 创建对象池
        /// </summary>
        /// <param name="factory">对象创建工厂</param>
        /// <param name="resetAction">对象归还时的重置方法</param>
        /// <param name="initialSize">初始容量</param>
        /// <param name="maxCapacity">最大容量（超过后不再缓存，直接GC）</param>
        public ObjectPool(Func<T> factory, Action<T> resetAction = null, int initialSize = 10, int maxCapacity = 100)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _resetAction = resetAction;
            _maxCapacity = maxCapacity;

            // 预初始化对象
            for (int i = 0; i < initialSize; i++)
            {
                _pool.Enqueue(factory());
                Interlocked.Increment(ref _totalCreated);
            }
        }

        /// <summary>
        /// 从池中获取对象
        /// </summary>
        public T Get()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ObjectPool<T>));

            if (_pool.TryDequeue(out var item))
            {
                return item;
            }

            // 池为空，创建新对象
            Interlocked.Increment(ref _totalCreated);
            return _factory();
        }

        /// <summary>
        /// 将对象归还到池
        /// </summary>
        public void Return(T item)
        {
            if (_disposed || item == null) return;

            // 重置对象状态
            _resetAction?.Invoke(item);

            // 超过最大容量则丢弃
            if (_pool.Count < _maxCapacity)
            {
                _pool.Enqueue(item);
            }
        }

        /// <summary>
        /// 清空池并释放所有对象
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            while (_pool.TryDequeue(out var item))
            {
                if (item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}