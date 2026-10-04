# Resiliency Policies

::: info
Marten's previous, homegrown `IRetryPolicy` mechanism was completely replaced by [Polly](https://www.nuget.org/packages/polly) in Marten V7.
:::

Out of the box, Marten is using [Polly.Core](https://www.pollydocs.org/) for resiliency on most operations with this setup:

<!-- snippet: sample_default_polly_setup -->
<a id='snippet-sample_default_polly_setup'></a>
```cs
// default Marten policies. A command that timed out is not retried: it has already spent
// the whole CommandTimeout, and every retry would hold the caller for that long again.
return builder
   .AddRetry(new()
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>(e => !IsTimeout(e))
            .Handle<MartenCommandException>(e => !IsTimeout(e))
            .Handle<EventLoaderException>(e => !IsTimeout(e)),
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(50),
        BackoffType = DelayBackoffType.Exponential
    });
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Util/ResilientPipelineBuilderExtensions.cs#L21-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_polly_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The general idea is to have _some_ level of retry with an exponential backoff on typical transient errors encountered
in database usage (network hiccups, a database being too busy, etc.).

A command that timed out — an `NpgsqlException` wrapping a `TimeoutException`, possibly inside a
`MartenCommandException` — is the exception. That attempt has already waited out the whole `CommandTimeout`, plus
Npgsql's cancel request, so retrying it three times would hold the caller for four timeouts instead of one. The
timeout is surfaced to the caller at once, as it already is on the commit path below. Connection-pool exhaustion
reaches you in the same shape, so it is not retried either. See
[Handle a timeout outside Marten](#handle-a-timeout-outside-marten) for what to do with the one you now get on the
first attempt.

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Util/ResilientPipelineBuilderExtensions.cs#L60-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_write_polly_setup' title='Start of snippet'>anchor</a></sup>
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
caller rather than being guessed at. The read policy inherited that ambiguity when it stopped retrying timeouts:
pool exhaustion is no longer retried there either, which is a deliberate trade of a retry that might have cleared
in 50-350 ms against the four-timeouts-instead-of-one behavior that ambiguity was buying.
:::

## Handle a timeout outside Marten

Neither pipeline retries a timeout, so one reaches your code on the first attempt. That is not a gap waiting to be
filled with a more clever retry — **there is no retry Marten can perform that would help.**

The usual suggestion is to cycle a fresh connection before retrying. Marten's default `AutoClosingLifetime`
**already does that**: the retried delegate is the whole lifetime call, which opens a connection, runs the command
and closes it, so every attempt has had its own connection all along. It makes no difference, because a timeout is
never a connection-health problem:

| What happened | The connection afterwards | What a fresh connection does |
| --- | --- | --- |
| `CommandTimeout` expired, server responsive | healthy and open | nothing — the query is slow, and it is equally slow on a new connection |
| `CommandTimeout` expired, server unreachable | broken | also fails, after another full timeout |
| `statement_timeout` cancelled the query | healthy and open | runs the same query against the same clock |
| Connection pool exhausted | n/a | a connection is precisely what cannot be had |

So the retry has to happen somewhere that can do something different. In order of preference:

1. **Fix the timeout budget.** A query that runs past `CommandTimeout` usually wants an index, a narrower filter, or
   a larger `CommandTimeout` — not a second attempt. Marten exposes the per-session value through
   `SessionOptions.Timeout` and the store-wide default through `StoreOptions.CommandTimeout`.
2. **Retry the whole unit of work from a message handler.** A [Wolverine](https://wolverinefx.net) message retry
   re-runs the handler, which constructs a **fresh session, a fresh connection and a fresh timeout budget**, and
   re-derives the work from the incoming message rather than replaying operations computed against a snapshot that
   may no longer hold. That is the one retry shape that can genuinely differ from the first attempt, and on the
   commit path it is also the only safe one — replaying the old batch can append the same events twice.
3. **Retry around your own call site** if you are not using Wolverine: catch the exception outside the `using` block
   for the session, build a new session, and redo the work. The key is that the retry is outside the session, not
   inside it. Retrying with the session still open gets you nothing on a read, and on a sticky session it can get
   you a `25P02` instead of the original error.

::: tip
Do not reach for `ConfigurePolly` to put timeout retries back. It governs reads only, so it cannot restore the
commit behavior anyway, and the table above is why it would not pay on reads either.
:::

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
