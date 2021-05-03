CREATE OR REPLACE FUNCTION {databaseSchema}.mt_grams_query(text)
        RETURNS tsquery		
        LANGUAGE plpgsql
        IMMUTABLE STRICT
AS $function$
BEGIN
        RETURN (SELECT array_to_string(mt_grams_array($1), ' & ')::tsquery);
END
$function$;