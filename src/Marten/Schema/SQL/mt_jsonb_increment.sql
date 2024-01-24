CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_increment(jsonb, text[], numeric)
RETURNS jsonb AS $$
DECLARE
    retval ALIAS FOR $1;
    location ALIAS FOR $2;
    increment_value ALIAS FOR $3;
    tmp_value jsonb;
BEGIN
    tmp_value = retval #> location;
    IF tmp_value IS NULL THEN
        tmp_value = to_jsonb(0);
    END IF;

    RETURN jsonb_set(retval, location, to_jsonb(tmp_value::numeric + increment_value), TRUE);
END;
$$ LANGUAGE PLPGSQL;
