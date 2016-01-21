using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

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
		[ValidateRange(1,128)]
		public int ThrottleLimit { get; set; } = 32;

		[Parameter]		
		[AllowNull]
		[Alias("iss")]
		public InitialSessionState InitialSessionState { get; set; }
		
		[Parameter(ValueFromPipeline = true, Mandatory = true)]
		public PSObject InputObject { get; set; }

		[Parameter(ParameterSetName = "NoProgress")]
		public SwitchParameter NoProgress { get; set; }

		private readonly CancellationTokenSource m_cancelationTokenSource = new CancellationTokenSource();
		private PowershellPool m_powershellPool;
		private ProgressManager m_progressManager;

		// this is only used when NoProgress is not specified
		// Input is then captured in ProcessRecored and processed in EndProcessing
		private List<PSObject> m_input;

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
				return (Dictionary<string, FunctionInfo>.ValueCollection) functionDrive[0].BaseObject;
				
			}
			catch (DriveNotFoundException)
			{
				return new FunctionInfo[] {};
			}
		}

		private static IEnumerable<PSVariable> GetVariables(SessionState sessionState)
		{
			try
			{
				string[] noTouchVariables = {"null", "true", "false", "Error"};
				var variables = sessionState.InvokeProvider.Item.Get("Variable:");
				var psVariables = (IEnumerable<PSVariable>) variables[0].BaseObject;
				return psVariables.Where(p=>!noTouchVariables.Contains(p.Name));
			}
			catch (DriveNotFoundException)
			{
				return new PSVariable[]{};
			}
		}

		private static void CaptureFunctions(SessionState sessionState, InitialSessionState initialSessionState)
		{
			var functions = GetFunctions(sessionState);
			foreach (var func in functions) { 
				initialSessionState.Commands.Add(new SessionStateFunctionEntry(func.Name, func.Definition));
			}
		}

		private static void CaptureVariables(SessionState sessionState, InitialSessionState initialSessionState)
		{
			var variables = GetVariables(sessionState);
			foreach(var variable in variables)
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
				foreach(var p in new[]{nameof(ProgressActivity), nameof(ParentProgressId), nameof(ProgressId)})
				{
					if (!boundParameters.ContainsKey(p)) continue;
					var argumentException = new ArgumentException($"'{p}' must not be specified together with 'NoProgress'", p);
					ThrowTerminatingError(new ErrorRecord(argumentException, "InvalidProgressParam", ErrorCategory.InvalidArgument, p));
				}
			}
		}

		InitialSessionState GetSessionState()
		{
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

		protected override void BeginProcessing()
		{
			ValidateParameters();
			var iss = GetSessionState();
			m_powershellPool = new PowershellPool(ThrottleLimit, iss, m_cancelationTokenSource.Token);
			m_powershellPool.Open();
			if (!NoProgress)
			{
				m_progressManager = new ProgressManager(ProgressId, ProgressActivity, $"Processing with {ThrottleLimit} workers", ParentProgressId);
				m_input = new List<PSObject>(500);
			}
		}
		

		protected override void ProcessRecord()
		{
			if(NoProgress)
			{
				while (!m_powershellPool.TryAddInput(ScriptBlock, InputObject))
				{
					WriteOutputs();
				}								
			}
			else
			{
				m_input.Add(InputObject);
			}
		}

		protected override void EndProcessing()
		{
			try 
			{ 
				if (!NoProgress)
				{
					m_progressManager.TotalCount = m_input.Count;
					foreach (var i in m_input)
					{
						var processed = m_powershellPool.ProcessedCount + m_powershellPool.GetPartiallyProcessedCount();
						m_progressManager.UpdateCurrentProgressRecord($"Starting processing of {i}", processed);
						WriteProgress(m_progressManager.ProgressRecord);
						while (!m_powershellPool.TryAddInput(ScriptBlock, i))
						{
							WriteOutputs();
						}												
					}
				}
				while(!m_powershellPool.WaitForAllPowershellCompleted(100))
				{	
					if(!NoProgress)
					{				
						m_progressManager.UpdateCurrentProgressRecord("All work queued. Waiting for remaining work to complete.", m_powershellPool.ProcessedCount);
						WriteProgress(m_progressManager.ProgressRecord);
					}
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
				if(!NoProgress)
				{
					WriteProgress(m_progressManager.Completed());
				}
			}
		}

		protected override void StopProcessing()
		{
			m_cancelationTokenSource.Cancel();
			m_powershellPool?.Stop();
		}

		private void WriteOutputs()
		{
			Debug.WriteLine("Processing output");
			if (m_cancelationTokenSource.IsCancellationRequested)
			{
				return;
			}
			var streams = m_powershellPool.Streams;
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
			var progressCount = streams.Progress.Count;
			if (progressCount > 0)
			{
				foreach (var p in streams.Progress.ReadAll())
				{
					if(!NoProgress)
					{
						p.ParentActivityId = m_progressManager.ActivityId;														
					}
					WriteProgress(p);				
				}		
				if(!NoProgress)
				{		
					m_progressManager.UpdateCurrentProgressRecord(m_powershellPool.ProcessedCount + m_powershellPool.GetPartiallyProcessedCount());
					WriteProgress(m_progressManager.ProgressRecord);
				}
			}
		}

		public void Dispose()
		{
			m_powershellPool?.Dispose();
			m_cancelationTokenSource.Dispose();
		}

	}
}
