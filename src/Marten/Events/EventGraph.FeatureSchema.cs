using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Tags;
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

    // 9.0: cache the schema-object enumeration (#4304). Construction iterates
    // every registered projection, every aggregate's NaturalKeyDefinition, and
    // every tag registration — work that's only meaningful for full-schema
    // operations like ApplyAllConfiguredChangesToDatabaseAsync or the CLI
    // tools, but the IFeatureSchema.Objects getter is reachable from
    // dependency-graph traversal at boot. Compute once on first read.
    private Lazy<ISchemaObject[]>? _objectsCache;

    IEnumerable<Type> IFeatureSchema.DependentTypes()
    {
        yield return typeof(DeadLetterEvent);
    }

    ISchemaObject[] IFeatureSchema.Objects =>
        (_objectsCache ??= new Lazy<ISchemaObject[]>(
            () => createAllSchemaObjects().ToArray(),
            LazyThreadSafetyMode.ExecutionAndPublication))
        .Value;

    Type IFeatureSchema.StorageType => typeof(EventGraph);
    string IFeatureSchema.Identifier { get; } = "eventstore";
    Migrator IFeatureSchema.Migrator => Options.Advanced.Migrator;

    void IFeatureSchema.WritePermissions(Migrator rules, TextWriter writer)
    {
        // Nothing
    }

    private IEnumerable<ISchemaObject> createAllSchemaObjects()
    {
        // DCB in HStore mode requires the Postgres hstore extension. Register it before
        // any table that depends on the hstore type is created.
        if (DcbStorageMode == DcbStorageMode.HStore && _tagTypes.Count > 0)
        {
            yield return new Extension("hstore");
        }

        yield return new StreamsTable(this);

        if (EnableStrictStreamIdentityEnforcement)
        {
            // Non-partitioned tracking table for cross-partition stream-identity
            // uniqueness. See StreamIdentityEnforcementTable for the rationale.
            yield return new StreamIdentityEnforcementTable(this);
        }

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

        if (EnableExtendedProgressionTracking)
        {
            yield return new SystemFunction(DatabaseSchemaName, "mt_mark_event_progression_extended",
                "varchar, bigint, timestamp with time zone, varchar, text, integer");
        }
        yield return new ArchiveStreamFunction(this);

        yield return new QuickAppendEventFunction(this);

        foreach (var schemaSource in Options.Projections.All.OfType<IProjectionSchemaSource>())
        {
            var objects = schemaSource.CreateSchemaObjects(this);
            foreach (var schemaObject in objects) yield return schemaObject;
        }

        // Natural key tables for aggregates with NaturalKeyDefinition
        foreach (var aggregate in Options.Projections.All.OfType<IAggregateProjection>())
        {
            if (aggregate.NaturalKeyDefinition != null)
            {
                yield return new NaturalKeyTable(this, aggregate.NaturalKeyDefinition);
            }
        }

        if (EnableAdvancedAsyncTracking)
        {
            yield return new EventProgressionSkippingTable(this);
            yield return new SystemFunction(DatabaseSchemaName, "mt_mark_progression_with_skip",
                "varchar, bigint, bigint");
        }

        // In HStore mode tags live inline on mt_events.tags; no per-type tag tables exist.
        if (DcbStorageMode != DcbStorageMode.HStore)
        {
            foreach (var tagRegistration in _tagTypes)
            {
                yield return new EventTagTable(this, tagRegistration);
            }
        }
    }
}
