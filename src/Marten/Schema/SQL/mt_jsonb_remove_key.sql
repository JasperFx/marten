CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_remove_key(jsonb, text[], text)
    RETURNS jsonb
    LANGUAGE plpgsql
AS $function$
DECLARE
    retval ALIAS FOR $1;
    location ALIAS FOR $2;
    _key ALIAS FOR $3;
    tmp_value jsonb;
    tmp_remove jsonb;
    patch_remove jsonb;
BEGIN
    tmp_value = retval #> location;
    IF tmp_value IS NOT NULL AND jsonb_typeof(tmp_value) = 'object' THEN
        RETURN jsonb_set(retval, location, tmp_value - _key, FALSE);
    END IF;
    RETURN retval;
END;
$function$;
