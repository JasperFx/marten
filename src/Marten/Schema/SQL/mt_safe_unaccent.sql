CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_safe_unaccent(use_unaccent BOOLEAN, word TEXT)
        RETURNS TEXT
        LANGUAGE plpgsql
        IMMUTABLE STRICT
AS $function$
BEGIN
IF use_unaccent THEN
    RETURN unaccent(word);
ELSE
    RETURN word;
END IF;
END;
$function$;
