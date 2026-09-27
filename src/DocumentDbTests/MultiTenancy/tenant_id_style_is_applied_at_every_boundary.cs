using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

// #5516. TenantIdStyle.ForceLowerCase was applied where a session or database was RESOLVED but not
// where the tenant id was subsequently STAMPED or KEYED, so four entry points took a mixed-case id
// and wrote or read a tenant_id nothing else would match:
//
//   * BulkInsertEventsAsync / BulkInsertEventStreamAsync resolved the tenant with the corrected id
//     and then stamped the raw one on mt_streams and mt_events.
//   * TenantIsOneOf(...) filtered on the raw values.
//
// The event facts write with one casing and read with another, because that mismatch is the bug --
// asserting on the stored tenant_id alone would still pass if BOTH sides were wrong in the same way.
//
// ForTenant(string) also keyed its nested-session cache on the raw id. That one is NOT a data bug --
// see the fact below for what it actually cost and why its test is shaped differently.
//
// The prior TenantIdStyle tests were configuration-level and used already-lowercase ids, so none of
// these paths was pinned in either direction.
public class tenant_id_style_is_applied_at_every_boundary: OneOffConfigurationsContext
{
    public class TenantedDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }

    public record Started(string Name);

    private async Task configureAsync()
    {
        StoreOptions(opts =>
        {
            opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Schema.For<TenantedDoc>().MultiTenanted();
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(TenantedDoc));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    [Fact]
    public async Task bulk_insert_events_stamps_the_corrected_tenant_id()
    {
        await configureAsync();

        var streamId = Guid.NewGuid();
        var stream = StreamAction.Start(theStore.Events, streamId, new Started("bulk"));

        await theStore.BulkInsertEventsAsync("MIXEDCase", [stream]);

        // The read normalises, so it can only find the rows if the write normalised too.
        await using var session = theStore.QuerySession("MIXEDCase");
        var events = await session.Events.FetchStreamAsync(streamId);

        events.ShouldHaveSingleItem().TenantId.ShouldBe("mixedcase");
    }

    [Fact]
    public async Task bulk_insert_event_stream_stamps_the_corrected_tenant_id()
    {
        await configureAsync();

        var streamId = Guid.NewGuid();
        var header = new BulkEventStreamHeader { Id = streamId, Version = 1 };

        await theStore.BulkInsertEventStreamAsync("StreamTENANT", [header], eventsFor(streamId));

        await using var session = theStore.QuerySession("streamtenant");
        var events = await session.Events.FetchStreamAsync(streamId);

        events.ShouldHaveSingleItem().TenantId.ShouldBe("streamtenant");
    }

    private static async IAsyncEnumerable<IEvent> eventsFor(Guid streamId)
    {
        yield return new Event<Started>(new Started("streamed"))
        {
            Id = Guid.NewGuid(), StreamId = streamId, Version = 1, Sequence = 1
        };
        await Task.CompletedTask;
    }

    // The two ForTenant paths are NOT a data bug, and this fact is deliberately narrower than the
    // others because of it. The issue that prompted the change (#5516) hedged -- "whether the nested
    // session then normalises before it writes needs checking" -- and it does: a document written
    // through ForTenant("UPPER") is readable from a session opened for "upper" both before and after
    // the normalisation added here, so a write/read fact over it discriminates nothing and was removed
    // rather than left in looking like coverage.
    //
    // What the raw id did affect is the nested-session CACHE, which was keyed on it. Two spellings of
    // one tenant produced two NestedTenantSessions over the same tenant, each with its own identity
    // map and its own queued operations -- so a caller that reached for both spellings in one session
    // got two views that could not see each other's pending work. That is what this pins.
    [Fact]
    public async Task for_tenant_caches_one_nested_session_per_corrected_tenant_id()
    {
        await configureAsync();

        await using var session = theStore.LightweightSession();

        session.ForTenant("CASED").ShouldBeSameAs(session.ForTenant("cased"));

        await using var query = theStore.QuerySession();

        query.ForTenant("CASED").ShouldBeSameAs(query.ForTenant("cased"));
    }

    [Fact]
    public async Task tenant_is_one_of_corrects_the_values_it_filters_on()
    {
        await configureAsync();

        var first = new TenantedDoc { Id = Guid.NewGuid(), Name = "one" };
        var second = new TenantedDoc { Id = Guid.NewGuid(), Name = "two" };

        await using (var session = theStore.LightweightSession("aaa"))
        {
            session.Store(first);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("bbb"))
        {
            session.Store(second);
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var names = await query.Query<TenantedDoc>()
            .Where(x => x.TenantIsOneOf("AAA", "BBB"))
            .Select(x => x.Name)
            .ToListAsync();

        names.OrderBy(x => x).ShouldBe(["one", "two"]);
    }
}
