using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using Microsoft.PowerShell.Commands;

namespace PSParallel
{
	public class ImportArgumentCompleter : IArgumentCompleter
	{
		private static readonly CompletionResult[] EmptyCompletion = new CompletionResult[0];

		public IEnumerable<CompletionResult> CompleteArgument(string commandName, string parameterName, string wordToComplete,
			CommandAst commandAst, IDictionary fakeBoundParameters)
		{
			var fakeParam = fakeBoundParameters[parameterName];
			var paramList = new List<string>();
			if (fakeParam.GetType().IsArray)
			{
				paramList.AddRange(from i in (object[]) fakeParam select i.ToString());
			}
			else
			{
				paramList.Add(fakeParam.ToString());
			}
			using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
			{
				switch (parameterName)
				{
					case "ImportModule":
					{
						powerShell
							.AddCommand(new CmdletInfo("Get-Module", typeof(GetModuleCommand)))
							.AddParameter("Name", wordToComplete + "*");
						return from mod in powerShell.Invoke<PSModuleInfo>()
							   where !paramList.Contains(mod.Name)
							  select new CompletionResult(mod.Name, mod.Name, CompletionResultType.ParameterValue, mod.Description.OrIfEmpty(mod.Name));
					}
					case "ImportVariable":
					{
						powerShell
							.AddCommand(new CmdletInfo("Get-Variable", typeof(GetVariableCommand)))
							.AddParameter("Name", wordToComplete + "*");
						return from varInfo in powerShell.Invoke<PSVariable>()
							   where !paramList.Contains(varInfo.Name)
							   select new CompletionResult(varInfo.Name, varInfo.Name, CompletionResultType.ParameterValue, varInfo.Description.OrIfEmpty(varInfo.Name) );
					}
					case "ImportFunction":
					{
						powerShell
							.AddCommand(new CmdletInfo("Get-Item", typeof(GetVariableCommand)))
							.AddParameter("Path", $"function:{wordToComplete}*");

						return
							from fi in powerShell.Invoke<IEnumerable<FunctionInfo>>().First()
							where !paramList.Contains(fi.Name)
							select new CompletionResult(fi.Name, fi.Name, CompletionResultType.ParameterValue, fi.Description.OrIfEmpty(fi.Name));
					}
				}
			}

			return EmptyCompletion;
		}
		
	}

	static class StringExtension
	{
		public static string OrIfEmpty(this string s, string other)
		{
			return string.IsNullOrEmpty(s) ? other : s;
		}
	}
}