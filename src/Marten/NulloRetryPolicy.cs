using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten
{
    /// <summary>
    /// No-op implementation of IRetryPolicy
    /// </summary>
    internal class NulloRetryPolicy: IRetryPolicy
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
