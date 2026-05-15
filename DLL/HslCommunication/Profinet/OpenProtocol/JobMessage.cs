using System;
using System.Collections.Generic;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// Job message
	/// </summary>
	public class JobMessage
	{
		private OpenProtocolNet openProtocol;

		/// <summary>
		/// 指定Open协议实例化一个任务消息对象
		/// </summary>
		/// <param name="openProtocol">连接通道</param>
		public JobMessage(OpenProtocolNet openProtocol)
		{
			this.openProtocol = openProtocol;
		}

		/// <summary>
		/// This is a request for a transmission of all the valid Job IDs of the controller. The result of this command is a transmission of all the valid Job IDs.
		/// </summary>
		/// <param name="revision">Revision</param>
		/// <returns>任务ID的列表信息</returns>
		public OperateResult<int[]> JobIDUpload(int revision = 1)
		{
			OperateResult<string> operateResult = openProtocol.ReadCustomer(30, revision, -1, -1, null);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<int[]>(operateResult);
			}
			return PraseMID0031(operateResult.Content);
		}

		/// <summary>
		/// Request to upload the data for a specific Job from the controller.
		/// </summary>
		/// <param name="id">job id</param>
		/// <returns>任务数据的结果对象</returns>
		public OperateResult<JobData> JobDataUpload(int id)
		{
			OperateResult<string> operateResult = openProtocol.ReadCustomer(32, 1, -1, -1, new List<string> { id.ToString("D2") });
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<JobData>(operateResult);
			}
			return PraseMID0033(operateResult.Content);
		}

		/// <summary>
		/// A subscription for the Job info. MID 0035 Job info is sent to the integrator when a new Job is selected and after each tightening performed during the Job.
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult JobInfoSubscribe()
		{
			return openProtocol.ReadCustomer(34, 1, -1, -1, null);
		}

		/// <summary>
		/// Reset the subscription for a Job info message.
		/// </summary>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult JobInfoUnsubscribe()
		{
			return openProtocol.ReadCustomer(37, 1, -1, -1, null);
		}

		/// <summary>
		/// Message to select Job. If the requested ID is not present in the controller, then the command will not be performed.
		/// </summary>
		/// <param name="id">Job ID</param>
		/// <param name="revision">Revision</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult SelectJob(int id, int revision = 1)
		{
			return openProtocol.ReadCustomer(38, revision, -1, -1, new List<string> { (revision == 1) ? id.ToString("D2") : id.ToString("D4") });
		}

		/// <summary>
		/// Job restart message.
		/// </summary>
		/// <param name="id">Job ID</param>
		/// <returns>是否成功的结果对象</returns>
		public OperateResult JobRestart(int id)
		{
			return openProtocol.ReadCustomer(39, 1, -1, -1, new List<string> { id.ToString("D2") });
		}

		private OperateResult<int[]> PraseMID0031(string reply)
		{
			try
			{
				int num = Convert.ToInt32(reply.Substring(8, 3));
				int num2 = ((num == 1) ? 2 : 4);
				int num3 = Convert.ToInt32(reply.Substring(20, num2));
				int[] array = new int[num3];
				for (int i = 0; i < num3; i++)
				{
					array[i] = Convert.ToInt32(reply.Substring(20 + num2 + i * num2, num2));
				}
				return OperateResult.CreateSuccessResult(array);
			}
			catch (Exception ex)
			{
				return new OperateResult<int[]>("MID0031 prase failed: " + ex.Message + Environment.NewLine + "Source: " + reply);
			}
		}

		private OperateResult<JobData> PraseMID0033(string reply)
		{
			try
			{
				JobData jobData = new JobData();
				jobData.JobID = Convert.ToInt32(reply.Substring(22, 2));
				jobData.JobName = reply.Substring(26, 25).Trim();
				jobData.ForcedOrder = Convert.ToInt32(reply.Substring(53, 1));
				jobData.MaxTimeForFirstTightening = Convert.ToInt32(reply.Substring(56, 4));
				jobData.MaxTimeToCompleteJob = Convert.ToInt32(reply.Substring(62, 5));
				jobData.JobBatchMode = Convert.ToInt32(reply.Substring(69, 1));
				jobData.LockAtJobDone = reply[72] == '1';
				jobData.UseLineControl = reply[75] == '1';
				jobData.RepeatJob = reply[78] == '1';
				jobData.ToolLoosening = Convert.ToInt32(reply.Substring(81, 1));
				jobData.Reserved = Convert.ToInt32(reply.Substring(86, 1));
				jobData.JobList = new List<JobItem>();
				int num = Convert.ToInt32(reply.Substring(89, 2));
				for (int i = 0; i < num; i++)
				{
					jobData.JobList.Add(new JobItem(reply.Substring(92 + i * 12, 11)));
				}
				return OperateResult.CreateSuccessResult(jobData);
			}
			catch (Exception ex)
			{
				return new OperateResult<JobData>("MID0033 prase failed: " + ex.Message + Environment.NewLine + "Source: " + reply);
			}
		}
	}
}
