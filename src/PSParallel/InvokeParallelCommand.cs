using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Threading;
using Microsoft.PowerShell.Commands;

namespace PSParallel
{
	[Alias("ipa")]
	[Cmdlet("Invoke", "Parallel", DefaultParameterSetName = "Progress")]
	public sealed class InvokeParallelCommand : PSCmdlet, IDisposable
	{
		[Parameter(Mandatory = true, Position = 0)]
		public ScriptBlock ScriptBlock { get; set; }

		[Alias("ppi")]
		[Parameter(ParameterSetName = "Progress")]
		public int ParentProgressId { get; set; } = -1;

		[Alias("pi")]
		[Parameter(ParameterSetName = "Progress")]
		public int ProgressId { get; set; } = 1000;

		[Alias("pa")]
		[Parameter(ParameterSetName = "Progress")]
		[ValidateNotNullOrEmpty]
		public string ProgressActivity { get; set; } = "Invoke-Parallel";

		[Parameter]
		[ValidateRange(1,128)]
		public int ThrottleLimit { get; set; } = 32;

		[Parameter(ValueFromPipeline = true, Mandatory = true)]
		public PSObject InputObject { get; set; }

		[Parameter(ParameterSetName = "NoProgress")]
		public SwitchParameter NoProgress { get; set; }

		private readonly CancellationTokenSource m_cancelationTokenSource = new CancellationTokenSource();
		private PowershellPool m_powershellPool;
		private InitialSessionState m_initialSessionState;
		private ProgressManager m_progressManager;

		// this is only used when NoProgress is not specified
		// Input is then captured in ProcessRecored and processed in EndProcessing
		private List<PSObject> m_input;

		private static InitialSessionState GetSessionState(ScriptBlock scriptBlock, SessionState sessionState)
		{
			var initialSessionState = InitialSessionState.CreateDefault2();

			CaptureVariables(scriptBlock, sessionState, initialSessionState);
			// this will get invoked recursively

			var functions = GetFunctions(sessionState);
			
			CaptureFunctions(scriptBlock, initialSessionState, functions, new HashSet<string>());
			return initialSessionState;
		}

		private static IDictionary<string, FunctionInfo> GetFunctions(SessionState sessionState)
		{
			var baseObject = (Dictionary<string,FunctionInfo>.ValueCollection) sessionState.InvokeProvider.Item.Get("function:")[0].BaseObject;
			return baseObject.ToDictionary(f=>f.Name);
		}

		private static void CaptureFunctions(ScriptBlock scriptBlock, InitialSessionState initialSessionState, 
			IDictionary<string, FunctionInfo> functions, ISet<string> processedFunctions)
		{
			var commands = scriptBlock.Ast.FindAll((ast) => ast is CommandAst, true);

			var nonProcessedCommandNames = commands.Cast<CommandAst>()
					.Select(commandAst => commandAst.CommandElements[0].Extent.Text)
					.Where(commandName => !processedFunctions.Contains(commandName));
			foreach (var commandName in nonProcessedCommandNames)
			{

				FunctionInfo functionInfo;
				if (!functions.TryGetValue(commandName, out functionInfo))
				{
					continue;
				}
				initialSessionState.Commands.Add(new SessionStateFunctionEntry(functionInfo.Name, functionInfo.Definition));
				processedFunctions.Add(commandName);
				CaptureFunctions(functionInfo.ScriptBlock, initialSessionState, functions, processedFunctions);
			}
		}


		private static void CaptureVariables(ScriptBlock scriptBlock, SessionState sessionState,
			InitialSessionState initialSessionState)
		{
			var variables = scriptBlock.Ast.FindAll(ast => ast is VariableExpressionAst, true);
			var varDict = new Dictionary<string, SessionStateVariableEntry>();
			foreach (var ast in variables)
			{
				var v = (VariableExpressionAst) ast;
				var variableName = v.VariablePath.UserPath;
				if (variableName == "_" || varDict.ContainsKey(variableName))
				{
					continue;
				}

				var variable = sessionState.PSVariable.Get(variableName);
				if (variable != null)
				{
					var ssve = new SessionStateVariableEntry(variable.Name, variable.Value,
						variable.Description, variable.Options, variable.Attributes);
					varDict.Add(variableName, ssve);
				}
			}

			var prefs = new[]
			{
				"ErrorActionPreference", "DebugPreference", "VerbosePreference", "WarningPreference",
				"ProgressPreference", "InformationPreference", "ConfirmPreference", "WhatIfPreference"
			};
			foreach (var pref in prefs)
			{
				var v = sessionState.PSVariable.Get(pref);
				if (v != null)
				{
					var ssve = new SessionStateVariableEntry(v.Name, v.Value,
						v.Description, v.Options, v.Attributes);
					varDict.Add(v.Name, ssve);
				}
			}

			initialSessionState.Variables.Add(varDict.Values);
		}

		protected override void BeginProcessing()
		{
			m_initialSessionState = GetSessionState(ScriptBlock, SessionState);
			m_powershellPool = new PowershellPool(ThrottleLimit,m_initialSessionState, m_cancelationTokenSource.Token);
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
			try {
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
					var pr = m_progressManager.GetCurrentProgressRecord("All work queued. Waiting for remaining work to complete.", m_powershellPool.ProcessedCount);
					WriteProgress(pr);
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
		}

		private void WriteOutputs()
		{
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
			m_powershellPool.Dispose();
			m_cancelationTokenSource.Dispose();
		}

	}
}
