#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Parking;

/// <summary>A string-identified ledger with a natural key, for the string stream half of marten#6132.</summary>
public class NaturalKeyStringLedger: IParkingLedger
{
    public string Id { get; set; } = string.Empty;

    [NaturalKey]
    public LedgerNumber Number { get; set; } = null!;

    public int Postings { get; set; }

    [NaturalKeySource]
    public void Apply(LedgerOpened e) => Number = new LedgerNumber(e.Number);

    public void Apply(LedgerPosted e) => Postings++;

    public void Apply(IEvent<LedgerBroken> e)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.StreamKey!);
        Postings++;
    }
}

/// <summary>Owns the ledger number that <see cref="MappedKeyLedger" /> maps, and never parks.</summary>
public class LedgerNumberOwner
{
    public Guid Id { get; set; }

    [NaturalKey]
    public LedgerNumber Number { get; set; } = null!;

    [NaturalKeySource]
    public void Apply(LedgerOpened e) => Number = new LedgerNumber(e.Number);
}

/// <summary>An Async read model that finds its ledger number through <see cref="LedgerNumberOwner" />'s lookup.</summary>
public class MappedKeyLedger: IParkingLedger
{
    public Guid Id { get; set; }

    [MappedNaturalKey<LedgerNumberOwner>]
    public LedgerNumber Number { get; set; } = null!;

    public int Postings { get; set; }

    public void Apply(LedgerOpened e) => Number = new LedgerNumber(e.Number);

    public void Apply(LedgerPosted e) => Postings++;

    public void Apply(IEvent<LedgerBroken> e)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.StreamId);
        Postings++;
    }
}

/// <summary>
///     marten#6132: <c>FetchParkedIdentitiesAsync</c> fills in <see cref="ParkedIdentity.NaturalKey" /> from the
///     aggregate's natural key table, so the parked list can show the key an operator knows the aggregate by.
/// </summary>
public class natural_keys_on_the_parked_identity_list: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 60.Seconds();

    private readonly List<(string TenantId, object Stream)> _poisoned = new();

    public override async ValueTask DisposeAsync()
    {
        foreach (var (tenantId, stream) in _poisoned) LedgerPoison.Off(tenantId, stream);
        await base.DisposeAsync();
    }

    private void poison(object stream, string tenantId = StorageConstants.DefaultTenantId)
    {
        LedgerPoison.On(tenantId, stream);
        _poisoned.Add((tenantId, stream));
    }

    private async Task<IReadOnlyList<ParkedIdentity>> parkedAsync(string projectionName)
        => await ((IEventDatabase)theStore.Storage.Database).FetchParkedIdentitiesAsync(
            new ParkedIdentityQuery { ProjectionName = projectionName }, TestContext.Current.CancellationToken);

    private async Task appendAsync(object stream, string tenantId, params object[] events)
    {
        await using var session = theStore.LightweightSession(tenantId);
        if (stream is Guid id)
        {
            session.Events.Append(id, events);
        }
        else
        {
            session.Events.Append((string)stream, events);
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task appendAsync(object stream, params object[] events)
        => appendAsync(stream, StorageConstants.DefaultTenantId, events);

    private Task waitAsync() => theStore.WaitForNonStaleProjectionDataAsync(Generous);

    [Fact]
    public async Task a_keyed_slice_gets_its_natural_key_and_a_keyless_one_does_not()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;

            // Both fold the same ledger streams. Only NaturalKeyLedger has a natural key.
            opts.Projections.Snapshot<NaturalKeyLedger>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<GuidLedger>(SnapshotLifecycle.Async);
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            var keyed = Guid.NewGuid();
            var neverKeyed = Guid.NewGuid();
            var healthy = Guid.NewGuid();

            await appendAsync(keyed, new LedgerOpened("L-100"), new LedgerPosted());

            // This stream never sees the event that sets the key
            await appendAsync(neverKeyed, new LedgerPosted());
            await appendAsync(healthy, new LedgerOpened("L-200"));
            await waitAsync();

            poison(keyed);
            poison(neverKeyed);
            await appendAsync(keyed, new LedgerBroken(), new LedgerPosted());
            await appendAsync(neverKeyed, new LedgerBroken(), new LedgerPosted());
            await waitAsync();

            #region sample_reading_the_natural_key_of_a_parked_identity

            var database = (IEventDatabase)theStore.Storage.Database;
            var withKeys = await database.FetchParkedIdentitiesAsync(
                new ParkedIdentityQuery { ProjectionName = nameof(NaturalKeyLedger) },
                TestContext.Current.CancellationToken);

            // The natural key the ledger was opened with, or null for a stream that never got one
            withKeys.Single(x => x.IdentityText == keyed.ToString("D")).NaturalKey.ShouldBe("L-100");
            withKeys.Single(x => x.IdentityText == neverKeyed.ToString("D")).NaturalKey.ShouldBeNull();

            #endregion

            withKeys.Count.ShouldBe(2);

            // The same streams parked in a projection whose aggregate has no natural key
            var withoutKeys = await parkedAsync(nameof(GuidLedger));
            withoutKeys.Count.ShouldBe(2);
            withoutKeys.ShouldAllBe(x => x.NaturalKey == null);

            // The whole list, both projections at once
            var all = await database.FetchParkedIdentitiesAsync(new ParkedIdentityQuery(),
                TestContext.Current.CancellationToken);
            all.Count.ShouldBe(4);
            all.Where(x => x.NaturalKey != null).ShouldHaveSingleItem().ProjectionName.ShouldBe(nameof(NaturalKeyLedger));
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    [Fact]
    public async Task a_read_model_with_a_mapped_key_gets_the_key_from_its_source()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            opts.Projections.Snapshot<LedgerNumberOwner>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<MappedKeyLedger>(SnapshotLifecycle.Async);
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            var stream = Guid.NewGuid();
            await appendAsync(stream, new LedgerOpened("M-9"), new LedgerPosted());
            await waitAsync();

            poison(stream);
            await appendAsync(stream, new LedgerBroken(), new LedgerPosted());
            await waitAsync();

            var parked = (await parkedAsync(nameof(MappedKeyLedger))).ShouldHaveSingleItem();
            parked.IdentityText.ShouldBe(stream.ToString("D"));
            parked.NaturalKey.ShouldBe("M-9");
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    [Fact]
    public async Task a_string_stream_gets_its_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            opts.Projections.Snapshot<NaturalKeyStringLedger>(SnapshotLifecycle.Async);
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            var stream = $"ledger-{Guid.NewGuid():N}";
            await appendAsync(stream, new LedgerOpened("S-7"), new LedgerPosted());
            await waitAsync();

            poison(stream);
            await appendAsync(stream, new LedgerBroken(), new LedgerPosted());
            await waitAsync();

            var parked = (await parkedAsync(nameof(NaturalKeyStringLedger))).ShouldHaveSingleItem();
            parked.IdentityText.ShouldBe(stream);
            parked.NaturalKey.ShouldBe("S-7");
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task each_tenant_gets_its_own_natural_key_for_the_same_stream(StreamIdentity streamIdentity)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = streamIdentity;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;

            if (streamIdentity == StreamIdentity.AsGuid)
            {
                opts.Projections.Snapshot<NaturalKeyLedger>(SnapshotLifecycle.Async);
            }
            else
            {
                opts.Projections.Snapshot<NaturalKeyStringLedger>(SnapshotLifecycle.Async);
            }
        });

        var projectionName = streamIdentity == StreamIdentity.AsGuid
            ? nameof(NaturalKeyLedger)
            : nameof(NaturalKeyStringLedger);

        // One stream id in both tenants, each with its own natural key
        object stream = streamIdentity == StreamIdentity.AsGuid ? Guid.NewGuid() : $"ledger-{Guid.NewGuid():N}";

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            await appendAsync(stream, "red", new LedgerOpened("RED-1"), new LedgerPosted());
            await appendAsync(stream, "blue", new LedgerOpened("BLUE-1"), new LedgerPosted());
            await waitAsync();

            poison(stream, "red");
            poison(stream, "blue");
            await appendAsync(stream, "red", new LedgerBroken(), new LedgerPosted());
            await appendAsync(stream, "blue", new LedgerBroken(), new LedgerPosted());
            await waitAsync();

            var parked = await parkedAsync(projectionName);
            parked.Count.ShouldBe(2);
            parked.Single(x => x.TenantId == "red").NaturalKey.ShouldBe("RED-1");
            parked.Single(x => x.TenantId == "blue").NaturalKey.ShouldBe("BLUE-1");
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }
}
