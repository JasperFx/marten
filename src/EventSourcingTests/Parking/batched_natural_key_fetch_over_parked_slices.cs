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
using Marten.Events.Aggregation;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Parking;

/// <summary>
///     A Guid-identified ledger whose natural key is a primitive string, the shape a batch reaches by natural key
///     through <c>IBatchEvents.FetchLatest&lt;T&gt;(string)</c> and <c>FetchForWriting&lt;T&gt;(string)</c>.
/// </summary>
public class CodedLedger
{
    public Guid Id { get; set; }

    [NaturalKey]
    public string? Code { get; set; }

    public int Postings { get; set; }

    [NaturalKeySource]
    public void Apply(LedgerOpened e) => Code = e.Number;

    public void Apply(LedgerPosted e) => Postings++;

    public void Apply(IEvent<LedgerBroken> e)
    {
        LedgerPoison.ThrowIfPoisoned(e.TenantId, e.StreamId);
        Postings++;
    }
}

/// <summary>
///     marten#6133, left out of #6003: one batched query fetches several parked slices by their natural keys. Each
///     slice's stored async snapshot is frozen at its last good state, so every item has to fold the events after it
///     for its own stream. A parked stream that never got a natural key rides in the same batch by its stream
///     identity, and neither breaks the batch nor picks up another slice's key.
/// </summary>
/// <remarks>
///     The batch reaches <c>FetchNaturalKeyPlan</c> only through its Guid and string overloads, and a natural key is an
///     int, long or string (<c>NaturalKeyDefinition.IsValid()</c>). So the only batched natural-key path is a string
///     key on a Guid-identified store: on a string-identified store the string overloads address the stream itself.
/// </remarks>
public class batched_natural_key_fetch_over_parked_slices: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 60.Seconds();

    private readonly List<Guid> _poisoned = new();

    public override async ValueTask DisposeAsync()
    {
        foreach (var stream in _poisoned) LedgerPoison.Off(StorageConstants.DefaultTenantId, stream);
        await base.DisposeAsync();
    }

    private void poison(Guid stream)
    {
        LedgerPoison.On(StorageConstants.DefaultTenantId, stream);
        _poisoned.Add(stream);
    }

    private void unpoisonAll()
    {
        foreach (var stream in _poisoned) LedgerPoison.Off(StorageConstants.DefaultTenantId, stream);
    }

    private Task waitAsync() => theStore.WaitForNonStaleProjectionDataAsync(Generous);

    private async Task<IReadOnlyList<ParkedIdentity>> parkedAsync(string projectionName)
        => await ((IEventDatabase)theStore.Storage.Database).FetchParkedIdentitiesAsync(
            new ParkedIdentityQuery { ProjectionName = projectionName }, TestContext.Current.CancellationToken);

    private async Task<long> naturalKeyRowsAsync(string aggregateName, string? where = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = conn.CreateCommand();
        command.CommandText = $"select count(*) from {SchemaName}.mt_natural_key_{aggregateName.ToLowerInvariant()}" +
                              (where == null ? "" : " where " + where);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task appendAsync(Guid stream, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(stream, events);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static object[] postings(int count) => Enumerable.Range(0, count).Select(_ => (object)new LedgerPosted()).ToArray();

    // Park every stream in `parked` with a failing event followed by one more posting, move the bystander on, and
    // check the parks landed one per stream
    private async Task parkAsync(string projectionName, Guid bystander, params Guid[] parked)
    {
        foreach (var stream in parked)
        {
            poison(stream);
            await appendAsync(stream, new LedgerBroken(), new LedgerPosted());
        }

        await appendAsync(bystander, new LedgerPosted());
        await waitAsync();

        var rows = await parkedAsync(projectionName);
        rows.Select(x => x.IdentityText).OrderBy(x => x).ShouldBe(
            parked.Select(x => x.ToString("D")).OrderBy(x => x));

        // Fixed, but not replayed: the stored snapshots stay frozen, so only a fetch that folds sees the new events
        unpoisonAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task one_batch_folds_each_parked_slice_by_its_own_natural_key(bool exclusive)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            opts.Projections.Snapshot<CodedLedger>(SnapshotLifecycle.Async);
        });

        var token = TestContext.Current.CancellationToken;
        string code() => $"C-{Guid.NewGuid():N}";
        var (codeA, codeB, codeC, codeBystander) = (code(), code(), code(), code());

        async Task<Guid> ledgerAsync(params object[] events)
        {
            await using var session = theStore.LightweightSession();
            var id = session.Events.StartStream<CodedLedger>(events).Id;
            await session.SaveChangesAsync(token);
            return id;
        }

        // A different number of postings per stream, so a slice folded over another's events shows
        var a = await ledgerAsync([new LedgerOpened(codeA), ..postings(1)]);
        var b = await ledgerAsync([new LedgerOpened(codeB), ..postings(2)]);
        var c = await ledgerAsync([new LedgerOpened(codeC), ..postings(3)]);
        var keyless = await ledgerAsync(postings(4));
        var bystander = await ledgerAsync([new LedgerOpened(codeBystander), ..postings(5)]);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await waitAsync();

        await parkAsync(nameof(CodedLedger), bystander, a, b, c, keyless);

        // The keyless stream has no lookup row of its own
        (await naturalKeyRowsAsync(nameof(CodedLedger))).ShouldBe(4);
        (await naturalKeyRowsAsync(nameof(CodedLedger), $"stream_id = '{keyless:D}'")).ShouldBe(0);

        await using (var query = theStore.QuerySession())
        {
            (await query.LoadAsync<CodedLedger>(a, token))!.Postings.ShouldBe(1);
            (await query.LoadAsync<CodedLedger>(b, token))!.Postings.ShouldBe(2);
            (await query.LoadAsync<CodedLedger>(c, token))!.Postings.ShouldBe(3);
            (await query.LoadAsync<CodedLedger>(keyless, token))!.Postings.ShouldBe(4);
        }

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();

        // The exclusive fetches run the whole batch inside the session's transaction, on one connection
        Task<IEventStream<CodedLedger>> writing(string naturalKey) => exclusive
            ? batch.Events.FetchForExclusiveWriting<CodedLedger>(naturalKey)
            : batch.Events.FetchForWriting<CodedLedger>(naturalKey);

        Task<IEventStream<CodedLedger>> writingById(Guid stream) => exclusive
            ? batch.Events.FetchForExclusiveWriting<CodedLedger>(stream)
            : batch.Events.FetchForWriting<CodedLedger>(stream);

        // Natural-key items interleaved with stream-id items, so a result set read by the wrong item shows
        var latestA = batch.Events.FetchLatest<CodedLedger>(codeA);
        var writingB = writing(codeB);
        var latestKeyless = batch.Events.FetchLatest<CodedLedger>(keyless);
        var latestC = batch.Events.FetchLatest<CodedLedger>(codeC);
        var writingKeyless = writingById(keyless);
        var writingC = writing(codeC);
        var nobody = batch.Events.FetchLatest<CodedLedger>(code());
        var latestBystander = batch.Events.FetchLatest<CodedLedger>(codeBystander);

        await batch.Execute(token);

        var ledgerA = (await latestA).ShouldNotBeNull();
        ledgerA.Id.ShouldBe(a);
        ledgerA.Code.ShouldBe(codeA);
        ledgerA.Postings.ShouldBe(3);

        var streamB = await writingB;
        streamB.Id.ShouldBe(b);
        streamB.StartingVersion.ShouldBe(5);
        streamB.Aggregate.ShouldNotBeNull().Code.ShouldBe(codeB);
        streamB.Aggregate.Postings.ShouldBe(4);

        var ledgerC = (await latestC).ShouldNotBeNull();
        ledgerC.Id.ShouldBe(c);
        ledgerC.Code.ShouldBe(codeC);
        ledgerC.Postings.ShouldBe(5);

        var streamC = await writingC;
        streamC.Id.ShouldBe(c);
        streamC.StartingVersion.ShouldBe(6);
        streamC.Aggregate.ShouldNotBeNull().Postings.ShouldBe(5);

        // The keyless slice folds too, and takes no key from its neighbours
        var keylessLedger = (await latestKeyless).ShouldNotBeNull();
        keylessLedger.Id.ShouldBe(keyless);
        keylessLedger.Code.ShouldBeNull();
        keylessLedger.Postings.ShouldBe(6);

        var keylessStream = await writingKeyless;
        keylessStream.Id.ShouldBe(keyless);
        keylessStream.StartingVersion.ShouldBe(6);
        keylessStream.Aggregate.ShouldNotBeNull().Code.ShouldBeNull();

        (await nobody).ShouldBeNull();

        var bystanderLedger = (await latestBystander).ShouldNotBeNull();
        bystanderLedger.Id.ShouldBe(bystander);
        bystanderLedger.Postings.ShouldBe(6);

        // Every stream handed out for writing carries its own expected version
        streamB.AppendOne(new LedgerPosted());
        streamC.AppendOne(new LedgerPosted());
        keylessStream.AppendOne(new LedgerPosted());
        await session.SaveChangesAsync(token);

        (await theSession.Events.FetchStreamStateAsync(b, token))!.Version.ShouldBe(6);
        (await theSession.Events.FetchStreamStateAsync(c, token))!.Version.ShouldBe(7);
        (await theSession.Events.FetchStreamStateAsync(keyless, token))!.Version.ShouldBe(7);

        await daemon.StopAllAsync();
    }
}
