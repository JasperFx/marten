using System;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;
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

    #region sample_using_Logger_in_projections

    // If you have to be all special and want to group the logging
    // your own way, just override this method:
    public override void AttachLogger(ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger<MyLoggingMarkerType>();
    }

    public void Project(
        IEvent<AppointmentStarted> @event,
        IDocumentOperations ops)
    {
        // Outside of AddMarten() usage, this would be a NullLogger
        // Inside of an app bootstrapped as an IHost with standard .NET
        // logging registered and Marten bootstrapped through AddMarten(),
        // Logger would be an ILogger<T> *by default* where T is the concrete
        // type of the actual projection
        Logger?.LogDebug("Hey, I'm inserting a row for appointment started");

        var sql = "insert into appointment_duration "
                  + "(id, start) values (?, ?)";
        ops.QueueSqlCommand(sql,
            @event.Id,
            @event.Timestamp);
    }

    #endregion

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

public class MyLoggingMarkerType
{
}
