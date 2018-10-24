using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten
{
    /// <summary>
    /// Interface defining the retry policy for handling NpgqlException with git statustransient failures
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
    public class NulloRetryPolicy : IRetryPolicy
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
}

