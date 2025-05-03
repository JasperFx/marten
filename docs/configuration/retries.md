# Resiliency Policies

::: info
Marten's previous, homegrown `IRetryPolicy` mechanism was completely replaced by [Polly](https://www.nuget.org/packages/polly) in Marten V7.
:::

Out of the box, Marten is using Polly for resiliency on most operations with this setup:

<!-- snippet: sample_default_Polly_setup -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Util/ResilientPipelineBuilderExtensions.cs#L14-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_polly_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The general idea is to have _some_ level of retry with an exponential backoff on typical transient errors encountered
in database usage (network hiccups, a database being too busy, etc.).

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
