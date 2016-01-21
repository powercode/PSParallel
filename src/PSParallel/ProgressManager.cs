using System;
using System.Diagnostics;
using System.Management.Automation;

namespace PSParallel
{
	class ProgressManager
	{
		public int TotalCount { get; set; }
		private ProgressRecord m_progressRecord;
		private readonly Stopwatch m_stopwatch;		

		public ProgressManager(int activityId, string activity, string statusDescription, int parentActivityId = -1, int totalCount = 0)
		{
			TotalCount = totalCount;
			m_stopwatch = new Stopwatch();
			m_progressRecord = new ProgressRecord(activityId, activity, statusDescription) {ParentActivityId = parentActivityId};
		}


		public void UpdateCurrentProgressRecord(int count)
		{
			if (!m_stopwatch.IsRunning && TotalCount > 0)
			{
				m_stopwatch.Start();
			}			
			m_progressRecord.RecordType = ProgressRecordType.Processing;
			if (TotalCount > 0)
			{
				var percentComplete = GetPercentComplete(count);
				if (percentComplete != m_progressRecord.PercentComplete)
				{
					m_progressRecord.PercentComplete = percentComplete;
					m_progressRecord.SecondsRemaining = GetSecondsRemaining(count);
				}				
			}			
		}

		public void UpdateCurrentProgressRecord(string currentOperation, int count)
		{
			UpdateCurrentProgressRecord(count);

			m_progressRecord.CurrentOperation = TotalCount > 0 ? $"({count}/{TotalCount}) {currentOperation}" : currentOperation;
		}

		public ProgressRecord ProgressRecord => m_progressRecord;
		

		public ProgressRecord Completed()
		{
			m_stopwatch.Reset();

			m_progressRecord.RecordType = ProgressRecordType.Completed;
			return m_progressRecord;
		}

		private int GetSecondsRemaining(int count) => count == 0 ? -1 : (int) ((TotalCount - count)*m_stopwatch.ElapsedMilliseconds/1000/count);
		private int GetPercentComplete(int count) => count*100/TotalCount;
		public int ActivityId => m_progressRecord.ActivityId;
	}


	class ProgressProjector
	{
		private readonly Stopwatch m_stopWatch;
		private int m_percentComplete;		
		public ProgressProjector()
		{
			m_stopWatch = new Stopwatch();
			m_percentComplete = -1;
		}

		public void ReportProgress(int percentComplete)
		{
			if (percentComplete > 100)
			{
				percentComplete = 100;
			}			
			m_percentComplete = percentComplete;			
		}

		public bool IsValid => m_percentComplete > 0 && m_stopWatch.IsRunning;
		public TimeSpan Elapsed => m_stopWatch.Elapsed;
		
		public TimeSpan ProjectedTotalTime => new TimeSpan(Elapsed.Ticks * 100 / m_percentComplete);

		public void Start()
		{
			m_stopWatch.Start();
			m_percentComplete = 0;
		}

		public void Stop()
		{
			m_stopWatch.Stop();
		}
	}
}
