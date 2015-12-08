DROP TABLE IF EXISTS mt_streams CASCADE;
CREATE TABLE mt_streams (
	id					uuid CONSTRAINT pk_mt_streams PRIMARY KEY,
	type				varchar(100) NOT NULL,
	version				integer NOT NULL,
	snapshot			jsonb,
	snapshot_version	integer	
);


DROP TABLE IF EXISTS mt_events;
CREATE TABLE mt_events (
	id 			uuid CONSTRAINT pk_mt_events PRIMARY KEY,
	stream_id	uuid REFERENCES mt_streams ON DELETE CASCADE,
	version		integer NOT NULL,
	data		jsonb NOT NULL,
	type 		varchar(100) NOT NULL,
	timestamp	timestamp without time zone default (now() at time zone 'utc') NOT NULL,
	CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version)
);

DROP TABLE IF EXISTS mt_projections CASCADE;
CREATE TABLE mt_projections (
	name			varchar(100) CONSTRAINT pk_mt_projections PRIMARY KEY,
	definition		varchar(30000) NOT NULL
);

DROP TABLE IF EXISTS mt_modules CASCADE;
CREATE TABLE mt_modules (
	name			varchar(100) CONSTRAINT pk_mt_modules PRIMARY KEY,
	definition		varchar(30000) NOT NULL
);

CREATE OR REPLACE FUNCTION mt_version_stream(stream uuid, stream_type varchar) RETURNS int AS $$
DECLARE
  v_next int;
  v_now int;
BEGIN
  select version into v_now from mt_streams where id = stream;
  
  IF v_now IS NULL THEN
	v_next := 1;
	insert into mt_streams (id, type, version) values (stream, stream_type, v_next);
  ELSE
	v_next := v_now + 1;
	update mt_streams set version = v_next where id = stream;
  END IF; 

  RETURN v_next;
END
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION mt_append_event(stream uuid, stream_type varchar, event_id uuid, event_type varchar, body jsonb) RETURNS int AS $$
DECLARE
	event_version int;
BEGIN
	select mt_version_stream(stream, stream_type) into event_version;

	insert into mt_events (id, stream_id, version, data, type) values (event_id, stream, event_version, body, event_type);

	return event_version;
END
$$ LANGUAGE plpgsql;


CREATE OR REPLACE FUNCTION mt_load_projection_body(proj_name varchar, body varchar) RETURNS VOID AS $$
BEGIN
  delete from mt_projections where name = proj_name;
  insert into mt_projections (name, definition) values (proj_name, body);
END
$$ LANGUAGE plpgsql;






