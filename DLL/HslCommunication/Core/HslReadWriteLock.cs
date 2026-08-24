#define DEBUG
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace HslCommunication.Core
{
	/// <summary>
	/// 一个高性能的读写锁，支持写锁定，读灵活，读时写锁定，写时读锁定
	/// </summary>
	public sealed class HslReadWriteLock : IDisposable
	{
		private enum OneManyLockStates
		{
			Free,
			OwnedByWriter,
			OwnedByReaders,
			OwnedByReadersAndWriterPending,
			ReservedForWriter
		}

		private const int c_lsStateStartBit = 0;

		private const int c_lsReadersReadingStartBit = 3;

		private const int c_lsReadersWaitingStartBit = 12;

		private const int c_lsWritersWaitingStartBit = 21;

		private const int c_lsStateMask = 7;

		private const int c_lsReadersReadingMask = 4088;

		private const int c_lsReadersWaitingMask = 2093056;

		private const int c_lsWritersWaitingMask = 1071644672;

		private const int c_lsAnyWaitingMask = 1073737728;

		private const int c_ls1ReaderReading = 8;

		private const int c_ls1ReaderWaiting = 4096;

		private const int c_ls1WriterWaiting = 2097152;

		private int m_LockState = 0;

		private Semaphore m_ReadersLock = new Semaphore(0, int.MaxValue);

		private Semaphore m_WritersLock = new Semaphore(0, int.MaxValue);

		private bool disposedValue = false;

		private bool m_exclusive;

		private static OneManyLockStates State(int ls)
		{
			return (OneManyLockStates)(ls & 7);
		}

		private static void SetState(ref int ls, OneManyLockStates newState)
		{
			ls = (ls & -8) | (int)newState;
		}

		private static int NumReadersReading(int ls)
		{
			return (ls & 0xFF8) >> 3;
		}

		private static void AddReadersReading(ref int ls, int amount)
		{
			ls += 8 * amount;
		}

		private static int NumReadersWaiting(int ls)
		{
			return (ls & 0x1FF000) >> 12;
		}

		private static void AddReadersWaiting(ref int ls, int amount)
		{
			ls += 4096 * amount;
		}

		private static int NumWritersWaiting(int ls)
		{
			return (ls & 0x3FE00000) >> 21;
		}

		private static void AddWritersWaiting(ref int ls, int amount)
		{
			ls += 2097152 * amount;
		}

		private static bool AnyWaiters(int ls)
		{
			return (ls & 0x3FFFF000) != 0;
		}

		private static string DebugState(int ls)
		{
			return string.Format(CultureInfo.InvariantCulture, "State={0}, RR={1}, RW={2}, WW={3}", State(ls), NumReadersReading(ls), NumReadersWaiting(ls), NumWritersWaiting(ls));
		}

		/// <summary>
		/// 返回本对象的描述字符串
		/// </summary>
		/// <returns>对象的描述字符串</returns>
		public override string ToString()
		{
			return DebugState(m_LockState);
		}

		/// <summary>
		/// 实例化一个读写锁的对象
		/// </summary>
		public HslReadWriteLock()
		{
		}

		private void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
				}
				m_WritersLock.Close();
				m_WritersLock = null;
				m_ReadersLock.Close();
				m_ReadersLock = null;
				disposedValue = true;
			}
		}

		/// <summary>
		/// 释放资源
		/// </summary>
		public void Dispose()
		{
			Dispose(disposing: true);
		}

		/// <summary>
		/// 根据读写情况请求锁
		/// </summary>
		/// <param name="exclusive">True为写请求，False为读请求</param>
		public void Enter(bool exclusive)
		{
			if (exclusive)
			{
				while (WaitToWrite(ref m_LockState))
				{
					m_WritersLock.WaitOne();
				}
			}
			else
			{
				while (WaitToRead(ref m_LockState))
				{
					m_ReadersLock.WaitOne();
				}
			}
			m_exclusive = exclusive;
		}

		private static bool WaitToWrite(ref int target)
		{
			int num = target;
			int num2;
			bool result;
			do
			{
				num2 = num;
				int ls = num2;
				result = false;
				switch (State(ls))
				{
				case OneManyLockStates.Free:
				case OneManyLockStates.ReservedForWriter:
					SetState(ref ls, OneManyLockStates.OwnedByWriter);
					break;
				case OneManyLockStates.OwnedByWriter:
					AddWritersWaiting(ref ls, 1);
					result = true;
					break;
				case OneManyLockStates.OwnedByReaders:
				case OneManyLockStates.OwnedByReadersAndWriterPending:
					SetState(ref ls, OneManyLockStates.OwnedByReadersAndWriterPending);
					AddWritersWaiting(ref ls, 1);
					result = true;
					break;
				default:
					Debug.Assert(condition: false, "Invalid Lock state");
					break;
				}
				num = Interlocked.CompareExchange(ref target, ls, num2);
			}
			while (num2 != num);
			return result;
		}

		/// <summary>
		/// 释放锁，将根据锁状态自动区分读写锁
		/// </summary>
		public void Leave()
		{
			int num;
			if (m_exclusive)
			{
				Debug.Assert(State(m_LockState) == OneManyLockStates.OwnedByWriter && NumReadersReading(m_LockState) == 0);
				num = DoneWriting(ref m_LockState);
			}
			else
			{
				OneManyLockStates oneManyLockStates = State(m_LockState);
				Debug.Assert(State(m_LockState) == OneManyLockStates.OwnedByReaders || State(m_LockState) == OneManyLockStates.OwnedByReadersAndWriterPending);
				num = DoneReading(ref m_LockState);
			}
			if (num == -1)
			{
				m_WritersLock.Release();
			}
			else if (num > 0)
			{
				m_ReadersLock.Release(num);
			}
		}

		private static int DoneWriting(ref int target)
		{
			int num = target;
			int num2 = 0;
			int num3;
			do
			{
				int ls = (num3 = num);
				if (!AnyWaiters(ls))
				{
					SetState(ref ls, OneManyLockStates.Free);
					num2 = 0;
				}
				else if (NumWritersWaiting(ls) > 0)
				{
					SetState(ref ls, OneManyLockStates.ReservedForWriter);
					AddWritersWaiting(ref ls, -1);
					num2 = -1;
				}
				else
				{
					num2 = NumReadersWaiting(ls);
					Debug.Assert(num2 > 0);
					SetState(ref ls, OneManyLockStates.OwnedByReaders);
					AddReadersWaiting(ref ls, -num2);
				}
				num = Interlocked.CompareExchange(ref target, ls, num3);
			}
			while (num3 != num);
			return num2;
		}

		private static bool WaitToRead(ref int target)
		{
			int num = target;
			int num2;
			bool result;
			do
			{
				int ls = (num2 = num);
				result = false;
				switch (State(ls))
				{
				case OneManyLockStates.Free:
					SetState(ref ls, OneManyLockStates.OwnedByReaders);
					AddReadersReading(ref ls, 1);
					break;
				case OneManyLockStates.OwnedByReaders:
					AddReadersReading(ref ls, 1);
					break;
				case OneManyLockStates.OwnedByWriter:
				case OneManyLockStates.OwnedByReadersAndWriterPending:
				case OneManyLockStates.ReservedForWriter:
					AddReadersWaiting(ref ls, 1);
					result = true;
					break;
				default:
					Debug.Assert(condition: false, "Invalid Lock state");
					break;
				}
				num = Interlocked.CompareExchange(ref target, ls, num2);
			}
			while (num2 != num);
			return result;
		}

		private static int DoneReading(ref int target)
		{
			int num = target;
			int num2;
			int result;
			do
			{
				int ls = (num2 = num);
				AddReadersReading(ref ls, -1);
				if (NumReadersReading(ls) > 0)
				{
					result = 0;
				}
				else if (!AnyWaiters(ls))
				{
					SetState(ref ls, OneManyLockStates.Free);
					result = 0;
				}
				else
				{
					Debug.Assert(NumWritersWaiting(ls) > 0);
					SetState(ref ls, OneManyLockStates.ReservedForWriter);
					AddWritersWaiting(ref ls, -1);
					result = -1;
				}
				num = Interlocked.CompareExchange(ref target, ls, num2);
			}
			while (num2 != num);
			return result;
		}
	}
}
