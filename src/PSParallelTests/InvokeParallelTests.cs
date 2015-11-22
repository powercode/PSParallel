﻿using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PSParallelTests
{
	[TestClass]
	public class InvokeParallelTests : IDisposable
	{
		readonly RunspacePool m_runspacePool;

		public InvokeParallelTests()
		{
			var path = Path.GetDirectoryName(typeof(InvokeParallelTests).Assembly.Location);
			var iss = InitialSessionState.CreateDefault2();			
			iss.ImportPSModule(new [] { $"{path}\\PSParallel.dll" });
             m_runspacePool = RunspaceFactory.CreateRunspacePool(iss);
			m_runspacePool.SetMaxRunspaces(10);
			m_runspacePool.Open();			
		}
		[TestMethod]
		public void TestOutput()
		{			
			PowerShell ps = PowerShell.Create();
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

		[TestMethod]
		public void TestParallelOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;

			ps.AddCommand("Invoke-Parallel")
				.AddParameter("ScriptBlock", ScriptBlock.Create("$_* 2"))
				.AddParameter("ThrottleLimit", 10);							
			var input = new PSDataCollection<int>(Enumerable.Range(1,1000));
			input.Complete();
			var output = ps.Invoke<int>(input);
			var sum = output.Aggregate(0, (a, b) => a + b);
			Assert.AreEqual(1001000, sum);
		}

		[TestMethod]
		public void TestVerboseOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;					
			ps.AddScript("$VerbosePreference='Continue'", false).Invoke();
			ps.Commands.Clear();			
			ps.AddStatement()
				.AddCommand("Invoke-Parallel",false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Verbose $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);			
			Assert.IsFalse(ps.HadErrors, "We don't expect errors here");			
			var vrb = ps.Streams.Verbose.ReadAll();			
			Assert.IsTrue(vrb.Any(v=> v.Message == "1"), "Some verbose message should be '1'");
		}

		[TestMethod]
		public void TestNoVerboseOutputWithoutPreference()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;			
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Verbose $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);
			Assert.IsFalse(ps.HadErrors, "We don't expect errors here");
			var vrb = ps.Streams.Verbose.ReadAll();
			Assert.IsFalse(vrb.Any(v => v.Message == "1"), "No verbose message should be '1'");
		}

		[TestMethod]
		public void TestDebugOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;			
			ps.AddScript("$DebugPreference='Continue'", false).Invoke();
			ps.Commands.Clear();
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Debug $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);
			Assert.IsFalse(ps.HadErrors, "We don't expect errors here");
			var dbg = ps.Streams.Debug.ReadAll();
			Assert.IsTrue(dbg.Any(d => d.Message == "1"), "Some debug message should be '1'");
		}

		[TestMethod]
		public void TestNoDebugOutputWithoutPreference()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;			
			ps.Commands.Clear();
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Debug $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);			
			var dbg = ps.Streams.Debug.ReadAll();
			Assert.IsFalse(dbg.Any(d => d.Message == "1"), "No debug message should be '1'");
		}

		[TestMethod]
		public void TestWarningOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;
			ps.AddScript("$WarningPreference='Continue'", false).Invoke();
			ps.Commands.Clear();
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Warning $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);			
			var wrn = ps.Streams.Warning.ReadAll();
			Assert.IsTrue(wrn.Any(w => w.Message == "1"), "Some warning message should be '1'");
		}

		[TestMethod]
		public void TestNoWarningOutputWithoutPreference()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;
			ps.AddScript("$WarningPreference='SilentlyContinue'", false).Invoke();
			ps.Commands.Clear();
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Warning $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);			
			var wrn = ps.Streams.Warning.ReadAll();
			Assert.IsFalse(wrn.Any(w => w.Message == "1"), "No warning message should be '1'");
		}


		[TestMethod]
		public void TestErrorOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;
			ps.AddScript("$ErrorActionPreference='Continue'", false).Invoke();
			ps.Commands.Clear();
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Error -Message $_ -TargetObject $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);
			var err = ps.Streams.Error.ReadAll();
			Assert.IsTrue(err.Any(e => e.Exception.Message == "1"), "Some warning message should be '1'");
		}

		[TestMethod]
		public void TestNoErrorOutputWithoutPreference()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;
			ps.AddScript("$ErrorActionPreference='SilentlyContinue'", false).Invoke();
			ps.Commands.Clear();
			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
					.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Error -message $_ -TargetObject $_"))
					.AddParameter("ThrottleLimit", 1);
			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke<int>(input);
			var err = ps.Streams.Error.ReadAll();
			Assert.IsFalse(err.Any(e => e.Exception.Message == "1"), "No Error message should be '1'");
		}

		[TestMethod]
		public void TestBinaryExpressionVariableCapture()
		{
			PowerShell ps = PowerShell.Create();
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

		[TestMethod]
		public void TestAssingmentExpressionVariableCapture()
		{
			PowerShell ps = PowerShell.Create();
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

		[TestMethod]
		public void TestProgressOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;

			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
				.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Progress -activity 'Test' -Status 'Status' -currentoperation $_"))
				.AddParameter("ThrottleLimit", 1);				

			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke(input);
			var progress = ps.Streams.Progress.ReadAll();			
			Assert.AreEqual(11, progress.Count(pr=>pr.Activity == "Invoke-Parallel" || pr.Activity == "Test"));
		}


		[TestMethod]
		public void TestNoProgressOutput()
		{
			PowerShell ps = PowerShell.Create();
			ps.RunspacePool = m_runspacePool;

			ps.AddStatement()
				.AddCommand("Invoke-Parallel", false)
				.AddParameter("ScriptBlock", ScriptBlock.Create("Write-Progress -activity 'Test' -Status 'Status' -currentoperation $_"))
				.AddParameter("ThrottleLimit", 1)
				.AddParameter("NoProgress");

			var input = new PSDataCollection<int> { 1, 2, 3, 4, 5 };
			input.Complete();
			ps.Invoke(input);
			var progress = ps.Streams.Progress.ReadAll();
			Assert.IsFalse( progress.Any(pr=>pr.Activity == "Invoke-Parallel"));
			Assert.AreEqual(5, progress.Count(pr=>pr.Activity == "Test"));
		}



		public void Dispose()
		{
			m_runspacePool.Dispose();
		}
	}
}