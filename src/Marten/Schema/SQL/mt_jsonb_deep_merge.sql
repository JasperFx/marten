CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_deep_merge(jsonb, jsonb)
    RETURNS jsonb
    LANGUAGE plpgsql
    IMMUTABLE
AS $function$
DECLARE
    target ALIAS FOR $1;
    source ALIAS FOR $2;
    retval jsonb;
BEGIN
    IF jsonb_typeof(target) IS DISTINCT FROM 'object' OR jsonb_typeof(source) IS DISTINCT FROM 'object' THEN
        RETURN source;
    END IF;

    SELECT coalesce(jsonb_object_agg(
        coalesce(t.key, s.key),
        CASE
            WHEN s.key IS NULL THEN t.value
            WHEN t.key IS NULL THEN s.value
            ELSE {databaseSchema}.mt_jsonb_deep_merge(t.value, s.value)
        END), '{}'::jsonb)
    INTO retval
    FROM jsonb_each(target) t FULL JOIN jsonb_each(source) s ON t.key = s.key;

    RETURN retval;
END;
$function$;
