using System;
using System.Collections.Generic;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// 参数设置相关的类
	/// </summary>
	public class ParameterSetMessages
	{
		private OpenProtocolNet openProtocol;

		/// <summary>
		/// 指定Open通信类实例化一个对象
		/// </summary>
		/// <param name="openProtocol">开放协议的对象</param>
		public ParameterSetMessages(OpenProtocolNet openProtocol)
		{
			this.openProtocol = openProtocol;
		}

		/// <summary>
		/// A request to get the valid parameter set IDs from the controller.
		/// </summary>
		/// <returns>IDs</returns>
		public OperateResult<int[]> ParameterSetIDUpload()
		{
			OperateResult<string> operateResult = openProtocol.ReadCustomer(10, 1, -1, -1, null);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int[]>(operateResult);
			}
			return PraseMID0011(operateResult.Content);
		}

		/// <summary>
		/// Request to upload parameter set data from the controller.
		/// </summary>
		/// <param name="id">parameter set id</param>
		/// <returns>parameter set data</returns>
		public OperateResult<ParameterSetData> ParameterSetDataUpload(int id)
		{
			OperateResult<string> operateResult = openProtocol.ReadCustomer(12, 1, -1, -1, new List<string> { id.ToString("D3") });
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<ParameterSetData>(operateResult);
			}
			return PraseMID0012(operateResult.Content);
		}

		/// <summary>
		/// A subscription for the parameter set selection. Each time a new parameter set is selected the MID 0015 Parameter set selected is sent to the integrator.
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult ParameterSetSelectedSubscribe()
		{
			return openProtocol.ReadCustomer(14, 1, -1, -1, null);
		}

		/// <summary>
		/// Reset the subscription for the parameter set selection.
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult ParameterSetSelectedUnsubscribe()
		{
			return openProtocol.ReadCustomer(17, 1, -1, -1, null);
		}

		/// <summary>
		/// Select a parameter set.
		/// </summary>
		/// <param name="id">id</param>
		/// <returns>是否选择成功的结果对象</returns>
		public OperateResult SelectParameterSet(int id)
		{
			return openProtocol.ReadCustomer(18, 1, -1, -1, new List<string> { id.ToString("D3") });
		}

		/// <summary>
		/// This message gives the possibility to set the batch size of a parameter set at run time.
		/// </summary>
		/// <param name="id">Parameter set ID</param>
		/// <param name="batchSize">Batch size</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult SetParameterSetBatchSize(int id, int batchSize)
		{
			return openProtocol.ReadCustomer(19, 1, -1, -1, new List<string>
			{
				id.ToString("D3"),
				batchSize.ToString("D2")
			});
		}

		/// <summary>
		/// This message gives the possibility to reset the batch counter of the running parameter set, at run time.
		/// </summary>
		/// <param name="id">Parameter set ID</param>
		/// <returns>是否操作成功的结果对象</returns>
		public OperateResult ResetParameterSetBatchCounter(int id)
		{
			return openProtocol.ReadCustomer(20, 1, -1, -1, new List<string> { id.ToString("D3") });
		}

		private OperateResult<int[]> PraseMID0011(string reply)
		{
			try
			{
				int num = Convert.ToInt32(reply.Substring(20, 3));
				int[] array = new int[num];
				for (int i = 0; i < num; i++)
				{
					array[i] = Convert.ToInt32(reply.Substring(23 + i * 3, 3));
				}
				return OperateResult.CreateSuccessResult(array);
			}
			catch (Exception ex)
			{
				return new OperateResult<int[]>("MID0011 prase failed: " + ex.Message + Environment.NewLine + "Source: " + reply);
			}
		}

		private OperateResult<ParameterSetData> PraseMID0012(string reply)
		{
			try
			{
				ParameterSetData parameterSetData = new ParameterSetData();
				parameterSetData.ParameterSetID = Convert.ToInt32(reply.Substring(22, 3));
				parameterSetData.ParameterSetName = reply.Substring(27, 25).Trim();
				parameterSetData.RotationDirection = ((reply[54] == '1') ? "CW" : "CCW");
				parameterSetData.BatchSize = Convert.ToInt32(reply.Substring(57, 2));
				parameterSetData.TorqueMin = Convert.ToDouble(reply.Substring(61, 6)) / 100.0;
				parameterSetData.TorqueMax = Convert.ToDouble(reply.Substring(69, 6)) / 100.0;
				parameterSetData.TorqueFinalTarget = Convert.ToDouble(reply.Substring(77, 6)) / 100.0;
				parameterSetData.AngleMin = Convert.ToInt32(reply.Substring(85, 5));
				parameterSetData.AngleMax = Convert.ToInt32(reply.Substring(92, 5));
				parameterSetData.AngleFinalTarget = Convert.ToInt32(reply.Substring(99, 5));
				return OperateResult.CreateSuccessResult(parameterSetData);
			}
			catch (Exception ex)
			{
				return new OperateResult<ParameterSetData>("MID0013 prase failed: " + ex.Message + Environment.NewLine + "Source: " + reply);
			}
		}
	}
}
