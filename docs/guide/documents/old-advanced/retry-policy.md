# Retry Policies

Marten can be configured to retry failing database operations by implementing an `IRetryPolicy`. Such policy is plugged into the `StoreOptions` when the `DocumentStore` is configured and bootstrapped.

The sample below demonstrates an `IRetryPolicy` implementation that retries any failing operation preconfigured number of times with an optional predicate on the thrown exception(s).

<!-- snippet: sample_retrypolicy-samplepolicy -->
<a id='snippet-sample_retrypolicy-samplepolicy'></a>
```cs
// Implement IRetryPolicy interface
public sealed class ExceptionFilteringRetryPolicy: IRetryPolicy
{
    private readonly int maxTries;
    private readonly Func<Exception, bool> filter;

    private ExceptionFilteringRetryPolicy(int maxTries, Func<Exception, bool> filter)
    {
        this.maxTries = maxTries;
        this.filter = filter;
    }

    public static IRetryPolicy Once(Func<Exception, bool> filter = null)
    {
        return new ExceptionFilteringRetryPolicy(2, filter ?? (_ => true));
    }

    public static IRetryPolicy Twice(Func<Exception, bool> filter = null)
    {
        return new ExceptionFilteringRetryPolicy(3, filter ?? (_ => true));
    }

    public static IRetryPolicy NTimes(int times, Func<Exception, bool> filter = null)
    {
        return new ExceptionFilteringRetryPolicy(times + 1, filter ?? (_ => true));
    }

    public void Execute(Action operation)
    {
        Try(() => { operation(); return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
    }

    public TResult Execute<TResult>(Func<TResult> operation)
    {
        return Try(() => Task.FromResult(operation()), CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        return Try(operation, cancellationToken);
    }

    public Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken)
    {
        return Try(operation, cancellationToken);
    }

    private async Task Try(Func<Task> operation, CancellationToken token)
    {
        for (var tries = 0; ; token.ThrowIfCancellationRequested())
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception e) when (++tries < maxTries && filter(e))
            {
            }
        }
    }

    private async Task<T> Try<T>(Func<Task<T>> operation, CancellationToken token)
    {
        for (var tries = 0; ; token.ThrowIfCancellationRequested())
        {
            try
            {
                return await operation();
            }
            catch (Exception e) when (++tries < maxTries && filter(e))
            {
            }
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RetryPolicyTests.cs#L12-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_retrypolicy-samplepolicy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The policy is then plugged into the `StoreOptions` via the `RetryPolicy` method:

<!-- snippet: sample_retrypolicy-samplepolicy-pluggingin -->
<a id='snippet-sample_retrypolicy-samplepolicy-pluggingin'></a>
```cs
// Plug in our custom retry policy via StoreOptions
// We retry operations twice if they yield and NpgsqlException that is transient
c.RetryPolicy(ExceptionFilteringRetryPolicy.Twice(e => e is NpgsqlException ne && ne.IsTransient));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RetryPolicyTests.cs#L100-L104' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_retrypolicy-samplepolicy-pluggingin' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, the filter is configured to retry failing operations twice, given they throw a `NpgsqlException` that is transient and thus might succeed later.

There's also a built-in `DefaultRetryPolicy` that has sane defaults for transient error handling. Like any custom policy, you can plug it into into the `StoreOptions` via the `RetryPolicy` method:

<!-- snippet: sample_retrypolicy-samplepolicy-default -->
<a id='snippet-sample_retrypolicy-samplepolicy-default'></a>
```cs
// Use DefaultRetryPolicy which handles Postgres's transient errors by default with sane defaults
// We retry operations twice if they yield and NpgsqlException that is transient
// Each error will cause sleep of N seconds where N is the current retry number
c.RetryPolicy(DefaultRetryPolicy.Twice());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/RetryPolicyTests.cs#L106-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_retrypolicy-samplepolicy-default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Also you could use the fantastic [Polly](https://www.nuget.org/packages/polly) library to easily build more resilient and expressive retry policies by implementing `IRetryPolicy`.
