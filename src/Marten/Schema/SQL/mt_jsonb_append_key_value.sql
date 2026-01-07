CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_append_key_value(jsonb, text[], jsonb, boolean)
    RETURNS jsonb
    LANGUAGE plpgsql
AS $function$
DECLARE
    retval ALIAS FOR $1;
    location ALIAS FOR $2;
    val ALIAS FOR $3;
    if_not_exists ALIAS FOR $4;
    tmp_value jsonb;
    _key text;
BEGIN
    tmp_value = retval #> location;

    IF tmp_value IS NOT NULL AND jsonb_typeof(tmp_value) = 'object' THEN
        CASE
            WHEN NOT if_not_exists THEN
                retval = jsonb_set(retval, location, tmp_value || val, FALSE);
            WHEN NOT tmp_value ?| ARRAY(SELECT jsonb_object_keys(val)) THEN
                retval = jsonb_set(retval, location, tmp_value || val, FALSE);
            ELSE NULL;
        END CASE;
    END IF;
    RETURN retval;
END;
$function$;
