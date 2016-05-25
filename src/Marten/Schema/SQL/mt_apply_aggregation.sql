CREATE OR REPLACE FUNCTION {databaseSchema}.mt_apply_aggregation(stream_id uuid, event_id uuid, projection varchar, event_type varchar, event json, aggregate json) RETURNS json AS $$
	if (!plv8.projections){
		plv8.execute('select {databaseSchema}.mt_initialize_projections()');
	}

	return plv8.projections.apply_aggregation(projection, event_type, event, aggregate, stream_id, event_id);

$$ LANGUAGE plv8;


CREATE OR REPLACE FUNCTION {databaseSchema}.mt_start_aggregation(stream_id uuid, event_id uuid, projection varchar, event_type varchar, event json) RETURNS json AS $$
	if (!plv8.projections){
		plv8.execute('select {databaseSchema}.mt_initialize_projections()');
	}

	return plv8.projections.apply_aggregation(projection, event_type, event, null, stream_id, event_id);

$$ LANGUAGE plv8;