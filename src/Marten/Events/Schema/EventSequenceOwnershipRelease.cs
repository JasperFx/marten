#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Core;
using Weasel.Postgresql;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;
using DbDataReader = System.Data.Common.DbDataReader;

namespace Marten.Events.Schema;

/// <summary>
///     #5812. Marten 9 and earlier created <c>mt_events_sequence</c> as <c>OWNED BY mt_events.seq_id</c>. When the
///     shape of <c>mt_events</c> changes in a way PostgreSQL cannot alter in place, most notably turning on
///     <c>UseArchivedStreamPartitioning</c> (the 10.0 default) for an existing event store, Weasel rebuilds the
///     table with <c>DROP TABLE ... CASCADE</c>, which drops an owned sequence along with it. The rebuilt table then
///     has no sequence to append with, and recreating one would restart the numbering under the existing events.
/// </summary>
/// <remarks>
///     The sequence is no longer created with an owner, and this object comes first in the event store's schema, so
///     a database created by an older Marten has the ownership released before any event table is rebuilt in the
///     same migration. It only reports a change while the sequence is still owned by a column.
/// </remarks>
internal class EventSequenceOwnershipRelease: ISchemaObject
{
    private readonly PostgresqlObjectName _sequence;

    public EventSequenceOwnershipRelease(string schemaName)
    {
        _sequence = new PostgresqlObjectName(schemaName, "mt_events_sequence");
        Identifier = new PostgresqlObjectName(schemaName, "mt_events_sequence_ownership");
    }

    public DbObjectName Identifier { get; }

    public void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
        writer.WriteLine($"ALTER SEQUENCE IF EXISTS {_sequence} OWNED BY NONE;");
    }

    public void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        // Nothing to drop: this only ever releases the old ownership
    }

    public void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        builder.Append(
            "select 1 from pg_depend d join pg_class s on s.oid = d.objid join pg_namespace n on n.oid = s.relnamespace " +
            "where s.relkind = 'S' and d.deptype = 'a' and d.refobjsubid > 0 and n.nspname = ");
        builder.AppendParameter(_sequence.Schema);
        builder.Append(" and s.relname = ");
        builder.AppendParameter(_sequence.Name);
        builder.Append(";");
    }

    public async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        var stillOwned = await reader.ReadAsync(ct).ConfigureAwait(false);
        return new SchemaObjectDelta(this, stillOwned ? SchemaPatchDifference.Create : SchemaPatchDifference.None);
    }

    public IEnumerable<DbObjectName> AllNames()
    {
        yield return Identifier;
    }
}
