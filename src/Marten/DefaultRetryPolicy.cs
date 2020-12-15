using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten
{
    /// <summary>
    /// Default retry policy, which accounts for <see cref="NpgsqlException.IsTransient"/>.
    /// </summary>
    /// <remarks>
    /// Based on example https://martendb.io/documentation/documents/advanced/retrypolicy/ by Joona-Pekka Kokko.
    /// </remarks>
    public sealed class DefaultRetryPolicy: IRetryPolicy
    {
        private readonly int _maxRetryCount;
        private readonly Func<Exception, bool> _filter;
        private readonly Func<int, TimeSpan> _sleep;

        private static readonly Func<Exception, bool> DefaultFilter = x => x is NpgsqlException npgsqlException && npgsqlException.IsTransient;
        private static readonly Func<int, TimeSpan> DefaultSleep = x => TimeSpan.FromSeconds(x);

        private DefaultRetryPolicy(int maxRetryCount, Func<Exception, bool> filter, Func<int, TimeSpan> sleep)
        {
            _maxRetryCount = maxRetryCount;
            _filter = filter ?? DefaultFilter;
            _sleep = sleep ?? DefaultSleep;
        }

        /// <summary>
        /// Initializes a retry policy that will retry once after failure that matches the filter.
        /// </summary>
        /// <param name="filter">Optional filter when to apply, default to checking for <see cref="NpgsqlException.IsTransient"/></param>
        /// <param name="sleep">Optional sleep after exception, gets retry number 1-N as parameter, defaults to sleeping retry number seconds</param>
        /// <returns>The configured retry policy.</returns>
        public static IRetryPolicy Once(Func<Exception, bool> filter = null, Func<int, TimeSpan> sleep = null)
        {
            return new DefaultRetryPolicy(1, filter, sleep);
        }

        /// <summary>
        /// Initializes a retry policy that will retry twice after failure that matches the filter.
        /// </summary>
        /// <param name="filter">Optional filter when to apply, default to checking for <see cref="NpgsqlException.IsTransient"/></param>
        /// <param name="sleep">Optional sleep after exception, gets retry number 1-N as parameter, defaults to sleeping retry number seconds</param>
        /// <returns>The configured retry policy.</returns>
        public static IRetryPolicy Twice(Func<Exception, bool> filter = null, Func<int, TimeSpan> sleep = null)
        {
            return new DefaultRetryPolicy(2, filter, sleep);
        }

        /// <summary>
        /// Initializes a retry policy that will retry given amount of times after failure.
        /// </summary>
        /// <param name="maxRetryCount">How many times the operation will be retried on failure that matches the filter.</param>
        /// <param name="filter">Optional filter when to apply, default to checking for <see cref="NpgsqlException.IsTransient"/></param>
        /// <param name="sleep">Optional sleep after exception, gets retry number 1-N as parameter, defaults to sleeping retry number seconds</param>
        /// <returns>The configured retry policy.</returns>
        public static IRetryPolicy Times(int maxRetryCount, Func<Exception, bool> filter = null, Func<int, TimeSpan> sleep = null)
        {
            return new DefaultRetryPolicy(maxRetryCount, filter, sleep);
        }

        void IRetryPolicy.Execute(Action operation)
        {
            using (Util.NoSynchronizationContextScope.Enter())
            {
                Try(() =>
                {
                    operation();
                    return Task.CompletedTask;
                }, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        TResult IRetryPolicy.Execute<TResult>(Func<TResult> operation)
        {
            using (Util.NoSynchronizationContextScope.Enter())
            {
                return Try(() => Task.FromResult(operation()), CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        Task IRetryPolicy.ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            return Try(operation, cancellationToken);
        }

        Task<TResult> IRetryPolicy.ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken)
        {
            return Try(operation, cancellationToken);
        }

        private async Task Try(Func<Task> operation, CancellationToken token)
        {
            for (var tries = 0;; token.ThrowIfCancellationRequested())
            {
                try
                {
                    await operation().ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (tries++ < _maxRetryCount && _filter(e))
                {
                    await Task.Delay(_sleep(tries), token).ConfigureAwait(false);
                }
            }
        }

        private async Task<T> Try<T>(Func<Task<T>> operation, CancellationToken token)
        {
            for (var tries = 0;; token.ThrowIfCancellationRequested())
            {
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception e) when (tries++ < _maxRetryCount && _filter(e))
                {
                    await Task.Delay(_sleep(tries), token).ConfigureAwait(false);
                }
            }
        }
    }
}
