using System;
using System.Collections.Generic;
using System.Linq;
using Baseline.Dates;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public class ExceptionHandling
    {
        private readonly IList<ExceptionHandler> _handlers = new List<ExceptionHandler>();

        public IExceptionAction DetermineAction(Exception ex, IMonitoredActivity activity)
        {
            return _handlers.FirstOrDefault(x => x.Match(ex))?.Action ?? DefaultAction;
        }

        public IExceptionAction DefaultAction { get; set; } = new Pause();

        public ExceptionExpression OnException<T>(Func<T, bool> filter = null) where T : Exception
        {
            Func<Exception, bool> condition = ex => ex is T;
            if (filter != null)
            {
                condition = ex =>
                {
                    return ex is T arg && filter(arg);
                };
            }

            return new ExceptionExpression(this, condition);
        }

        public ExceptionExpression OnException(Func<Exception, bool> condition)
        {
            return new ExceptionExpression(this, condition);
        }

        public TimeSpan Cooldown { get; set; } = 5.Seconds();

        public class ExceptionExpression
        {
            private readonly ExceptionHandling _parent;
            private readonly Func<Exception, bool> _condition;

            public ExceptionExpression(ExceptionHandling parent, Func<Exception, bool> condition)
            {
                _parent = parent;
                _condition = condition;
            }

            public void Pause()
            {
                Do(new Pause());
            }

            public void Stop(Action<Exception> logger)
            {
                Do(new Stop(logger));
            }

            public void StopAll(Action<Exception> logger)
            {
                Do(new StopAll(logger));
            }

            public Retry Retry(int maxAttempts, TimeSpan cooldown = default(TimeSpan))
            {
                var retry = new Retry(maxAttempts, cooldown);

                Do(retry);

                return retry;
            }

            public void Do(IExceptionAction action)
            {
                _parent._handlers.Add(new ExceptionHandler(_condition, action));
            }
        }
    }
}
