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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Util/ResilientPipelineBuilderExtensions.cs#L15-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_polly_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The general idea is to have _some_ level of retry with an exponential backoff on typical transient errors encountered
in database usage (network hiccups, a database being too busy, etc.).

Committing a unit of work is held to a stricter policy. A batch of document and event operations is
**not idempotent** — replaying it appends the same events a second time — so `SaveChangesAsync()` only
retries when the previous transaction is known to be gone. A command timeout or a dropped connection
kills the connector, which means the `ROLLBACK` never reaches PostgreSQL and the server may well have
committed while the response never made it back to the client. Retrying in that situation is what turns
one lost commit into a duplicated one, so Marten does not do it:

<!-- snippet: sample_default_write_polly_setup -->
<!-- endSnippet -->

Errors that PostgreSQL itself reports (a serialization failure, a deadlock, a constraint violation) do
leave the transaction definitively aborted, and are still retried.

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
