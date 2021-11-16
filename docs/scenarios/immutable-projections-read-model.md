# Immutable projections as read model

::: warning TODO
This whole page will need to be revisited due to the revamped event store/projections implementation in v4. Code samples are unavailable as well.
:::

This use case demonstrates how to create immutable projections from event streams.

## Scenario

To make projections immutable, the event application methods invoked by aggregators need to be made private, as well as any property setters.

-- snippet: sample_scenarios-immutableprojections-projection

To run aggregators against such projections, aggregator lookup strategy is configured to use aggregators that look for private `Apply([Event Type])` methods. Furthermore, document deserialization is configured to look for private property setters, allowing hydration of the projected objects from the database.

This can be done in the store configuration as follows:

-- snippet: sample_scenarios-immutableprojections-storesetup

The serializer contract applied customizes the default behavior of the Json.NET serializer:

-- snippet: sample_scenarios-immutableprojections-serializer

Given the setup, a stream can now be projected using `AggregateWithPrivateEventApply` shown above. Furthermore, the created projection can be hydrated from the document store:

-- snippet: sample_scenarios-immutableprojections-projectstream
