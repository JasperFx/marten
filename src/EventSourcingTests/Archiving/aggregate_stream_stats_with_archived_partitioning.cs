using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Archiving;

public record StatsBerthOpened;

public record StatsBerthUsed;

public class StatsBerth
{
    public Guid Id { get; set; }
    public int Uses { get; set; }

    public static StatsBerth Create(StatsBerthOpened _) => new();

    public void Apply(StatsBerthUsed _) => Uses++;
}

/// <summary>
///     marten#6152: under UseArchivedStreamPartitioning an archived stream's row moves to the archived partition of
///     mt_streams. FetchAggregateStreamStatsAsync reads the parent table, so it leaves those streams out by default and
///     counts them when IncludeArchived is set. The compliance suite cannot turn the partitioning on.
/// </summary>
public class aggregate_stream_stats_with_archived_partitioning: OneOffConfigurationsContext
{
    [Fact]
    public async Task archived_partition_counts_only_when_asked_for()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Projections.Snapshot<StatsBerth>(SnapshotLifecycle.Inline);
        });

        var live = theSession.Events.StartStream<StatsBerth>(new StatsBerthOpened(), new StatsBerthUsed()).Id;
        var archived = theSession.Events.StartStream<StatsBerth>(new StatsBerthOpened(), new StatsBerthUsed(),
            new StatsBerthUsed(), new StatsBerthUsed()).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.ArchiveStream(archived);
        await theSession.SaveChangesAsync();

        var database = (IEventDatabase)theStore.Storage.Database;

        var withoutArchived = (await database.FetchAggregateStreamStatsAsync(new AggregateStreamStatsRequest()))
            .ShouldHaveSingleItem();
        withoutArchived.AggregateType.ShouldBe(typeof(StatsBerth));
        withoutArchived.StreamCount.ShouldBe(1);
        withoutArchived.MaxVersion.ShouldBe(2);
        withoutArchived.LongestStreams.Single().Id.ShouldBe(live);

        var withArchived = (await database.FetchAggregateStreamStatsAsync(
            new AggregateStreamStatsRequest { IncludeArchived = true })).ShouldHaveSingleItem();
        withArchived.StreamCount.ShouldBe(2);
        withArchived.TotalEvents.ShouldBe(6);
        withArchived.LongestStreams.Select(x => x.Id).ShouldBe([archived, live]);
    }
}
