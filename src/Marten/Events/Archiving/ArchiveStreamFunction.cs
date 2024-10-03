using System.IO;
using System.Linq;
using JasperFx.Core;
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
        var eventColumns = new EventsTable(_events).Columns.Where(x => x.Name != IsArchivedColumn.ColumnName)
            .Select(x => x.Name).Join(", ");

        var streamColumns = new StreamsTable(_events).Columns.Where(x => x.Name != IsArchivedColumn.ColumnName)
            .Select(x => x.Name).Join(", ");

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  insert into {_events.DatabaseSchemaName}.mt_streams select {streamColumns}, TRUE from {_events.DatabaseSchemaName}.mt_streams where id = streamid {tenantWhere};
  insert into {_events.DatabaseSchemaName}.mt_events select {eventColumns}, TRUE from {_events.DatabaseSchemaName}.mt_events where stream_id = streamid {tenantWhere};
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
