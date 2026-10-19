#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Cold;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.ReadQuery;

/// <summary>
/// The cold part of a stream under conjoined tenancy with per-tenant event partitions: the same stream key
/// in two tenants has two cold parts, and each tenant only ever folds its own.
/// </summary>
public class cold_stream_part_per_tenant: PartitionedStoreContext
{
    public record Opened(string By);
    public record Noted(string Text);

    public class Notes
    {
        public string Id { get; set; } = null!;
        public List<string> Lines { get; set; } = new();
    }

    public partial class NotesProjection: SingleStreamProjection<Notes, string>
    {
        public override Notes Evolve(Notes? snapshot, string id, IEvent e)
        {
            snapshot ??= new Notes { Id = id };
            switch (e.Data)
            {
                case Opened opened:
                    snapshot.Lines.Add($"opened by {opened.By}");
                    break;
                case Noted noted:
                    snapshot.Lines.Add(noted.Text);
                    break;
            }

            return snapshot;
        }
    }

    public class TenantColdStore: IColdEventStore
    {
        public readonly ConcurrentDictionary<(string Tenant, string Key), List<ColdEvent>> Streams = new();

        public async IAsyncEnumerable<ColdEvent> ReadAsync(IQuerySession session, string streamKey,
            long throughVersion, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            if (!Streams.TryGetValue((session.TenantId, streamKey), out var events)) yield break;
            foreach (var e in events.Where(x => x.Version <= throughVersion)) yield return e;
        }
    }

    public class ColdVersionListener: DocumentSessionListenerBase
    {
        public readonly ConcurrentDictionary<(string Tenant, string Key), long?> Seen = new();

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            foreach (var stream in session.PendingChanges.Streams())
            {
                Seen[(session.TenantId, stream.Key!)] = session.Events.ColdVersionOf(stream);
            }

            return Task.CompletedTask;
        }
    }

    private readonly TenantColdStore _cold = new();
    private readonly ColdVersionListener _listener = new();

    protected override string SchemaPrefix => "tp_cold";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Events.UseColdEventStore(_cold);
        opts.Projections.Add(new NotesProjection(), ProjectionLifecycle.Live);
        opts.Listeners.Add(_listener);
    }

    private async Task moveToCold(string tenant, string key, long throughVersion)
    {
        await using var session = Store.LightweightSession(tenant);
        var events = await session.Events.FetchStreamAsync(key);
        _cold.Streams[(tenant, key)] = events.Where(x => x.Version <= throughVersion).Select(x => new ColdEvent
        {
            Id = x.Id,
            Version = x.Version,
            Sequence = x.Sequence,
            EventTypeName = x.EventTypeName,
            DotNetTypeName = x.DotNetTypeName,
            Timestamp = x.Timestamp,
            Data = Store.Options.Serializer().ToJson(x.Data)
        }).ToList();

        session.Events.MarkStreamCold(key, throughVersion);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task each_tenant_folds_its_own_cold_part_of_the_same_stream_key()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        var key = Guid.NewGuid().ToString();

        foreach (var tenant in new[] { "alpha", "beta" })
        {
            await using var session = Store.LightweightSession(tenant);
            session.Events.StartStream<Notes>(key, new Opened(tenant), new Noted($"{tenant} 1"), new Noted($"{tenant} 2"));
            await session.SaveChangesAsync();
        }

        await moveToCold("alpha", key, 3);
        await moveToCold("beta", key, 2);

        await using (var session = Store.LightweightSession("alpha"))
        {
            (await session.Events.FetchStreamAsync(key)).ShouldBeEmpty();

            var stream = await session.Events.FetchForWriting<Notes>(key);
            stream.Aggregate!.Lines.ShouldBe(["opened by alpha", "alpha 1", "alpha 2"]);
            stream.StartingVersion.ShouldBe(3);
            stream.AppendOne(new Noted("alpha 3"));
            await session.SaveChangesAsync();
        }

        await using (var session = Store.LightweightSession("beta"))
        {
            (await session.Events.FetchLatest<Notes>(key))!.Lines.ShouldBe(["opened by beta", "beta 1", "beta 2"]);
            (await session.Events.FetchStreamIncludingColdAsync(key)).ShouldAllBe(x => x.TenantId == "beta");

            session.Events.Append(key, new Noted("beta 3"));
            await session.SaveChangesAsync();
        }

        _listener.Seen[("alpha", key)].ShouldBe(3);
        _listener.Seen[("beta", key)].ShouldBe(2);

        await using (var session = Store.LightweightSession("alpha"))
        {
            (await session.Events.FetchLatest<Notes>(key))!.Lines.ShouldBe(["opened by alpha", "alpha 1", "alpha 2", "alpha 3"]);
            (await session.Events.FetchStreamAsync(key)).Single().Version.ShouldBe(4);
        }
    }
}
