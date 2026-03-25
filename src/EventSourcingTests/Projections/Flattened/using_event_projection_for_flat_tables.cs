using System;
using JasperFx.Events;
using Marten;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Weasel.Postgresql.Tables;

namespace EventSourcingTests.Projections.Flattened;

#region sample_event_projection_flat_table_events

public record ImportStarted(
    DateTimeOffset Started,
    string ActivityType,
    string CustomerId,
    int PlannedSteps);

public record ImportProgress(
    string StepName,
    int Records,
    int Invalids);

public record ImportFinished(DateTimeOffset Finished);

public record ImportFailed;

#endregion

#region sample_import_sql_event_projection

public partial class ImportSqlProjection: EventProjection
{
    public ImportSqlProjection()
    {
        // Define the table structure here so that
        // Marten can manage this for us in its schema
        // management
        var table = new Table("import_history");
        table.AddColumn<Guid>("id").AsPrimaryKey();
        table.AddColumn<string>("activity_type").NotNull();
        table.AddColumn<string>("customer_id").NotNull();
        table.AddColumn<DateTimeOffset>("started").NotNull();
        table.AddColumn<DateTimeOffset>("finished");

        SchemaObjects.Add(table);

        // Telling Marten to delete the table data as the
        // first step in rebuilding this projection
        Options.DeleteDataInTableOnTeardown(table.Identifier);
    }

    // Use the IEvent<T> envelope to access event metadata
    // like stream identity and timestamps
    public void Project(IEvent<ImportStarted> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "insert into import_history (id, activity_type, customer_id, started) values (?, ?, ?, ?)",
            e.StreamId, e.Data.ActivityType, e.Data.CustomerId, e.Data.Started
        );
    }

    public void Project(IEvent<ImportFinished> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "update import_history set finished = ? where id = ?",
            e.Data.Finished, e.StreamId
        );
    }

    // You can use any SQL operation, including deletes
    public void Project(IEvent<ImportFailed> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "delete from import_history where id = ?",
            e.StreamId
        );
    }
}

#endregion
