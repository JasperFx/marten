using System;

namespace Marten.Events.Projections.Async.ErrorHandling
{
    public class ExceptionHandler
    {
        public ExceptionHandler(Func<Exception, bool> match, IExceptionAction action)
        {
            Match = match;
            Action = action;
        }

        public Func<Exception, bool> Match { get; }
        public IExceptionAction Action { get; }

    }
}