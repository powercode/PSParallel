using System.Diagnostics;
using System.Management.Automation;

namespace PSParallel
{
	class ProgressManager
	{
		private int m_count;
		public int TotalCount { get; set; }
		private readonly ProgressRecord m_progressRecord;
		private readonly Stopwatch m_stopwatch;

		public ProgressManager(int activityId, string activity, string statusDescription, int parentActivityId = -1, int totalCount = 0)
		{
			TotalCount = totalCount;
			m_stopwatch = new Stopwatch();
			m_progressRecord = new ProgressRecord(activityId, activity, statusDescription) {ParentActivityId = parentActivityId};
		}

		public ProgressRecord GetCurrentProgressRecord(string currentOperation)
		{
			if(!m_stopwatch.IsRunning && TotalCount > 0)
			{
				m_stopwatch.Start();
			}
			m_progressRecord.RecordType =  ProgressRecordType.Processing;
			m_count++;
			if(TotalCount > 0)
			{
				var percentComplete = PercentComplete;
				if (percentComplete != m_progressRecord.PercentComplete)
				{
					m_progressRecord.PercentComplete = percentComplete;
					m_progressRecord.SecondsRemaining = SecondsRemaining;
				}
				m_progressRecord.CurrentOperation = $"{m_count}/{TotalCount} {currentOperation}";
			}
			else
			{
				m_progressRecord.CurrentOperation = currentOperation;
			}
			return m_progressRecord;
		}


		public ProgressRecord Completed()
		{
			m_stopwatch.Reset();

			m_progressRecord.RecordType = ProgressRecordType.Completed;
			return m_progressRecord;
		}

		private int SecondsRemaining => m_count == 0 ? -1 : (int) ((TotalCount - m_count)*m_stopwatch.ElapsedMilliseconds/1000/m_count);
		private int PercentComplete => m_count*100/TotalCount;
		public int ActivityId => m_progressRecord.ActivityId;
	}
}
