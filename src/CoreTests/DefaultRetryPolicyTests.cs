using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class DefaultRetryPolicyTests
{


    [Fact]
    public async Task transient_exception_is_retried()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var retryPolicy = DefaultRetryPolicy.Times(1, sleep: x => TimeSpan.FromMilliseconds(20));
        var retryPolicyDecorator = RetryPolicyDecorator.For(retryPolicy, runNumber =>
        {
            if (runNumber == 1)
            {
                throw createNpgsqlException(true);
            }
        });

        await retryPolicyDecorator.ExecuteAsync(() => conn.CreateCommand("select 1").ExecuteNonQueryAsync(), default);

        retryPolicyDecorator.ExecutionCount.ShouldBe(2);
    }

    [Fact]
    public void transient_exception_is_retried_but_throws_eventually()
    {
        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        conn.Open();

        var retryPolicy = DefaultRetryPolicy.Twice(sleep: x => TimeSpan.FromMilliseconds(20));
        var retryPolicyDecorator = RetryPolicyDecorator.For(retryPolicy, runNumber =>
            throw createNpgsqlException(true));

        Should.Throw<NpgsqlException>(() => retryPolicyDecorator.Execute(() => conn.CreateCommand("").ExecuteNonQuery()));

        retryPolicyDecorator.ExecutionCount.ShouldBe(3);
    }

    [Fact]
    public void non_transient_exception_is_not_retried()
    {
        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        conn.Open();

        var retryPolicy = DefaultRetryPolicy.Times(1, sleep: x => TimeSpan.FromMilliseconds(20));
        var retryPolicyDecorator = RetryPolicyDecorator.For(retryPolicy, runNumber =>
            throw createNpgsqlException(false));

        Should.Throw<NpgsqlException>(() => retryPolicyDecorator.Execute(() => conn.CreateCommand().ExecuteNonQuery()));

        retryPolicyDecorator.ExecutionCount.ShouldBe(1);

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