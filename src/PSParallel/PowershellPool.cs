using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Threading;


namespace PSParallel
{
	sealed class PowershellPool : IDisposable
	{
		private readonly object _countLock = new object();
		private int _busyCount;
		private readonly CancellationToken _cancellationToken;		
		private readonly List<PowerShellPoolMember> _poolMembers;
		private readonly InitialSessionState _initialSessionState;
		private readonly BlockingCollection<PowerShellPoolMember> _availablePoolMembers = new BlockingCollection<PowerShellPoolMember>(new ConcurrentQueue<PowerShellPoolMember>());
		public readonly PowerShellPoolStreams Streams = new PowerShellPoolStreams();
		private int _processedCount;

		public PowershellPool(int poolSize, InitialSessionState initialSessionState, CancellationToken cancellationToken)
		{
			_poolMembers= new List<PowerShellPoolMember>(poolSize);
			_initialSessionState = initialSessionState;
			_cancellationToken = cancellationToken;

			for (var i = 0; i < poolSize; i++)
			{				
				var powerShellPoolMember = new PowerShellPoolMember(this, i+1, initialSessionState);
				_poolMembers.Add(powerShellPoolMember);
				_availablePoolMembers.Add(powerShellPoolMember);
			}			
		}

		private int GetPartiallyProcessedCount()
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
			var partiallyProcessedCount = totalPercentComplete / 100;
			return partiallyProcessedCount;
		}

		public int GetEstimatedProgressCount()
		{
			lock(_countLock) {
				return _processedCount + GetPartiallyProcessedCount();
			}
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
			return true;
		}


		public void Dispose()
		{
			Streams.Dispose();
			_availablePoolMembers.Dispose();			
		}

		public void ReportAvailable(PowerShellPoolMember poolmember)
		{
			Interlocked.Decrement(ref _busyCount);
			lock (_countLock)
			{
				_processedCount++;
				poolmember.PercentComplete = 0;
			}
			
			poolmember.PercentComplete = 0;
			while (!_availablePoolMembers.TryAdd(poolmember, 1000, _cancellationToken))
			{
				_cancellationToken.ThrowIfCancellationRequested();
				Debug.WriteLine("WaitForAvailablePowershell - TryAdd failed");
			}
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