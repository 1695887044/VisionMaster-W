using System;
using System.Collections.Generic;
using HslCommunication.LogNet;

namespace HslCommunication.Core
{
	/// <summary>
	/// 文件标记对象类，标记了一个文件的当前状态，是否处于下载中，删除的操作信息<br />
	/// File tag object class, which marks the current status of a file, whether it is downloading, or delete operation information
	/// </summary>
	public class FileMarkId
	{
		private int readStatus = 0;

		private readonly ILogNet logNet;

		private readonly string fileName = null;

		private readonly Queue<Action> queues = new Queue<Action>();

		private readonly object hybirdLock = new object();

		/// <summary>
		/// 当前的对象创建的时间<br />
		/// Current object creation time
		/// </summary>
		public DateTime CreateTime { get; private set; }

		/// <summary>
		/// 当前的对象最后一次活跃的时间<br />
		/// Last active time of the current object
		/// </summary>
		public DateTime ActiveTime { get; private set; }

		/// <summary>
		/// 当前的文件的读取次数，通常也是下载次数。<br />
		/// The current number of reads of the file, usually also the number of downloads.
		/// </summary>
		public long DownloadTimes { get; private set; }

		/// <summary>
		/// 实例化一个文件标记对象，需要传入日志信息和文件名<br />
		/// To instantiate a file tag object, you need to pass in log information and file name
		/// </summary>
		/// <param name="logNet">日志对象</param>
		/// <param name="fileName">完整的文件名称</param>
		public FileMarkId(ILogNet logNet, string fileName)
		{
			this.logNet = logNet;
			this.fileName = fileName;
			CreateTime = DateTime.Now;
			ActiveTime = DateTime.Now;
		}

		/// <summary>
		/// 新增一个对当前的文件的操作，如果没有读取，就直接执行，如果有读取，就等待读取完成的时候执行。<br />
		/// Add an operation on the current file. If there is no read, it will be executed directly. If there is a read, it will be executed when the read is completed.
		/// </summary>
		/// <param name="action">对当前文件的操作内容</param>
		public void AddOperation(Action action)
		{
			lock (hybirdLock)
			{
				if (readStatus == 0)
				{
					action?.Invoke();
					return;
				}
				logNet?.WriteDebug(ToString(), "action delay");
				queues.Enqueue(action);
			}
		}

		/// <summary>
		/// 获取该对象是否能被清除<br />
		/// Gets whether the object can be cleared
		/// </summary>
		/// <returns>是否能够删除</returns>
		public bool CanClear()
		{
			bool result = false;
			lock (hybirdLock)
			{
				result = readStatus == 0 && queues.Count == 0;
			}
			return result;
		}

		/// <summary>
		/// 进入文件的读取状态<br />
		/// Enter the read state of the file
		/// </summary>
		public void EnterReadOperator()
		{
			lock (hybirdLock)
			{
				readStatus++;
				ActiveTime = DateTime.Now;
			}
		}

		/// <summary>
		/// 离开本次的文件读取状态，如果没有任何的客户端在读取了，就执行缓存队列里的操作信息。<br />
		/// Leaving the current file reading status, if no client is reading, the operation information in the cache queue is executed.
		/// </summary>
		public void LeaveReadOperator()
		{
			lock (hybirdLock)
			{
				readStatus--;
				DownloadTimes++;
				if (readStatus == 0)
				{
					while (queues.Count > 0)
					{
						try
						{
							queues.Dequeue()?.Invoke();
							logNet?.WriteDebug(ToString(), "action delay execute");
						}
						catch (Exception ex)
						{
							logNet?.WriteException("FileMarkId", "File Action Failed:", ex);
						}
					}
				}
				ActiveTime = DateTime.Now;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "FileMarkId[" + fileName + "]";
		}
	}
}
