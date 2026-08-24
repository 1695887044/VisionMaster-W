using System.Collections.Generic;

namespace HslCommunication.Profinet.OpenProtocol
{
	/// <summary>
	/// Job data
	/// </summary>
	public class JobData
	{
		/// <summary>
		/// Job ID
		/// </summary>
		public int JobID { get; set; }

		/// <summary>
		/// Job name
		/// </summary>
		public string JobName { get; set; }

		/// <summary>
		/// Forced order: 0=free order, 1=forced order, 2=free and forced
		/// </summary>
		public int ForcedOrder { get; set; }

		/// <summary>
		/// Max time for first tightening
		/// </summary>
		public int MaxTimeForFirstTightening { get; set; }

		/// <summary>
		/// Max time to complete Job
		/// </summary>
		public int MaxTimeToCompleteJob { get; set; }

		/// <summary>
		/// Job batch mode
		/// </summary>
		public int JobBatchMode { get; set; }

		/// <summary>
		/// Lock at Job done
		/// </summary>
		public bool LockAtJobDone { get; set; }

		/// <summary>
		/// Use line control
		/// </summary>
		public bool UseLineControl { get; set; }

		/// <summary>
		/// Repeat Job
		/// </summary>
		public bool RepeatJob { get; set; }

		/// <summary>
		/// Tool loosening: 0=Enable, 1=Disable, 2=Enable only on NOK tightening
		/// </summary>
		public int ToolLoosening { get; set; }

		/// <summary>
		/// Reserved for Job repair. 0=E, 1=G
		/// </summary>
		public int Reserved { get; set; }

		/// <summary>
		/// A list of parameter sets
		/// </summary>
		public List<JobItem> JobList { get; set; }
	}
}
