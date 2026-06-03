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

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  insert into {_events.DatabaseSchemaName}.mt_streams ({streamInsertColumns}) select {streamColumnList}, TRUE from {_events.DatabaseSchemaName}.mt_streams where id = streamid {tenantWhere};
  insert into {_events.DatabaseSchemaName}.mt_events ({eventInsertColumns}) select {eventColumnList}, TRUE from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid {tenantWhere};
  delete from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
  delete from {_events.DatabaseSchemaName}.mt_streams where id = streamid and {IsArchivedColumn.ColumnName} = FALSE {tenantWhere};
END;
$function$;
");
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
