using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

/// <summary>
///     An inline snapshot looks up the existing documents only for the streams a commit APPENDS to -- a stream being
///     started has no snapshot yet. When a commit only starts streams, that leaves nothing to look up, and the
///     lookup must not go to the database at all: an empty <c>id = ANY($1)</c> query is a round trip that can only
///     return no rows.
///     <para>
///     Reads are counted from Npgsql's own activities rather than an <see cref="IMartenSessionLogger" />, because
///     with <c>UseIdentityMapForAggregates</c> off the lookup runs on a connection of its own that the session
///     logger never sees.
///     </para>
/// </summary>
public class inline_snapshot_reads_when_starting_streams: OneOffConfigurationsContext
{
    private void ConfigureStore(bool useIdentityMapForAggregates, TenancyStyle tenancy = TenancyStyle.Single,
        StreamIdentity identity = StreamIdentity.AsGuid)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseIdentityMapForAggregates = useIdentityMapForAggregates;
            opts.Events.TenancyStyle = tenancy;
            opts.Events.StreamIdentity = identity;

            if (identity == StreamIdentity.AsString)
            {
                opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Inline);
                if (tenancy == TenancyStyle.Conjoined) opts.Schema.For<SimpleAggregateAsString>().MultiTenanted();
            }
            else
            {
                opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
                if (tenancy == TenancyStyle.Conjoined) opts.Schema.For<SimpleAggregate>().MultiTenanted();
            }
        });
    }

    private async Task<SnapshotReadCounter> CountReadsOf<T>()
    {
        // Create the tables up front so schema checks are not part of what is measured.
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return new SnapshotReadCounter(theStore.Options.Storage.MappingFor(typeof(T)).TableName.QualifiedName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task starting_a_stream_does_not_look_for_an_existing_snapshot(bool useIdentityMapForAggregates)
    {
        ConfigureStore(useIdentityMapForAggregates);
        var streamId = Guid.NewGuid();

        using var reads = await CountReadsOf<SimpleAggregate>();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        reads.Count.ShouldBe(0);

        await using var query = theStore.QuerySession();
        var aggregate = await query.LoadAsync<SimpleAggregate>(streamId);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task appending_to_an_existing_stream_still_loads_its_snapshot(bool useIdentityMapForAggregates)
    {
        ConfigureStore(useIdentityMapForAggregates);
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        using var reads = await CountReadsOf<SimpleAggregate>();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new BEvent());
            await session.SaveChangesAsync();
        }

        reads.Count.ShouldBe(1);

        await using var query = theStore.QuerySession();
        var aggregate = await query.LoadAsync<SimpleAggregate>(streamId);
        aggregate!.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task a_start_and_an_append_in_one_commit_load_only_the_appended_snapshot(bool useIdentityMapForAggregates)
    {
        ConfigureStore(useIdentityMapForAggregates);
        var existing = Guid.NewGuid();
        var started = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(existing, new AEvent());
        await theSession.SaveChangesAsync();

        using var reads = await CountReadsOf<SimpleAggregate>();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(started, new AEvent(), new CEvent());
            session.Events.Append(existing, new BEvent());
            await session.SaveChangesAsync();
        }

        reads.Count.ShouldBe(1);

        await using var query = theStore.QuerySession();
        var appended = await query.LoadAsync<SimpleAggregate>(existing);
        appended!.ACount.ShouldBe(1);
        appended.BCount.ShouldBe(1);
        appended.Version.ShouldBe(2);

        var fresh = await query.LoadAsync<SimpleAggregate>(started);
        fresh!.ACount.ShouldBe(1);
        fresh.CCount.ShouldBe(1);
        fresh.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task starting_a_stream_with_an_existing_id_still_fails_and_leaves_the_snapshot_alone(
        bool useIdentityMapForAggregates)
    {
        ConfigureStore(useIdentityMapForAggregates);
        var streamId = Guid.NewGuid();

        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new BEvent(), new CEvent());
            await Should.ThrowAsync<ExistingStreamIdCollisionException>(() => session.SaveChangesAsync());
        }

        await using var query = theStore.QuerySession();
        var aggregate = await query.LoadAsync<SimpleAggregate>(streamId);
        aggregate!.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(0);
        aggregate.CCount.ShouldBe(0);
        aggregate.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task starting_string_keyed_streams_in_several_tenants_does_not_look_for_snapshots(
        bool useIdentityMapForAggregates)
    {
        ConfigureStore(useIdentityMapForAggregates, TenancyStyle.Conjoined, StreamIdentity.AsString);
        var streamKey = Guid.NewGuid().ToString();

        using var reads = await CountReadsOf<SimpleAggregateAsString>();
        await using (var session = theStore.LightweightSession())
        {
            session.ForTenant("one").Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent());
            session.ForTenant("two").Events.StartStream<SimpleAggregateAsString>(streamKey, new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        reads.Count.ShouldBe(0);

        await using var one = theStore.QuerySession("one");
        (await one.LoadAsync<SimpleAggregateAsString>(streamKey))!.Version.ShouldBe(1);

        await using var two = theStore.QuerySession("two");
        var inTwo = await two.LoadAsync<SimpleAggregateAsString>(streamKey);
        inTwo!.BCount.ShouldBe(1);
        inTwo.Version.ShouldBe(2);
    }

    /// <summary>
    ///     Counts the commands Npgsql runs that select from one document table. The table name is qualified with this
    ///     fixture's own schema, so commands from tests running in parallel elsewhere are never counted.
    /// </summary>
    private sealed class SnapshotReadCounter: IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly Regex _selectFromTable;
        private int _count;

        public SnapshotReadCounter(string qualifiedTableName)
        {
            _selectFromTable = new Regex($@"\bfrom\s+{Regex.Escape(qualifiedTableName)}\s+as\s+d\b",
                RegexOptions.IgnoreCase);

            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = onStopped
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public int Count => Volatile.Read(ref _count);

        private void onStopped(Activity activity)
        {
            // Npgsql 9 tags the command text as db.statement, Npgsql 10 as db.query.text.
            var sql = activity.GetTagItem("db.statement") as string ?? activity.GetTagItem("db.query.text") as string;
            if (sql != null && _selectFromTable.IsMatch(sql)) Interlocked.Increment(ref _count);
        }

        public void Dispose() => _listener.Dispose();
    }
}
