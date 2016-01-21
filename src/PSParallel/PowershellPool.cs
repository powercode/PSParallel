using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

namespace PSParallel
{
	sealed class PowershellPool : IDisposable
	{
		private int m_busyCount;
		private int m_processedCount;
		private readonly CancellationToken m_cancellationToken;
		private readonly RunspacePool m_runspacePool;
		private readonly List<PowerShellPoolMember> m_poolMembers;
		private readonly BlockingCollection<PowerShellPoolMember> m_availablePoolMembers = new BlockingCollection<PowerShellPoolMember>(new ConcurrentQueue<PowerShellPoolMember>());
		public readonly PowerShellPoolStreams Streams = new PowerShellPoolStreams();

		public int ProcessedCount => m_processedCount;		

		public PowershellPool(int poolSize, InitialSessionState initialSessionState, CancellationToken cancellationToken)
		{
			m_poolMembers= new List<PowerShellPoolMember>(poolSize);
			m_processedCount = 0;
			m_cancellationToken = cancellationToken;

			for (var i = 0; i < poolSize; i++)
			{
				var powerShellPoolMember = new PowerShellPoolMember(this, i+1);
				m_poolMembers.Add(powerShellPoolMember);
				m_availablePoolMembers.Add(powerShellPoolMember);
			}

			m_runspacePool = RunspaceFactory.CreateRunspacePool(initialSessionState);
			m_runspacePool.SetMaxRunspaces(poolSize);
		}

		public int GetPartiallyProcessedCount()
		{
			var totalPercentComplete = 0;
			var count = m_poolMembers.Count;
			for (int i = 0; i < count; ++i)
			{
				var percentComplete = m_poolMembers[i].PercentComplete;
				if (percentComplete < 0)
				{
					percentComplete = 0;
				}
				else if(percentComplete > 100)
				{
					percentComplete = 100;
				}
				totalPercentComplete += percentComplete;
			}			
			return totalPercentComplete / 100;
		}

		public bool TryAddInput(ScriptBlock scriptblock,PSObject inputObject)
		{			
			PowerShellPoolMember poolMember;
			if(!TryWaitForAvailablePowershell(100, out poolMember))
			{
				return false;							
			}

			Interlocked.Increment(ref m_busyCount);
			poolMember.BeginInvoke(scriptblock, inputObject);
			return true;
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

		private bool TryWaitForAvailablePowershell(int milliseconds, out PowerShellPoolMember poolMember)
		{			
			if(!m_availablePoolMembers.TryTake(out poolMember, milliseconds, m_cancellationToken))
			{
				m_cancellationToken.ThrowIfCancellationRequested();
				Debug.WriteLine($"WaitForAvailablePowershell - TryTake failed");
				poolMember = null;
				return false;
			}
			
			poolMember.PowerShell.RunspacePool = m_runspacePool;
			Debug.WriteLine($"WaitForAvailablePowershell - Busy: {m_busyCount} _processed {m_processedCount}, member = {poolMember.Index}");
			return true;
		}


		public void Dispose()
		{
			Streams.Dispose();
			m_availablePoolMembers.Dispose();
			m_runspacePool?.Dispose();
		}

		public void ReportAvailable(PowerShellPoolMember poolmember)
		{
			Interlocked.Decrement(ref m_busyCount);
			Interlocked.Increment(ref m_processedCount);
			while (!m_availablePoolMembers.TryAdd(poolmember, 1000, m_cancellationToken))
			{
				m_cancellationToken.ThrowIfCancellationRequested();
				Debug.WriteLine($"WaitForAvailablePowershell - TryAdd failed");
			}			
			Debug.WriteLine($"ReportAvailable - Busy: {m_busyCount} _processed {m_processedCount}, member = {poolmember.Index}");	
			
		}

		public void ReportStopped(PowerShellPoolMember powerShellPoolMember)
		{
			Interlocked.Decrement(ref m_busyCount);
		}

		public void Stop()
		{
			m_availablePoolMembers.CompleteAdding();
			foreach (var poolMember in m_poolMembers)
			{
				poolMember.Stop();
			}
			WaitForAllPowershellCompleted(5000);
		}
	}
}