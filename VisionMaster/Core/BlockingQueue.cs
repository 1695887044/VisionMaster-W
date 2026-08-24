using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 高性能阻塞队列（基于System.Threading.Channels）
    /// </summary>
    /// <typeparam name="T">队列元素类型</typeparam>
    public sealed class BlockingQueue<T> : IDisposable
    {
        private readonly Channel<T> _channel;
        private bool _disposed;

        /// <summary>
        /// 创建阻塞队列
        /// </summary>
        /// <param name="capacity">最大容量（0表示无界）</param>
        public BlockingQueue(int capacity = 0)
        {
            _channel = capacity > 0
                ? Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = false,
                    SingleReader = false
                })
                : Channel.CreateUnbounded<T>(new UnboundedChannelOptions
                {
                    SingleWriter = false,
                    SingleReader = false
                });
        }

        /// <summary>
        /// 入队（阻塞直到成功）
        /// </summary>
        public void Enqueue(T item)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BlockingQueue<T>));
            _channel.Writer.TryWrite(item);
        }

        /// <summary>
        /// 出队（阻塞直到有数据）
        /// </summary>
        public T Dequeue(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BlockingQueue<T>));
            return _channel.Reader.ReadAsync(cancellationToken).AsTask().Result;
        }

        /// <summary>
        /// 尝试出队（非阻塞）
        /// </summary>
        public bool TryDequeue(out T item)
        {
            if (_disposed)
            {
                item = default;
                return false;
            }
            return _channel.Reader.TryRead(out item);
        }

        /// <summary>
        /// 队列当前元素数量
        /// </summary>
        public int Count => _channel.Reader.Count;

        /// <summary>
        /// 关闭队列（不再接受新元素）
        /// </summary>
        public void Complete() => _channel.Writer.TryComplete();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.Writer.TryComplete();
        }
    }
}
