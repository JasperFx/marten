CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_grams_query(text, use_unaccent boolean DEFAULT false)
        RETURNS tsquery
        LANGUAGE plpgsql
        IMMUTABLE STRICT
AS $function$
BEGIN
RETURN (SELECT array_to_string({databaseSchema}.mt_grams_array($1, use_unaccent), ' & ') ::tsquery);
END
$function$;
