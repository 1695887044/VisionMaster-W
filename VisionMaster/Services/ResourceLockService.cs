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
            public string OwnerSessionId { get; set; }

            /// <summary>
            /// 锁计数（支持重入）
            /// </summary>
            public int LockCount { get; set; }
        }

        /// <summary>
        /// 异步获取资源锁（无限等待）
        /// </summary>
        public async Task<IDisposable> AcquireLockAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            return await AcquireLockAsync(resourceName, Timeout.Infinite, cancellationToken);
        }

        /// <summary>
        /// 异步获取资源锁（带超时）
        /// </summary>
        public async Task<IDisposable> AcquireLockAsync(string resourceName, int timeoutMs, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(resourceName))
                throw new ArgumentNullException(nameof(resourceName));

            var resourceLock = _locks.GetOrAdd(resourceName, _ => new ResourceLock());

            bool acquired = await resourceLock.Semaphore.WaitAsync(timeoutMs, cancellationToken);

            if (!acquired)
                throw new TimeoutException($"获取资源锁 '{resourceName}' 超时");

            resourceLock.LockCount++;

            return new LockReleaseHandle(this, resourceName);
        }

        /// <summary>
        /// 尝试获取资源锁（非阻塞）
        /// </summary>
        public bool TryAcquireLock(string resourceName, out IDisposable releaseHandle)
        {
            releaseHandle = null;

            if (string.IsNullOrEmpty(resourceName))
                return false;

            var resourceLock = _locks.GetOrAdd(resourceName, _ => new ResourceLock());

            if (resourceLock.Semaphore.Wait(0))
            {
                resourceLock.LockCount++;
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
                return resourceLock.LockCount > 0;
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
            if (_locks.TryGetValue(resourceName, out var resourceLock))
            {
                resourceLock.LockCount--;
                resourceLock.Semaphore.Release();

                if (resourceLock.LockCount <= 0)
                {
                    resourceLock.OwnerSessionId = null;
                    resourceLock.LockCount = 0;
                }
            }
        }

        /// <summary>
        /// 释放所有资源锁
        /// </summary>
        public void ReleaseAllLocks()
        {
            foreach (var kvp in _locks)
            {
                kvp.Value.Semaphore.Release(kvp.Value.LockCount);
                kvp.Value.LockCount = 0;
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