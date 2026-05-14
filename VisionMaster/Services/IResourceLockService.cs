using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Services
{
    /// <summary>
    /// 资源锁服务接口
    /// 提供硬件资源的互斥访问机制，支持异步获取、超时控制和自动释放
    /// </summary>
    public interface IResourceLockService
    {
        /// <summary>
        /// 异步获取资源锁（无限等待）
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>锁释放句柄，使用 using 语句自动释放</returns>
        Task<IDisposable> AcquireLockAsync(string resourceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步获取资源锁（带超时）
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>锁释放句柄，使用 using 语句自动释放</returns>
        /// <exception cref="TimeoutException">获取锁超时时抛出</exception>
        Task<IDisposable> AcquireLockAsync(string resourceName, int timeoutMs, CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试获取资源锁（非阻塞）
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <param name="releaseHandle">锁释放句柄（成功时返回）</param>
        /// <returns>是否成功获取锁</returns>
        bool TryAcquireLock(string resourceName, out IDisposable releaseHandle);

        /// <summary>
        /// 检查资源是否被锁定
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <returns>是否被锁定</returns>
        bool IsLocked(string resourceName);

        /// <summary>
        /// 获取资源锁的拥有者会话ID
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        /// <returns>拥有者会话ID，未锁定时返回 null</returns>
        string GetLockOwner(string resourceName);

        /// <summary>
        /// 手动释放资源锁
        /// </summary>
        /// <param name="resourceName">资源名称</param>
        void ReleaseLock(string resourceName);

        /// <summary>
        /// 释放所有资源锁
        /// 用于紧急情况或系统关闭时
        /// </summary>
        void ReleaseAllLocks();
    }
}