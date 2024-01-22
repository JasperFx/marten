using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class delete_single_event_stream: OneOffConfigurationsContext
{
    [Fact]
    public void delete_stream_by_guid_id()
    {

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        using (var session = TheStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream1, joined, departed);

            var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream2, joined2, departed2);

            session.SaveChanges();
        }

        TheStore.Advanced.Clean.DeleteSingleEventStream(stream1);

        using (var session = TheStore.LightweightSession())
        {
            session.Events.QueryAllRawEvents().ToList().All(x => x.StreamId == stream2)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public void delete_stream_by_guid_id_conjoined_tenancy()
    {
        StoreOptions(opts => opts.Events.TenancyStyle = TenancyStyle.Conjoined);

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        using (var session = TheStore.LightweightSession("one"))
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream1, joined, departed);

            var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream2, joined2, departed2);

            session.SaveChanges();
        }

        TheStore.Advanced.Clean.DeleteSingleEventStream(stream1, "one");

        using (var session = TheStore.LightweightSession())
        {
            session.Events.QueryAllRawEvents().ToList().All(x => x.StreamId == stream2)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public async Task delete_stream_by_guid_id_async()
    {

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        await using (var session = TheStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream1, joined, departed);

            var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream2, joined2, departed2);

            await session.SaveChangesAsync();
        }

        await TheStore.Advanced.Clean.DeleteSingleEventStreamAsync(stream1);

        await using (var session = TheStore.LightweightSession())
        {
            session.Events.QueryAllRawEvents().ToList().All(x => x.StreamId == stream2)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public async Task delete_stream_by_guid_id_async_with_multi_tenancy()
    {
        StoreOptions(_ => _.Events.TenancyStyle = TenancyStyle.Conjoined);

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();

        await using (var session = TheStore.LightweightSession("one"))
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream1, joined, departed);

            var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream2, joined2, departed2);

            await session.SaveChangesAsync();
        }

        await TheStore.Advanced.Clean.DeleteSingleEventStreamAsync(stream1, "one");

        await using (var session = TheStore.LightweightSession("one"))
        {
            session.Events.QueryAllRawEvents().ToList().All(x => x.StreamId == stream2)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public void delete_stream_by_string_key()
    {
        StoreOptions(_ =>
        {
            _.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var stream1 = "one";
        var stream2 = "two";

        using (var session = TheStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream1, joined, departed);

            var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream2, joined2, departed2);

            session.SaveChanges();
        }

        TheStore.Advanced.Clean.DeleteSingleEventStream(stream1);

        using (var session = TheStore.LightweightSession())
        {
            session.Events.QueryAllRawEvents().ToList().All(x => x.StreamKey == stream2)
                .ShouldBeTrue();
        }
    }

    [Fact]
    public void delete_stream_by_string_key_multi_tenanted()
    {
        StoreOptions(_ =>
        {
            _.Events.StreamIdentity = StreamIdentity.AsString;
            _.Events.TenancyStyle = TenancyStyle.Conjoined;
        });

        var stream1 = "one";
        var stream2 = "two";

        using (var session = TheStore.LightweightSession("one"))
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream1, joined, departed);

            var joined2 = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed2 = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(stream2, joined2, departed2);

            session.SaveChanges();
        }

        TheStore.Advanced.Clean.DeleteSingleEventStream(stream1, "one");

        using (var session = TheStore.LightweightSession())
        {
            session.Events.QueryAllRawEvents().ToList().All(x => x.StreamKey == stream2)
                .ShouldBeTrue();
        }
    }

}