using System.IO;
using System.Linq;
using JasperFx.Events;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql.Functions;

namespace Marten.Events.Schema;

    public class QuickAppendEventFunction : Function
    {
        private readonly EventGraph _events;

        public QuickAppendEventFunction(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_quick_append_events"))
        {
            _events = events;
        }

        public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
        {
            var streamIdType = _events.GetStreamIdDBType();
            var databaseSchema = _events.DatabaseSchemaName;

            var tenancyStyle = _events.TenancyStyle;

            var streamsWhere = "id = stream";

            if (tenancyStyle == TenancyStyle.Conjoined)
            {
                streamsWhere += " AND tenant_id = tenantid";
            }

            var table = new EventsTable(_events);
            var metadataColumns = "";
            var metadataParameters = "";
            var metadataValues = "";

            if (table.Columns.OfType<CausationIdColumn>().Any())
            {
                metadataColumns += ", " + CausationIdColumn.ColumnName;
                metadataParameters += ", causation_ids varchar[]";
                metadataValues += ", causation_ids[index]";
            }

            if (table.Columns.OfType<CorrelationIdColumn>().Any())
            {
                metadataColumns += ", " + CorrelationIdColumn.ColumnName;
                metadataParameters += ", correlation_ids varchar[]";
                metadataValues += ", correlation_ids[index]";
            }

            if (table.Columns.OfType<HeadersColumn>().Any())
            {
                metadataColumns += ", " + HeadersColumn.ColumnName;
                metadataParameters += ", headers jsonb[]";
                metadataValues += ", headers[index]";
            }

            var timestampValue = "(now() at time zone 'utc')";
            if (_events.AppendMode == EventAppendMode.QuickWithServerTimestamps)
            {
                timestampValue = "timestamps[index]";
                metadataParameters += ", timestamps timestamptz[]";
            }


            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier}(stream {streamIdType}, stream_type varchar, tenantid varchar, event_ids uuid[], event_types varchar[], dotnet_types varchar[], bodies jsonb[]{metadataParameters}) RETURNS int[] AS $$
DECLARE
	event_version int;
	event_type varchar;
	event_id uuid;
	body jsonb;
	index int;
	seq int;
    actual_tenant varchar;
	return_value int[];
BEGIN
	select version into event_version from {databaseSchema}.mt_streams where {streamsWhere};
	if event_version IS NULL then
		event_version = 0;
		insert into {databaseSchema}.mt_streams (id, type, version, timestamp, tenant_id) values (stream, stream_type, 0, now(), tenantid);
    else
        if tenantid IS NOT NULL then
            select tenant_id into actual_tenant from {databaseSchema}.mt_streams where {streamsWhere};
            if actual_tenant != tenantid then
                RAISE EXCEPTION 'The tenantid does not match the existing stream';
            end if;
        end if;
	end if;

	index := 1;
	return_value := ARRAY[event_version + array_length(event_ids, 1)];

	foreach event_id in ARRAY event_ids
	loop
	    seq := nextval('{databaseSchema}.mt_events_sequence');
		return_value := array_append(return_value, seq);

	    event_version := event_version + 1;
		event_type = event_types[index];
		body = bodies[index];

		insert into {databaseSchema}.mt_events
			(seq_id, id, stream_id, version, data, type, tenant_id, timestamp, {SchemaConstants.DotNetTypeColumn}, is_archived{metadataColumns})
		values
			(seq, event_id, stream, event_version, body, event_type, tenantid, {timestampValue}, dotnet_types[index], FALSE{metadataValues});

		index := index + 1;
	end loop;

	update {databaseSchema}.mt_streams set version = event_version, timestamp = now() where {streamsWhere};

	return return_value;
END
$$ LANGUAGE plpgsql;
");
        }

    }
