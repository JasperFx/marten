CREATE OR REPLACE FUNCTION {databaseSchema}.mt_grams_array(words text)
        RETURNS text[]		
        LANGUAGE plpgsql
        IMMUTABLE STRICT
AS $function$
        DECLARE result text[];
        DECLARE word text;
        DECLARE clean_word text;
        BEGIN
                FOREACH word IN ARRAY string_to_array(words, ' ')
                LOOP
                     clean_word = regexp_replace(word, '[^a-zA-Z0-9]+', '','g');
                     FOR i IN 1 .. length(clean_word)
                     LOOP
                         result := result || quote_literal(substr(lower(clean_word), i, 1));
                         result := result || quote_literal(substr(lower(clean_word), i, 2));
                         result := result || quote_literal(substr(lower(clean_word), i, 3));
                     END LOOP;
                END LOOP;

                RETURN ARRAY(SELECT DISTINCT e FROM unnest(result) AS a(e) ORDER BY e);
        END;
$function$;