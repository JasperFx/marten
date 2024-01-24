CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_path_to_array(text, char(1))
RETURNS text[] AS $$
DECLARE
    location ALIAS FOR $1;
    regex_pattern ALIAS FOR $2;
BEGIN
    RETURN regexp_split_to_array(location, regex_pattern)::text[];
END;
$$ LANGUAGE PLPGSQL;

