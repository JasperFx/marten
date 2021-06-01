# Retry Policies

Marten can be configured to retry failing database operations by implementing an `IRetryPolicy`. Such policy is plugged into the `StoreOptions` when the `DocumentStore` is configured and bootstrapped.

The sample below demonstrates an `IRetryPolicy` implementation that retries any failing operation preconfigured number of times with an optional predicate on the thrown exception(s).

<!-- snippet: sample_retrypolicy-samplepolicy -->
<!-- endSnippet -->

The policy is then plugged into the `StoreOptions` via the `RetryPolicy` method:

<!-- snippet: sample_retrypolicy-samplepolicy-pluggingin -->
<!-- endSnippet -->

Lastly, the filter is configured to retry failing operations twice, given they throw a `NpgsqlException` that is transient and thus might succeed later.

There's also a built-in `DefaultRetryPolicy` that has sane defaults for transient error handling. Like any custom policy, you can plug it into into the `StoreOptions` via the `RetryPolicy` method:

<!-- snippet: sample_retrypolicy-samplepolicy-default -->
<!-- endSnippet -->

Also you could use the fantastic [Polly](https://www.nuget.org/packages/polly) library to easily build more resilient and expressive retry policies by implementing `IRetryPolicy`.
