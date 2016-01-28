using System;
using System.Management.Automation;

namespace PSParallel
{
	class PowerShellPoolMember : IDisposable
	{
		private readonly PowershellPool _pool;
		private readonly int _index;
		private readonly PowerShellPoolStreams _poolStreams;
		private PowerShell _powerShell;
		public PowerShell PowerShell => _powerShell;
		public int Index => _index ;

		private readonly PSDataCollection<PSObject> _input =new PSDataCollection<PSObject>();
		private PSDataCollection<PSObject> _output;
		private int _percentComplete;
		public int PercentComplete => _percentComplete;
		

		public PowerShellPoolMember(PowershellPool pool, int index)
		{
			_pool = pool;
			_index = index;
			_poolStreams = _pool.Streams;
			_input.Complete();			
			CreatePowerShell();			
		}

		private void PowerShellOnInvocationStateChanged(object sender, PSInvocationStateChangedEventArgs psInvocationStateChangedEventArgs)
		{
			switch (psInvocationStateChangedEventArgs.InvocationStateInfo.State)
			{
				case PSInvocationState.Stopped:
					ReleasePowerShell();
					_pool.ReportStopped(this);
					break;
				case PSInvocationState.Completed:
				case PSInvocationState.Failed:
					ReleasePowerShell();
					CreatePowerShell();
					_pool.ReportAvailable(this);
					break;
			}
		}

		private void CreatePowerShell()
		{
			var powerShell = PowerShell.Create();
			HookStreamEvents(powerShell.Streams);
			powerShell.InvocationStateChanged += PowerShellOnInvocationStateChanged;
			_powerShell = powerShell;
			_output = new PSDataCollection<PSObject>();
			_output.DataAdded += OutputOnDataAdded;
		}

		private void ReleasePowerShell()
		{
			UnhookStreamEvents(_powerShell.Streams);
			_powerShell.InvocationStateChanged -= PowerShellOnInvocationStateChanged;
			_output.DataAdded -= OutputOnDataAdded;
			_powerShell.Dispose();
			_powerShell = null;
		}


		private void HookStreamEvents(PSDataStreams streams)
		{
			streams.Debug.DataAdded += DebugOnDataAdded;
			streams.Error.DataAdded += ErrorOnDataAdded;
			streams.Progress.DataAdded += ProgressOnDataAdded;
			streams.Information.DataAdded += InformationOnDataAdded;
			streams.Verbose.DataAdded += VerboseOnDataAdded;
			streams.Warning.DataAdded += WarningOnDataAdded;
		}


		private void UnhookStreamEvents(PSDataStreams streams)
		{
			streams.Warning.DataAdded -= WarningOnDataAdded;
			streams.Verbose.DataAdded -= VerboseOnDataAdded;
			streams.Information.DataAdded -= InformationOnDataAdded;
			streams.Progress.DataAdded -= ProgressOnDataAdded;
			streams.Error.DataAdded -= ErrorOnDataAdded;
			streams.Debug.DataAdded -= DebugOnDataAdded;
		}


		public void BeginInvoke(ScriptBlock scriptblock, PSObject inputObject)
		{
			_percentComplete = 0;
			string command = $"param($_,$PSItem, $PSPArallelIndex,$PSParallelProgressId){scriptblock}";
			_powerShell.AddScript(command)
				.AddParameter("_", inputObject)
				.AddParameter("PSItem", inputObject)
				.AddParameter("PSParallelIndex", _index)
				.AddParameter("PSParallelProgressId", _index+1000);
			_powerShell.BeginInvoke(_input, _output);
		}

		public void Dispose()
		{
			var ps = _powerShell;
			if (ps != null)
			{
				UnhookStreamEvents(ps.Streams);
				ps.Dispose();
			}
			_output.Dispose();
			_input.Dispose();
		}

		private void OutputOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var item = ((PSDataCollection<PSObject>)sender)[dataAddedEventArgs.Index];
			_poolStreams.Output.Add(item);
		}


		private void InformationOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var ir = ((PSDataCollection<InformationRecord>)sender)[dataAddedEventArgs.Index];
			_poolStreams.Information.Add(ir);
		}

		private void ProgressOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<ProgressRecord>)sender)[dataAddedEventArgs.Index];
			var change = record.PercentComplete - _percentComplete;
			_percentComplete = record.PercentComplete;
			_poolStreams.Progress.Add(record);
			_pool.AddProgressChange(change);
		}

		private void ErrorOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<ErrorRecord>)sender)[dataAddedEventArgs.Index];
			_poolStreams.Error.Add(record);
		}

		private void DebugOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<DebugRecord>)sender)[dataAddedEventArgs.Index];
			_poolStreams.Debug.Add(record);
		}

		private void WarningOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<WarningRecord>)sender)[dataAddedEventArgs.Index];
			_poolStreams.Warning.Add(record);
		}

		private void VerboseOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<VerboseRecord>)sender)[dataAddedEventArgs.Index];
			_poolStreams.Verbose.Add(record);
		}

		public void Stop()
		{
			if(_powerShell.InvocationStateInfo.State != PSInvocationState.Stopped) 
			{ 
				UnhookStreamEvents(_powerShell.Streams);
				_powerShell.BeginStop(OnStopped, null);
			}
		}

		private void OnStopped(IAsyncResult ar)
		{
			var ps = _powerShell;
			if (ps == null)
			{
				return;
			}
			ps.EndStop(ar);
			_powerShell = null;
		}
	}
}