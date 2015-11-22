using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PSParallel
{
	class PowerShellInvokation : IDisposable
	{
		public int Index { get; }


		public PowerShellInvokation(ScriptBlock process, RunspacePool runspacePool, int i, InitialSessionState initialSessionState)
		{
			Index = i;
			m_process = process;
			m_runspacePool = runspacePool;
			m_initialSessionState = initialSessionState;
		}

		public void BeginInvoke(PSObject inputObject)
		{
			m_powershell = PowerShell.Create(m_initialSessionState);
			m_powershell.RunspacePool = m_runspacePool;
			string command = $"param($_,$PSItem){m_process}";
			m_powershell.AddScript(command)
				.AddParameter("_", inputObject)
				.AddParameter("PSItem", inputObject);				
			m_asyncResult = m_powershell.BeginInvoke();
		}

		private PowerShell m_powershell;

		private readonly ScriptBlock m_process;
		private readonly RunspacePool m_runspacePool;
		private readonly InitialSessionState m_initialSessionState;
		IAsyncResult m_asyncResult;

		public void Dispose()
		{
			m_powershell?.Dispose();
		}

		public bool IsCompleted => m_asyncResult?.IsCompleted ?? false;
		public bool IsAvailable => m_powershell == null || (m_asyncResult?.IsCompleted ?? false);

		public PSDataCollection<PSObject> EndInvoke()
		{
			var ps = m_powershell;
			var asyncRes = m_asyncResult;
			m_asyncResult = null;
			m_powershell = null;
			var res = ps.EndInvoke(asyncRes);

			return res;
		}
	}
}