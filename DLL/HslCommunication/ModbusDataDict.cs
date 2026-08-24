using System;
using System.Collections.Generic;
using System.Linq;

internal class ModbusDataDict : IDisposable
{
	private Dictionary<int, ModbusDataPool> dictModbusDataPool;

	private bool stationDataIsolation = false;

	public ModbusDataDict()
	{
		dictModbusDataPool = new Dictionary<int, ModbusDataPool>();
		dictModbusDataPool.Add(0, new ModbusDataPool(0));
	}

	public void Set(bool stationDataIsolation)
	{
		if (this.stationDataIsolation == stationDataIsolation)
		{
			return;
		}
		this.stationDataIsolation = stationDataIsolation;
		if (this.stationDataIsolation)
		{
			dictModbusDataPool = new Dictionary<int, ModbusDataPool>();
			for (int i = 0; i < 255; i++)
			{
				dictModbusDataPool.Add(i, new ModbusDataPool((byte)i));
			}
		}
		else
		{
			dictModbusDataPool = new Dictionary<int, ModbusDataPool>();
			dictModbusDataPool.Add(0, new ModbusDataPool(0));
		}
	}

	public ModbusDataPool GetModbusPool(byte station)
	{
		if (stationDataIsolation)
		{
			return dictModbusDataPool[station];
		}
		return dictModbusDataPool.FirstOrDefault().Value;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		foreach (ModbusDataPool value in dictModbusDataPool.Values)
		{
			value.Dispose();
		}
		dictModbusDataPool.Clear();
	}
}
