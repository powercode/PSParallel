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
		private int _busyCount;
		private int _processedCount;
		private readonly CancellationToken _cancellationToken;
		private readonly RunspacePool _runspacePool;
		private readonly List<PowerShellPoolMember> _poolMembers;
		private readonly BlockingCollection<PowerShellPoolMember> _availablePoolMembers = new BlockingCollection<PowerShellPoolMember>(new ConcurrentQueue<PowerShellPoolMember>());
		public readonly PowerShellPoolStreams Streams = new PowerShellPoolStreams();

		public int ProcessedCount => _processedCount;		

		public PowershellPool(int poolSize, InitialSessionState initialSessionState, CancellationToken cancellationToken)
		{
			_poolMembers= new List<PowerShellPoolMember>(poolSize);
			_processedCount = 0;
			_cancellationToken = cancellationToken;

			for (var i = 0; i < poolSize; i++)
			{
				var powerShellPoolMember = new PowerShellPoolMember(this, i+1);
				_poolMembers.Add(powerShellPoolMember);
				_availablePoolMembers.Add(powerShellPoolMember);
			}

			_runspacePool = RunspaceFactory.CreateRunspacePool(initialSessionState);
			_runspacePool.SetMaxRunspaces(poolSize);
		}

		public int GetPartiallyProcessedCount()
		{
			var totalPercentComplete = 0;
			var count = _poolMembers.Count;
			for (int i = 0; i < count; ++i)
			{
				var percentComplete = _poolMembers[i].PercentComplete;
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

			Interlocked.Increment(ref _busyCount);
			poolMember.BeginInvoke(scriptblock, inputObject);
			return true;
		}

		public void Open()
		{
			_runspacePool.Open();
		}
		

		public bool WaitForAllPowershellCompleted(int timeoutMilliseconds)
		{
			Contract.Requires(timeoutMilliseconds >= 0);
			var startTicks = Environment.TickCount;
			var currendTicks = startTicks;
			while (currendTicks - startTicks < timeoutMilliseconds)
			{
				currendTicks = Environment.TickCount;
				if (_cancellationToken.IsCancellationRequested)
				{
					return false;
				}
				if (Interlocked.CompareExchange(ref _busyCount, 0, 0) == 0)
				{
					return true;
				}
				Thread.Sleep(10);
			}
			return false;
		}

		private bool TryWaitForAvailablePowershell(int milliseconds, out PowerShellPoolMember poolMember)
		{
			if (!_availablePoolMembers.TryTake(out poolMember, milliseconds, _cancellationToken))
			{
				_cancellationToken.ThrowIfCancellationRequested();
				Debug.WriteLine("WaitForAvailablePowershell - TryTake failed");
				poolMember = null;
				return false;
			}

			poolMember.PowerShell.RunspacePool = _runspacePool;
			Debug.WriteLine($"WaitForAvailablePowershell - Busy: {_busyCount} _processed {_processedCount}, member = {poolMember.Index}");
			return true;
		}


		public void Dispose()
		{
			Streams.Dispose();
			_availablePoolMembers.Dispose();
			_runspacePool?.Dispose();
		}

		public void ReportAvailable(PowerShellPoolMember poolmember)
		{
			Interlocked.Decrement(ref _busyCount);
			Interlocked.Increment(ref _processedCount);
			while (!_availablePoolMembers.TryAdd(poolmember, 1000, _cancellationToken))
			{
				_cancellationToken.ThrowIfCancellationRequested();
				Debug.WriteLine("WaitForAvailablePowershell - TryAdd failed");
			}
			Debug.WriteLine($"ReportAvailable - Busy: {_busyCount} _processed {_processedCount}, member = {poolmember.Index}");
		}

		public void ReportStopped(PowerShellPoolMember powerShellPoolMember)
		{
			Interlocked.Decrement(ref _busyCount);
		}

		public void Stop()
		{
			_availablePoolMembers.CompleteAdding();
			foreach (var poolMember in _poolMembers)
			{
				poolMember.Stop();
			}
			WaitForAllPowershellCompleted(5000);
		}
	}
}