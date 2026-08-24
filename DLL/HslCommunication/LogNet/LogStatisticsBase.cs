using System;
using HslCommunication.Reflection;

namespace HslCommunication.LogNet
{
	/// <summary>
	/// 一个按照实际进行数据分割的辅助基类，可以用于实现对某个的API按照每天进行调用次数统计，也可以实现对某个设备数据按照天进行最大最小值均值分析，这些都是需要继承实现。<br />
	/// An auxiliary base class that divides the data according to the actual data can be used to implement statistics on the number of calls per day for a certain API, and it can also implement the maximum and minimum average analysis of a certain device data according to the day. These all need to be inherited. .
	/// </summary>
	/// <typeparam name="T">统计的数据类型</typeparam>
	public class LogStatisticsBase<T>
	{
		private T[] statistics = null;

		/// <summary>
		/// 当前的实际模式
		/// </summary>
		protected GenerateMode generateMode = GenerateMode.ByEveryDay;

		private int arrayLength = 30;

		private long lastDataMark = -1L;

		private object lockStatistics;

		/// <summary>
		/// 获取当前的统计类信息时间统计规则<br />
		/// Get the current statistical information time statistics rule
		/// </summary>
		public GenerateMode GenerateMode => generateMode;

		/// <summary>
		/// 获取当前的统计类信息的数据总量<br />
		/// Get the total amount of current statistical information
		/// </summary>
		public int ArrayLength => arrayLength;

		/// <summary>
		/// 实例化一个新的数据统计内容，需要指定当前的时间统计方式，按小时，按天，按月等等，还需要指定统计的数据数量，比如按天统计30天。<br />
		/// To instantiate a new data statistics content, you need to specify the current time statistics method, by hour, by day, by month, etc., 
		/// and also need to specify the number of statistics, such as 30 days by day.
		/// </summary>
		/// <param name="generateMode">时间的统计方式</param>
		/// <param name="arrayLength">数据的数量信息，如果本值为-1，则表示数据一直增长</param>
		public LogStatisticsBase(GenerateMode generateMode, int arrayLength)
		{
			this.generateMode = generateMode;
			this.arrayLength = arrayLength;
			if (this.arrayLength >= 0)
			{
				statistics = new T[arrayLength];
			}
			else
			{
				statistics = new T[1024];
			}
			lastDataMark = GetDataMarkFromDateTime(DateTime.Now);
			lockStatistics = new object();
		}

		/// <summary>
		/// 重置当前的统计信息，需要指定统计的数据内容，最后一个数据的标记信息，本方法主要用于还原统计信息<br />
		/// To reset the current statistical information, you need to specify the content of the statistical data, 
		/// and the tag information of the last data. This method is mainly used to restore statistical information
		/// </summary>
		/// <param name="statistics">统计结果数据信息</param>
		/// <param name="lastDataMark">最后一次标记的内容</param>
		public void Reset(T[] statistics, long lastDataMark)
		{
			if (arrayLength >= 0)
			{
				if (statistics.Length > arrayLength)
				{
					Array.Copy(statistics, statistics.Length - arrayLength, this.statistics, 0, arrayLength);
				}
				else if (statistics.Length < arrayLength)
				{
					Array.Copy(statistics, 0, this.statistics, arrayLength - statistics.Length, statistics.Length);
				}
				else
				{
					this.statistics = statistics;
				}
				arrayLength = statistics.Length;
			}
			else
			{
				this.statistics = statistics;
			}
			this.lastDataMark = lastDataMark;
		}

		/// <summary>
		/// 新增一个统计信息，将会根据当前的时间来决定插入数据位置，如果数据位置发生了变化，则数据向左发送移动。如果没有移动或是移动完成后，最后一个数进行自定义的数据操作<br />
		/// Adding a new statistical information will determine the position to insert the data according to the current time. If the data position changes, 
		/// the data will be sent to the left. If there is no movement or after the movement is completed, Custom data operations on the last number
		/// </summary>
		/// <param name="newValue">增对最后一个数的自定义操作</param>
		protected void StatisticsCustomAction(Func<T, T> newValue)
		{
			lock (lockStatistics)
			{
				long dataMarkFromDateTime = GetDataMarkFromDateTime(DateTime.Now);
				if (lastDataMark != dataMarkFromDateTime)
				{
					int times = (int)(dataMarkFromDateTime - lastDataMark);
					statistics = GetLeftMoveTimes(times);
					lastDataMark = dataMarkFromDateTime;
				}
				statistics[statistics.Length - 1] = newValue(statistics[statistics.Length - 1]);
			}
		}

		/// <summary>
		/// 新增一个统计信息，将会根据当前的时间来决定插入数据位置，如果数据位置发生了变化，则数据向左发送移动。如果没有移动或是移动完成后，最后一个数进行自定义的数据操作<br />
		/// Adding a new statistical information will determine the position to insert the data according to the current time. If the data position changes, 
		/// the data will be sent to the left. If there is no movement or after the movement is completed, Custom data operations on the last number
		/// </summary>
		/// <param name="newValue">增对最后一个数的自定义操作</param>
		/// <param name="time">增加的时间信息</param>
		protected void StatisticsCustomAction(Func<T, T> newValue, DateTime time)
		{
			lock (lockStatistics)
			{
				long dataMarkFromDateTime = GetDataMarkFromDateTime(DateTime.Now);
				if (lastDataMark != dataMarkFromDateTime)
				{
					int num = (int)(dataMarkFromDateTime - lastDataMark);
					if (num > 0)
					{
						statistics = GetLeftMoveTimes(num);
					}
					lastDataMark = dataMarkFromDateTime;
				}
				long dataMarkFromDateTime2 = GetDataMarkFromDateTime(time);
				if (dataMarkFromDateTime2 <= dataMarkFromDateTime)
				{
					int num2 = (int)(dataMarkFromDateTime2 - (dataMarkFromDateTime - statistics.Length + 1));
					if (num2 >= 0 && num2 < statistics.Length)
					{
						statistics[num2] = newValue(statistics[num2]);
					}
				}
			}
		}

		/// <summary>
		/// 获取当前的统计信息的数据快照，这是数据的副本，修改了里面的值不影响<br />
		/// Get a data snapshot of the current statistics. This is a copy of the data. Modifying the value inside does not affect
		/// </summary>
		/// <returns>实际的统计数据信息</returns>
		[HslMqttApi(Description = "Get a data snapshot of the current statistics")]
		public T[] GetStatisticsSnapshot()
		{
			return GetStatisticsSnapAndDataMark().Content2;
		}

		/// <summary>
		/// 根据指定的时间范围来获取统计的数据信息快照，包含起始时间，包含结束时间，这是数据的副本，修改了里面的值不影响<br />
		/// Get a snapshot of statistical data information according to the specified time range, including the start time, 
		/// also the end time. This is a copy of the data. Modifying the value inside does not affect
		/// </summary>
		/// <param name="start">起始时间</param>
		/// <param name="finish">结束时间</param>
		/// <returns>指定实际范围内的数据副本</returns>
		[HslMqttApi(Description = "Get a snapshot of statistical data information according to the specified time range")]
		public T[] GetStatisticsSnapshotByTime(DateTime start, DateTime finish)
		{
			if (finish <= start)
			{
				return new T[0];
			}
			lock (lockStatistics)
			{
				long dataMarkFromDateTime = GetDataMarkFromDateTime(DateTime.Now);
				if (lastDataMark != dataMarkFromDateTime)
				{
					int times = (int)(dataMarkFromDateTime - lastDataMark);
					statistics = GetLeftMoveTimes(times);
					lastDataMark = dataMarkFromDateTime;
				}
				long num = dataMarkFromDateTime - statistics.Length + 1;
				long num2 = GetDataMarkFromDateTime(start);
				long num3 = GetDataMarkFromDateTime(finish);
				if (num2 < num)
				{
					num2 = num;
				}
				if (num3 > dataMarkFromDateTime)
				{
					num3 = dataMarkFromDateTime;
				}
				int num4 = (int)(num2 - num);
				int num5 = (int)(num3 - num2 + 1);
				if (num2 == num3)
				{
					return new T[1] { statistics[num4] };
				}
				T[] array = new T[num5];
				for (int i = 0; i < num5; i++)
				{
					array[i] = statistics[num4 + i];
				}
				return array;
			}
		}

		/// <summary>
		/// 获取当前的统计信息的数据快照，这是数据的副本，修改了里面的值不影响<br />
		/// Get a data snapshot of the current statistics. This is a copy of the data. Modifying the value inside does not affect
		/// </summary>
		/// <returns>实际的统计数据信息</returns>
		public OperateResult<long, T[]> GetStatisticsSnapAndDataMark()
		{
			lock (lockStatistics)
			{
				long dataMarkFromDateTime = GetDataMarkFromDateTime(DateTime.Now);
				if (lastDataMark != dataMarkFromDateTime)
				{
					int times = (int)(dataMarkFromDateTime - lastDataMark);
					statistics = GetLeftMoveTimes(times);
					lastDataMark = dataMarkFromDateTime;
				}
				return OperateResult.CreateSuccessResult(dataMarkFromDateTime, statistics.CopyArray());
			}
		}

		/// <summary>
		/// 根据当前数据统计的时间模式，获取最新的数据标记信息<br />
		/// Obtain the latest data mark information according to the time mode of current data statistics
		/// </summary>
		/// <returns>数据标记</returns>
		[HslMqttApi(Description = "Obtain the latest data mark information according to the time mode of current data statistics")]
		public long GetDataMarkFromTimeNow()
		{
			return GetDataMarkFromDateTime(DateTime.Now);
		}

		/// <summary>
		/// 根据指定的时间，获取到该时间指定的数据标记信息<br />
		/// According to the specified time, get the data mark information specified at that time
		/// </summary>
		/// <param name="dateTime">指定的时间</param>
		/// <returns>数据标记</returns>
		[HslMqttApi(Description = "According to the specified time, get the data mark information specified at that time")]
		public long GetDataMarkFromDateTime(DateTime dateTime)
		{
			return generateMode switch
			{
				GenerateMode.ByEveryMinute => GetMinuteFromTime(dateTime), 
				GenerateMode.ByEveryHour => GetHourFromTime(dateTime), 
				GenerateMode.ByEveryDay => GetDayFromTime(dateTime), 
				GenerateMode.ByEveryWeek => GetWeekFromTime(dateTime), 
				GenerateMode.ByEveryMonth => GetMonthFromTime(dateTime), 
				GenerateMode.ByEverySeason => GetSeasonFromTime(dateTime), 
				GenerateMode.ByEveryYear => GetYearFromTime(dateTime), 
				_ => GetDayFromTime(dateTime), 
			};
		}

		private long GetMinuteFromTime(DateTime dateTime)
		{
			return (long)(dateTime.Date - new DateTime(1970, 1, 1)).Days * 24L * 60 + dateTime.Hour * 60 + dateTime.Minute;
		}

		private long GetHourFromTime(DateTime dateTime)
		{
			return (long)(dateTime.Date - new DateTime(1970, 1, 1)).Days * 24L + dateTime.Hour;
		}

		private long GetDayFromTime(DateTime dateTime)
		{
			return (dateTime.Date - new DateTime(1970, 1, 1)).Days;
		}

		private long GetWeekFromTime(DateTime dateTime)
		{
			return ((long)(dateTime.Date - new DateTime(1970, 1, 1)).Days + 3L) / 7;
		}

		private long GetMonthFromTime(DateTime dateTime)
		{
			return (long)(dateTime.Year - 1970) * 12L + (dateTime.Month - 1);
		}

		private long GetSeasonFromTime(DateTime dateTime)
		{
			return (long)(dateTime.Year - 1970) * 4L + (dateTime.Month - 1) / 3;
		}

		private long GetYearFromTime(DateTime dateTime)
		{
			return dateTime.Year - 1970;
		}

		private T[] GetLeftMoveTimes(int times)
		{
			if (arrayLength >= 0)
			{
				if (times >= statistics.Length || times <= -statistics.Length)
				{
					return new T[arrayLength];
				}
				T[] array = new T[arrayLength];
				if (times >= 0)
				{
					Array.Copy(statistics, times, array, 0, statistics.Length - times);
				}
				else
				{
					Array.Copy(statistics, 0, array, -times, statistics.Length + times);
				}
				return array;
			}
			T[] array2 = new T[statistics.Length + times];
			Array.Copy(statistics, 0, array2, 0, Math.Min(statistics.Length, array2.Length));
			return array2;
		}
	}
}
