using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Events.Daemon;
using Marten.Events.Archiving;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Events;

public partial class EventGraph: IFeatureSchema
{
    internal DbObjectName ProgressionTable => new PostgresqlObjectName(DatabaseSchemaName, "mt_event_progression");
    internal DbObjectName StreamsTable => new PostgresqlObjectName(DatabaseSchemaName, "mt_streams");


    IEnumerable<Type> IFeatureSchema.DependentTypes()
    {
        yield return typeof(DeadLetterEvent);
    }

    ISchemaObject[] IFeatureSchema.Objects => createAllSchemaObjects().ToArray();

    Type IFeatureSchema.StorageType => typeof(EventGraph);
    string IFeatureSchema.Identifier { get; } = "eventstore";
    Migrator IFeatureSchema.Migrator => Options.Advanced.Migrator;

    void IFeatureSchema.WritePermissions(Migrator rules, TextWriter writer)
    {
        // Nothing
    }

    private IEnumerable<ISchemaObject> createAllSchemaObjects()
    {
        yield return new StreamsTable(this);
        var eventsTable = new EventsTable(this);
        yield return eventsTable;

        #region sample_using-sequence

        var sequence = new Sequence(new PostgresqlObjectName(DatabaseSchemaName, "mt_events_sequence"))
        {
            Owner = eventsTable.Identifier, OwnerColumn = "seq_id"
        };

        #endregion

        yield return sequence;

        yield return new EventProgressionTable(this);

        yield return new SystemFunction(DatabaseSchemaName, "mt_mark_event_progression", "varchar, bigint");
        yield return new ArchiveStreamFunction(this);

        yield return new QuickAppendEventFunction(this);

        foreach (var schemaSource in Options.Projections.All.OfType<IProjectionSchemaSource>())
        {
            var objects = schemaSource.CreateSchemaObjects(this);
            foreach (var schemaObject in objects) yield return schemaObject;
        }
    }
}
