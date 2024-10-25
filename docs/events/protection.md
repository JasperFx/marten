# Removing Protected Information

::: info
The Marten team is at least considering support for [crypto-shredding](https://en.wikipedia.org/wiki/Crypto-shredding) in Marten 8.0,
but no definite plans have been made yet.
:::

For a variety of reasons, you may wish to remove or mask sensitive data elements in a Marten database without necessarily deleting the information as a whole. Documents can be amended
with Marten's Patching API. With event data, you now have options to reach into the event data and rewrite selected
members as well as to add custom headers. First, start by defining data masking rules by event type like so:

<!-- snippet: sample_defining_masking_rules -->
<a id='snippet-sample_defining_masking_rules'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // By a single, concrete type
    opts.Events.AddMaskingRuleForProtectedInformation<AccountChanged>(x =>
    {
        // I'm only masking a single property here, but you could do as much as you want
        x.Name = "****";
    });

    // Maybe you have an interface that multiple event types implement that would help
    // make these rules easier by applying to any event type that implements this interface
    opts.Events.AddMaskingRuleForProtectedInformation<IAccountEvent>(x => x.Name = "****");

    // Little fancier
    opts.Events.AddMaskingRuleForProtectedInformation<MembersJoined>(x =>
    {
        for (int i = 0; i < x.Members.Length; i++)
        {
            x.Members[i] = "*****";
        }
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/removing_protected_information.cs#L367-L395' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_defining_masking_rules' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

That's strictly a configuration time effort. Next, you can apply the masking on demand to any subset of events with 
the `IDocumentStore.Advanced.ApplyEventDataMasking()` API. First, you can apply the masking for a single stream:

<!-- snippet: sample_apply_masking_to_a_single_stream -->
<a id='snippet-sample_apply_masking_to_a_single_stream'></a>
```cs
public static Task apply_masking_to_streams(IDocumentStore store, Guid streamId, CancellationToken token)
{
    return store
        .Advanced
        .ApplyEventDataMasking(x =>
        {
            x.IncludeStream(streamId);

            // You can add or modify event metadata headers as well
            // BUT, you'll of course need event header tracking to be enabled
            x.AddHeader("masked", DateTimeOffset.UtcNow);
        }, token);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/removing_protected_information.cs#L398-L414' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_apply_masking_to_a_single_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
Regardless of what events you specify, only events that match a pre-registered masking rule will have the header changes
applied.
:::

To apply the event data masking across streams on an arbitrary grouping, you can use a LINQ expression as well:

<!-- snippet: sample_apply_masking_by_filter -->
<a id='snippet-sample_apply_masking_by_filter'></a>
```cs
public static Task apply_masking_by_filter(IDocumentStore store, Guid[] streamIds)
{
    return store.Advanced.ApplyEventDataMasking(x =>
        {
            x.IncludeEvents(e => e.EventTypesAre(typeof(QuestStarted)) && e.StreamId.IsOneOf(streamIds));
        });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/removing_protected_information.cs#L416-L426' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_apply_masking_by_filter' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Finally, if you are using multi-tenancy, you can specify the tenant id as part of the same fluent interface:

<!-- snippet: sample_apply_masking_with_multi_tenancy -->
<a id='snippet-sample_apply_masking_with_multi_tenancy'></a>
```cs
public static Task apply_masking_by_tenant(IDocumentStore store, string tenantId, Guid streamId)
{
    return store
        .Advanced
        .ApplyEventDataMasking(x =>
        {
            x.IncludeStream(streamId);

            // Specify the tenant id, and it doesn't matter
            // in what order this appears in
            x.ForTenant(tenantId);
        });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/removing_protected_information.cs#L428-L444' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_apply_masking_with_multi_tenancy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Here's a couple more facts you might need to know:

* The masking rules can only be done at configuration time (as of right now)
* You can apply multiple masking rules for certain event types, and all will be applied when you use the masking API
* The masking has absolutely no impact on event archiving or projected data -- unless you rebuild the projection data after applying the data masking of course
