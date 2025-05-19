CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_grams_vector(text, use_unaccent boolean DEFAULT false)
        RETURNS tsvector
        LANGUAGE plpgsql
        IMMUTABLE STRICT
AS $function$
BEGIN
RETURN (SELECT array_to_string({databaseSchema}.mt_grams_array($1, use_unaccent), ' ') ::tsvector);
END
$function$;
