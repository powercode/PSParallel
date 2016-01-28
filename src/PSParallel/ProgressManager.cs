﻿using System.Diagnostics;
using System.Management.Automation;

namespace PSParallel
{
	class ProgressManager
	{
		public int TotalCount { get; set; }
		private readonly ProgressRecord _progressRecord;
		private readonly Stopwatch _stopwatch;		

		public ProgressManager(int activityId, string activity, string statusDescription, int parentActivityId = -1, int totalCount = 0)
		{
			TotalCount = totalCount;
			_stopwatch = new Stopwatch();
			_progressRecord = new ProgressRecord(activityId, activity, statusDescription) {ParentActivityId = parentActivityId};
		}


		public void UpdateCurrentProgressRecord(int count)
		{
			if (!_stopwatch.IsRunning && TotalCount > 0)
			{
				_stopwatch.Start();
			}			
			_progressRecord.RecordType = ProgressRecordType.Processing;
			if (TotalCount > 0)
			{
				var percentComplete = GetPercentComplete(count);
				if (percentComplete != _progressRecord.PercentComplete)
				{
					_progressRecord.PercentComplete = percentComplete;
					_progressRecord.SecondsRemaining = GetSecondsRemaining(count);
				}				
			}			
		}

		public void UpdateCurrentProgressRecord(string currentOperation, int count)
		{
			UpdateCurrentProgressRecord(count);

			_progressRecord.CurrentOperation = TotalCount > 0 ? $"({count}/{TotalCount}) {currentOperation}" : currentOperation;
		}

		public ProgressRecord ProgressRecord => _progressRecord;
		

		public ProgressRecord Completed()
		{
			_stopwatch.Reset();

			_progressRecord.RecordType = ProgressRecordType.Completed;
			return _progressRecord;
		}

		private int GetSecondsRemaining(int count) => count == 0 ? -1 : (int) ((TotalCount - count)*_stopwatch.ElapsedMilliseconds/1000/count);
		private int GetPercentComplete(int count) => count*100/TotalCount;
		public int ActivityId => _progressRecord.ActivityId;
	}
}
