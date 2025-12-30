using System;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Weasel.Postgresql.Tables;

namespace DaemonTests.TeleHealth;

public class AppointmentDurationProjection : EventProjection
{
    public AppointmentDurationProjection()
    {
        // Defining an extra table so Marten
        // can manage it for us behind the scenes
        var table = new Table("appointment_duration");
        table.AddColumn<Guid>("id").AsPrimaryKey();
        table.AddColumn<DateTimeOffset>("start");
        table.AddColumn<DateTimeOffset>("end");

        SchemaObjects.Add(table);

        // This is to let Marten know that we want the data
        // in this table wiped out before doing a rebuild
        // of this projection
        Options.DeleteDataInTableOnTeardown(table.Identifier.QualifiedName);
    }

    public void Project(
        IEvent<AppointmentStarted> @event,
        IDocumentOperations ops)
    {
        var sql = "insert into appointment_duration "
                  + "(id, start) values (?, ?)";
        ops.QueueSqlCommand(sql,
            @event.Id,
            @event.Timestamp);
    }

    public void Project(
        IEvent<AppointmentCompleted> @event,
        IDocumentOperations ops)
    {
        var sql = "update appointment_duration "
                  + "set end = ? where id = ?";
        ops.QueueSqlCommand(sql,
            @event.Timestamp,
            @event.Id);
    }
}
