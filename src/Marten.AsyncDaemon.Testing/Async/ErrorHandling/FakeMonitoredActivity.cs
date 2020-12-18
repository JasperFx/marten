using System;
using System.Threading.Tasks;
using Marten.Events.Projections.Async.ErrorHandling;

namespace Marten.Testing.Events.Projections.Async.ErrorHandling
{
    public class FakeMonitoredActivity: IMonitoredActivity
    {
        public Exception StartError = null;
        public Exception StopError = null;

        public bool WasStopped = false;
        public bool WasStarted = false;

        public Task Stop()
        {
            WasStopped = true;

            if (StopError != null)
                throw StopError;

            return Task.CompletedTask;
        }

        public Task Start()
        {
            WasStarted = true;

            if (StartError != null)
                throw StartError;

            return Task.CompletedTask;
        }

        public int Attempted = 0;

        public int SuccessfulAttempt = 0;

        public Exception[] AttemptExceptions = new Exception[0];

        public Task Action()
        {
            Attempted++;
            if (AttemptExceptions.Length >= Attempted)
            {
                throw AttemptExceptions[Attempted - 1];
            }

            return Task.CompletedTask;
        }
    }
}
