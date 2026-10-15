using System.IO;
using System.Linq;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;

namespace Marten.Events.Archiving;

internal class ArchiveStreamFunction: Function
{
    internal const string Name = "mt_archive_stream";

    private readonly EventGraph _events;

    public ArchiveStreamFunction(EventGraph events): base(new PostgresqlObjectName(events.DatabaseSchemaName, Name))
    {
        _events = events;
    }

    public override void WriteCreateStatement(Migrator rules, TextWriter writer)
    {
        var argList = _events.StreamIdentity == StreamIdentity.AsGuid
            ? "streamid uuid"
            : "streamid varchar";

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            argList += ", tenantid varchar";
        }

        var tenantWhere = _events.TenancyStyle == TenancyStyle.Conjoined ? " and tenant_id = tenantid" : "";

        if (_events.UseArchivedStreamPartitioning)
        {
            writeWithPartitioning(writer, argList, tenantWhere);
        }
        else
        {
            writeSimple(writer, argList, tenantWhere);
        }
    }

    private void writeWithPartitioning(TextWriter writer, string argList, string tenantWhere)
    {
        // #4619: emit an EXPLICIT column list on both the INSERT target AND the
        // SELECT source. The previous implementation relied on positional
        // ordering — the SELECT enumerated columns from the in-memory
        // EventsTable/StreamsTable model and the INSERT had no target column
        // list. That works on a freshly-created table where the model order
        // matches the physical order, but breaks the moment ALTER TABLE ADD
        // COLUMN appends a new column at the end of an existing table (PG
        // can't reposition columns), shifting the positional alignment by one.
        // The #4515 bdata column added at model position 3 reproduces this on
        // any upgrade: physical layout is `... data, type, timestamp, ...,
        // bdata` while the model + SELECT walks `... data, bdata, type,
        // timestamp, ...`, so the timestamptz `timestamp` column receives the
        // varchar `type` value → 42804. Naming the columns on both sides
        // makes the function robust to physical-order drift forever.
        var eventColumnNames = new EventsTable(_events).Columns
            .Where(x => x.Name != IsArchivedColumn.ColumnName)
            .Select(x => x.Name)
            .ToList();
        var eventColumnList = eventColumnNames.Join(", ");
        var eventInsertColumns = eventColumnNames
            .Append(IsArchivedColumn.ColumnName)
            .Join(", ");

        var streamColumnNames = new StreamsTable(_events).Columns
            .Where(x => x.Name != IsArchivedColumn.ColumnName)
            .Select(x => x.Name)
            .ToList();
        var streamColumnList = streamColumnNames.Join(", ");
        var streamInsertColumns = streamColumnNames
            .Append(IsArchivedColumn.ColumnName)
            .Join(", ");

        // Serialize archives of the same active stream before copying it. Filtering alone lets
        // concurrent calls select the same row and collide in the archived partition.
        //
        // #5675: the tag rows move with their events, and the ORDER of these statements is the whole fix.
        // Each mt_event_tag_* table is itself partitioned by is_archived and carries a composite foreign
        // key (seq_id, is_archived) to mt_events, which is NOT deferrable -- so every statement here has to
        // leave both sides of that key consistent on its own:
        //
        //   1. the archived event rows are inserted FIRST, so (seq_id, TRUE) exists to point at;
        //   2. then the archived tag rows, which reference it;
        //   3. then the original tag rows are deleted, BEFORE the events they still reference;
        //   4. and only then the original event rows.
        //
        // Before this, step 4 ran with the tag rows still pointing at (seq_id, FALSE), so archiving a
        // tagged stream came back as a foreign key violation on the delete.
        var archiveTags = tagMoves(archived: true);
        var deleteTags = tagMoves(archived: false);

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  perform 1 from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere} for update;
  insert into {_events.DatabaseSchemaName}.mt_streams ({streamInsertColumns}) select {streamColumnList}, TRUE from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
  insert into {_events.DatabaseSchemaName}.mt_events ({eventInsertColumns}) select {eventColumnList}, TRUE from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};{archiveTags}{deleteTags}
  delete from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
  delete from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
END;
$function$;
");
    }

    /// <summary>
    ///     The statements that copy each tag table's rows into its archived partition
    ///     (<paramref name="archived" /> true) or remove the originals (false). Empty when there are no tag
    ///     tables to move — no registered tag types, or HStore mode, where tags live inline on
    ///     <c>mt_events.tags</c> and travel with the row.
    /// </summary>
    /// <remarks>
    ///     The column list is explicit for the same reason #4619 made the event and stream inserts explicit:
    ///     an ALTER TABLE ADD COLUMN on an existing tag table appends the column physically, and a
    ///     positional insert would then silently load the wrong column.
    /// </remarks>
    private string tagMoves(bool archived)
    {
        if (_events.DcbStorageMode == DcbStorageMode.HStore) return "";

        var schema = _events.DatabaseSchemaName;
        var isConjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var writer = new StringWriter();

        foreach (var registration in _events.TagTypes)
        {
            var table = $"{schema}.mt_event_tag_{registration.TableSuffix}";

            // Aliased because both the tag table and mt_events carry tenant_id under conjoined tenancy,
            // and the subquery would otherwise resolve the outer predicate's column to the inner table.
            var tagTenantWhere = isConjoined ? " and t.tenant_id = tenantid" : "";
            var ofThisStream =
                $"t.seq_id in (select e.seq_id from {schema}.mt_events e where e.stream_id = streamid and e.{IsArchivedColumn.ColumnName} = FALSE{(isConjoined ? " and e.tenant_id = tenantid" : "")})";

            if (archived)
            {
                var columns = isConjoined
                    ? $"value, tenant_id, seq_id, {IsArchivedColumn.ColumnName}"
                    : $"value, seq_id, {IsArchivedColumn.ColumnName}";
                var source = isConjoined ? "t.value, t.tenant_id, t.seq_id" : "t.value, t.seq_id";

                writer.WriteLine();
                writer.Write(
                    $"  insert into {table} ({columns}) select {source}, TRUE from {table} t where {ofThisStream} and t.{IsArchivedColumn.ColumnName} = FALSE{tagTenantWhere};");
            }
            else
            {
                writer.WriteLine();
                writer.Write(
                    $"  delete from {table} t where {ofThisStream} and t.{IsArchivedColumn.ColumnName} = FALSE{tagTenantWhere};");
            }
        }

        return writer.ToString();
    }

    private void writeSimple(TextWriter writer, string argList, string tenantWhere)
    {
        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  update {_events.DatabaseSchemaName}.mt_streams set {IsArchivedColumn.ColumnName} = TRUE where id = streamid {tenantWhere};
  update {_events.DatabaseSchemaName}.mt_events set is_archived = TRUE where stream_id = streamid {tenantWhere};
END;
$function$;
");
    }
}
