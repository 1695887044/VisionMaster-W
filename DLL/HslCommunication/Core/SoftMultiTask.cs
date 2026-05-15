using System;
using System.Threading;

namespace HslCommunication.Core
{
	/// <summary>
	/// 一个用于多线程并发处理数据的模型类，适用于处理数据量非常庞大的情况
	/// </summary>
	/// <typeparam name="T">等待处理的数据类型</typeparam>
	public sealed class SoftMultiTask<T>
	{
		/// <summary>
		/// 一个双参数委托
		/// </summary>
		/// <param name="item"></param>
		/// <param name="ex"></param>
		public delegate void MultiInfo(T item, Exception ex);

		/// <summary>
		/// 用于报告进度的委托，当finish等于count时，任务完成
		/// </summary>
		/// <param name="finish">已完成操作数量</param>
		/// <param name="count">总数量</param>
		/// <param name="success">成功数量</param>
		/// <param name="failed">失败数量</param>
		public delegate void MultiInfoTwo(int finish, int count, int success, int failed);

		/// <summary>
		/// 操作总数，判定操作是否完成
		/// </summary>
		private int m_opCount = 0;

		/// <summary>
		/// 判断是否所有的线程是否处理完成
		/// </summary>
		private int m_opThreadCount = 1;

		/// <summary>
		/// 准备启动的处理数据的线程数量
		/// </summary>
		private int m_threadCount = 10;

		/// <summary>
		/// 指示多线程处理是否在运行中，防止冗余调用
		/// </summary>
		private int m_runStatus = 0;

		/// <summary>
		/// 列表数据
		/// </summary>
		private T[] m_dataList = null;

		/// <summary>
		/// 需要操作的方法
		/// </summary>
		private Func<T, bool> m_operater = null;

		/// <summary>
		/// 已处理完成数量，无论是否异常
		/// </summary>
		private int m_finishCount = 0;

		/// <summary>
		/// 处理完成并实现操作数量
		/// </summary>
		private int m_successCount = 0;

		/// <summary>
		/// 处理过程中异常数量
		/// </summary>
		private int m_failedCount = 0;

		/// <summary>
		/// 用于触发事件的混合线程锁
		/// </summary>
		private SimpleHybirdLock HybirdLock = new SimpleHybirdLock();

		/// <summary>
		/// 指示处理状态是否为暂停状态
		/// </summary>
		private bool m_isRunningStop = false;

		/// <summary>
		/// 指示系统是否需要强制退出
		/// </summary>
		private bool m_isQuit = false;

		/// <summary>
		/// 在发生错误的时候是否强制退出后续的操作
		/// </summary>
		private bool m_isQuitAfterException = false;

		/// <summary>
		/// 在发生错误的时候是否强制退出后续的操作
		/// </summary>
		public bool IsQuitAfterException
		{
			get
			{
				return m_isQuitAfterException;
			}
			set
			{
				m_isQuitAfterException = value;
			}
		}

		/// <summary>
		/// 异常发生时事件
		/// </summary>
		public event MultiInfo OnExceptionOccur;

		/// <summary>
		/// 报告处理进度时发生
		/// </summary>
		public event MultiInfoTwo OnReportProgress;

		/// <summary>
		/// 实例化一个数据处理对象
		/// </summary>
		/// <param name="dataList">数据处理列表</param>
		/// <param name="operater">数据操作方法，应该是相对耗时的任务</param>
		/// <param name="threadCount">需要使用的线程数</param>
		public SoftMultiTask(T[] dataList, Func<T, bool> operater, int threadCount = 10)
		{
			m_dataList = dataList ?? throw new ArgumentNullException("dataList");
			m_operater = operater ?? throw new ArgumentNullException("operater");
			if (threadCount < 1)
			{
				throw new ArgumentException("threadCount can not less than 1", "threadCount");
			}
			m_threadCount = threadCount;
			Interlocked.Add(ref m_opCount, dataList.Length);
			Interlocked.Add(ref m_opThreadCount, threadCount);
		}

		/// <summary>
		/// 启动多线程进行数据处理
		/// </summary>
		public void StartOperater()
		{
			if (Interlocked.CompareExchange(ref m_runStatus, 0, 1) == 0)
			{
				for (int i = 0; i < m_threadCount; i++)
				{
					Thread thread = new Thread(ThreadBackground);
					thread.IsBackground = true;
					thread.Start();
				}
				JustEnded();
			}
		}

		/// <summary>
		/// 暂停当前的操作
		/// </summary>
		public void StopOperater()
		{
			if (m_runStatus == 1)
			{
				m_isRunningStop = true;
			}
		}

		/// <summary>
		/// 恢复暂停的操作
		/// </summary>
		public void ResumeOperater()
		{
			m_isRunningStop = false;
		}

		/// <summary>
		/// 直接手动强制结束操作
		/// </summary>
		public void EndedOperater()
		{
			if (m_runStatus == 1)
			{
				m_isQuit = true;
			}
		}

		private void ThreadBackground()
		{
			while (true)
			{
				bool flag = true;
				while (m_isRunningStop)
				{
				}
				int num = Interlocked.Decrement(ref m_opCount);
				if (num < 0)
				{
					break;
				}
				T val = m_dataList[num];
				bool flag2 = false;
				bool flag3 = false;
				try
				{
					if (!m_isQuit)
					{
						flag2 = m_operater(val);
					}
				}
				catch (Exception ex)
				{
					flag3 = true;
					this.OnExceptionOccur?.Invoke(val, ex);
					if (m_isQuitAfterException)
					{
						EndedOperater();
					}
				}
				finally
				{
					HybirdLock.Enter();
					if (flag2)
					{
						m_successCount++;
					}
					if (flag3)
					{
						m_failedCount++;
					}
					m_finishCount++;
					this.OnReportProgress?.Invoke(m_finishCount, m_dataList.Length, m_successCount, m_failedCount);
					HybirdLock.Leave();
				}
			}
			JustEnded();
		}

		private void JustEnded()
		{
			if (Interlocked.Decrement(ref m_opThreadCount) == 0)
			{
				m_finishCount = 0;
				m_failedCount = 0;
				m_successCount = 0;
				Interlocked.Exchange(ref m_opCount, m_dataList.Length);
				Interlocked.Exchange(ref m_opThreadCount, m_threadCount + 1);
				Interlocked.Exchange(ref m_runStatus, 0);
				m_isRunningStop = false;
				m_isQuit = false;
			}
		}
	}
}
