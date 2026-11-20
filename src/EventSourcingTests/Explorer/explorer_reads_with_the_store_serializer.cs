#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Explorer;

// #6107: a polymorphic type whose discriminator isn't the first property once Postgres' jsonb has reordered the
// keys ("Text" sorts before "type"), so reading it back needs the store's AllowOutOfOrderMetadataProperties.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserMessageItem), "user_message")]
[JsonDerivedType(typeof(AssistantMessageItem), "assistant_message")]
public abstract record ConversationItem;

public sealed record UserMessageItem(string Text): ConversationItem;

public sealed record AssistantMessageItem(string Text): ConversationItem;

public record ConversationItemsRecorded(List<ConversationItem> Items);

public record ConversationTag(Guid Value);

public class Conversation
{
    public Guid Id { get; set; }
    public List<ConversationItem> Items { get; set; } = new();

    public void Apply(ConversationItemsRecorded e) => Items.AddRange(e.Items);
}

[Collection("OneOffs")]
public class explorer_reads_with_the_store_serializer: OneOffConfigurationsContext
{
    private void configureStore(Casing casing = Casing.Default)
    {
        StoreOptions(opts =>
        {
            // Postgres' jsonb doesn't keep the discriminator first
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsString, casing,
                configure: json => json.AllowOutOfOrderMetadataProperties = true);

            opts.Events.AddEventType<ConversationItemsRecorded>();
            opts.Events.RegisterTagType<ConversationTag>("conversation");
            opts.Projections.LiveStreamAggregation<Conversation>();
        });
    }

    private async Task<(Guid streamId, ConversationTag tag)> recordConversation()
    {
        var streamId = Guid.NewGuid();
        var tag = new ConversationTag(Guid.NewGuid());

        var recorded = theSession.Events.BuildEvent(new ConversationItemsRecorded(
            [new UserMessageItem("hello"), new AssistantMessageItem("hi there")]));
        recorded.WithTag(tag);
        theSession.Events.Append(streamId, recorded);
        await theSession.SaveChangesAsync();

        return (streamId, tag);
    }

    [Fact]
    public async Task projected_state_for_tags_reads_the_events_with_the_store_serializer()
    {
        configureStore();
        var (_, tag) = await recordConversation();

        var explorer = (IEventStore)theStore;
        var tags = new Dictionary<string, string> { { nameof(ConversationTag), tag.Value.ToString() } };

        var projected = await explorer.GetProjectedStateForTagsAsync(nameof(Conversation), tags, CancellationToken.None);

        projected.ShouldNotBeNull();
        var items = projected!.State.GetProperty("Items");
        items.GetArrayLength().ShouldBe(2);
        items[0].GetProperty("type").GetString().ShouldBe("user_message");
        items[1].GetProperty("Text").GetString().ShouldBe("hi there");
    }

    [Fact]
    public async Task rehydrated_state_is_written_with_the_store_serializer()
    {
        configureStore(Casing.CamelCase);
        var (streamId, _) = await recordConversation();

        var explorer = (IEventStore)theStore;
        var rehydrated = await explorer.RehydrateAtVersionByNameAsync(nameof(Conversation), streamId, version: 1,
            CancellationToken.None);

        rehydrated.ShouldNotBeNull();
        var items = rehydrated!.State.GetProperty("items");
        items.GetArrayLength().ShouldBe(2);
        items[0].GetProperty("text").GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task stepping_through_the_stream_reads_the_events_with_the_store_serializer()
    {
        configureStore();
        var (streamId, _) = await recordConversation();

        var explorer = (IEventStore)theStore;
        var records = new List<EventRecord>();
        await foreach (var record in explorer.ReadStreamAsync(streamId.ToString(), CancellationToken.None))
        {
            records.Add(record);
        }

        var timeline = await explorer.RunProjectionByNameAsync(nameof(Conversation), streamId, records,
            startingState: null, CancellationToken.None);

        timeline.Steps.Count.ShouldBe(1);
        timeline.Steps[0].Error.ShouldBeNull();
        timeline.FinalState!.Value.GetProperty("Items").GetArrayLength().ShouldBe(2);
    }
}
