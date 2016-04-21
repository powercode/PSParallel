using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using static PSParallel.PsParallelEventSource;

namespace PSParallel
{
	class PowerShellPoolStreams : IDisposable
	{
		public PSDataCollection<PSObject> Output { get; } = new PSDataCollection<PSObject>(100);
		public PSDataCollection<DebugRecord> Debug { get; } = new PSDataCollection<DebugRecord>();
		private PSDataCollection<ProgressRecord> Progress { get; } = new PSDataCollection<ProgressRecord>();
		public PSDataCollection<ErrorRecord> Error { get; } = new PSDataCollection<ErrorRecord>();
		public PSDataCollection<WarningRecord> Warning { get; } = new PSDataCollection<WarningRecord>();
		public PSDataCollection<InformationRecord> Information { get; } = new PSDataCollection<InformationRecord>();
		public PSDataCollection<VerboseRecord> Verbose { get; } = new PSDataCollection<VerboseRecord>();

		public void Dispose()
		{
			Output.Dispose();
			Debug.Dispose();
			Progress.Dispose();
			Error.Dispose();
			Information.Dispose();
			Verbose.Dispose();
		}

		public void AddProgress(ProgressRecord progress, int index)
		{
			Log.OnProgress(index, progress.PercentComplete, progress.CurrentOperation);
			DoAddProgress(progress);
			OnProgressChanged(progress.PercentComplete, index);
		}

		public void ClearProgress(int index)
		{			
			OnProgressChanged(0, index);
		}

		protected void DoAddProgress(ProgressRecord progress)
		{
			Progress.Add(progress);
		}

		protected virtual void OnProgressChanged(int progress, int index){}

		public Collection<ProgressRecord> ReadAllProgress()
		{
			return Progress.ReadAll();
		}
	}

	class ProgressTrackingPowerShellPoolStreams : PowerShellPoolStreams
	{
		private readonly int _maxPoolSize;
		private readonly int[] _poolProgress;
		private int _currentProgress;
		public ProgressTrackingPowerShellPoolStreams(int maxPoolSize)
		{
			_maxPoolSize = maxPoolSize;
			_poolProgress = new int[maxPoolSize];
		}

		protected override void OnProgressChanged(int progress, int index)
		{
			lock(_poolProgress) {
				_poolProgress[index] = progress;
				_currentProgress = _poolProgress.Sum();
			}
		}

		public int PoolPercentComplete => _currentProgress/_maxPoolSize;

	}

}