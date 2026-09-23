using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Services
{
    /// <summary>
    /// 资源锁服务实现
    /// 基于 SemaphoreSlim 实现高效的资源互斥访问机制
    /// </summary>
    public class ResourceLockService : IResourceLockService
    {
        /// <summary>
        /// 资源锁字典，存储每个资源的锁信息
        /// </summary>
        private readonly ConcurrentDictionary<string, ResourceLock> _locks = new ConcurrentDictionary<string, ResourceLock>();

        /// <summary>
        /// 资源锁内部类
        /// 封装信号量和所有权信息
        /// </summary>
        private class ResourceLock
        {
            /// <summary>
            /// 信号量（初始计数为1，最大计数为1，实现互斥）
            /// </summary>
            public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

            /// <summary>
            /// 拥有锁的会话ID
            /// </summary>
            public string OwnerSessionId;

            /// <summary>
            /// 持有标记：0=未持有，1=已持有。
            ///
            /// 注意它不是属性而是普通字段，因为读写一律走 Interlocked ——
            /// 应急路径（ReleaseAllLocks）会从并不持有该锁的线程上并发进来。
            ///
            /// 原注释写"锁计数（支持重入）"是不成立的：信号量最大计数为 1，
            /// 同一资源第二次 Acquire 只会永久挂住，永远轮不到计数加到 2。
            /// </summary>
            public int LockCount;
        }

        /// <summary>
        /// 异步获取资源锁（无限等待）
        /// </summary>
        public async Task<IDisposable> AcquireLockAsync(string resourceName, string ownerSessionId = null, CancellationToken cancellationToken = default)
        {
            return await AcquireLockAsync(resourceName, Timeout.Infinite, ownerSessionId, cancellationToken);
        }

        /// <summary>
        /// 异步获取资源锁（带超时）
        /// </summary>
        public async Task<IDisposable> AcquireLockAsync(string resourceName, int timeoutMs, string ownerSessionId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resourceName))
                throw new ArgumentNullException(nameof(resourceName));

            var resourceLock = _locks.GetOrAdd(resourceName, _ => new ResourceLock());

            bool acquired = await resourceLock.Semaphore.WaitAsync(timeoutMs, cancellationToken);

            if (!acquired)
                throw new TimeoutException($"获取资源锁 '{resourceName}' 超时");

            resourceLock.OwnerSessionId = ownerSessionId;
            Interlocked.Exchange(ref resourceLock.LockCount, 1);

            return new LockReleaseHandle(this, resourceName);
        }

        /// <summary>
        /// 尝试获取资源锁（非阻塞）
        /// </summary>
        public bool TryAcquireLock(string resourceName, out IDisposable releaseHandle, string ownerSessionId = null)
        {
            releaseHandle = null;

            if (string.IsNullOrEmpty(resourceName))
                return false;

            var resourceLock = _locks.GetOrAdd(resourceName, _ => new ResourceLock());

            if (resourceLock.Semaphore.Wait(0))
            {
                resourceLock.OwnerSessionId = ownerSessionId;
                Interlocked.Exchange(ref resourceLock.LockCount, 1);
                releaseHandle = new LockReleaseHandle(this, resourceName);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 检查资源是否被锁定
        /// </summary>
        public bool IsLocked(string resourceName)
        {
            if (_locks.TryGetValue(resourceName, out var resourceLock))
            {
                return Volatile.Read(ref resourceLock.LockCount) > 0;
            }
            return false;
        }

        /// <summary>
        /// 获取资源锁的拥有者会话ID
        /// </summary>
        public string GetLockOwner(string resourceName)
        {
            if (_locks.TryGetValue(resourceName, out var resourceLock))
            {
                return resourceLock.OwnerSessionId;
            }
            return null;
        }

        /// <summary>
        /// 手动释放资源锁
        /// </summary>
        public void ReleaseLock(string resourceName)
        {
            if (!_locks.TryGetValue(resourceName, out var resourceLock))
                return;

            // 用 Exchange 的返回值当闸门：只有真正持有过的那一次才会拿到 1，
            // 之后一律返回 0 直接退出。少了这道闸门，句柄被 Dispose 两次
            // （或手工 Release 与 using 自动释放撞车）就会在最大计数为 1 的
            // 信号量上第二次 Release —— 抛 SemaphoreFullException。
            if (Interlocked.Exchange(ref resourceLock.LockCount, 0) == 0)
                return;

            resourceLock.OwnerSessionId = null;
            resourceLock.Semaphore.Release();
        }

        /// <summary>
        /// 释放所有资源锁
        /// </summary>
        public void ReleaseAllLocks()
        {
            foreach (var kvp in _locks)
            {
                // 原来这里直接 Semaphore.Release(LockCount)，有两颗雷：
                //   LockCount == 0 → Release(0) 抛 ArgumentOutOfRangeException
                //                  （条目是 TryAcquireLock 抢失败时 GetOrAdd 顺手建的，本来就没持有）
                //   LockCount >  1 → 超过信号量最大计数 1，抛 SemaphoreFullException
                // 所以先取值再决定是否释放，且永远只释放一份。
                if (Interlocked.Exchange(ref kvp.Value.LockCount, 0) > 0)
                    kvp.Value.Semaphore.Release();

                kvp.Value.OwnerSessionId = null;
            }
        }

        /// <summary>
        /// 锁释放句柄
        /// 使用 using 语句时自动释放锁
        /// </summary>
        private sealed class LockReleaseHandle : IDisposable
        {
            private readonly ResourceLockService _service;
            private readonly string _resourceName;
            private bool _disposed;

            public LockReleaseHandle(ResourceLockService service, string resourceName)
            {
                _service = service;
                _resourceName = resourceName;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _service.ReleaseLock(_resourceName);
                }
            }
        }
    }
}