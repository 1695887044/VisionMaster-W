using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.Reflection;

namespace HslCommunication.LogNet
{
	/// <summary>
	/// 日志存储类的基类，提供一些基础的服务
	/// </summary>
	/// <remarks>
	/// 基于此类可以实现任意的规则的日志存储规则，欢迎大家补充实现，本组件实现了3个日志类
	/// <list type="number">
	/// <item>单文件日志类 <see cref="T:HslCommunication.LogNet.LogNetSingle" /></item>
	/// <item>根据文件大小的类 <see cref="T:HslCommunication.LogNet.LogNetFileSize" /></item>
	/// <item>根据时间进行存储的类 <see cref="T:HslCommunication.LogNet.LogNetDateTime" /></item>
	/// </list>
	/// </remarks>
	public abstract class LogNetBase : IDisposable
	{
		/// <summary>
		/// 文件存储的锁
		/// </summary>
		protected SimpleHybirdLock m_fileSaveLock;

		private HslMessageDegree m_messageDegree = HslMessageDegree.DEBUG;

		private Queue<HslMessageItem> m_WaitForSave;

		private SimpleHybirdLock m_simpleHybirdLock;

		private int m_SaveStatus = 0;

		private List<string> filtrateKeyword;

		private object filtrateLock;

		private string lastLogSaveFileName = string.Empty;

		private bool disposedValue = false;

		/// <inheritdoc cref="P:HslCommunication.LogNet.ILogNet.LogSaveMode" />
		public LogSaveMode LogSaveMode { get; protected set; }

		/// <inheritdoc cref="P:HslCommunication.LogNet.ILogNet.LogNetStatistics" />
		public LogStatistics LogNetStatistics { get; set; }

		/// <inheritdoc cref="P:HslCommunication.LogNet.ILogNet.ConsoleOutput" />
		public bool ConsoleOutput { get; set; }

		/// <inheritdoc cref="P:HslCommunication.LogNet.ILogNet.LogThreadID" />
		public bool LogThreadID { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.LogNet.ILogNet.LogStxAsciiCode" />
		public bool LogStxAsciiCode { get; set; } = true;


		/// <inheritdoc cref="P:HslCommunication.LogNet.ILogNet.HourDeviation" />
		public int HourDeviation { get; set; } = 0;


		/// <inheritdoc cref="E:HslCommunication.LogNet.ILogNet.BeforeSaveToFile" />
		public event EventHandler<HslEventArgs> BeforeSaveToFile = null;

		/// <summary>
		/// 实例化一个日志对象<br />
		/// Instantiate a log object
		/// </summary>
		public LogNetBase()
		{
			m_fileSaveLock = new SimpleHybirdLock();
			m_simpleHybirdLock = new SimpleHybirdLock();
			m_WaitForSave = new Queue<HslMessageItem>();
			filtrateKeyword = new List<string>();
			filtrateLock = new object();
		}

		private void OnBeforeSaveToFile(HslEventArgs args)
		{
			this.BeforeSaveToFile?.Invoke(this, args);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteDebug(System.String)" />
		[HslMqttApi]
		public void WriteDebug(string text)
		{
			WriteDebug(string.Empty, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteDebug(System.String,System.String)" />
		[HslMqttApi(ApiTopic = "WriteDebugKeyWord")]
		public void WriteDebug(string keyWord, string text)
		{
			RecordMessage(HslMessageDegree.DEBUG, keyWord, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteInfo(System.String)" />
		[HslMqttApi]
		public void WriteInfo(string text)
		{
			WriteInfo(string.Empty, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteInfo(System.String,System.String)" />
		[HslMqttApi(ApiTopic = "WriteInfoKeyWord")]
		public void WriteInfo(string keyWord, string text)
		{
			RecordMessage(HslMessageDegree.INFO, keyWord, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteWarn(System.String)" />
		[HslMqttApi]
		public void WriteWarn(string text)
		{
			WriteWarn(string.Empty, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteWarn(System.String,System.String)" />
		[HslMqttApi(ApiTopic = "WriteWarnKeyWord")]
		public void WriteWarn(string keyWord, string text)
		{
			RecordMessage(HslMessageDegree.WARN, keyWord, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteError(System.String)" />
		[HslMqttApi]
		public void WriteError(string text)
		{
			WriteError(string.Empty, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteError(System.String,System.String)" />
		[HslMqttApi(ApiTopic = "WriteErrorKeyWord")]
		public void WriteError(string keyWord, string text)
		{
			RecordMessage(HslMessageDegree.ERROR, keyWord, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteFatal(System.String)" />
		[HslMqttApi]
		public void WriteFatal(string text)
		{
			WriteFatal(string.Empty, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteFatal(System.String,System.String)" />
		[HslMqttApi(ApiTopic = "WriteFatalKeyWord")]
		public void WriteFatal(string keyWord, string text)
		{
			RecordMessage(HslMessageDegree.FATAL, keyWord, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteException(System.String,System.Exception)" />
		public void WriteException(string keyWord, Exception ex)
		{
			WriteException(keyWord, string.Empty, ex);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteException(System.String,System.String,System.Exception)" />
		public void WriteException(string keyWord, string text, Exception ex)
		{
			RecordMessage(HslMessageDegree.FATAL, keyWord, LogNetManagment.GetSaveStringFromException(text, ex));
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.RecordMessage(HslCommunication.LogNet.HslMessageDegree,System.String,System.String)" />
		public void RecordMessage(HslMessageDegree degree, string keyWord, string text)
		{
			WriteToFile(degree, keyWord, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteDescrition(System.String)" />
		[HslMqttApi]
		public void WriteDescrition(string description)
		{
			if (string.IsNullOrEmpty(description))
			{
				return;
			}
			StringBuilder stringBuilder = new StringBuilder("\u0002");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("\u0002/");
			int num = 118 - CalculateStringOccupyLength(description);
			if (num >= 8)
			{
				int num2 = (num - 8) / 2;
				AppendCharToStringBuilder(stringBuilder, '*', num2);
				stringBuilder.Append("   ");
				stringBuilder.Append(description);
				stringBuilder.Append("   ");
				if (num % 2 == 0)
				{
					AppendCharToStringBuilder(stringBuilder, '*', num2);
				}
				else
				{
					AppendCharToStringBuilder(stringBuilder, '*', num2 + 1);
				}
			}
			else if (num >= 2)
			{
				int num3 = (num - 2) / 2;
				AppendCharToStringBuilder(stringBuilder, '*', num3);
				stringBuilder.Append(description);
				if (num % 2 == 0)
				{
					AppendCharToStringBuilder(stringBuilder, '*', num3);
				}
				else
				{
					AppendCharToStringBuilder(stringBuilder, '*', num3 + 1);
				}
			}
			else
			{
				stringBuilder.Append(description);
			}
			stringBuilder.Append("/");
			stringBuilder.Append(Environment.NewLine);
			RecordMessage(HslMessageDegree.None, string.Empty, stringBuilder.ToString());
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteAnyString(System.String)" />
		[HslMqttApi]
		public void WriteAnyString(string text)
		{
			RecordMessage(HslMessageDegree.None, string.Empty, text);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.WriteNewLine" />
		[HslMqttApi]
		public void WriteNewLine()
		{
			RecordMessage(HslMessageDegree.None, string.Empty, "\u0002" + Environment.NewLine);
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.SetMessageDegree(HslCommunication.LogNet.HslMessageDegree)" />
		public void SetMessageDegree(HslMessageDegree degree)
		{
			m_messageDegree = degree;
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.FiltrateKeyword(System.String)" />
		[HslMqttApi]
		public void FiltrateKeyword(string keyword)
		{
			lock (filtrateLock)
			{
				if (!filtrateKeyword.Contains(keyword))
				{
					filtrateKeyword.Add(keyword);
				}
			}
		}

		/// <inheritdoc cref="M:HslCommunication.LogNet.ILogNet.RemoveFiltrate(System.String)" />
		[HslMqttApi]
		public void RemoveFiltrate(string keyword)
		{
			lock (filtrateLock)
			{
				if (filtrateKeyword.Contains(keyword))
				{
					filtrateKeyword.Remove(keyword);
				}
			}
		}

		private void WriteToFile(HslMessageDegree degree, string keyword, string text)
		{
			if (degree <= m_messageDegree)
			{
				HslMessageItem hslMessageItem = GetHslMessageItem(degree, keyword, text);
				AddItemToCache(hslMessageItem, start: true);
			}
		}

		private void AddItemToCache(HslMessageItem item, bool start)
		{
			m_simpleHybirdLock.Enter();
			m_WaitForSave.Enqueue(item);
			m_simpleHybirdLock.Leave();
			if (start)
			{
				StartSaveFile();
			}
		}

		private void StartSaveFile()
		{
			if (Interlocked.CompareExchange(ref m_SaveStatus, 1, 0) == 0)
			{
				ThreadPool.QueueUserWorkItem(ThreadPoolSaveFile, null);
			}
		}

		private HslMessageItem GetAndRemoveLogItem()
		{
			HslMessageItem result = null;
			m_simpleHybirdLock.Enter();
			try
			{
				result = ((m_WaitForSave.Count > 0) ? m_WaitForSave.Dequeue() : null);
			}
			catch
			{
			}
			m_simpleHybirdLock.Leave();
			return result;
		}

		private void ConsoleWriteLog(HslMessageItem log)
		{
			if (log.Degree == HslMessageDegree.DEBUG)
			{
				Console.ForegroundColor = ConsoleColor.DarkGray;
			}
			else if (log.Degree == HslMessageDegree.INFO)
			{
				Console.ForegroundColor = ConsoleColor.White;
			}
			else if (log.Degree == HslMessageDegree.WARN)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
			}
			else if (log.Degree == HslMessageDegree.ERROR)
			{
				Console.ForegroundColor = ConsoleColor.Red;
			}
			else if (log.Degree == HslMessageDegree.FATAL)
			{
				Console.ForegroundColor = ConsoleColor.DarkRed;
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.White;
			}
			Console.WriteLine(HslMessageFormat(log, writeFile: false));
		}

		private void ThreadPoolSaveFile(object obj)
		{
			HslMessageItem andRemoveLogItem = GetAndRemoveLogItem();
			m_fileSaveLock.Enter();
			string fileSaveName = GetFileSaveName();
			bool createNewLogFile = false;
			if (!string.IsNullOrEmpty(fileSaveName))
			{
				if (fileSaveName != lastLogSaveFileName)
				{
					createNewLogFile = true;
					lastLogSaveFileName = fileSaveName;
				}
				StreamWriter streamWriter = null;
				try
				{
					streamWriter = new StreamWriter(fileSaveName, append: true, Encoding.UTF8);
					while (andRemoveLogItem != null)
					{
						if (!andRemoveLogItem.HasLogOutput)
						{
							andRemoveLogItem.HasLogOutput = true;
							if (ConsoleOutput)
							{
								ConsoleWriteLog(andRemoveLogItem);
							}
							OnBeforeSaveToFile(new HslEventArgs
							{
								HslMessage = andRemoveLogItem
							});
							LogNetStatistics?.StatisticsAdd(1L);
						}
						bool flag = true;
						lock (filtrateLock)
						{
							flag = !filtrateKeyword.Contains(andRemoveLogItem.KeyWord);
						}
						if (andRemoveLogItem.Cancel)
						{
							flag = false;
						}
						if (flag)
						{
							streamWriter.Write(HslMessageFormat(andRemoveLogItem, writeFile: true));
							streamWriter.Write(Environment.NewLine);
							streamWriter.Flush();
						}
						andRemoveLogItem = GetAndRemoveLogItem();
					}
				}
				catch
				{
					Interlocked.Increment(ref andRemoveLogItem.WriteRetryTimes);
					AddItemToCache(andRemoveLogItem, start: false);
				}
				finally
				{
					streamWriter?.Dispose();
				}
			}
			else
			{
				while (andRemoveLogItem != null)
				{
					if (ConsoleOutput)
					{
						ConsoleWriteLog(andRemoveLogItem);
					}
					OnBeforeSaveToFile(new HslEventArgs
					{
						HslMessage = andRemoveLogItem
					});
					andRemoveLogItem = GetAndRemoveLogItem();
				}
			}
			m_fileSaveLock.Leave();
			Interlocked.Exchange(ref m_SaveStatus, 0);
			OnWriteCompleted(createNewLogFile);
			if (m_WaitForSave.Count > 0)
			{
				HslHelper.ThreadSleep(0);
				StartSaveFile();
			}
		}

		/// <summary>
		/// 根据需要存储的日志消息，获取实际存储的字符串，重写本方法就可以自定义输出内容<br />
		/// According to the log message that needs to be stored, get the actual stored string, and the rewrite method can customize the output content
		/// </summary>
		/// <param name="hslMessage">等待存储或是输出的日志对象</param>
		/// <param name="writeFile">是否输出到文件里</param>
		/// <returns>最终等待存储的字符串内容</returns>
		protected virtual string HslMessageFormat(HslMessageItem hslMessage, bool writeFile)
		{
			StringBuilder stringBuilder = new StringBuilder();
			if (hslMessage.Degree != HslMessageDegree.None)
			{
				if (writeFile && LogStxAsciiCode)
				{
					stringBuilder.Append("\u0002");
				}
				stringBuilder.Append("[");
				stringBuilder.Append(LogNetManagment.GetDegreeDescription(hslMessage.Degree));
				stringBuilder.Append("] ");
				stringBuilder.Append(hslMessage.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"));
				stringBuilder.Append(" ");
				if (LogThreadID)
				{
					stringBuilder.Append("Thread:[");
					stringBuilder.Append(hslMessage.ThreadId.ToString("D3"));
					stringBuilder.Append("] ");
				}
				if (hslMessage.WriteRetryTimes == 2)
				{
					stringBuilder.Append("[Retry] ");
				}
				else if (hslMessage.WriteRetryTimes > 2)
				{
					stringBuilder.Append($"[Retry{hslMessage.WriteRetryTimes}] ");
				}
				if (!string.IsNullOrEmpty(hslMessage.KeyWord))
				{
					stringBuilder.Append(hslMessage.KeyWord);
					stringBuilder.Append(" : ");
				}
			}
			stringBuilder.Append(hslMessage.Text);
			return stringBuilder.ToString();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"LogNetBase[{LogSaveMode}]";
		}

		/// <inheritdoc />
		protected virtual string GetFileSaveName()
		{
			return string.Empty;
		}

		/// <summary>
		/// 当写入文件完成的时候触发，这时候已经释放了文件的句柄了。<br />
		/// Triggered when writing to the file is complete, and the file handle has been released.
		/// </summary>
		protected virtual void OnWriteCompleted(bool createNewLogFile)
		{
		}

		private HslMessageItem GetHslMessageItem(HslMessageDegree degree, string keyWord, string text)
		{
			if (HourDeviation == 0)
			{
				return new HslMessageItem
				{
					KeyWord = keyWord,
					Degree = degree,
					Text = text,
					ThreadId = Thread.CurrentThread.ManagedThreadId
				};
			}
			return new HslMessageItem
			{
				KeyWord = keyWord,
				Degree = degree,
				Text = text,
				ThreadId = Thread.CurrentThread.ManagedThreadId,
				Time = DateTime.Now.AddHours(HourDeviation)
			};
		}

		private int CalculateStringOccupyLength(string str)
		{
			if (string.IsNullOrEmpty(str))
			{
				return 0;
			}
			int num = 0;
			for (int i = 0; i < str.Length; i++)
			{
				num = ((str[i] < '一' || str[i] > '龻') ? (num + 1) : (num + 2));
			}
			return num;
		}

		private void AppendCharToStringBuilder(StringBuilder sb, char c, int count)
		{
			for (int i = 0; i < count; i++)
			{
				sb.Append(c);
			}
		}

		/// <summary>
		/// 释放资源
		/// </summary>
		/// <param name="disposing">是否初次调用</param>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					this.BeforeSaveToFile = null;
					m_simpleHybirdLock.Enter();
					m_WaitForSave.Clear();
					m_simpleHybirdLock.Leave();
					m_simpleHybirdLock.Dispose();
					m_fileSaveLock.Dispose();
				}
				disposedValue = true;
			}
		}

		/// <inheritdoc cref="M:System.IDisposable.Dispose" />
		public void Dispose()
		{
			Dispose(disposing: true);
		}
	}
}
