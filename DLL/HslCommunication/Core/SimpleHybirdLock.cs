using System;
using System.Threading;

namespace HslCommunication.Core
{
	/// <summary>
	/// 一个简单的混合线程同步锁，采用了基元用户加基元内核同步构造实现<br />
	/// A simple hybrid thread editing lock, implemented by the base user plus the element kernel synchronization.
	/// </summary>
	/// <remarks>
	/// 当前的锁适用于，竞争频率比较低，锁部分的代码运行时间比较久的情况，当前的简单混合锁可以达到最大性能。
	/// </remarks>
	/// <example>
	/// 以下演示常用的锁的使用方式，还包含了如何优雅的处理异常锁
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\ThreadLock.cs" region="SimpleHybirdLockExample1" title="SimpleHybirdLock示例" />
	/// </example>
	public sealed class SimpleHybirdLock : IDisposable
	{
		private bool disposedValue = false;

		/// <summary>
		/// 基元用户模式构造同步锁
		/// </summary>
		private int m_waiters = 0;

		private int m_lock_tick = 0;

		private DateTime m_enterlock_time = DateTime.Now;

		/// <summary>
		/// 基元内核模式构造同步锁
		/// </summary>
		private readonly Lazy<AutoResetEvent> m_waiterLock = new Lazy<AutoResetEvent>(() => new AutoResetEvent(initialState: false));

		private static long simpleHybirdLockCount;

		private static long simpleHybirdLockWaitCount;

		/// <summary>
		/// 获取当前锁是否在等待当中<br />
		/// Gets whether the current lock is waiting
		/// </summary>
		public bool IsWaitting => m_waiters != 0;

		/// <summary>
		/// 获取当前进入等待锁的数量<br />
		/// Gets the number of pending locks currently entered
		/// </summary>
		public int LockingTick => m_lock_tick;

		/// <summary>
		/// 获取当前HslCommunication组件里正总的所有进入锁的信息<br />
		/// Gets the information about all incoming locks in the current HslCommunication component
		/// </summary>
		public static long SimpleHybirdLockCount => simpleHybirdLockCount;

		/// <summary>
		/// 当前HslCommunication组件里正在等待的锁的统计信息，此时已经发生了竞争了<br />
		/// Statistics on locks currently waiting in the HslCommunication component are now in contention
		/// </summary>
		public static long SimpleHybirdLockWaitCount => simpleHybirdLockWaitCount;

		private void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
				}
				m_waiterLock.Value.Close();
				disposedValue = true;
			}
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public void Dispose()
		{
			Dispose(disposing: true);
		}

		/// <summary>
		/// 获取锁，可以指定获取锁的超时时间，如果指定的时间没有获取锁，则返回<c>False</c>，反之，返回<c>True</c><br />
		/// To acquire a lock, you can specify the timeout period for acquiring a lock, return <c>False</c> if the specified time does not acquire a lock, and vice versa, return <c>True</c>
		/// </summary>
		/// <returns>是否正确的获得锁</returns>
		public bool Enter()
		{
			Interlocked.Increment(ref simpleHybirdLockCount);
			if (Interlocked.Increment(ref m_waiters) == 1)
			{
				m_enterlock_time = DateTime.Now;
				return true;
			}
			Interlocked.Increment(ref simpleHybirdLockWaitCount);
			Interlocked.Increment(ref m_lock_tick);
			bool flag = m_waiterLock.Value.WaitOne();
			if (!flag)
			{
				Interlocked.Decrement(ref simpleHybirdLockCount);
				Interlocked.Decrement(ref simpleHybirdLockWaitCount);
				Interlocked.Decrement(ref m_lock_tick);
			}
			else
			{
				m_enterlock_time = DateTime.Now;
			}
			return flag;
		}

		/// <summary>
		/// 离开锁<br />
		/// Leave the lock
		/// </summary>
		/// <returns>如果该操作成功，返回<c>True</c>，反之，返回<c>False</c></returns>
		public bool Leave()
		{
			Interlocked.Decrement(ref simpleHybirdLockCount);
			if (Interlocked.Decrement(ref m_waiters) == 0)
			{
				return true;
			}
			bool flag = false;
			flag = m_waiterLock.Value.Set();
			Interlocked.Decrement(ref simpleHybirdLockWaitCount);
			Interlocked.Decrement(ref m_lock_tick);
			return flag;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (m_lock_tick > 0)
			{
				return $"SimpleHybirdLock[WaitOne-{DateTime.Now - m_enterlock_time}]";
			}
			if (m_waiters != 0)
			{
				return $"SimpleHybirdLock[OneLock-{DateTime.Now - m_enterlock_time}]";
			}
			return "SimpleHybirdLock[NoneLock]";
		}
	}
}
