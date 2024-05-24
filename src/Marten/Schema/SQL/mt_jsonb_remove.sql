CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_remove(jsonb, text[], jsonb)
    RETURNS jsonb
    LANGUAGE plpgsql
AS $function$
DECLARE
    retval ALIAS FOR $1;
    location ALIAS FOR $2;
    val ALIAS FOR $3;
    tmp_value jsonb;
BEGIN
    tmp_value = retval #> location;
    IF tmp_value IS NOT NULL AND jsonb_typeof(tmp_value) = 'array' THEN
        tmp_value =(SELECT jsonb_agg(elem)
        FROM jsonb_array_elements(tmp_value) AS elem
        WHERE elem <> val);

        IF tmp_value IS NULL THEN
            tmp_value = '[]'::jsonb;
        END IF;
    END IF;
    RETURN jsonb_set(retval, location, tmp_value, FALSE);
END;
$function$;
