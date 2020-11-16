using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services
{
    public class DefaultRetryPolicyTests
    {
        [Fact]
        public async Task transient_exception_is_retried()
        {
            var retryPolicy = DefaultRetryPolicy.Times(1, sleep: x => TimeSpan.FromMilliseconds(20));
            var retryPolicyDecorator = RetryPolicyDecorator.For(retryPolicy, runNumber =>
            {
                if (runNumber == 1)
                {
                    throw createNpgsqlException(true);
                }
            });
            using var connection = new ManagedConnection(new ConnectionSource(), retryPolicyDecorator);
            var cmd = new NpgsqlCommand("select 1");

            await connection.ExecuteAsync(cmd);

            retryPolicyDecorator.ExecutionCount.ShouldBe(2);
            cmd.Dispose();
            connection.Dispose();
        }

        [Fact]
        public void transient_exception_is_retried_but_throws_eventually()
        {
            var retryPolicy = DefaultRetryPolicy.Twice(sleep: x => TimeSpan.FromMilliseconds(20));
            var retryPolicyDecorator = RetryPolicyDecorator.For(retryPolicy, runNumber =>
                throw createNpgsqlException(true));

            using var connection = new ManagedConnection(new ConnectionSource(), retryPolicyDecorator);
            var cmd = new NpgsqlCommand();

            Exception<NpgsqlException>
                .ShouldBeThrownBy(() => connection.Execute(cmd));

            retryPolicyDecorator.ExecutionCount.ShouldBe(3);

            cmd.Dispose();
            connection.Dispose();
        }

        [Fact]
        public void non_transient_exception_is_not_retried()
        {
            var retryPolicy = DefaultRetryPolicy.Times(1, sleep: x => TimeSpan.FromMilliseconds(20));
            var retryPolicyDecorator = RetryPolicyDecorator.For(retryPolicy, runNumber =>
                throw createNpgsqlException(false));

            using var connection = new ManagedConnection(new ConnectionSource(), retryPolicyDecorator);
            var cmd = new NpgsqlCommand();

            Exception<NpgsqlException>
                .ShouldBeThrownBy(() => connection.Execute(cmd));

            retryPolicyDecorator.ExecutionCount.ShouldBe(1);
            cmd.Dispose();
            connection.Dispose();
        }

        private static NpgsqlException createNpgsqlException(bool transient)
        {
            var innerException = transient ? (Exception)new TimeoutException() : new DivideByZeroException();
            var ex = new NpgsqlException("exception occurred", innerException);
            return ex;
        }
    }

    internal class RetryPolicyDecorator: IRetryPolicy
    {
        private readonly IRetryPolicy defaultRetryPolicy;
        public int ExecutionCount { get; private set; } = 0;
        private readonly Action<int> doBefore;

        public static RetryPolicyDecorator For(IRetryPolicy retryPolicy, Action<int> doBefore)
            => new RetryPolicyDecorator(retryPolicy, doBefore);

        private RetryPolicyDecorator(IRetryPolicy defaultRetryPolicy, Action<int> doBefore)
        {
            this.defaultRetryPolicy = defaultRetryPolicy;
            this.doBefore = doBefore;
        }

        public void Execute(Action operation)
        {
            ExecutionCount = 0;

            defaultRetryPolicy.Execute(() =>
            {
                doBefore(++ExecutionCount);
                operation();
            });
        }

        public TResult Execute<TResult>(Func<TResult> operation)
        {
            ExecutionCount = 0;

            return defaultRetryPolicy.Execute(() =>
            {
                doBefore(++ExecutionCount);
                return operation();
            });
        }

        public Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken)
        {
            ExecutionCount = 0;

            return defaultRetryPolicy.ExecuteAsync(() =>
            {
                doBefore(++ExecutionCount);
                return operation();
            }, cancellationToken);
        }

        public Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken)
        {
            ExecutionCount = 0;

            return defaultRetryPolicy.ExecuteAsync(() =>
            {
                doBefore(++ExecutionCount);
                return operation();
            }, cancellationToken);
        }
    }
}
