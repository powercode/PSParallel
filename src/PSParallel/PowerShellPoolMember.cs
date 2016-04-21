using System;
using System.Management.Automation;

namespace PSParallel
{
	class PowerShellPoolMember : IDisposable
	{
		private readonly PowershellPool m_pool;
		private readonly int m_index;
		private readonly PowerShellPoolStreams m_poolStreams;
		private PowerShell m_powerShell;
		public PowerShell PowerShell => m_powerShell;
		public int Index => m_index ;

		private readonly PSDataCollection<PSObject> m_input =new PSDataCollection<PSObject>();
		private PSDataCollection<PSObject> m_output;
		private int m_percentComplete;
		public int PercentComplete
		{
			get { return m_percentComplete; }
			set { m_percentComplete = value; }
		}


		public PowerShellPoolMember(PowershellPool pool, int index)
		{
			m_pool = pool;
			m_index = index;
			m_poolStreams = m_pool.Streams;
			m_input.Complete();			
			CreatePowerShell();			
		}

		private void PowerShellOnInvocationStateChanged(object sender, PSInvocationStateChangedEventArgs psInvocationStateChangedEventArgs)
		{
			switch (psInvocationStateChangedEventArgs.InvocationStateInfo.State)
			{
				case PSInvocationState.Stopped:
					ReleasePowerShell();
					m_pool.ReportStopped(this);
					break;
				case PSInvocationState.Completed:
				case PSInvocationState.Failed:
					ReleasePowerShell();
					CreatePowerShell();
					m_pool.ReportAvailable(this);
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
		}

		private void ReleasePowerShell()
		{
			UnhookStreamEvents(m_powerShell.Streams);
			m_powerShell.InvocationStateChanged -= PowerShellOnInvocationStateChanged;
			m_output.DataAdded -= OutputOnDataAdded;
			m_powerShell.Dispose();
			m_powerShell = null;
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
			m_percentComplete = 0;
			string command = $"param($_,$PSItem, $PSPArallelIndex,$PSParallelProgressId){scriptblock}";
			m_powerShell.AddScript(command)
				.AddParameter("_", inputObject)
				.AddParameter("PSItem", inputObject)
				.AddParameter("PSParallelIndex", m_index)
				.AddParameter("PSParallelProgressId", m_index+1000);
			m_powerShell.BeginInvoke(m_input, m_output);
		}

		public void Dispose()
		{
			var ps = m_powerShell;
			if (ps != null)
			{
				UnhookStreamEvents(ps.Streams);
				ps.Dispose();
			}
			m_output.Dispose();
			m_input.Dispose();
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
			m_percentComplete = record.PercentComplete;
			m_poolStreams.AddProgress(record, m_index);
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

		public void Stop()
		{
			if(m_powerShell.InvocationStateInfo.State != PSInvocationState.Stopped) 
			{ 
				UnhookStreamEvents(m_powerShell.Streams);
				m_powerShell.BeginStop(OnStopped, null);
			}
		}

		private void OnStopped(IAsyncResult ar)
		{
			var ps = m_powerShell;
			if (ps == null)
			{
				return;
			}
			ps.EndStop(ar);
			m_powerShell = null;
		}
	}
}