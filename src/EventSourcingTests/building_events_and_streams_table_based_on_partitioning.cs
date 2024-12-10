using System.Linq;
using Marten;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace EventSourcingTests;

public class building_events_and_streams_table_based_on_partitioning
{
    private readonly EventGraph theGraph = new EventGraph(new StoreOptions());

    [Fact]
    public void no_partitioning_by_default()
    {
        new EventsTable(theGraph).Partitioning.ShouldBeNull();
        new StreamsTable(theGraph).Partitioning.ShouldBeNull();
    }

    [Fact]
    public void events_table_build_partitioning_when_active()
    {
        theGraph.UseArchivedStreamPartitioning = true;

        var table = new EventsTable(theGraph);
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.Single().ShouldBe(IsArchivedColumn.ColumnName);
        partitioning.Partitions.Single().ShouldBe(new ListPartition("archived", "true"));

        table.PrimaryKeyColumns.ShouldContain(IsArchivedColumn.ColumnName);
    }

    [Fact]
    public void streams_table_build_partitioning_when_active()
    {
        theGraph.UseArchivedStreamPartitioning = true;

        var table = new StreamsTable(theGraph);
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.Single().ShouldBe(IsArchivedColumn.ColumnName);
        partitioning.Partitions.Single().ShouldBe(new ListPartition("archived", "true"));

        table.PrimaryKeyColumns.ShouldContain(IsArchivedColumn.ColumnName);
    }
}
