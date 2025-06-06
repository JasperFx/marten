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
