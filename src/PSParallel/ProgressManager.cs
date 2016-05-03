using System;
using System.Diagnostics;
using System.Management.Automation;
namespace PSParallel
{
	class ProgressManager
	{
		public int TotalCount { get; set; }
		private ProgressRecord _progressRecord;
		private readonly Stopwatch _stopwatch;
		private string _currentOperation;

		public ProgressManager(int activityId, string activity, string statusDescription, int parentActivityId = -1, int totalCount = 0)
		{
			TotalCount = totalCount;
			_stopwatch = new Stopwatch();
			_progressRecord = new ProgressRecord(activityId, activity, statusDescription) {ParentActivityId = parentActivityId};
		}


		private void UpdateCurrentProgressRecordInternal(int count)
		{
			if (!_stopwatch.IsRunning && TotalCount > 0)
			{
				_stopwatch.Start();
			}
			var current = TotalCount > 0 ? $"({count}/{TotalCount}) {_currentOperation}" : _currentOperation;
			var pr = _progressRecord.Clone();
			pr.CurrentOperation = current;
			pr.RecordType = ProgressRecordType.Processing;			
			if (TotalCount > 0)
			{				
				pr.PercentComplete = GetPercentComplete(count);
				pr.SecondsRemaining = GetSecondsRemaining(count);								
			}
			_progressRecord = pr;
		}		

		public void SetCurrentOperation(string currentOperation)
		{
			_currentOperation = currentOperation;
		}

		public void UpdateCurrentProgressRecord(int count)
		{
			
			UpdateCurrentProgressRecordInternal(count);						
		}

		public ProgressRecord ProgressRecord => _progressRecord;
		

		public ProgressRecord Completed()
		{
			_stopwatch.Reset();			
			_progressRecord = _progressRecord.WithRecordType(ProgressRecordType.Completed);
			return _progressRecord;
		}

		
		private int GetSecondsRemaining(int count)
		{
			var secondsRemaining = count == 0 ? -1 : (int) ((TotalCount - count)*_stopwatch.ElapsedMilliseconds/1000/count);
			return secondsRemaining;
		}

		private int GetPercentComplete(int count)
		{
			var percentComplete = count*100/TotalCount;
			return percentComplete;
		}
			
		public int ActivityId => _progressRecord.ActivityId;
	}


	class ProgressProjector
	{
		private readonly Stopwatch _stopWatch;
		private int _percentComplete;		
		public ProgressProjector()
		{
			_stopWatch = new Stopwatch();
			_percentComplete = -1;
		}

		public void ReportProgress(int percentComplete)
		{
			if (percentComplete > 100)
			{
				percentComplete = 100;
			}			
			_percentComplete = percentComplete;			
		}

		public bool IsValid => _percentComplete > 0 && _stopWatch.IsRunning;
		public TimeSpan Elapsed => _stopWatch.Elapsed;
		
		public TimeSpan ProjectedTotalTime => new TimeSpan(Elapsed.Ticks * 100 / _percentComplete);

		public void Start()
		{
			_stopWatch.Start();
			_percentComplete = 0;
		}

		public void Stop()
		{
			_stopWatch.Stop();
		}
	}

	static class ProgressRecordExtension
	{
		static ProgressRecord CloneProgressRecord(ProgressRecord record)
		{
			return new ProgressRecord(record.ActivityId, record.Activity, record.StatusDescription)
			{
				CurrentOperation = record.CurrentOperation,
				ParentActivityId = record.ParentActivityId,
				SecondsRemaining = record.SecondsRemaining,
				PercentComplete = record.PercentComplete,
				RecordType = record.RecordType
			};
		}

		public static ProgressRecord Clone(this ProgressRecord record)
		{
			return CloneProgressRecord(record);
		}

		public static ProgressRecord WithCurrentOperation(this ProgressRecord record, string currentOperation)
		{
			var r = CloneProgressRecord(record);
			r.CurrentOperation = currentOperation;
			return r;
		}

		public static ProgressRecord WithRecordType(this ProgressRecord record, ProgressRecordType recordType)
		{
			var r = CloneProgressRecord(record);
			r.RecordType = recordType;
			return r;
		}

		public static ProgressRecord WithPercentCompleteAndSecondsRemaining(this ProgressRecord record, int percentComplete, int secondsRemaining)
		{
			var r = CloneProgressRecord(record);
			r.PercentComplete = percentComplete;
			r.SecondsRemaining = secondsRemaining;
			return r;
		}

	}
}
