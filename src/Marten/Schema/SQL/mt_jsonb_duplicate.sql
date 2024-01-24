CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_duplicate(jsonb, text[], jsonb)
RETURNS jsonb AS $$
DECLARE
    retval ALIAS FOR $1;
    location ALIAS FOR $2;
    targets ALIAS FOR $3;
    tmp_value jsonb;
    target_path text[];
    target text;
BEGIN
    FOR target IN SELECT jsonb_array_elements_text(targets)
    LOOP
        target_path = {databaseSchema}.mt_jsonb_path_to_array(target);
        retval = {databaseSchema}.mt_jsonb_copy(retval, location, target_path);
    END LOOP;

    RETURN retval;
END;
$$ LANGUAGE PLPGSQL;
