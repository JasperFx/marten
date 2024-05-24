CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_path_to_array(text, character)
    RETURNS text[]
    LANGUAGE plpgsql
AS $function$
DECLARE
    location ALIAS FOR $1;
    regex_pattern ALIAS FOR $2;
BEGIN
RETURN regexp_split_to_array(location, regex_pattern)::text[];
END;
$function$;
