#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Schema;
using Weasel.Core;
using Weasel.Postgresql;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Marten.Events.Archiving.ObjectStorage;

/// <summary>
///     The view every archive-inclusive event query reads from: <c>mt_events</c> plus the archived
///     events in object storage.
/// </summary>
internal sealed class ArchiveInclusiveEventsView: ISchemaObject
{
    public const string ViewName = "mt_events_with_archive";

    private readonly EventGraph _events;
    private readonly IArchivedEventObjectStorage _storage;

    public ArchiveInclusiveEventsView(EventGraph events, IArchivedEventObjectStorage storage)
    {
        _events = events;
        _storage = storage;
        Identifier = new PostgresqlObjectName(events.DatabaseSchemaName, ViewName);
    }

    public DbObjectName Identifier { get; }

    internal static IReadOnlyList<ArchivedEventColumn> ColumnsOf(EventsTable table) =>
        table.Columns.Select(x => new ArchivedEventColumn(x.Name, x.Type)).ToList();

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        var table = new EventsTable(_events);
        var columns = ColumnsOf(table);
        foreach (var sql in _storage.InitializationSql(columns))
        {
            writer.WriteLine(sql + ";");
        }

        writer.WriteLine(
            $"CREATE OR REPLACE VIEW {Identifier.QualifiedName} AS {_storage.ArchiveInclusiveViewSql(table.Identifier.QualifiedName, columns)};");
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        writer.WriteLine($"DROP VIEW IF EXISTS {Identifier.QualifiedName};");
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        builder.Append("select 1 from pg_views where schemaname = ");
        builder.AppendParameter(Identifier.Schema);
        builder.Append(" and viewname = ");
        builder.AppendParameter(Identifier.Name);
        builder.Append(";");
    }

    public async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        var exists = await reader.ReadAsync(ct).ConfigureAwait(false);
        return new SchemaObjectDelta(this, exists ? SchemaPatchDifference.None : SchemaPatchDifference.Create);
    }

    public IEnumerable<DbObjectName> AllNames()
    {
        yield return Identifier;
    }
}
