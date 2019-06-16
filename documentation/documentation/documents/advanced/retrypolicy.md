<!--title: Retry Policies -->

Marten can be configured to retry failing database operations by implementing an `IRetryPolicy`. Such policy is plugged into the `StoreOptions` when the `DocumentStore` is configured and bootstrapped.

The sample below demonstrates an `IRetryPolicy` implementation that retries any failing operation preconfigured number of times with an optional predicate on the thrown exception(s).

<[sample:retrypolicy-samplepolicy]>

The policy is then plugged into the `StoreOptions` via the `RetryPolicy` method:

<[sample:retrypolicy-samplepolicy-pluggingin]>

Lastly, the filter is configured to retry failing operations twice, given they throw a `NpgsqlException` that is non-transient (for the sake of demonstrability).