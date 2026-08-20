# Resiliency Policies

::: info
Marten's previous, homegrown `IRetryPolicy` mechanism was completely replaced by [Polly](https://www.nuget.org/packages/polly) in Marten V7.
:::

Out of the box, Marten is using [Polly.Core](https://www.pollydocs.org/) for resiliency on most operations with this setup:

<!-- snippet: sample_default_polly_setup -->
<a id='snippet-sample_default_polly_setup'></a>
```cs
// default Marten policies
return builder
   .AddRetry(new()
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>()
            .Handle<MartenCommandException>()
            .Handle<EventLoaderException>(),
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(50),
        BackoffType = DelayBackoffType.Exponential
    });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Util/ResilientPipelineBuilderExtensions.cs#L21-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_polly_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The general idea is to have _some_ level of retry with an exponential backoff on typical transient errors encountered
in database usage (network hiccups, a database being too busy, etc.).

## Committing a unit of work is retried differently

That policy governs reads and other **idempotent** work. Committing a session is not idempotent: one
`SaveChangesAsync()` is a single transaction carrying document writes **and** event appends, and appending the same
events a second time is not something anything downstream can detect or undo.

The catch is that a client cannot always tell whether the previous attempt failed. If PostgreSQL answered with an
error, the transaction is definitively gone and a retry starts clean. But if the failure happened at the I/O layer —
a read timeout, a dropped connection — the `ROLLBACK` never reached the server, and "the transaction rolled back"
and "the transaction committed and the reply was lost" look exactly the same from here. Retrying there can duplicate
every event in the batch. (This is what [#5262](https://github.com/JasperFx/marten/issues/5262) turned out to be.)

So commits run through a second, deliberately narrower pipeline:

<!-- snippet: sample_default_write_polly_setup -->
<a id='snippet-sample_default_write_polly_setup'></a>
```cs
// Marten's policy for committing a unit of work. A commit is NOT idempotent -- replaying it
// appends the same events a second time -- so unlike the read policy, this one retries only
// when the previous attempt is KNOWN to have left nothing behind.
return builder
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = args => new ValueTask<bool>(
            args.Outcome.Exception is { } e && WriteRetryClassifier.IsSafeToRetry(e)),
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(50),
        BackoffType = DelayBackoffType.Exponential
    });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Util/ResilientPipelineBuilderExtensions.cs#L45-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_write_polly_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`SaveChangesAsync()` is retried when, and only when:

- **PostgreSQL reported a transient error itself.** The transaction is known to be gone, and the condition may pass:
  `serialization_failure` (40001), `deadlock_detected` (40P01), `transaction_rollback` (40000), the resource class
  (53000, 53100, 53200, 53300, 53400), the lock-contention class (55000, 55006, 55P03), `cannot_connect_now` (57P03),
  and the server-side system errors 58000 / 58030.
- **Marten's own post-processing threw** while reading results back, with the connection healthy. Marten rolls the
  batch back itself in that case, so the outcome is known.

It is **not** retried when the outcome cannot be established — a command timeout, a connection dropped mid-batch, or
any SQLSTATE that arrives as the connection is being torn down (the whole `08xxx` class, `admin_shutdown`,
`crash_shutdown`, `idle_session_timeout`, `transaction_resolution_unknown`, `statement_completion_unknown`,
`query_canceled`). It is also not retried for deterministic failures such as constraint violations or syntax errors,
where a replay produces the identical error.

::: warning
This is deliberately **not** `NpgsqlException.IsTransient`. Npgsql's definition of transient is built for idempotent
work, so it includes the entire connection-exception class plus `admin_shutdown`, `crash_shutdown`,
`idle_session_timeout` and `transaction_resolution_unknown` — every one of which means the connection died, possibly
with a `COMMIT` in flight. That is the right answer for a `SELECT` and the wrong one for an append.

Note also that connection-pool exhaustion — where nothing was ever sent and a replay would be perfectly safe —
reaches you as an `NpgsqlException` wrapping a `TimeoutException`, which is structurally identical to a read timeout
that stalled halfway through a batch. Since the two cannot be told apart from the exception, both surface to the
caller rather than being guessed at.
:::

When a commit is not retried, the exception reaches your code, where it can be handled at a level that knows how to
rebuild the work — a Wolverine message retry re-runs the handler and constructs a fresh session, which is safe in a
way that replaying the old batch is not.

## Replacing or extending the policies

You can **replace** Marten's Polly configuration through:

<!-- snippet: sample_configure_polly -->
<a id='snippet-sample_configure_polly'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.ConfigurePolly(builder =>
    {
        builder.AddRetry(new()
        {
            ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>().Handle<MartenCommandException>(),
            MaxRetryAttempts = 10, // this is excessive, but just wanted to show something different
            Delay = TimeSpan.FromMilliseconds(50),
            BackoffType = DelayBackoffType.Linear
        });
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ErrorHandling.cs#L12-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_polly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or you can **extend** default marten configuration with your custom policies. Any user supplied policies will take precedence over the default policies.

<!-- snippet: sample_extend_polly -->
<a id='snippet-sample_extend_polly'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.ExtendPolly(builder =>
    {
        // custom policies are configured before marten default policies
        builder.AddRetry(new()
        {
            // retry on your custom exceptions (ApplicationException as an example)
            ShouldHandle = new PredicateBuilder().Handle<ApplicationException>(),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(50),
            BackoffType = DelayBackoffType.Linear
        });
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ErrorHandling.cs#L35-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_extend_polly' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
`ConfigurePolly` and `ExtendPolly` govern the read pipeline only — they deliberately leave the commit path on
Marten's write defaults, so that tuning retries for reads cannot silently take the replay protection off a
non-idempotent write. Use `ConfigureWritePolly` / `ExtendWritePolly` when you mean to change commits as well:

```cs
opts.ExtendWritePolly(builder =>
{
    builder.AddRetry(new()
    {
        ShouldHandle = new PredicateBuilder().Handle<ApplicationException>(),
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(50),
        BackoffType = DelayBackoffType.Linear
    });
});
```

If you replace the write pipeline outright with `ConfigureWritePolly`, you take on responsibility for the
duplicate-append hazard described above.
:::
