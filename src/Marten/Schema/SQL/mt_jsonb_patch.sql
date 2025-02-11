CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_patch(jsonb, jsonb)
    RETURNS jsonb
    LANGUAGE plpgsql
AS $function$
DECLARE
    retval ALIAS FOR $1;
    patchset ALIAS FOR $2;
    patch jsonb;
    patch_path text[];
    patch_values jsonb;
    patch_value jsonb;
    value jsonb;
BEGIN
    FOR patch IN SELECT * from jsonb_array_elements(patchset)
    LOOP
        patch_path = {databaseSchema}.mt_jsonb_path_to_array((patch->>'path')::text, '\.');

        IF (patch->>'type') IN ('remove') AND (patch->>'expression') IS NOT NULL THEN
            patch_values = jsonb_path_query_array(retval->(patch->>'path'), (patch->>'expression')::jsonpath);
        ELSE
            patch_values = jsonb_build_array((patch->'value')::jsonb);
        END IF;

        FOR patch_value IN SELECT * FROM jsonb_array_elements(patch_values)
        LOOP
            CASE patch->>'type'
                WHEN 'set' THEN
                    retval = jsonb_set(retval, patch_path, patch_value, TRUE);
                WHEN 'delete' THEN
                    retval = retval#-patch_path;
                WHEN 'append' THEN
                    retval = {databaseSchema}.mt_jsonb_append(retval, patch_path, patch_value, FALSE);
                WHEN 'append_if_not_exists' THEN
                    retval = {databaseSchema}.mt_jsonb_append(retval, patch_path, patch_value, TRUE);
                WHEN 'insert' THEN
                    retval = {databaseSchema}.mt_jsonb_insert(retval, patch_path, patch_value, (patch->>'index')::integer, FALSE);
                WHEN 'insert_if_not_exists' THEN
                    retval = {databaseSchema}.mt_jsonb_insert(retval, patch_path, patch_value, (patch->>'index')::integer, TRUE);
                WHEN 'remove' THEN
                    retval = {databaseSchema}.mt_jsonb_remove(retval, patch_path, patch_value);
                WHEN 'duplicate' THEN
                    retval = {databaseSchema}.mt_jsonb_duplicate(retval, patch_path, (patch->'targets')::jsonb);
                WHEN 'rename' THEN
                    retval = {databaseSchema}.mt_jsonb_move(retval, patch_path, (patch->>'to')::text);
                WHEN 'increment' THEN
                    retval = {databaseSchema}.mt_jsonb_increment(retval, patch_path, (patch->>'increment')::numeric);
                WHEN 'increment_float' THEN
                    retval = {databaseSchema}.mt_jsonb_increment(retval, patch_path, (patch->>'increment')::numeric);
                ELSE NULL;
            END CASE;
        END LOOP;
    END LOOP;
    RETURN retval;
END;
$function$;
