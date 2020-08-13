using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten
{
    /// <summary>
    /// Interface defining the retry policy for handling NpgqlException with transient failures
    /// </summary>
    public interface IRetryPolicy
    {
        /// <summary>
        /// Execute operation with the relevant retry policy
        /// </summary>
        /// <param name="operation"></param>
        void Execute(Action operation);

        /// <summary>
        /// Execute operation with the relevant retry policy and return result
        /// </summary>
        /// <param name="operation"></param>
        /// <typeparam name="TResult"></typeparam>
        /// <returns></returns>
        TResult Execute<TResult>(Func<TResult> operation);

        /// <summary>
        /// Async execute operation with the relevant retry policy
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken);

        /// <summary>
        /// Async Execute operation with the relevant retry policy and return result
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="cancellationToken"></param>
        /// <typeparam name="TResult"></typeparam>
        /// <returns></returns>
        Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Default implementation of IRetryPolicy
    /// </summary>
    public class NulloRetryPolicy: IRetryPolicy
    {
        public void Execute(Action operation)
        {
            operation();
        }

        public TResult Execute<TResult>(Func<TResult> operation)
        {
            return operation();
        }

        public Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            return operation();
        }

        public Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken)
        {
            return operation();
        }
    }

    /// <summary>
    /// Default retry policy, which accounts for <see cref="NpgsqlException.IsTransient"/>.
    /// </summary>
    /// <remarks>
    /// Based on example https://martendb.io/documentation/documents/advanced/retrypolicy/ by Joona-Pekka Kokko.
    /// </remarks>
    public sealed class DefaultRetryPolicy: IRetryPolicy
    {
        private readonly int _maxTries;
        private readonly Func<Exception, bool> _filter;

        private static readonly Func<Exception, bool> DefaultFilter = x => x is NpgsqlException npgsqlException && npgsqlException.IsTransient;

        private DefaultRetryPolicy(int maxTries, Func<Exception, bool> filter)
        {
            _maxTries = maxTries;
            _filter = filter;
        }

        /// <summary>
        /// Initializes a retry policy that will retry once after failure.
        /// </summary>
        /// <param name="filter">Optional filter, default to checking for <see cref="NpgsqlException.IsTransient"/></param>
        /// <returns>The configured retry policy.</returns>
        public static IRetryPolicy Once(Func<Exception, bool> filter)
        {
            return new DefaultRetryPolicy(2, filter ?? DefaultFilter);
        }

        /// <summary>
        /// Initializes a retry policy that will retry twice after failure.
        /// </summary>
        /// <param name="filter">Optional filter, default to checking for <see cref="NpgsqlException.IsTransient"/></param>
        /// <returns>The configured retry policy.</returns>
        public static IRetryPolicy Twice(Func<Exception, bool> filter = null)
        {
            return new DefaultRetryPolicy(3, filter ?? DefaultFilter);
        }

        /// <summary>
        /// Initializes a retry policy that will retry given amount of times after failure.
        /// </summary>
        /// <param name="filter">Optional filter, default to checking for <see cref="NpgsqlException.IsTransient"/></param>
        /// <returns>The configured retry policy.</returns>
        public static IRetryPolicy Times(int times, Func<Exception, bool> filter = null)
        {
            return new DefaultRetryPolicy(times + 1, filter ?? DefaultFilter);
        }

        void IRetryPolicy.Execute(Action operation)
        {
            Try(() =>
            {
                operation();
                return Task.CompletedTask;
            }, CancellationToken.None).GetAwaiter().GetResult();
        }

        TResult IRetryPolicy.Execute<TResult>(Func<TResult> operation)
        {
            return Try(() => Task.FromResult(operation()), CancellationToken.None).GetAwaiter().GetResult();
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
                catch (Exception e) when (++tries < _maxTries && _filter(e))
                {
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
                catch (Exception e) when (++tries < _maxTries && _filter(e))
                {
                }
            }
        }
    }
}
