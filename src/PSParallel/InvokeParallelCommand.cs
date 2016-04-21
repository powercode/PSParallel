using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

using static PSParallel.PsParallelEventSource;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global
namespace PSParallel
{
	[Alias("ipa")]
	[Cmdlet("Invoke", "Parallel", DefaultParameterSetName = "Progress")]
	public sealed class InvokeParallelCommand : PSCmdlet, IDisposable
	{
		[Parameter(Mandatory = true, Position = 0)]
		public ScriptBlock ScriptBlock { get; set; }

		[Parameter(ParameterSetName = "Progress")]
		[Alias("ppi")]
		public int ParentProgressId { get; set; } = -1;

		[Parameter(ParameterSetName = "Progress")]
		[Alias("pi")]
		public int ProgressId { get; set; } = 1000;

		[Parameter(ParameterSetName = "Progress")]
		[Alias("pa")]
		[ValidateNotNullOrEmpty]
		public string ProgressActivity { get; set; } = "Invoke-Parallel";

		[Parameter]
		[ValidateRange(1, 128)]
		public int ThrottleLimit { get; set; } = 32;

		[Parameter]
		[AllowNull]
		[Alias("iss")]
		public InitialSessionState InitialSessionState { get; set; }

		[Parameter(ValueFromPipeline = true, Mandatory = true)]
		public PSObject InputObject { get; set; }

		[Parameter(ParameterSetName = "NoProgress")]
		public SwitchParameter NoProgress { get; set; }

		private readonly CancellationTokenSource _cancelationTokenSource = new CancellationTokenSource();
		internal PowershellPool PowershellPool;

		private static InitialSessionState GetSessionState(SessionState sessionState)
		{
			var initialSessionState = InitialSessionState.CreateDefault2();
			CaptureVariables(sessionState, initialSessionState);
			CaptureFunctions(sessionState, initialSessionState);
			return initialSessionState;
		}

		private static IEnumerable<FunctionInfo> GetFunctions(SessionState sessionState)
		{
			try
			{
				var functionDrive = sessionState.InvokeProvider.Item.Get("function:");
				return (Dictionary<string, FunctionInfo>.ValueCollection)functionDrive[0].BaseObject;

			}
			catch (DriveNotFoundException)
			{
				return new FunctionInfo[] { };
			}
		}

		private static IEnumerable<PSVariable> GetVariables(SessionState sessionState)
		{
			try
			{
				string[] noTouchVariables = { "null", "true", "false", "Error" };
				var variables = sessionState.InvokeProvider.Item.Get("Variable:");
				var psVariables = (IEnumerable<PSVariable>)variables[0].BaseObject;
				return psVariables.Where(p => !noTouchVariables.Contains(p.Name));
			}
			catch (DriveNotFoundException)
			{
				return new PSVariable[] { };
			}
		}

		private static void CaptureFunctions(SessionState sessionState, InitialSessionState initialSessionState)
		{
			var functions = GetFunctions(sessionState);
			foreach (var func in functions)
			{
				initialSessionState.Commands.Add(new SessionStateFunctionEntry(func.Name, func.Definition));
			}
		}

		private static void CaptureVariables(SessionState sessionState, InitialSessionState initialSessionState)
		{
			var variables = GetVariables(sessionState);
			foreach (var variable in variables)
			{
				var existing = initialSessionState.Variables[variable.Name].FirstOrDefault();
				if (existing != null && (existing.Options & (ScopedItemOptions.Constant | ScopedItemOptions.ReadOnly)) != ScopedItemOptions.None)
				{
					continue;
				}
				initialSessionState.Variables.Add(new SessionStateVariableEntry(variable.Name, variable.Value, variable.Description, variable.Options, variable.Attributes));
			}
		}

		private void ValidateParameters()
		{
			if (NoProgress)
			{
				var boundParameters = MyInvocation.BoundParameters;
				foreach (var p in new[] { nameof(ProgressActivity), nameof(ParentProgressId), nameof(ProgressId) })
				{
					if (!boundParameters.ContainsKey(p)) continue;
					var argumentException = new ArgumentException($"'{p}' must not be specified together with 'NoProgress'", p);
					ThrowTerminatingError(new ErrorRecord(argumentException, "InvalidProgressParam", ErrorCategory.InvalidArgument, p));
				}
			}
		}

		InitialSessionState GetSessionState()
		{
			try
			{
				Log.BeginGetSessionState();
				if (MyInvocation.BoundParameters.ContainsKey(nameof(InitialSessionState)))
				{
					if (InitialSessionState == null)
					{
						return InitialSessionState.Create();
					}
					return InitialSessionState;
				}
				return GetSessionState(SessionState);
			}
			finally
			{
				Log.EndGetSessionState();
			}
		}
		
		
		private WorkerBase _worker;
		protected override void BeginProcessing()
		{
			ValidateParameters();
			var iss = GetSessionState();
			PowershellPool = new PowershellPool(ThrottleLimit, iss, _cancelationTokenSource.Token);
			PowershellPool.Open();
			_worker = NoProgress ? (WorkerBase) new NoProgressWorker(this) : new ProgressWorker(this);
		}


		protected override void ProcessRecord()
		{
			_worker.ProcessRecord(InputObject);
		}

		protected override void EndProcessing()
		{
			_worker.EndProcessing();

		}

		protected override void StopProcessing()
		{
			_cancelationTokenSource.Cancel();
			PowershellPool?.Stop();
		}

		private void WriteOutputs()
		{
			Debug.WriteLine("Processing output");
			if (_cancelationTokenSource.IsCancellationRequested)
			{
				return;
			}
			var streams = PowershellPool.Streams;
			foreach (var o in streams.Output.ReadAll())
			{
				WriteObject(o, false);
			}

			foreach (var o in streams.Debug.ReadAll())
			{
				WriteDebug(o.Message);
			}
			foreach (var e in streams.Error.ReadAll())
			{
				WriteError(e);
			}
			foreach (var w in streams.Warning.ReadAll())
			{
				WriteWarning(w.Message);
			}
			foreach (var i in streams.Information.ReadAll())
			{
				WriteInformation(i);
			}
			foreach (var v in streams.Verbose.ReadAll())
			{
				WriteVerbose(v.Message);
			}
			_worker.WriteProgress(streams.ReadAllProgress());			
		}

		public void Dispose()
		{
			PowershellPool?.Dispose();
			_cancelationTokenSource.Dispose();
		}


		private abstract class WorkerBase
		{
			protected readonly InvokeParallelCommand Cmdlet;
			protected readonly PowershellPool Pool;
			protected bool Stopping => Cmdlet.Stopping;
			protected void WriteOutputs() => Cmdlet.WriteOutputs();
			protected void WriteProgress(ProgressRecord record) => Cmdlet.WriteProgress(record);
			public abstract void ProcessRecord(PSObject inputObject);
			public abstract void EndProcessing();
			public abstract void WriteProgress(Collection<ProgressRecord> progress);
			protected ScriptBlock ScriptBlock => Cmdlet.ScriptBlock;

			protected WorkerBase(InvokeParallelCommand cmdlet)
			{
				Cmdlet = cmdlet;
				Pool = cmdlet.PowershellPool;
			}
		}

		class NoProgressWorker : WorkerBase
		{
			public NoProgressWorker(InvokeParallelCommand cmdlet) : base(cmdlet)
			{
			}

			public override void ProcessRecord(PSObject inputObject)
			{
				while (!Pool.TryAddInput(Cmdlet.ScriptBlock, Cmdlet.InputObject))
				{
					Cmdlet.WriteOutputs();
				}
			}

			public override void EndProcessing()
			{
				while (!Pool.WaitForAllPowershellCompleted(100))
				{
					if (Stopping)
					{
						return;
					}
					WriteOutputs();
				}
				WriteOutputs();
			}

			public override void WriteProgress(Collection<ProgressRecord> progress)
			{
				foreach (var p in progress)
				{
					base.WriteProgress(p);
				}
			}
		}

		class ProgressWorker : WorkerBase
		{
			readonly ProgressManager _progressManager;
			private readonly List<PSObject> _input;
			public ProgressWorker(InvokeParallelCommand cmdlet) : base(cmdlet)
			{
				_progressManager = new ProgressManager(cmdlet.ProgressId, cmdlet.ProgressActivity, $"Processing with {cmdlet.ThrottleLimit} workers", cmdlet.ParentProgressId);
				_input = new List<PSObject>(500);
			}

			public override void ProcessRecord(PSObject inputObject)
			{
				_input.Add(inputObject);
			}

			public override void EndProcessing()
			{
				try
				{
					_progressManager.TotalCount = _input.Count;
					foreach (var i in _input)
					{
						var processed = Pool.GetEstimatedProgressCount();
						_progressManager.UpdateCurrentProgressRecord($"Starting processing of {i}", processed);
						WriteProgress(_progressManager.ProgressRecord);
						while (!Pool.TryAddInput(ScriptBlock, i))
						{
							WriteOutputs();
						}
					}

					while (!Pool.WaitForAllPowershellCompleted(100))
					{

						_progressManager.UpdateCurrentProgressRecord("All work queued. Waiting for remaining work to complete.", Pool.GetEstimatedProgressCount());
						WriteProgress(_progressManager.ProgressRecord);

						if (Stopping)
						{
							return;
						}
						WriteOutputs();
					}
					WriteOutputs();
				}
				finally
				{
					WriteProgress(_progressManager.Completed());
				}
			}

			public override void WriteProgress(Collection<ProgressRecord> progress)
			{
				foreach (var p in progress)
				{
					p.ParentActivityId = _progressManager.ActivityId;
					WriteProgress(p);
				}
				_progressManager.UpdateCurrentProgressRecord(Pool.GetEstimatedProgressCount());
				WriteProgress(_progressManager.ProgressRecord);
			}
		}
	}
}
