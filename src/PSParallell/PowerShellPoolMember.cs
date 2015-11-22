using System;
using System.Management.Automation;
using System.Threading;

namespace PSParallel
{
	class PowerShellPoolMember : IDisposable
	{
		private readonly PowershellPool m_pool;
		private readonly PowerShellPoolStreams m_poolStreams;
		private PowerShell m_powerShell;
		private readonly AutoResetEvent m_resetEvent = new AutoResetEvent(false);
		public PowerShell PowerShell => m_powerShell;
		public WaitHandle WaitHandle => m_resetEvent;
		private readonly PSDataCollection<PSObject> m_input =new PSDataCollection<PSObject>();
		private PSDataCollection<PSObject> m_output;

		public PowerShellPoolMember(PowershellPool pool)
		{
			m_pool = pool;
			m_poolStreams = m_pool.Streams;
			m_input.Complete();
			CreatePowerShell();			
		}

		private void PowerShellOnInvocationStateChanged(object sender, PSInvocationStateChangedEventArgs psInvocationStateChangedEventArgs)
		{
			switch (psInvocationStateChangedEventArgs.InvocationStateInfo.State)
			{

				case PSInvocationState.Stopped:
				case PSInvocationState.Completed:
				case PSInvocationState.Failed:					
					ReturnPowerShell(m_powerShell);
					m_pool.ReportCompletion();
					CreatePowerShell();		
													
					break;
			}
		}

		private void CreatePowerShell()
		{
			var powerShell = PowerShell.Create();
			HookStreamEvents(powerShell.Streams);
			powerShell.InvocationStateChanged += PowerShellOnInvocationStateChanged;			
			m_powerShell = powerShell;
			m_output = new PSDataCollection<PSObject>();
			m_output.DataAdded += OutputOnDataAdded;
			m_resetEvent.Set();
		}

		private void ReturnPowerShell(PowerShell powershell)
		{
			UnhookStreamEvents(powershell.Streams);
			powershell.InvocationStateChanged -= PowerShellOnInvocationStateChanged;
			m_output.DataAdded -= OutputOnDataAdded;			
			powershell.Dispose();
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
			string command = $"param($_,$PSItem){scriptblock}";
			m_powerShell.AddScript(command)
				.AddParameter("_", inputObject)
				.AddParameter("PSItem", inputObject);
			m_powerShell.BeginInvoke(m_input, m_output);
		}

		public void Dispose()
		{
			m_resetEvent.Set();
			m_resetEvent.Dispose();			
			if (m_powerShell != null)
			{
				UnhookStreamEvents(m_powerShell.Streams);
			}
			m_output?.Dispose();
			m_powerShell?.Dispose();
		}

		private void OutputOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{			
			var item = ((PSDataCollection<PSObject>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Output.Add(item);
		}


		private void InformationOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var ir = ((PSDataCollection<InformationRecord>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Information.Add(ir);
		}

		private void ProgressOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<ProgressRecord>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Progress.Add(record);
		}

		private void ErrorOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<ErrorRecord>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Error.Add(record);
		}

		private void DebugOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<DebugRecord>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Debug.Add(record);
		}

		private void WarningOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<WarningRecord>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Warning.Add(record);
		}

		private void VerboseOnDataAdded(object sender, DataAddedEventArgs dataAddedEventArgs)
		{
			var record = ((PSDataCollection<VerboseRecord>)sender)[dataAddedEventArgs.Index];
			m_poolStreams.Verbose.Add(record);
		}
	}
}