using System.IO;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events
{
	// SAMPLE: AppendEventFunction
    public class AppendEventFunction : Function
    {
        private readonly EventGraph _events;

        public AppendEventFunction(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_append_event"))
        {
            _events = events;
        }

        public override void Write(DdlRules rules, StringWriter writer)
        {
            var streamIdType = _events.GetStreamIdType();
            var databaseSchema = _events.DatabaseSchemaName;

            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier}(stream {streamIdType}, stream_type varchar, tenantid varchar, event_ids uuid[], event_types varchar[], dotnet_types varchar[], bodies jsonb[]) RETURNS int[] AS $$
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
	select version into event_version from {databaseSchema}.mt_streams where id = stream;
	if event_version IS NULL then
		event_version = 0;
		insert into {databaseSchema}.mt_streams (id, type, version, timestamp, tenant_id) values (stream, stream_type, 0, now(), tenantid);
    else
        if tenantid IS NOT NULL then
            select tenant_id into actual_tenant from {databaseSchema}.mt_streams where id = stream;
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
			(seq_id, id, stream_id, version, data, type, tenant_id, {DocumentMapping.DotNetTypeColumn}) 
		values 
			(seq, event_id, stream, event_version, body, event_type, tenantid, dotnet_types[index]);

		
		index := index + 1;
	end loop;

	update {databaseSchema}.mt_streams set version = event_version, timestamp = now() where id = stream;

	return return_value;
END
$$ LANGUAGE plpgsql;
");
        }

        protected override string toDropSql()
        {
            var streamIdType = _events.GetStreamIdType();
            return $"drop function if exists {Identifier} ({streamIdType}, varchar, varchar, uuid[], varchar[], jsonb[])";
        }
    }
	// ENDSAMPLE
}