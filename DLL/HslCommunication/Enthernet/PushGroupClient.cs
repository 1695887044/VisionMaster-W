using System;
using System.Collections.Generic;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.Core.Net;

namespace HslCommunication.Enthernet
{
	/// <summary>
	/// 订阅分类的核心组织对象
	/// </summary>
	public class PushGroupClient : IDisposable
	{
		private List<AppSession> appSessions;

		private SimpleHybirdLock simpleHybird;

		private long pushTimesCount = 0L;

		private bool disposedValue = false;

		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public PushGroupClient()
		{
			appSessions = new List<AppSession>();
			simpleHybird = new SimpleHybirdLock();
		}

		/// <summary>
		/// 新增一个订阅的会话
		/// </summary>
		/// <param name="session">会话</param>
		public void AddPushClient(AppSession session)
		{
			simpleHybird.Enter();
			appSessions.Add(session);
			simpleHybird.Leave();
		}

		/// <summary>
		/// 移除一个订阅的会话
		/// </summary>
		/// <param name="clientID">客户端唯一的ID信息</param>
		public bool RemovePushClient(string clientID)
		{
			bool result = false;
			simpleHybird.Enter();
			for (int i = 0; i < appSessions.Count; i++)
			{
				if (appSessions[i].ClientUniqueID == clientID)
				{
					appSessions[i].WorkSocket?.Close();
					appSessions.RemoveAt(i);
					result = true;
					break;
				}
			}
			simpleHybird.Leave();
			return result;
		}

		/// <summary>
		/// 使用固定的发送方法将数据发送出去
		/// </summary>
		/// <param name="content">数据内容</param>
		/// <param name="send">指定的推送方法</param>
		public void PushString(string content, Action<AppSession, string> send)
		{
			simpleHybird.Enter();
			Interlocked.Increment(ref pushTimesCount);
			for (int i = 0; i < appSessions.Count; i++)
			{
				send(appSessions[i], content);
			}
			simpleHybird.Leave();
		}

		/// <summary>
		/// 移除并关闭所有的客户端
		/// </summary>
		public int RemoveAllClient()
		{
			int num = 0;
			simpleHybird.Enter();
			for (int i = 0; i < appSessions.Count; i++)
			{
				appSessions[i].WorkSocket?.Close();
			}
			num = appSessions.Count;
			appSessions.Clear();
			simpleHybird.Leave();
			return num;
		}

		/// <summary>
		/// 获取是否推送过数据
		/// </summary>
		/// <returns>True代表有，False代表没有</returns>
		public bool HasPushedContent()
		{
			return pushTimesCount > 0;
		}

		/// <summary>
		/// 释放当前的程序所占用的资源
		/// </summary>
		/// <param name="disposing">是否释放资源</param>
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
				}
				simpleHybird.Enter();
				appSessions.ForEach(delegate(AppSession m)
				{
					m.WorkSocket?.Close();
				});
				appSessions.Clear();
				simpleHybird.Leave();
				simpleHybird.Dispose();
				disposedValue = true;
			}
		}

		/// <summary>
		/// 释放当前的对象所占用的资源
		/// </summary>
		public void Dispose()
		{
			Dispose(disposing: true);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return "PushGroupClient";
		}
	}
}
