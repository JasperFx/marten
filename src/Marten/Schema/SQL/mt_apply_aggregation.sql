CREATE OR REPLACE FUNCTION mt_apply_aggregation(stream_id uuid, event_id uuid, projection varchar, event_type varchar, event jsonb, aggregate jsonb) RETURNS jsonb AS $$
	if (!plv8.transforms){
		plv8.execute('select mt_initialize_projections()');
	}

	return plv8.transforms.apply_aggregation(projection, event_type, event, aggregate, stream_id, event_id);

$$ LANGUAGE plv8;