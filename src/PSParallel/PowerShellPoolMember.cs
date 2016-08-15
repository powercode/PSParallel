using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

namespace PSParallel
{
	class PowerShellPoolMember : IDisposable
	{
		private readonly PowershellPool _pool;
		private readonly int _index;
		private readonly InitialSessionState _initialSessionState;
		private readonly PowerShellPoolStreams _poolStreams;
		private PowerShell _powerShell;
		public PowerShell PowerShell => _powerShell;
		public int Index => _index ;

		private readonly PSDataCollection<PSObject> _input =new PSDataCollection<PSObject>();
		private PSDataCollection<PSObject> _output;
		private int _percentComplete;
		public int PercentComplete
		{
			get { return _percentComplete; }
			set { _percentComplete = value; }
		}


		public PowerShellPoolMember(PowershellPool pool, int index, InitialSessionState initialSessionState)
		{
			_pool = pool;
			_index = index;
			_initialSessionState = initialSessionState;
			_poolStreams = _pool.Streams;
			_input.Complete();			
			CreatePowerShell(initialSessionState);			
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
					ResetPowerShell();
					_pool.ReportAvailable(this);
					break;
			}
		}

		private void CreatePowerShell(InitialSessionState initialSessionState)
		{
			var powerShell = PowerShell.Create(RunspaceMode.NewRunspace);
			var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
			runspace.ApartmentState = ApartmentState.MTA;
			powerShell.Runspace = runspace;
			runspace.Open();			
			HookStreamEvents(powerShell.Streams);
			powerShell.InvocationStateChanged += PowerShellOnInvocationStateChanged;
			_powerShell = powerShell;
			_output = new PSDataCollection<PSObject>();
			_output.DataAdded += OutputOnDataAdded;
		}

		public void ResetPowerShell()
		{
			UnhookStreamEvents(_powerShell.Streams);
			_powerShell.Runspace.ResetRunspaceState();
			var runspace = _powerShell.Runspace;
			_powerShell = PowerShell.Create(RunspaceMode.NewRunspace);
			_powerShell.Runspace = runspace;

			HookStreamEvents(_powerShell.Streams);
			_powerShell.InvocationStateChanged += PowerShellOnInvocationStateChanged;			
			_output = new PSDataCollection<PSObject>();
			_output.DataAdded += OutputOnDataAdded;
		}

		private void ReleasePowerShell()
		{
			UnhookStreamEvents(_powerShell.Streams);
			_powerShell.InvocationStateChanged -= PowerShellOnInvocationStateChanged;
			_output.DataAdded -= OutputOnDataAdded;
			_powerShell.Dispose();			
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
				.AddParameter("PSParallelProgressId", _index + 1000);
			_powerShell.BeginInvoke(_input, _output);
		}

		public void Dispose()
		{
			var ps = _powerShell;
			if (ps != null)
			{
				UnhookStreamEvents(ps.Streams);
				ps.Runspace?.Dispose();
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
			var psDataCollection = ((PSDataCollection<ProgressRecord>) sender);
			var record = psDataCollection[dataAddedEventArgs.Index];
			_poolStreams.AddProgress(record, _index);			
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