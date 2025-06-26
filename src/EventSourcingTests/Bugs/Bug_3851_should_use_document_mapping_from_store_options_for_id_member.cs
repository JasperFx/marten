using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3851_should_use_document_mapping_from_store_options_for_id_member : BugIntegrationContext
{
    [Fact]
    public async Task aggregate_correctly()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<EntityWithoutConventionalId>().Identity(x => x.OtherId);

        });

        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<EntityWithoutConventionalId>(streamId, new EntityCreated("one"),
            new ContentAdded("two"));

        await theSession.SaveChangesAsync();

        var entity = await theSession.Events.AggregateStreamAsync<EntityWithoutConventionalId>(streamId);

        entity.InitialContent.ShouldBe("one");
        entity.MoreContent.ShouldBe("two");
    }
}

public class EntityWithoutConventionalId
{
    public Guid OtherId { get; set; }
    public string? InitialContent { get; set; }
    public string? MoreContent { get; set; }

    public void Apply(EntityCreated @event)
    {
        InitialContent = @event.Content;
    }

    public void Apply(ContentAdded @event)
    {
        MoreContent = @event.Content;
    }
}

public record EntityCreated(string Content);
public record ContentAdded(string Content);
