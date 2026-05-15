using System;
using System.Collections.Generic;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// 工具类消息
	/// </summary>
	public class ToolMessages
	{
		private OpenProtocolNet openProtocol;

		/// <summary>
		/// 指定Open通信来实例化一个工具类消息的对象
		/// </summary>
		/// <param name="openProtocol">工具类消息</param>
		public ToolMessages(OpenProtocolNet openProtocol)
		{
			this.openProtocol = openProtocol;
		}

		/// <summary>
		/// A request for some of the data stored in the tool. The result of this command is the transmission of the tool data.
		/// </summary>
		/// <param name="revision">Revision</param>
		/// <returns>工具数据的结果对象</returns>
		public OperateResult<ToolData> ToolDataUpload(int revision = 1)
		{
			OperateResult<string> operateResult = openProtocol.ReadCustomer(40, revision, -1, -1, null);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<ToolData>(operateResult);
			}
			return PraseMID0041(operateResult.Content);
		}

		/// <summary>
		/// Disable tool.
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult DisableTool()
		{
			return openProtocol.ReadCustomer(42, 1, -1, -1, null);
		}

		/// <summary>
		/// Enable tool
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult EnableTool()
		{
			return openProtocol.ReadCustomer(43, 1, -1, -1, null);
		}

		/// <summary>
		/// This command is sent by the integrator in order to request the possibility to disconnect the tool from the controller.The command is rejected if the tool is currently used.
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult DisconnectToolRequest()
		{
			return openProtocol.ReadCustomer(44, 1, -1, -1, null);
		}

		/// <summary>
		/// This message is sent by the integrator in order to set the calibration value of the tool.
		/// </summary>
		/// <param name="calibrationValueUnit">The unit in which the calibration value is sent. 1=Nm, 2=Lbf.ft, 3=Lbf.In, 4=Kpm</param>
		/// <param name="calibrationValue">The calibration value</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult SetCalibrationValueRequest(int calibrationValueUnit, double calibrationValue)
		{
			string item = "01" + calibrationValueUnit;
			string item2 = "02" + Convert.ToInt32(calibrationValue * 100.0).ToString("D6");
			return openProtocol.ReadCustomer(45, 1, -1, -1, new List<string> { item, item2 });
		}

		private OperateResult<ToolData> PraseMID0041(string reply)
		{
			try
			{
				int num = Convert.ToInt32(reply.Substring(8, 3));
				ToolData toolData = new ToolData();
				toolData.ToolSerialNumber = reply.Substring(22, 14);
				toolData.ToolNumberOfTightening = Convert.ToUInt32(reply.Substring(38, 10));
				toolData.LastCalibrationDate = DateTime.ParseExact(reply.Substring(50, 19), "yyyy-MM-dd:HH:mm:ss", null);
				toolData.ControllerSerialNumber = reply.Substring(71, 10);
				if (num > 1)
				{
					toolData.CalibrationValue = Convert.ToDouble(reply.Substring(83, 6)) / 100.0;
					toolData.LastServiceDate = DateTime.ParseExact(reply.Substring(91, 19), "yyyy-MM-dd:HH:mm:ss", null);
					toolData.TighteningsSinceService = Convert.ToUInt32(reply.Substring(112, 10));
					toolData.ToolType = Convert.ToInt32(reply.Substring(124, 2));
					toolData.MotorSize = Convert.ToInt32(reply.Substring(128, 2));
					toolData.UseOpenEnd = reply[132] == '1';
					toolData.TighteningDirection = ((reply[133] == '1') ? "CCW" : "CW");
					toolData.MotorRotation = Convert.ToInt32(reply.Substring(134, 1));
					toolData.ControllerSoftwareVersion = reply.Substring(137, 19);
				}
				return OperateResult.CreateSuccessResult(toolData);
			}
			catch (Exception ex)
			{
				return new OperateResult<ToolData>("MID0031 prase failed: " + ex.Message + Environment.NewLine + "Source: " + reply);
			}
		}
	}
}
