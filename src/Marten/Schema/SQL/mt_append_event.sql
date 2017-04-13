CREATE OR REPLACE FUNCTION {databaseSchema}.mt_append_event(stream uuid, stream_type varchar, event_ids uuid[], event_types varchar[], bodies jsonb[]) RETURNS int[] AS $$
DECLARE
	event_version int;
	event_type varchar;
	event_id uuid;
	body jsonb;
	index int;
	seq int;
	return_value int[];
BEGIN
	select version into event_version from {databaseSchema}.mt_streams where id = stream;
	if event_version IS NULL then
		event_version = 0;
		insert into {databaseSchema}.mt_streams (id, type, version, timestamp) values (stream, stream_type, 0, now());
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
			(seq_id, id, stream_id, version, data, type) 
		values 
			(seq, event_id, stream, event_version, body, event_type);

		
		index := index + 1;
	end loop;

	update {databaseSchema}.mt_streams set version = event_version, timestamp = now() where id = stream;

	return return_value;
END
$$ LANGUAGE plpgsql;
