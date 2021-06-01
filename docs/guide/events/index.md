# Marten as Event Store

Marten's Event Store functionality is a powerful way to utilize Postgresql in the [event sourcing](http://martinfowler.com/eaaDev/EventSourcing.html) style of persistence in your application. Beyond simple event capture and access to the raw event
stream data, Marten also helps you create "read side" views of the raw event data through its support for projections.

## Event Store quick start

There is not anything special you need to do to enable the event store functionality in Marten, and it obeys the same rules about automatic schema generation described in [schema](guide/schema/]>. Marten is just a client library,
and there's nothing to install other than the Marten NuGet.

Because I’ve read way too much epic fantasy fiction, my sample problem domain is an application that records, analyses, and visualizes the status of quests. During a quest, you may want to record events like:

<!-- snippet: sample_sample-events -->
<!-- endSnippet -->

Now, let's say that we're starting a new "quest" with the first couple of events, then appending a couple more as other quest party members join up:

<!-- snippet: sample_event-store-start-stream-with-explicit-type -->
<!-- endSnippet -->

In addition to generic `StartStream<T>`, `IEventStore` has a non-generic `StartStream` overload that let you pass explicit type.

<!-- snippet: sample_event-store-quickstart -->
<!-- endSnippet -->

It has also overload to create streams without associating them with aggregate type (stored in `mt_streams` table).

<!-- snippet: sample_event-store-start-stream-with-explicit-type -->
<!-- endSnippet -->
