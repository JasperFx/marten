using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

public class always_enforce_consistency_conjoined_tenancy: OneOffConfigurationsContext
{
    // Under conjoined tenancy the same stream key can exist for multiple tenants.
    // The AlwaysEnforceConsistency "assert version, append nothing" path must scope
    // its version check to the fetching tenant, not read another tenant's same-keyed
    // stream row. The "wrong" tenant here sorts first by (tenant_id, id) and is
    // inserted first, so an unscoped query would read its version first.

    [Fact]
    public async Task enforce_consistency_no_events_scopes_to_tenant_happy_path()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        }, true);

        const string streamKey = "shared-key";

        // Other tenant's same-keyed stream sits at version 5.
        theSession.ForTenant("aaa-other").Events.StartStream<SimpleAggregateAsString>(streamKey,
            new AEvent(), new BEvent(), new CEvent(), new DEvent(), new EEvent());

        // Our tenant's stream sits at version 3.
        theSession.ForTenant("zzz-target").Events.StartStream<SimpleAggregateAsString>(streamKey,
            new AEvent(), new BEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        var stream = await theSession.ForTenant("zzz-target").Events
            .FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency = true;

        // No events appended. Our tenant's version (3) has not changed, so the
        // consistency check must succeed. If the check reads the other tenant's
        // version (5), it throws a spurious ConcurrencyException.
        await Should.NotThrowAsync(async () => await theSession.SaveChangesAsync());
    }

    [Fact]
    public async Task enforce_consistency_no_events_scopes_to_tenant_sad_path()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        }, true);

        const string streamKey = "shared-key";

        // Other tenant's same-keyed stream sits at version 3 — coincidentally the
        // same version our tenant will be fetched at.
        theSession.ForTenant("aaa-other").Events.StartStream<SimpleAggregateAsString>(streamKey,
            new AEvent(), new BEvent(), new CEvent());

        theSession.ForTenant("zzz-target").Events.StartStream<SimpleAggregateAsString>(streamKey,
            new AEvent(), new BEvent(), new CEvent());

        await theSession.SaveChangesAsync();

        var stream = await theSession.ForTenant("zzz-target").Events
            .FetchForWriting<SimpleAggregateAsString>(streamKey);
        stream.AlwaysEnforceConsistency = true;

        // Our tenant's stream advances to version 4 out of band.
        await using (var otherSession = theStore.LightweightSession("zzz-target"))
        {
            otherSession.Events.Append(streamKey, new DEvent());
            await otherSession.SaveChangesAsync();
        }

        // Our tenant's version changed (3 -> 4), so the check must fail. If it reads
        // the other tenant's still-at-3 row, it would miss the conflict (fail open).
        await Should.ThrowAsync<ConcurrencyException>(async () => await theSession.SaveChangesAsync());
    }
}
