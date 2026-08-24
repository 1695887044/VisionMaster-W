using System.Threading;

namespace HslCommunication.Core
{
	/// <summary>
	/// 一个双检锁的示例，适合一些占内存的静态数据对象，获取的时候才实例化真正的对象
	/// </summary>
	internal sealed class Singleton
	{
		private static object m_lock = new object();

		private static Singleton SValue = null;

		public static Singleton GetSingleton()
		{
			if (SValue != null)
			{
				return SValue;
			}
			Monitor.Enter(m_lock);
			if (SValue == null)
			{
				Singleton value = new Singleton();
				Volatile.Write(ref SValue, value);
				SValue = new Singleton();
			}
			Monitor.Exit(m_lock);
			return SValue;
		}
	}
}
