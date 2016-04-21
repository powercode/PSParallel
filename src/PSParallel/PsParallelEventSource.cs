using Microsoft.Diagnostics.Tracing;

namespace PSParallel
{
	[EventSource(Name = "PowerCode-PSParallel-EventLog")]
	public sealed class PsParallelEventSource : EventSource
	{
		public static PsParallelEventSource Log = new PsParallelEventSource();

		[Event(1, Message = "UpdateProgress - Poolmember {0} -> {1}% : pool:{2}%", Channel = EventChannel.Debug)]
		public void UpdateProgress(int poolMember, int progress, string poolProgress)
		{
			WriteEvent(1, poolMember, progress, poolProgress);
		}
		[Event(2, Message = "Seconds remaining - count: {0}/{1} : {2}s remaining, {3} ticks", Channel = EventChannel.Debug)]
		public void SecondsRemaining(int count, int totalCount, int secondsRemaining, long elapsedTicks)
		{
			WriteEvent(2, count, totalCount, secondsRemaining, elapsedTicks);
		}

		[Event(3, Message = "UpdateCurrentProgressRecord Count: {0}", Channel = EventChannel.Debug)]
		public void UpdateCurrentProgressRecord(int count)
		{
			WriteEvent(3, count);
		}
		
		[Event(4, Message = "ProcessComplete Count: {0}, TotalCount={1}", Channel = EventChannel.Debug)]
		public void GetProcessComplete(int count, int totalCount)
		{
			WriteEvent(4, count, totalCount);
		}
		
		[Event(5, Message = "Paritally processed Count: {0} TotalPercentComplete={1}", Channel = EventChannel.Debug)]
		public void PartiallyProcessed(int processedCount, int totalPercentComplete)
		{
			WriteEvent(5, processedCount, totalPercentComplete);
		}

		[Event(6, Message = "Worker {0}: {1}% {2}", Channel = EventChannel.Debug)]
		public void OnProgress(int index, int percentComplete, string currentOperation)
		{
			WriteEvent(6, index, percentComplete, currentOperation);
		}

		[Event(14, Message = "BeginGetSessionState", Task=Tasks.SessionState,  Opcode  = EventOpcode.Start, Channel = EventChannel.Debug)]
		public void BeginGetSessionState()
		{
			WriteEvent(14);
		}
		[Event(15, Message = "EndGetSessionState", Task = Tasks.SessionState, Opcode = EventOpcode.Stop, Channel = EventChannel.Debug)]
		public void EndGetSessionState()
		{
			WriteEvent(15);
		}
	}

	public class Tasks
	{
		public const EventTask SessionState = (EventTask)1;
		public const EventTask CreateRunspace = (EventTask)2;
	}
}