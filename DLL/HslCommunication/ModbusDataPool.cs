using System;
using System.Linq;
using HslCommunication;
using HslCommunication.BasicFramework;
using HslCommunication.Core.Address;
using HslCommunication.ModBus;

internal class ModbusDataPool : IDisposable
{
	private byte station = 1;

	private SoftBuffer coilBuffer;

	private SoftBuffer inputBuffer;

	private SoftBuffer registerBuffer;

	private SoftBuffer inputRegisterBuffer;

	private const int DataPoolLength = 65536;

	public ModbusDataPool(byte station)
	{
		coilBuffer = new SoftBuffer(65536);
		inputBuffer = new SoftBuffer(65536);
		registerBuffer = new SoftBuffer(131072);
		inputRegisterBuffer = new SoftBuffer(131072);
		registerBuffer.IsBoolReverseByWord = true;
		inputRegisterBuffer.IsBoolReverseByWord = true;
		this.station = station;
	}

	public OperateResult<byte[]> Read(string address, ushort length)
	{
		OperateResult<ModbusAddress> operateResult = ModbusInfo.AnalysisAddress(address, station, isStartWithZero: true, 3);
		if (!operateResult.IsSuccess)
		{
			return OperateResult.CreateFailedResult<byte[]>(operateResult);
		}
		if (operateResult.Content.Function == 3)
		{
			return OperateResult.CreateSuccessResult(registerBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
		}
		if (operateResult.Content.Function == 4)
		{
			return OperateResult.CreateSuccessResult(inputRegisterBuffer.GetBytes(operateResult.Content.AddressStart * 2, length * 2));
		}
		return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
	}

	public OperateResult Write(string address, byte[] value)
	{
		OperateResult<ModbusAddress> operateResult = ModbusInfo.AnalysisAddress(address, station, isStartWithZero: true, 3);
		if (!operateResult.IsSuccess)
		{
			return OperateResult.CreateFailedResult<byte[]>(operateResult);
		}
		if (operateResult.Content.Function == 3 || operateResult.Content.Function == 6 || operateResult.Content.Function == 16)
		{
			registerBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			return OperateResult.CreateSuccessResult();
		}
		if (operateResult.Content.Function == 4)
		{
			inputRegisterBuffer.SetBytes(value, operateResult.Content.AddressStart * 2);
			return OperateResult.CreateSuccessResult();
		}
		return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
	}

	public OperateResult<bool[]> ReadBool(string address, ushort length)
	{
		if (address.IndexOf('.') < 0)
		{
			OperateResult<ModbusAddress> operateResult = ModbusInfo.AnalysisAddress(address, station, isStartWithZero: true, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult);
			}
			if (operateResult.Content.Function == 1 || operateResult.Content.Function == 5 || operateResult.Content.Function == 15)
			{
				return OperateResult.CreateSuccessResult((from m in coilBuffer.GetBytes(operateResult.Content.AddressStart, length)
					select m != 0).ToArray());
			}
			if (operateResult.Content.Function == 2)
			{
				return OperateResult.CreateSuccessResult((from m in inputBuffer.GetBytes(operateResult.Content.AddressStart, length)
					select m != 0).ToArray());
			}
			return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
		}
		try
		{
			int num = Convert.ToInt32(address.Substring(address.IndexOf('.') + 1));
			address = address.Substring(0, address.IndexOf('.'));
			OperateResult<ModbusAddress> operateResult2 = ModbusInfo.AnalysisAddress(address, station, isStartWithZero: true, 3);
			if (!operateResult2.IsSuccess)
			{
				return OperateResult.CreateFailedResult<bool[]>(operateResult2);
			}
			num = operateResult2.Content.AddressStart * 16 + num;
			if (operateResult2.Content.Function == 3)
			{
				return OperateResult.CreateSuccessResult(registerBuffer.GetBool(num, length));
			}
			if (operateResult2.Content.Function == 4)
			{
				return OperateResult.CreateSuccessResult(inputRegisterBuffer.GetBool(num, length));
			}
			return new OperateResult<bool[]>(StringResources.Language.NotSupportedDataType);
		}
		catch (Exception ex)
		{
			return new OperateResult<bool[]>(ex.Message);
		}
	}

	public OperateResult Write(string address, bool[] value)
	{
		if (address.IndexOf('.') < 0)
		{
			OperateResult<ModbusAddress> operateResult = ModbusInfo.AnalysisAddress(address, station, isStartWithZero: true, 1);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<byte[]>(operateResult);
			}
			if (operateResult.Content.Function == 1 || operateResult.Content.Function == 15 || operateResult.Content.Function == 5)
			{
				coilBuffer.SetBytes(value.Select((bool m) => (byte)(m ? 1u : 0u)).ToArray(), operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			}
			if (operateResult.Content.Function == 2)
			{
				inputBuffer.SetBytes(value.Select((bool m) => (byte)(m ? 1u : 0u)).ToArray(), operateResult.Content.AddressStart);
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult<byte[]>(StringResources.Language.NotSupportedDataType);
		}
		try
		{
			int num = Convert.ToInt32(address.Substring(address.IndexOf('.') + 1));
			address = address.Substring(0, address.IndexOf('.'));
			OperateResult<ModbusAddress> operateResult2 = ModbusInfo.AnalysisAddress(address, station, isStartWithZero: true, 3);
			if (!operateResult2.IsSuccess)
			{
				return operateResult2;
			}
			num = operateResult2.Content.AddressStart * 16 + num;
			if (operateResult2.Content.Function == 3)
			{
				registerBuffer.SetBool(value, num);
				return OperateResult.CreateSuccessResult();
			}
			if (operateResult2.Content.Function == 4)
			{
				inputRegisterBuffer.SetBool(value, num);
				return OperateResult.CreateSuccessResult();
			}
			return new OperateResult(StringResources.Language.NotSupportedDataType);
		}
		catch (Exception ex)
		{
			return new OperateResult(ex.Message);
		}
	}

	/// <summary>
	/// 读取地址的线圈的通断情况
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <returns><c>True</c>或是<c>False</c></returns>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public bool ReadCoil(string address)
	{
		ushort index = ushort.Parse(address);
		return coilBuffer.GetByte(index) != 0;
	}

	/// <summary>
	/// 批量读取地址的线圈的通断情况
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <param name="length">读取长度</param>
	/// <returns><c>True</c>或是<c>False</c></returns>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public bool[] ReadCoil(string address, ushort length)
	{
		ushort index = ushort.Parse(address);
		return (from m in coilBuffer.GetBytes(index, length)
			select m != 0).ToArray();
	}

	/// <summary>
	/// 写入线圈的通断值
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <param name="data">是否通断</param>
	/// <returns><c>True</c>或是<c>False</c></returns>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public void WriteCoil(string address, bool data)
	{
		ushort index = ushort.Parse(address);
		coilBuffer.SetValue((byte)(data ? 1u : 0u), index);
	}

	/// <summary>
	/// 写入线圈数组的通断值
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <param name="data">是否通断</param>
	/// <returns><c>True</c>或是<c>False</c></returns>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public void WriteCoil(string address, bool[] data)
	{
		if (data != null)
		{
			ushort destIndex = ushort.Parse(address);
			coilBuffer.SetBytes(data.Select((bool m) => (byte)(m ? 1u : 0u)).ToArray(), destIndex);
		}
	}

	/// <summary>
	/// 读取地址的离散线圈的通断情况
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <returns><c>True</c>或是<c>False</c></returns>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public bool ReadDiscrete(string address)
	{
		ushort index = ushort.Parse(address);
		return inputBuffer.GetByte(index) != 0;
	}

	/// <summary>
	/// 批量读取地址的离散线圈的通断情况
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <param name="length">读取长度</param>
	/// <returns><c>True</c>或是<c>False</c></returns>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public bool[] ReadDiscrete(string address, ushort length)
	{
		ushort index = ushort.Parse(address);
		return (from m in inputBuffer.GetBytes(index, length)
			select m != 0).ToArray();
	}

	/// <summary>
	/// 写入离散线圈的通断值
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <param name="data">是否通断</param>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public void WriteDiscrete(string address, bool data)
	{
		ushort index = ushort.Parse(address);
		inputBuffer.SetValue((byte)(data ? 1u : 0u), index);
	}

	/// <summary>
	/// 写入离散线圈数组的通断值
	/// </summary>
	/// <param name="address">起始地址，示例："100"</param>
	/// <param name="data">是否通断</param>
	/// <exception cref="T:System.IndexOutOfRangeException"></exception>
	public void WriteDiscrete(string address, bool[] data)
	{
		if (data != null)
		{
			ushort destIndex = ushort.Parse(address);
			inputBuffer.SetBytes(data.Select((bool m) => (byte)(m ? 1u : 0u)).ToArray(), destIndex);
		}
	}

	/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpServer.SaveToBytes" />
	public byte[] SaveToBytes()
	{
		byte[] array = new byte[393216];
		Array.Copy(coilBuffer.GetBytes(), 0, array, 0, 65536);
		Array.Copy(inputBuffer.GetBytes(), 0, array, 65536, 65536);
		Array.Copy(registerBuffer.GetBytes(), 0, array, 131072, 131072);
		Array.Copy(inputRegisterBuffer.GetBytes(), 0, array, 262144, 131072);
		return array;
	}

	/// <inheritdoc cref="M:HslCommunication.ModBus.ModbusTcpServer.LoadFromBytes(System.Byte[])" />
	public void LoadFromBytes(byte[] content, int index)
	{
		if (content.Length < 393216)
		{
			throw new Exception("File is not correct");
		}
		coilBuffer.SetBytes(content, index, 0, 65536);
		inputBuffer.SetBytes(content, 65536 + index, 0, 65536);
		registerBuffer.SetBytes(content, 131072 + index, 0, 131072);
		inputRegisterBuffer.SetBytes(content, 262144 + index, 0, 131072);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		coilBuffer?.Dispose();
		inputBuffer?.Dispose();
		registerBuffer?.Dispose();
		inputRegisterBuffer?.Dispose();
	}
}
