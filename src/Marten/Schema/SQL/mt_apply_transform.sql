CREATE OR REPLACE FUNCTION mt_apply_transform(stream_id uuid, event_id uuid, projection varchar, event_type varchar, event json) RETURNS json AS $$
	if (!plv8.transforms){
		plv8.execute('select mt_initialize_projections()');
	}

	return plv8.transforms.apply_transformation(projection, event_type, event, stream_id, event_id);

$$ LANGUAGE plv8;