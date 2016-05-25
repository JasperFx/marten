CREATE OR REPLACE FUNCTION {databaseSchema}.mt_apply_transform(stream_id uuid, event_id uuid, projection varchar, event_type varchar, event json) RETURNS json AS $$
	if (!plv8.projections){
		plv8.execute('select {databaseSchema}.mt_initialize_projections()');
	}

	return plv8.projections.apply_transformation(projection, event_type, event, stream_id, event_id);

$$ LANGUAGE plv8;