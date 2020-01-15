using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public class Retry: IExceptionAction
    {
        public int Attempts { get; }
        public TimeSpan Cooldown { get; }

        public Retry(int attempts, TimeSpan cooldown = default(TimeSpan))
        {
            Attempts = attempts;
            Cooldown = cooldown;
        }

        public async Task<ExceptionAction> Handle(Exception ex, int attempts, IMonitoredActivity activity)
        {
            if (attempts < Attempts)
            {
                await Task.Delay(Cooldown).ConfigureAwait(false);

                return ExceptionAction.Retry;
            }

            return await AfterMaxAttempts.Handle(ex, attempts, activity).ConfigureAwait(false);
        }

        public IExceptionAction AfterMaxAttempts { get; set; } = new Pause();
    }
}
