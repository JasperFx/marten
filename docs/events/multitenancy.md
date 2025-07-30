# Event Store Multi-Tenancy

The event store feature in Marten supports an opt-in multi-tenancy model that captures
events by the current tenant. Use this syntax to specify that:

<!-- snippet: sample_making_the_events_multi_tenanted -->
<a id='snippet-sample_making_the_events_multi_tenanted'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // And that's all it takes, the events are now multi-tenanted
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ConfiguringDocumentStore.cs#L235-L245' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_making_the_events_multi_tenanted' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Global Streams & Projections Within Multi-Tenancy <Badge type="tip" text="8.5" />

Document storage allows you to mix conjoined- and single-tenanted documents in one database. You can now do the same
thing with event storage and projected aggregate documents from `SingleStreamProjection<TDoc, TId>` projections.

Let's say that you have a document (cut us some slack, this came from testing) called `SpecialCounter` that is aggregated from events
in your system that otherwise has a conjoined tenancy model for the event store, but `SpecialCounter` should
be global within your system. 

Let's start with a possible implementation of a single stream projection:

snippet: sample_SpecialCounterProjection

Or this equivalent, but see how I'm explicitly registering event types, because that's going to be important:

snippet: sample_SpecialCounterProjection2

And finally, let's register our projection within our application's bootstrapping:

snippet: sample_bootstrapping_with_global_projection

The impact of this global registration is that any events appended to a stream with an aggregate type of `SpecialCounter`
or really any events at all of the types known to be included in the globally registered single stream projection will
be appended as the default tenant id _no matter what the session's tenant id is_. There's a couple implications here:

1. The event types of a globally applied projection should not be used against other types of streams
2. Marten "corrects" the tenant id applied to events from globally projected aggregates regardless of how the events are appended or how the session was created
3. Marten automatically marks the storage for the aggregate type as single tenanted
4. Live, Async, or Inline projections have all been tested with this functionality
5. `AppendOptimistic()` and `AppendPessimistic()` do not work (yet) with this setting, but you should probably
   be using `FetchForWriting()` instead anyway. 
