DROP TABLE IF EXISTS {databaseSchema}.mt_streams CASCADE;
CREATE TABLE {databaseSchema}.mt_streams (
	id					uuid CONSTRAINT pk_mt_streams PRIMARY KEY,
	type				varchar(100) NULL,
	version				integer NOT NULL,
	timestamp           timestamp without time zone default (now() at time zone 'utc') NOT NULL,
	snapshot			jsonb,
	snapshot_version	integer	
);


DROP TABLE IF EXISTS {databaseSchema}.mt_events;
CREATE TABLE {databaseSchema}.mt_events (
	id 			uuid CONSTRAINT pk_mt_events PRIMARY KEY,
	stream_id	uuid REFERENCES {databaseSchema}.mt_streams ON DELETE CASCADE,
	version		integer NOT NULL,
	data		jsonb NOT NULL,
	type 		varchar(100) NOT NULL,
	timestamp	timestamp without time zone default (now() at time zone 'utc') NOT NULL,
	CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version)
);


CREATE OR REPLACE FUNCTION {databaseSchema}.mt_append_event(stream uuid, stream_type varchar, event_ids uuid[], event_types varchar[], bodies jsonb[]) RETURNS int AS $$
DECLARE
	event_version int;
	event_type varchar;
	event_id uuid;
	body jsonb;
	index int;
BEGIN
	select version into event_version from {databaseSchema}.mt_streams where id = stream;
	if event_version IS NULL then
		event_version = 0;
		insert into {databaseSchema}.mt_streams (id, type, version, timestamp) values (stream, stream_type, 0, now());
	end if;


	index := 1;

	foreach event_id in ARRAY event_ids
	loop
	    event_version := event_version + 1;
		event_type = event_types[index];
		body = bodies[index];

		insert into {databaseSchema}.mt_events 
			(id, stream_id, version, data, type) 
		values 
			(event_id, stream, event_version, body, event_type);

		
		index := index + 1;
	end loop;

	update {databaseSchema}.mt_streams set version = event_version, timestamp = now() where id = stream;

	return event_version;
END
$$ LANGUAGE plpgsql;











