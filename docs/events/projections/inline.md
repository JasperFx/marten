# Inline Projections

An "inline" projection just means that Marten will process the projection against new events being appended
to the event store at the time that `IDocumentSession.SaveChanges()` is called to commit a unit of work. Here's a small example projection:

<!-- snippet: sample_MonsterDefeatedTransform -->
<a id='snippet-sample_monsterdefeatedtransform'></a>
```cs
public class MonsterDefeatedTransform: EventProjection
{
    public MonsterDefeated Create(IEvent<MonsterSlayed> input)
    {
        return new MonsterDefeated { Id = input.Id, Monster = input.Data.Name };
    }
}

public class MonsterDefeated
{
    public Guid Id { get; set; }
    public string Monster { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/inline_transformation_of_events.cs#L160-L176' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_monsterdefeatedtransform' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that the inline projection is able to use the [event metadata](/events/metadata) at the time the inline projection is executed. That was previously a limitation of Marten that was fixed in Marten V4.

<!-- snippet: sample_usage_of_inline_projection -->
<a id='snippet-sample_usage_of_inline_projection'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.Projections.Add(new MonsterDefeatedTransform(),
        ProjectionLifecycle.Inline);
});

await using var session = store.LightweightSession();

var streamId = session.Events
    .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;

// The projection is going to be applied right here during
// the call to SaveChangesAsync() and the resulting document update
// of the new MonsterDefeated document will happen in the same database
// transaction
await theSession.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/inline_transformation_of_events.cs#L36-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_usage_of_inline_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
