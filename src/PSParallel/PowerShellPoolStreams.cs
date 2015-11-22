using System;
using System.Management.Automation;

namespace PSParallel
{
	class PowerShellPoolStreams : IDisposable
	{
		public PSDataCollection<PSObject> Output { get; } = new PSDataCollection<PSObject>(100);
		public PSDataCollection<DebugRecord> Debug { get; } = new PSDataCollection<DebugRecord>();
		public PSDataCollection<ProgressRecord> Progress { get; } = new PSDataCollection<ProgressRecord>();
		public PSDataCollection<ErrorRecord> Error { get; } = new PSDataCollection<ErrorRecord>();
		public PSDataCollection<WarningRecord> Warning  { get; } = new PSDataCollection<WarningRecord>();
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
	}
}