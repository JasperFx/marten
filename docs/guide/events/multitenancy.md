# Event Store Multi-Tenancy

::: tip
The V4 version of the async daemon is able to fully support multi-tenanted event store projections
now.
:::

The event store feature in Marten supports an opt-in multi-tenancy model that captures
events by the current tenant. Use this syntax to specify that:

snippet: sample_making_the_events_multi_tenanted

