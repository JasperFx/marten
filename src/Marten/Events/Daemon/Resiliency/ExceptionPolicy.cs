using System;
using System.Collections.Generic;
using System.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation.PredefinedTransformations;

namespace Marten.Events.Daemon.Resiliency
{
    public class ExceptionPolicy : IHandlerDefinition, IThenExpression
    {
        private readonly DaemonSettings _parent;
        private readonly List<Func<Exception, bool>> _filters = new List<Func<Exception, bool>>();

        internal ExceptionPolicy(DaemonSettings parent, Func<Exception, bool> filter)
        {
            _parent = parent;
            _filters.Add(filter);
        }

        internal List<IContinuation> Continuations { get; } = new List<IContinuation>();

        /// <summary>
        /// Specifies an additional type of exception that this policy can handle with additional filters on this exception type.
        /// </summary>
        /// <param name="exceptionPredicate">The exception predicate to filter the type of exception this policy can handle.</param>
        /// <returns>The PolicyBuilder instance.</returns>
        public ExceptionPolicy AndInner(Func<Exception, bool> exceptionPredicate)
        {
            _filters.Add(ex => ex.InnerException != null && exceptionPredicate(ex.InnerException));
            return this;
        }


        /// <summary>
        /// Specifies an additional type of exception that this policy can handle with additional filters on this exception type.
        /// </summary>
        /// <typeparam name="TException">The type of the exception.</typeparam>
        /// <param name="exceptionPredicate">The exception predicate to filter the type of exception this policy can handle.</param>
        /// <returns>The PolicyBuilder instance.</returns>
        public ExceptionPolicy AndInner<TException>(Func<TException, bool> exceptionPredicate)
            where TException : Exception
        {
            _filters.Add(ex => (ex.InnerException is TException e) && exceptionPredicate(e));
            return this;
        }

        /// <summary>
        ///     Specifies an additional type of exception that this policy can handle if found as an InnerException of a regular
        ///     <see cref="Exception" />, or at any level of nesting within an <see cref="AggregateException" />.
        /// </summary>
        /// <typeparam name="TException">The type of the exception to handle.</typeparam>
        /// <returns>The PolicyBuilder instance, for fluent chaining.</returns>
        public ExceptionPolicy AndInner<TException>() where TException : Exception
        {
            _filters.Add(ex => ex.InnerException is TException);
            return this;
        }

        internal bool Matches(Exception ex)
        {
            return _filters.All(x => x(ex));
        }

        public void Pause(TimeSpan timeSpan)
        {
            Continuations.Add(new PauseProjection(timeSpan));
        }

        public void PauseAll(TimeSpan timeSpan)
        {
            Continuations.Add(new PauseAllProjections(timeSpan));
        }

        public void Stop()
        {
            Continuations.Add(new StopProjection());
        }

        public void StopAll()
        {
            Continuations.Add(new StopAllProjections());
        }

        public IThenExpression RetryLater(params TimeSpan[] timeSpans)
        {
            Continuations.AddRange(timeSpans.Select(x => new RetryLater(x)));
            return this;
        }

        ICoreHandlerDefinition IThenExpression.Then => this;

        public void DoNothing()
        {
            Continuations.Add(new DoNothing());
        }
    }

    public interface ICoreHandlerDefinition
    {
        void Pause(TimeSpan timeSpan);
        void PauseAll(TimeSpan timeSpan);
        void Stop();
        void StopAll();

        void DoNothing();
    }

    public interface IThenExpression
    {
        ICoreHandlerDefinition Then { get; }

    }

    public interface IHandlerDefinition : ICoreHandlerDefinition
    {
        IThenExpression RetryLater(params TimeSpan[] timeSpans);
    }
}
