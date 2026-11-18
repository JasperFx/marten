#nullable enable
using System.IO;
using System.Linq;
using JasperFx.Core;
using Marten.Events.Archiving;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

/// <summary>
///     #6081: the migration of a DCB tag table created without archived stream partitioning (Marten 9's default)
///     to one with it (Marten 10's default). The table gains <c>is_archived</c>, is partitioned by it, and its
///     foreign key to <c>mt_events</c> moves to <c>(seq_id, is_archived)</c>.
/// </summary>
/// <remarks>
///     <para>
///         Weasel's own rebuild cannot do this. It adds <c>is_archived</c> as <c>false</c> on every row, so a tag
///         row of an archived event no longer matches its event and the copy back fails the new foreign key.
///         This rebuild takes each row's <c>is_archived</c> from its event.
///     </para>
///     <para>
///         It is deliberately not a <see cref="TableDelta" />. <c>mt_events</c> is rebuilt earlier in the same
///         migration, and the <see cref="TableDelta" /> of a table whose primary key changes adds back the expected
///         foreign key of every other <see cref="TableDelta" /> that references it. For a tag table that foreign
///         key names <c>is_archived</c>, which the table does not have yet (42703). This delta adds its foreign
///         key itself, after the column is there.
///     </para>
/// </remarks>
internal class EventTagTableArchivingUpgrade: ISchemaObjectDelta
{
    private readonly EventTagTable _expected;
    private readonly Table _actual;
    private readonly string _eventsTable;

    public EventTagTableArchivingUpgrade(EventTagTable expected, Table actual, string schemaName)
    {
        _expected = expected;
        _actual = actual;
        _eventsTable = new PostgresqlObjectName(schemaName, "mt_events").QualifiedName;
    }

    /// <summary>
    ///     True when <paramref name="actual" /> is a tag table from before archived stream partitioning, which is
    ///     the only table this delta rebuilds.
    /// </summary>
    public static bool Applies(Table? actual)
        => actual != null && actual.Partitioning == null &&
           !actual.Columns.Any(x => x.Name.EqualsIgnoreCase(IsArchivedColumn.ColumnName));

    public ISchemaObject SchemaObject => _expected;

    public SchemaPatchDifference Difference => SchemaPatchDifference.Update;

    public void WriteUpdate(Migrator rules, TextWriter writer)
    {
        var table = _expected.Identifier.QualifiedName;
        var temp = new PostgresqlObjectName(_expected.Identifier.Schema, _expected.Identifier.Name + "_temp")
            .QualifiedName;

        // Held to the end of the transaction the command runs in. Inside a DO block, so it works whether or not
        // the migration opened a transaction block, which a top level LOCK TABLE needs.
        writer.WriteLine($"DO $$ BEGIN LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE; END $$;");

        var existing = _actual.Columns.Select(x => $"t.{x.Name}").Join(", ");
        writer.WriteLine(
            $"create table {temp} as select {existing}, exists (select 1 from {_eventsTable} e where e.seq_id = t.seq_id and e.{IsArchivedColumn.ColumnName}) as {IsArchivedColumn.ColumnName} from {table} t;");
        writer.WriteLine($"drop table {table} cascade;");

        _expected.WriteCreateStatement(rules, writer);
        writer.WriteLine();

        var columns = _expected.Columns.Select(x => x.Name).Join(", ");
        writer.WriteLine($"insert into {table}({columns}) select {columns} from {temp};");
        writer.WriteLine($"drop table {temp} cascade;");
    }

    public void WriteRollback(Migrator rules, TextWriter writer)
    {
        // A rebuild has no incremental rollback, the same as Weasel's partitioning rebuild
    }

    public void WriteRestorationOfPreviousState(Migrator rules, TextWriter writer)
    {
        _actual.WriteCreateStatement(rules, writer);
    }

    public override string ToString() => $"{_expected.Identifier} (archived stream partitioning upgrade, #6081)";
}
