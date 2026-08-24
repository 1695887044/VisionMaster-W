using System;
using System.Threading;

namespace HslCommunication.LogNet
{
	/// <summary>
	/// 单条日志的记录信息，包含了消息等级，线程号，关键字，文本信息<br />
	/// Record information of a single log, including message level, thread number, keywords, text information
	/// </summary>
	public class HslMessageItem
	{
		private static long IdNumber;

		/// <summary>
		/// 写入到文件的尝试的次数，一般都是1次，可能部分情况会存在多次写入才成功，那么此处的数字就是变为实际尝试的次数。<br />
		/// </summary>
		internal int WriteRetryTimes = 1;

		/// <summary>
		/// 指示当前的日志是否已经输出到控制台，有可能因为可能异常，不进行重复输出
		/// </summary>
		internal bool HasLogOutput = false;

		/// <summary>
		/// 单个记录信息的标识ID，程序重新运行时清空，代表程序从运行以来的日志计数，不管存储的或是未存储的<br />
		/// The ID of a single record of information. It is cleared when the program is re-run. 
		/// It represents the log count of the program since it was run, whether stored or unstored.
		/// </summary>
		public long Id { get; private set; }

		/// <summary>
		/// 消息的等级，包括DEBUG，INFO，WARN，ERROR，FATAL，NONE共计六个等级<br />
		/// Message levels, including DEBUG, INFO, WARN, ERROR, FATAL, NONE total six levels
		/// </summary>
		public HslMessageDegree Degree { get; set; } = HslMessageDegree.DEBUG;


		/// <summary>
		/// 线程ID，发生异常时的线程号<br />
		/// Thread ID, the thread number when the exception occurred
		/// </summary>
		public int ThreadId { get; set; }

		/// <summary>
		/// 消息文本，记录日志的时候给定<br />
		/// Message text, given when logging
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// 记录日志的时间，而非存储日志的时间<br />
		/// The time the log was recorded, not the time it was stored
		/// </summary>
		public DateTime Time { get; set; }

		/// <summary>
		/// 消息的关键字<br />
		/// Keyword of the message
		/// </summary>
		public string KeyWord { get; set; }

		/// <summary>
		/// 是否取消写入到文件中去，在事件 <see cref="E:HslCommunication.LogNet.LogNetBase.BeforeSaveToFile" /> 触发的时候捕获即可设置。<br />
		/// Whether to cancel writing to the file, can be set when the event <see cref="E:HslCommunication.LogNet.LogNetBase.BeforeSaveToFile" /> is triggered.
		/// </summary>
		public bool Cancel { get; set; }

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public HslMessageItem()
		{
			Id = Interlocked.Increment(ref IdNumber);
			Time = DateTime.Now;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (Degree != HslMessageDegree.None)
			{
				if (string.IsNullOrEmpty(KeyWord))
				{
					return $"[{LogNetManagment.GetDegreeDescription(Degree)}] {Time:yyyy-MM-dd HH:mm:ss.fff} Thread [{ThreadId:D3}] {Text}";
				}
				return $"[{LogNetManagment.GetDegreeDescription(Degree)}] {Time:yyyy-MM-dd HH:mm:ss.fff} Thread [{ThreadId:D3}] {KeyWord} : {Text}";
			}
			return Text;
		}

		/// <summary>
		/// 返回表示当前对象的字符串，剔除了关键字<br />
		/// Returns a string representing the current object, excluding keywords
		/// </summary>
		/// <returns>字符串信息</returns>
		public string ToStringWithoutKeyword()
		{
			if (Degree != HslMessageDegree.None)
			{
				return $"[{LogNetManagment.GetDegreeDescription(Degree)}] {Time:yyyy-MM-dd HH:mm:ss.fff} Thread [{ThreadId:D3}] {Text}";
			}
			return Text;
		}
	}
}
