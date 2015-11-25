using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using Microsoft.PowerShell.Commands;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global
namespace PSParallel
{
	[Alias("ipa")]
	[Cmdlet("Invoke", "Parallel", DefaultParameterSetName = "SessionStateParams")]
	public sealed class InvokeParallelCommand : PSCmdlet, IDisposable
	{
		[Parameter(Mandatory = true, Position = 0)]
		public ScriptBlock ScriptBlock { get; set; }

		[Alias("ppi")]
		[Parameter(ParameterSetName = "InitialSessionState")]
		[Parameter(ParameterSetName = "SessionStateParams")]
		[Parameter(ParameterSetName = "")]
		public int ParentProgressId { get; set; } = -1;

		[Alias("pi")]
		[Parameter(ParameterSetName = "InitialSessionState")]
		[Parameter(ParameterSetName = "SessionStateParams")]
		public int ProgressId { get; set; } = 1000;

		[Alias("pa")]
		[Parameter(ParameterSetName = "InitialSessionState")]
		[Parameter(ParameterSetName = "SessionStateParams")]
		[ValidateNotNullOrEmpty]
		public string ProgressActivity { get; set; } = "Invoke-Parallel";

		[Parameter]
		[ValidateRange(1,128)]
		public int ThrottleLimit { get; set; } = 32;

		[Parameter(ParameterSetName = "InitialSessionState", Mandatory = true)]		
		[AllowNull]
		[Alias("iss")]
		public InitialSessionState InitialSessionState { get; set; }

		[Parameter(ParameterSetName = "SessionStateParams")]
		[ValidateNotNull]
		[Alias("imo")]
		[ArgumentCompleter(typeof(ImportArgumentCompleter))]
		public string[] ImportModule { get; set; }

		[Parameter(ParameterSetName = "SessionStateParams")]
		[ValidateNotNull]
		[ArgumentCompleter(typeof(ImportArgumentCompleter))]
		[Alias("iva")]
		public string[] ImportVariable{ get; set; }

		[Parameter(ParameterSetName = "SessionStateParams")]
		[ValidateNotNull]
		[ArgumentCompleter(typeof(ImportArgumentCompleter))]
		[Alias("ifu")]
		public string[] ImportFunction{ get; set; }

		[Parameter(ValueFromPipeline = true, Mandatory = true)]
		public PSObject InputObject { get; set; }

		[Parameter]
		public SwitchParameter NoProgress { get; set; }

		private readonly CancellationTokenSource m_cancelationTokenSource = new CancellationTokenSource();
		private PowershellPool m_powershellPool;
		private ProgressManager m_progressManager;

		// this is only used when NoProgress is not specified
		// Input is then captured in ProcessRecored and processed in EndProcessing
		private List<PSObject> m_input;

		private static InitialSessionState GetSessionState(SessionState sessionState, string[] modulesToImport, string[] variablesToImport, string[] functionsToImport)
		{
			var initialSessionState = InitialSessionState.CreateDefault2();
			CaptureVariables(sessionState, initialSessionState, variablesToImport);
			CaptureFunctions(sessionState, initialSessionState, functionsToImport);
			if (modulesToImport != null)
			{
				initialSessionState.ImportPSModule(modulesToImport);
			}
			return initialSessionState;
		}

		private static IEnumerable<FunctionInfo> GetFunctions(SessionState sessionState, string[] functionsToImport)
		{
			try
			{
				var functionDrive = sessionState.InvokeProvider.Item.Get("function:");
				var baseObject = (Dictionary<string, FunctionInfo>.ValueCollection) functionDrive[0].BaseObject;
				return functionsToImport == null ? baseObject : baseObject.Where(f=>functionsToImport.Contains(f.Name, StringComparer.OrdinalIgnoreCase));
			}
			catch (DriveNotFoundException)
			{
				return new FunctionInfo[] {};
			}
		}

		private static IEnumerable<PSVariable> GetVariables(SessionState sessionState, string[] variablesToImport)
		{
			try
			{
				string[] noTouchVariables = {"null", "true", "false", "Error"};
				var variables = sessionState.InvokeProvider.Item.Get("Variable:");
				var psVariables = (IEnumerable<PSVariable>) variables[0].BaseObject;
				return psVariables.Where(p=>!noTouchVariables.Contains(p.Name) && 
										(variablesToImport == null || variablesToImport.Contains(p.Name, StringComparer.OrdinalIgnoreCase)));
			}
			catch (DriveNotFoundException)
			{
				return new PSVariable[]{};
			}
		}

		private static void CaptureFunctions(SessionState sessionState, InitialSessionState initialSessionState, string[] functionsToImport)
		{
			var functions = GetFunctions(sessionState, functionsToImport);
			foreach (var func in functions) { 
				initialSessionState.Commands.Add(new SessionStateFunctionEntry(func.Name, func.Definition));
			}
		}

		private static void CaptureVariables(SessionState sessionState, InitialSessionState initialSessionState, string[] variablesToImport)
		{
			var variables = GetVariables(sessionState, variablesToImport);
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
			return GetSessionState(SessionState, ImportModule, ImportVariable, ImportFunction);
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
				m_powershellPool.AddInput(ScriptBlock, InputObject);
				WriteOutputs();
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
						var pr = m_progressManager.GetCurrentProgressRecord($"Starting processing of {i}", m_powershellPool.ProcessedCount);
						WriteProgress(pr);
						m_powershellPool.AddInput(ScriptBlock, i);
						WriteOutputs();
					}
				}
				while(!m_powershellPool.WaitForAllPowershellCompleted(100))
				{	
					if(!NoProgress)
					{				
						var pr = m_progressManager.GetCurrentProgressRecord("All work queued. Waiting for remaining work to complete.", m_powershellPool.ProcessedCount);
						WriteProgress(pr);
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
			foreach (var p in streams.Progress.ReadAll())
			{
				if(!NoProgress)
				{
					p.ParentActivityId = m_progressManager.ActivityId;
				}
				WriteProgress(p);
			}
		}

		public void Dispose()
		{
			m_powershellPool?.Dispose();
			m_cancelationTokenSource.Dispose();
		}

	}
}
