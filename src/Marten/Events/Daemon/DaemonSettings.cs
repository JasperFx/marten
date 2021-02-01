using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Baseline.Dates;
using Marten.Events.Daemon.Resiliency;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Events.Daemon
{
    public class DaemonSettings
    {
        public DaemonSettings()
        {
            OnException<EventFetcherException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
                .Then.Pause(30.Seconds());

            OnException<ShardStopException>().DoNothing();

            OnException<ShardStartException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
                .Then.DoNothing();

            OnException<NpgsqlException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
                .Then.Pause(30.Seconds());

            OnException<MartenCommandException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
                .Then.Pause(30.Seconds());

            BaselinePolicies.AddRange(Policies);
            Policies.Clear();
        }


        public TimeSpan StaleSequenceThreshold { get; set; } = 3.Seconds();

        public TimeSpan SlowPollingTime { get; set; } = 1.Seconds();
        public TimeSpan FastPollingTime { get; set; } = 250.Milliseconds();


        public TimeSpan HealthCheckPollingTime { get; set; } = 5.Seconds();

        public DaemonMode Mode { get; set; } = DaemonMode.Disabled;

        internal IList<ExceptionPolicy> Policies { get; } = new List<ExceptionPolicy>();

        internal IList<ExceptionPolicy> BaselinePolicies { get; } = new List<ExceptionPolicy>();

        /// <summary>
        ///     Specifies the type of exception that this policy can handle.
        /// </summary>
        /// <typeparam name="TException">The type of the exception to handle.</typeparam>
        /// <returns>The PolicyBuilder instance.</returns>
        public ExceptionPolicy OnException<TException>() where TException : Exception
        {
            return OnException(e => e is TException);
        }

        /// <summary>
        ///     Specifies the type of exception that this policy can handle with additional filters on this exception type.
        /// </summary>
        /// <typeparam name="TException">The type of the exception.</typeparam>
        /// <param name="policies"></param>
        /// <param name="exceptionPredicate">The exception predicate to filter the type of exception this policy can handle.</param>
        /// <returns>The PolicyBuilder instance.</returns>
        public ExceptionPolicy OnException(Func<Exception, bool> exceptionPredicate)
        {
            var handler = new ExceptionPolicy(this, exceptionPredicate);
            Policies.Add(handler);

            return handler;
        }

        /// <summary>
        ///     Specifies the type of exception that this policy can handle with additional filters on this exception type.
        /// </summary>
        /// <param name="policies"></param>
        /// <param name="exceptionType">An exception type to match against</param>
        /// <returns>The PolicyBuilder instance.</returns>
        public ExceptionPolicy OnExceptionOfType(Type exceptionType)
        {
            return OnException(ex => ex.GetType().CanBeCastTo(exceptionType));
        }


        /// <summary>
        ///     Specifies the type of exception that this policy can handle with additional filters on this exception type.
        /// </summary>
        /// <typeparam name="TException">The type of the exception.</typeparam>
        /// <param name="policies"></param>
        /// <param name="exceptionPredicate">The exception predicate to filter the type of exception this policy can handle.</param>
        /// <returns>The PolicyBuilder instance.</returns>
        public ExceptionPolicy OnException<TException>(Func<TException, bool> exceptionPredicate)
            where TException : Exception
        {
            return OnException(ex =>
            {
                if (ex is TException e)
                {
                    return exceptionPredicate(e);
                }

                return false;
            });
        }

        internal IContinuation DetermineContinuation(Exception exception, int attempts)
        {
            var handler = Policies.Concat(BaselinePolicies).FirstOrDefault(x => x.Matches(exception));
            if (handler == null) return new StopProjection();
            // attempts are zero based in this case
            return handler.Continuations.Count > attempts
                ? handler.Continuations[attempts]
                : new StopProjection();
        }
    }
}
