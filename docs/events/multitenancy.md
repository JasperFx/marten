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

<!-- snippet: sample_SpecialCounterProjection -->
<a id='snippet-sample_specialcounterprojection'></a>
```cs
public class SpecialCounterProjection: SingleStreamProjection<SpecialCounter, Guid>
{
    public void Apply(SpecialCounter c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounter c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounter c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounter c, SpecialD _) => c.DCount++;

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs#L395-L406' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_specialcounterprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Or this equivalent, but see how I'm explicitly registering event types, because that's going to be important:

<!-- snippet: sample_SpecialCounterProjection2 -->
<a id='snippet-sample_specialcounterprojection2'></a>
```cs
public class SpecialCounterProjection2: SingleStreamProjection<SpecialCounter, Guid>
{
    public SpecialCounterProjection2()
    {
        // This is normally just an optimization for the async daemon,
        // but as a "global" projection, this also helps Marten
        // "know" that all events of these types should always be captured
        // to the default tenant id
        IncludeType<SpecialA>();
        IncludeType<SpecialB>();
        IncludeType<SpecialC>();
        IncludeType<SpecialD>();
    }

    public void Apply(SpecialCounter c, SpecialA _) => c.ACount++;
    public void Apply(SpecialCounter c, SpecialB _) => c.BCount++;
    public void Apply(SpecialCounter c, SpecialC _) => c.CCount++;
    public void Apply(SpecialCounter c, SpecialD _) => c.DCount++;

    public override SpecialCounter Evolve(SpecialCounter snapshot, Guid id, IEvent e)
    {
        snapshot ??= new SpecialCounter { Id = id };
        switch (e.Data)
        {
            case SpecialA _:
                snapshot.ACount++;
                break;
            case SpecialB _:
                snapshot.BCount++;
                break;
            case SpecialC _:
                snapshot.CCount++;
                break;
            case SpecialD _:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs#L410-L454' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_specialcounterprojection2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And finally, let's register our projection within our application's bootstrapping:

<!-- snippet: sample_bootstrapping_with_global_projection -->
<a id='snippet-sample_bootstrapping_with_global_projection'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // The event store has conjoined tenancy...
    opts.Events.TenancyStyle = TenancyStyle.Conjoined;

    // But we want any events appended to a stream that is related
    // to a SpecialCounter to be single or global tenanted
    // And this works with any ProjectionLifecycle
    opts.Projections.AddGlobalProjection(new SpecialCounterProjection(), ProjectionLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/global_tenanted_streams_within_conjoined_tenancy.cs#L360-L376' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrapping_with_global_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The impact of this global registration is that any events appended to a stream with an aggregate type of `SpecialCounter`
or really any events at all of the types known to be included in the globally registered single stream projection will
be appended as the default tenant id _no matter what the session's tenant id is_. There's a couple implications here:

1. The event types of a globally applied projection should not be used against other types of streams
2. Marten "corrects" the tenant id applied to events from globally projected aggregates regardless of how the events are appended or how the session was created
3. Marten automatically marks the storage for the aggregate type as single tenanted
4. Live, Async, or Inline projections have all been tested with this functionality
5. `AppendOptimistic()` and `AppendPessimistic()` do not work (yet) with this setting, but you should probably
   be using `FetchForWriting()` instead anyway. 
