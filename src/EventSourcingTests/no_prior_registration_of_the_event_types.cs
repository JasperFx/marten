using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class no_prior_registration_of_the_event_types: OneOffConfigurationsContext
{
    [Fact]
    public async Task can_fetch_sync_with_guids()
    {
        var stream = Guid.NewGuid();
        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
            await session.SaveChangesAsync();
        }

        // Needs to be an isolated, separate document store to the same db
        using (var store = SeparateStore())
        {
            using (var session = store.LightweightSession())
            {
                var events = await session.Events.FetchStreamAsync(stream);
                events[0].Data.ShouldBeOfType<MembersJoined>();
                events[1].Data.ShouldBeOfType<MembersDeparted>();
            }
        }
    }

    [Fact]
    public async Task can_fetch_sync_with_strings()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        var stream = "Something";
        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
            await session.SaveChangesAsync();
        }

        // Needs to be an isolated, separate document store to the same db
        using var store = DocumentStore.For(_ =>
        {
            _.DatabaseSchemaName = theStore.Options.DatabaseSchemaName;
            _.Events.StreamIdentity = StreamIdentity.AsString;
            _.Connection(ConnectionSource.ConnectionString);
        });

        using (var session = store.LightweightSession())
        {
            var events = await session.Events.FetchStreamAsync(stream);
            events[0].Data.ShouldBeOfType<MembersJoined>();
            events[1].Data.ShouldBeOfType<MembersDeparted>();
        }
    }

    [Fact]
    public async Task can_fetch_async_with_guids()
    {
        var stream = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
            await session.SaveChangesAsync();
        }

        // Needs to be an isolated, separate document store to the same db
        using (var store = SeparateStore())
        {
            await using (var session = store.LightweightSession())
            {
                var events = await session.Events.FetchStreamAsync(stream);
                events[0].Data.ShouldBeOfType<MembersJoined>();
                events[1].Data.ShouldBeOfType<MembersDeparted>();
            }
        }
    }

    [Fact]
    public async Task can_fetch_async_with_strings()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        var stream = "Something";
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined(), new MembersDeparted());
            await session.SaveChangesAsync();
        }

        // Needs to be an isolated, separate document store to the same db
        using (var store = DocumentStore.For(_ =>
               {
                   _.DatabaseSchemaName = theStore.Options.DatabaseSchemaName;
                   _.Events.StreamIdentity = StreamIdentity.AsString;
                   _.Connection(ConnectionSource.ConnectionString);
               }))
        {
            await using (var session = store.LightweightSession())
            {
                var events = await session.Events.FetchStreamAsync(stream);
                events[0].Data.ShouldBeOfType<MembersJoined>();
                events[1].Data.ShouldBeOfType<MembersDeparted>();
            }
        }
    }

}
