CREATE OR REPLACE FUNCTION public.mt_immutable_timestamp(value text) RETURNS TIMESTAMP IMMUTABLE AS $$
    select value::timestamptz at time zone 'utc'
$$ LANGUAGE SQL;