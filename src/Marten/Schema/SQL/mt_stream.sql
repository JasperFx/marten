

DROP TABLE IF EXISTS {databaseSchema}.mt_streams CASCADE;
CREATE TABLE {databaseSchema}.mt_streams (
	id					uuid CONSTRAINT pk_mt_streams PRIMARY KEY,
	type				varchar(100) NULL,
	version				integer NOT NULL,
	timestamp           timestamptz default (now() at time zone 'utc') NOT NULL,
	snapshot			jsonb,
	snapshot_version	integer	
);

DROP SEQUENCE IF EXISTS {databaseSchema}.mt_events_sequence;
CREATE SEQUENCE {databaseSchema}.mt_events_sequence;

DROP TABLE IF EXISTS {databaseSchema}.mt_events;
CREATE TABLE {databaseSchema}.mt_events (
    seq_id		    bigint CONSTRAINT pk_mt_events PRIMARY KEY,
	id 	uuid NOT NULL,
	stream_id	uuid REFERENCES {databaseSchema}.mt_streams ON DELETE CASCADE,
	version		integer NOT NULL,
	data		jsonb NOT NULL,
	type 		varchar(100) NOT NULL,
	timestamp	timestamp with time zone default (now()) NOT NULL,
	CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version),
	CONSTRAINT pk_mt_events_id_unique UNIQUE(id)
);

ALTER SEQUENCE {databaseSchema}.mt_events_sequence OWNED BY {databaseSchema}.mt_events.seq_id;


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




DROP TABLE IF EXISTS {databaseSchema}.mt_event_progression CASCADE;
CREATE TABLE {databaseSchema}.mt_event_progression (
	name				varchar CONSTRAINT pk_mt_event_progression PRIMARY KEY,
	last_seq_id			bigint NULL
);



CREATE OR REPLACE FUNCTION {databaseSchema}.mt_mark_event_progression(name varchar, last_encountered bigint) RETURNS VOID LANGUAGE plpgsql AS $function$
BEGIN
INSERT INTO {databaseSchema}.mt_event_progression (name, last_seq_id) VALUES (name, last_encountered)
  ON CONFLICT ON CONSTRAINT pk_mt_event_progression
  DO UPDATE SET last_seq_id = last_encountered;

END;
$function$;











