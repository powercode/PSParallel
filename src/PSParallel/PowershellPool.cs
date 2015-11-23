using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

namespace PSParallel
{
	class PowershellPool : IDisposable
	{
		private int m_busyCount;
		private int m_processedCount;
		private readonly CancellationToken m_cancellationToken;
		private readonly RunspacePool m_runspacePool;
		private readonly List<PowerShellPoolMember> m_poolMembers;
		private readonly BlockingCollection<PowerShellPoolMember> m_availablePoolMembers = new BlockingCollection<PowerShellPoolMember>(new ConcurrentStack<PowerShellPoolMember> ());
		public readonly PowerShellPoolStreams Streams = new PowerShellPoolStreams();

		public int ProcessedCount => m_processedCount;

		public PowershellPool(int poolSize, InitialSessionState initialSessionState, CancellationToken cancellationToken)
		{
			m_poolMembers= new List<PowerShellPoolMember>(poolSize);
			m_processedCount = 0;
			m_cancellationToken = cancellationToken;

			for (int i = 0; i < poolSize; i++)
			{
				var powerShellPoolMember = new PowerShellPoolMember(this);
				m_poolMembers.Add(powerShellPoolMember);
				m_availablePoolMembers.Add(powerShellPoolMember);
			}

			m_runspacePool = RunspaceFactory.CreateRunspacePool(initialSessionState);
			m_runspacePool.SetMaxRunspaces(poolSize);
		}

		public void AddInput(ScriptBlock scriptblock,PSObject inputObject)
		{
			try { 
				var powerShell = WaitForAvailablePowershell();		
				powerShell.BeginInvoke(scriptblock, inputObject);
				Interlocked.Increment(ref m_busyCount);
			}
			catch(OperationCanceledException)
			{
				Stop();
			}
        }

		public void Open()
		{
			m_runspacePool.Open();
		}

		public bool WaitForAllPowershellCompleted(int timeoutMilliseconds)
		{
			Contract.Requires(timeoutMilliseconds >=0);
			var startTicks = Environment.TickCount;
			var currendTicks = startTicks;
			while (currendTicks - startTicks < timeoutMilliseconds)
			{
				currendTicks = Environment.TickCount;
				if (m_cancellationToken.IsCancellationRequested)
				{
					return false;
				}
				if (Interlocked.CompareExchange(ref m_busyCount, 0, 0) == 0)
				{
					return true;
				}
				Thread.Sleep(10);

			}
			return false;
		}

		private PowerShellPoolMember WaitForAvailablePowershell()
		{
			var poolmember = m_availablePoolMembers.Take(m_cancellationToken);
			poolmember.PowerShell.RunspacePool = m_runspacePool;
			return poolmember;
		}


		public void Dispose()
		{
			foreach (var pm in m_poolMembers)
			{
				pm.Dispose();
			}
			Streams.Dispose();
			m_availablePoolMembers.Dispose();
		}

		public void ReportCompletion(PowerShellPoolMember poolmember)
		{
			Interlocked.Decrement(ref m_busyCount);
			Interlocked.Increment(ref m_processedCount);
			if(poolmember.PowerShell != null)
			{ 
				m_availablePoolMembers.Add(poolmember);
			}
		}

		private void Stop()
		{
			foreach (var poolMember in m_availablePoolMembers.GetConsumingEnumerable())
			{
				poolMember.Stop();
			}
		}
	}
}