using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.PowerShell.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PSParallel;

namespace PSParallelTests
{
	[TestClass]
	public sealed class InvokeParallelTests : IDisposable
	{
		readonly RunspacePool m_runspacePool;

		public InvokeParallelTests()
		{						
			var iss = InitialSessionState.Create();
			iss.LanguageMode = PSLanguageMode.FullLanguage;
			iss.Commands.Add(new []
			{
				new SessionStateCmdletEntry("Write-Error",		typeof(WriteErrorCommand), null),
				new SessionStateCmdletEntry("Write-Verbose",	typeof(WriteVerboseCommand), null),
				new SessionStateCmdletEntry("Write-Debug",		typeof(WriteDebugCommand), null),
				new SessionStateCmdletEntry("Write-Progress",	typeof(WriteProgressCommand), null),
				new SessionStateCmdletEntry("Write-Warning",	typeof(WriteWarningCommand), null),
				new SessionStateCmdletEntry("Write-Information", typeof(WriteInformationCommand), null),
				new SessionStateCmdletEntry("Invoke-Parallel",	typeof(InvokeParallelCommand), null), 
			});
			iss.Providers.Add(new SessionStateProviderEntry("Function", typeof(FunctionProvider), null));
			iss.Providers.Add(new SessionStateProviderEntry("Variable", typeof(VariableProvider), null));
			iss.Variables.Add(new []
			{
				new SessionStateVariableEntry("ErrorActionPreference", ActionPreference.Continue, "Dictates the action taken when an error message is delivered"), 
			});
			m_runspacePool = RunspaceFactory.CreateRunspacePool(iss);
			m_runspacePool.SetMaxRunspaces(10);
			m_runspacePool.Open();
		}
		[TestMethod]
		public void TestOutput()
		{
			using (var ps = PowerShell.Create())
			{
				ps.RunspacePool = m_runspacePool;

				ps.AddCommand("Invoke-Parallel")
					.AddParameter("ScriptBlock", ScriptBlock.Create("$_* 2"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1,2,3,4,5};
				input.Complete();
				var output = ps.Invoke<int>(input);
				var sum = output.Aggregate(0, (a, b) => a + b);
				Assert.AreEqual(30, sum);
			}
		}

		[TestMethod]
		public void TestParallelOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;

				ps.AddCommand("Invoke-Parallel")
					.AddParameter("ScriptBlock", ScriptBlock.Create("$_* 2"))
					.AddParameter("ThrottleLimit", 10);
				var input = new PSDataCollection<int>(Enumerable.Range(1, 1000));
				input.Complete();
				var output = ps.Invoke<int>(input);
				var sum = output.Aggregate(0, (a, b) => a + b);
				Assert.AreEqual(1001000, sum);
			}
		}

		[TestMethod]
		public void TestVerboseOutput()
		{
			using (var ps = PowerShell.Create())
			{
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("$VerbosePreference='Continue'", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Verbose $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				Assert.IsFalse(ps.HadErrors, "We don't expect errors here");
				var vrb = ps.Streams.Verbose.ReadAll();
				Assert.IsTrue(vrb.Any(v => v.Message == "1"), "Some verbose message should be '1'");
			}
		}

		[TestMethod]
		public void TestNoVerboseOutputWithoutPreference()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Verbose $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				Assert.IsFalse(ps.HadErrors, "We don't expect errors here");
				var vrb = ps.Streams.Verbose.ReadAll();
				Assert.IsFalse(vrb.Any(v => v.Message == "1"), "No verbose message should be '1'");
			}
		}

		[TestMethod]
		public void TestDebugOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("$DebugPreference='Continue'", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Debug $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				Assert.IsFalse(ps.HadErrors, "We don't expect errors here");
				var dbg = ps.Streams.Debug.ReadAll();
				Assert.IsTrue(dbg.Any(d => d.Message == "1"), "Some debug message should be '1'");
			}
		}

		[TestMethod]
		public void TestNoDebugOutputWithoutPreference()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Debug $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				var dbg = ps.Streams.Debug.ReadAll();
				Assert.IsFalse(dbg.Any(d => d.Message == "1"), "No debug message should be '1'");
			}
		}

		[TestMethod]
		public void TestWarningOutput()
		{

			using (var ps = PowerShell.Create())
			{
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("$WarningPreference='Continue'", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Warning $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				var wrn = ps.Streams.Warning.ReadAll();
				Assert.IsTrue(wrn.Any(w => w.Message == "1"), "Some warning message should be '1'");
			}
		}

		[TestMethod]
		public void TestNoWarningOutputWithoutPreference()
		{
			using (var ps = PowerShell.Create())
			{
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("$WarningPreference='SilentlyContinue'", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Warning $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				var wrn = ps.Streams.Warning.ReadAll();
				Assert.IsFalse(wrn.Any(w => w.Message == "1"), "No warning message should be '1'");
			}
		}


		[TestMethod]
		public void TestErrorOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("$ErrorActionPreference='Continue'", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Error -Message $_ -TargetObject $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				var err = ps.Streams.Error.ReadAll();
				Assert.IsTrue(err.Any(e => e.Exception.Message == "1"), "Some warning message should be '1'");
			}
		}

		[TestMethod]
		public void TestNoErrorOutputWithoutPreference()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("$ErrorActionPreference='SilentlyContinue'", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Error -message $_ -TargetObject $_"))
					.AddParameter("ThrottleLimit", 1);
				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke<int>(input);
				var err = ps.Streams.Error.ReadAll();
				Assert.IsFalse(err.Any(e => e.Exception.Message == "1"), "No Error message should be '1'");
			}
		}

		[TestMethod]
		public void TestBinaryExpressionVariableCapture()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("[int]$x=10", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("$x -eq 10"))
					.AddParameter("ThrottleLimit", 1)
					.AddParameter("InputObject", 1);

				var result = ps.Invoke<bool>().First();
				Assert.IsTrue(result);
			}
		}

		[TestMethod]
		public void TestAssingmentExpressionVariableCapture()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript("[int]$x=10;", false).Invoke();
				ps.Commands.Clear();
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("$y = $x * 5; $y"))
					.AddParameter("ThrottleLimit", 1)
					.AddParameter("InputObject", 1);

				var result = ps.Invoke<int>().First();
				Assert.AreEqual(50, result);
			}
		}

		[TestMethod]
		public void TestProgressOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;

				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock",
						ScriptBlock.Create("Write-Progress -activity 'Test' -Status 'Status' -currentoperation $_"))
					.AddParameter("ThrottleLimit", 1);

				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke(input);
				var progress = ps.Streams.Progress.ReadAll();
				Assert.AreEqual(13, progress.Count(pr => pr.Activity == "Invoke-Parallel" || pr.Activity == "Test"));
			}
		}


		[TestMethod]
		public void TestNoProgressOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;

				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock",
						ScriptBlock.Create("Write-Progress -activity 'Test' -Status 'Status' -currentoperation $_"))
					.AddParameter("ThrottleLimit", 1)
					.AddParameter("NoProgress");

				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				ps.Invoke(input);
				var progress = ps.Streams.Progress.ReadAll();
				Assert.IsFalse(progress.Any(pr => pr.Activity == "Invoke-Parallel"));
				Assert.AreEqual(5, progress.Count(pr => pr.Activity == "Test"));
			}
		}


		[TestMethod]
		public void TestFunctionCaptureOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript(@"
function foo($x) {return $x * 2}
", false);
				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("foo $_"))
					.AddParameter("ThrottleLimit", 1)
					.AddParameter("NoProgress");

				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				var output = ps.Invoke<int>(input);
				var sum = output.Aggregate(0, (a, b) => a + b);
				Assert.AreEqual(30, sum);
			}
		}



		[TestMethod]
		public void TestRecursiveFunctionCaptureOutput()
		{
			using (var ps = PowerShell.Create())
			{				
				ps.RunspacePool = m_runspacePool;
				ps.AddScript(@"
function foo($x) {return 2 * $x}
function bar($x) {return 3 * (foo $x)}
", false);

				ps.AddStatement()
					.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("bar $_"))
					.AddParameter("ThrottleLimit", 1)
					.AddParameter("NoProgress");

				var input = new PSDataCollection<int> {1, 2, 3, 4, 5};
				input.Complete();
				var output = ps.Invoke<int>(input);
				var sum = output.Aggregate(0, (a, b) => a + b);
				Assert.AreEqual(90, sum);
			}
		}

	
		public void Dispose()
		{
			m_runspacePool.Dispose();
		}
	}
}
