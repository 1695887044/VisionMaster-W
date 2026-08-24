using System;
using System.IO;
using System.Threading;
using HslCommunication.Core;
using HslCommunication.Reflection;

namespace HslCommunication.LogNet
{
	/// <summary>
	/// 一个用于数值范围记录的类，可以按照时间进行分类统计，比如计算一个温度值的每天的开始值，结束值，最大值，最小值，平均值信息。详细见API文档信息。<br />
	/// A class used to record the value range, which can be classified according to time, such as calculating the start value, end value, 
	/// maximum value, minimum value, and average value of a temperature value. See the API documentation for details.
	/// </summary>
	/// <example>
	/// 我们来举个例子：我们需要对一个温度数据进行分析，分析60天之内的最大值最小值等等信息
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\LogNet\LogValueLimitSample.cs" region="Sample1" title="简单的记录调用次数" />
	/// 因为这个数据是保存在内存里的，程序重新运行就丢失了，如果希望让这个数据一直在程序的话，在软件退出的时候需要存储文件，在软件启动的时候，加载文件数据
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\LogNet\LogValueLimitSample.cs" region="Sample2" title="存储与加载" />
	/// </example>
	public class LogValueLimit : LogStatisticsBase<ValueLimit>
	{
		private RegularByteTransform byteTransform;

		private long valueCount = 0L;

		/// <summary>
		/// 当前设置数据的总的数量<br />
		/// The total amount of data currently set
		/// </summary>
		[HslMqttApi(HttpMethod = "GET", Description = "The total amount of data currently set")]
		public long ValueCount => valueCount;

		/// <inheritdoc cref="M:HslCommunication.LogNet.LogStatisticsBase`1.#ctor(HslCommunication.LogNet.GenerateMode,System.Int32)" />
		public LogValueLimit(GenerateMode generateMode, int dataCount)
			: base(generateMode, dataCount)
		{
			byteTransform = new RegularByteTransform();
		}

		/// <summary>
		/// 新增一个数据用于分析，将会根据当前的时间来决定插入数据位置，如果数据位置发生了变化，则数据向左发送移动。如果没有移动或是移动完成后，最后一个数进行数据更新，包括最大值，最小值，平均值。<br />
		/// Add a new data for analysis, and will determine the position to insert the data according to the current time. If the data position changes, 
		/// the data will be sent to the left. If there is no movement or after the movement is completed, data update for the last number, including maximum, minimum, and average.
		/// </summary>
		/// <param name="value">当前的新的数据值</param>
		[HslMqttApi(Description = "Add a new data for analysis")]
		public void AnalysisNewValue(double value)
		{
			Interlocked.Increment(ref valueCount);
			StatisticsCustomAction((ValueLimit m) => m.SetNewValue(value));
		}

		/// <summary>
		/// 新增一个数据用于分析，将会指定的时间来决定插入数据位置，如果数据位置发生了变化，则数据向左发送移动。如果没有移动或是移动完成后，最后一个数进行数据更新，包括最大值，最小值，平均值。<br />
		/// dd a new data for analysis, and will determine the position to insert the data according to the specified time. If the data position changes, 
		/// the data will be sent to the left. If there is no movement or after the movement is completed, data update for the last number, including maximum, minimum, and average.
		/// </summary>
		/// <param name="value">当前的新的数据值</param>
		/// <param name="time">指定的时间信息</param>
		[HslMqttApi(Description = "Add a new data for analysis")]
		public void AnalysisNewValueByTime(double value, DateTime time)
		{
			Interlocked.Increment(ref valueCount);
			StatisticsCustomAction((ValueLimit m) => m.SetNewValue(value), time);
		}

		/// <summary>
		/// 将当前所有的数据都写入到二进制的内存里去，可以用来写入文件或是网络发送。<br />
		/// Write all current data into binary memory, which can be used to write files or send over the network.
		/// </summary>
		/// <returns>二进制的byte数组</returns>
		public byte[] SaveToBinary()
		{
			OperateResult<long, ValueLimit[]> statisticsSnapAndDataMark = GetStatisticsSnapAndDataMark();
			int num = 1024;
			int num2 = 64;
			byte[] array = new byte[statisticsSnapAndDataMark.Content2.Length * num2 + num];
			BitConverter.GetBytes(305419897).CopyTo(array, 0);
			BitConverter.GetBytes((ushort)num).CopyTo(array, 4);
			BitConverter.GetBytes((ushort)base.GenerateMode).CopyTo(array, 6);
			BitConverter.GetBytes(statisticsSnapAndDataMark.Content2.Length).CopyTo(array, 8);
			BitConverter.GetBytes(statisticsSnapAndDataMark.Content1).CopyTo(array, 12);
			BitConverter.GetBytes(valueCount).CopyTo(array, 20);
			BitConverter.GetBytes(num2).CopyTo(array, 28);
			for (int i = 0; i < statisticsSnapAndDataMark.Content2.Length; i++)
			{
				byteTransform.TransByte(statisticsSnapAndDataMark.Content2[i].StartValue).CopyTo(array, i * num2 + num);
				byteTransform.TransByte(statisticsSnapAndDataMark.Content2[i].Current).CopyTo(array, i * num2 + num + 8);
				byteTransform.TransByte(statisticsSnapAndDataMark.Content2[i].MaxValue).CopyTo(array, i * num2 + num + 16);
				byteTransform.TransByte(statisticsSnapAndDataMark.Content2[i].MinValue).CopyTo(array, i * num2 + num + 24);
				byteTransform.TransByte(statisticsSnapAndDataMark.Content2[i].Average).CopyTo(array, i * num2 + num + 32);
				byteTransform.TransByte(statisticsSnapAndDataMark.Content2[i].Count).CopyTo(array, i * num2 + num + 40);
			}
			return array;
		}

		/// <summary>
		/// 将当前的统计信息及数据内容写入到指定的文件里面，需要指定文件的路径名称<br />
		/// Write the current statistical information and data content to the specified file, you need to specify the path name of the file
		/// </summary>
		/// <param name="fileName">文件的完整的路径名称</param>
		public void SaveToFile(string fileName)
		{
			File.WriteAllBytes(fileName, SaveToBinary());
		}

		/// <summary>
		/// 从二进制的数据内容加载，会对数据的合法性进行检查，如果数据不匹配，会报异常<br />
		/// Loading from the binary data content will check the validity of the data. If the data does not match, an exception will be reported
		/// </summary>
		/// <param name="buffer">等待加载的二进制数据</param>
		/// <exception cref="T:System.Exception"></exception>
		public void LoadFromBinary(byte[] buffer)
		{
			int num = BitConverter.ToInt32(buffer, 0);
			if (num != 305419897)
			{
				throw new Exception("File is not LogValueLimit file, can't load data.");
			}
			int num2 = BitConverter.ToUInt16(buffer, 4);
			GenerateMode generateMode = (GenerateMode)BitConverter.ToUInt16(buffer, 6);
			int num3 = BitConverter.ToInt32(buffer, 8);
			long num4 = BitConverter.ToInt64(buffer, 12);
			long num5 = BitConverter.ToInt64(buffer, 20);
			int num6 = BitConverter.ToInt32(buffer, 28);
			base.generateMode = generateMode;
			valueCount = num5;
			ValueLimit[] array = new ValueLimit[num3];
			for (int i = 0; i < array.Length; i++)
			{
				array[i].StartValue = byteTransform.TransDouble(buffer, i * num6 + num2);
				array[i].Current = byteTransform.TransDouble(buffer, i * num6 + num2 + 8);
				array[i].MaxValue = byteTransform.TransDouble(buffer, i * num6 + num2 + 16);
				array[i].MinValue = byteTransform.TransDouble(buffer, i * num6 + num2 + 24);
				array[i].Average = byteTransform.TransDouble(buffer, i * num6 + num2 + 32);
				array[i].Count = byteTransform.TransInt32(buffer, i * num6 + num2 + 40);
			}
			Reset(array, num4);
		}

		/// <summary>
		/// 从指定的文件加载对应的统计信息，通常是调用<see cref="M:HslCommunication.LogNet.LogValueLimit.SaveToFile(System.String)" />方法存储的文件，如果文件不存在，将会跳过加载<br />
		/// Load the corresponding statistical information from the specified file, usually the file stored by calling the <see cref="M:HslCommunication.LogNet.LogValueLimit.SaveToFile(System.String)" /> method. 
		/// If the file does not exist, the loading will be skipped
		/// </summary>
		/// <param name="fileName">文件的完整的路径名称</param>
		public void LoadFromFile(string fileName)
		{
			if (File.Exists(fileName))
			{
				LoadFromBinary(File.ReadAllBytes(fileName));
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"LogValueLimit[{base.GenerateMode}:{base.ArrayLength}]";
		}
	}
}
