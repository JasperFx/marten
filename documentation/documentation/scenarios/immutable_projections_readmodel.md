<!--Title: Immutable projections as read model-->

This use case demonstrates how to create immutable projections from event streams.

## Scenario

To make projections immutable, the event application methods invoked by aggregators need to be made private, as well as any property setters.

<[sample:scenarios-immutableprojections-projection]>

To run aggregators against such projections, aggregator lookup strategy is configured to use aggregators that look for private `Apply([Event Type])` methods. Furthermore, document deserialization is configured to look for private property setters, allowing hydration of the projected objects from the database.

This can be done in the store configuration as follows:

<[sample:scenarios-immutableprojections-storesetup]>

The serializer contract applied customises the default behaviour of the Json.NET serializer:

<[sample:scenarios-immutableprojections-serializer]>
 
Given the setup, a stream can now be projected using `AggregateWithPrivateEventApply` shown above. Furthermore, the created projection can be hydrated from the document store:

<[sample:scenarios-immutableprojections-projectstream]>