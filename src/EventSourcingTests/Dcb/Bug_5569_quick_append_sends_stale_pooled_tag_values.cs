#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

[Collection("OneOffs")]
public class Bug_5569_quick_append_sends_stale_pooled_tag_values: OneOffConfigurationsContext
{
    // A uuid-shaped leftover casts fine and silently tags the event with a foreign id.
    private const string UuidShapedLeftover = "0f08faed-2625-4c05-9a1e-642ffc17fc00";

    [Theory]
    [InlineData("api-version")]
    [InlineData(UuidShapedLeftover)]
    public async Task untagged_append_succeeds_with_an_explicitly_registered_tag_type(string leftover)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.RegisterTagType<CustomerId>("customer");
        });

        await assertUntaggedAppendSucceeds(typeof(CustomerId), leftover);
    }

    [Theory]
    [InlineData("api-version")]
    [InlineData(UuidShapedLeftover)]
    public async Task untagged_append_succeeds_with_a_tag_type_auto_discovered_from_a_projection(string leftover)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.AddEventType<GroupCreated>();

            // Nothing here mentions tags. The projection's strong-typed GroupId identity is enough
            // for the store to register GroupId as a tag type and add a tag column to every
            // quick append, including appends to streams that have nothing to do with groups.
            opts.Projections.Add<GroupProjection>(ProjectionLifecycle.Inline);
        });

        theStore.Events.TagTypes.Select(x => x.TagType).ShouldContain(typeof(GroupId));

        await assertUntaggedAppendSucceeds(typeof(GroupId), leftover);
    }

    private async Task assertUntaggedAppendSucceeds(Type tagType, string leftover)
    {
        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = Guid.NewGuid();
        await using (var setup = theStore.LightweightSession())
        {
            setup.Events.StartStream(streamId, new OrderPlaced("first"));
            await setup.SaveChangesAsync();
        }

        leaveStaleStringsInTheSharedArrayPool(leftover);

        // This event has no tags, so every tag slot should reach Postgres as NULL.
        theSession.Events.Append(streamId, new OrderPlaced("second"));
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(2);

        var tagTable = "mt_event_tag_" + theStore.Events.TagTypes.Single(x => x.TagType == tagType).TableSuffix;
        var tagRows = await theSession.QueryAsync<long>($"select count(*) from {SchemaName}.{tagTable}");
        tagRows.Single().ShouldBe(0);
    }

    // Stands in for any other code in the process that rents string arrays from the shared pool
    // and returns them without clearing. The append rents several string columns ahead of the
    // tag column, so more than a handful of dirty arrays are needed for one to land on it.
    private static void leaveStaleStringsInTheSharedArrayPool(string leftover)
    {
        var rented = new List<string[]>();
        for (var i = 0; i < 64; i++)
        {
            var array = ArrayPool<string>.Shared.Rent(1);
            Array.Fill(array, leftover);
            rented.Add(array);
        }

        foreach (var array in rented)
        {
            ArrayPool<string>.Shared.Return(array, clearArray: false);
        }
    }

    public record OrderPlaced(string Note);
    public record struct CustomerId(Guid Value);
    public record struct GroupId(Guid Value);
    public record GroupCreated(GroupId Id, string Name);

    public class Group
    {
        public GroupId Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class GroupProjection: SingleStreamProjection<Group, GroupId>
    {
        public static Group Create(GroupCreated e) => new() { Id = e.Id, Name = e.Name };
    }
}
