using System;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Aggregation.Rebuilds;

internal class AggregateRebuildTable: Table
{
    public const string Name = "mt_aggregate_rebuild";

    public AggregateRebuildTable(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, Name))
    {
        AddColumn("number", "serial").AsPrimaryKey();

        if (events.StreamIdentity == StreamIdentity.AsGuid)
        {
            AddColumn<Guid>("id").NotNull();
        }
        else
        {
            AddColumn<string>("id").NotNull();
        }

        AddColumn<string>("stream_type").NotNull();
        AddColumn<string>(TenantIdColumn.Name).NotNull();
        AddColumn<bool>("completed");
    }
}