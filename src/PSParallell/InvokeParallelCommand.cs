using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Runtime.CompilerServices;
using System.Threading;
namespace PSParallel
{
	[Cmdlet("Invoke", "Parallel", DefaultParameterSetName = "Progress")]
	// ReSharper disable once UnusedMember.Global
	public class InvokeParallelCommand : PSCmdlet, IDisposable
	{
		[Parameter(Mandatory = true, Position = 0)]
		// ReSharper disable once MemberCanBePrivate.Global
		// ReSharper disable once UnusedAutoPropertyAccessor.Global
		public ScriptBlock Process { get; set; }

		[Parameter(ParameterSetName = "Progress")]
		public int ParentActivityId { get; set; } = -1;
		[Parameter(ParameterSetName = "Progress")]
		public int ActivityId { get; set; } = 47;

		[Parameter(ParameterSetName = "Progress")]
		[ValidateNotNullOrEmpty]
		public string Activity { get; set; } = "Invoke-Parallel";
		[Parameter(ParameterSetName = "Progress")]
		[ValidateNotNullOrEmpty]
		public string StatusDescription { get; set; }

		[Parameter()]
		[ValidateRange(1,48)]
		// ReSharper disable once MemberCanBePrivate.Global
		// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        public int ThrottleLimit { get; set; } = 10;
		
		
		[Parameter(ValueFromPipeline = true, Mandatory = true)]
		// ReSharper disable once MemberCanBePrivate.Global
		// ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
		// ReSharper disable once UnusedAutoPropertyAccessor.Global
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
			var variables = scriptBlock.Ast.FindAll(ast =>
			{
				var v  = ast as VariableExpressionAst;
				if (v == null) return false;
				var assignment = v.Parent as AssignmentStatementAst;
				if (assignment != null && ReferenceEquals(assignment.Left,v))
				{
					return false;
				}
				return true;
			}, true);
			var varDict = new Dictionary<string, SessionStateVariableEntry>();
			foreach (var ast in variables)
			{
				var v = (VariableExpressionAst)ast;
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
				var v= sessionState.PSVariable.Get(pref);
				if (v != null)
				{
					var ssve = new SessionStateVariableEntry(v.Name, v.Value,
						v.Description, v.Options, v.Attributes);
					varDict.Add(v.Name, ssve);
				}
			}			

			initialSessionState.Variables.Add(varDict.Values);			
			return initialSessionState;
		}

		protected override void BeginProcessing()
		{
			m_initialSessionState = GetSessionState(Process, SessionState);
			m_powershellPool = new PowershellPool(ThrottleLimit,m_initialSessionState, m_cancelationTokenSource.Token);
			m_powershellPool.Open();
			if (!NoProgress)
			{
				m_progressManager = new ProgressManager(ActivityId, Activity, $"Processing with {ThrottleLimit} workers", ParentActivityId);
				m_input = new List<PSObject>(500);
			}	
		}



		protected override void ProcessRecord()
		{	
			if(NoProgress)
			{					
				m_powershellPool.AddInput(Process, InputObject);			
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
						var pr = m_progressManager.GetCurrentProgressRecord(i.ToString());
						WriteProgress(pr);
						m_powershellPool.AddInput(Process, i);
						WriteOutputs();
					}
				}
				while(!m_powershellPool.WaitForAllPowershellCompleted(100))
				{
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
		}
		
	}
}
