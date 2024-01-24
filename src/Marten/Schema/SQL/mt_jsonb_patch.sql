CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_patch(jsonb, jsonb)
    RETURNS jsonb
    LANGUAGE plpgsql
AS $function$
DECLARE
    retval ALIAS FOR $1;
    patch ALIAS FOR $2;
    patch_path text[];
    value jsonb;
BEGIN
    patch_path = {databaseSchema}.mt_jsonb_path_to_array((patch->>'path')::text, '\.');

    CASE patch->>'type'
        WHEN 'set' THEN
            retval = jsonb_set(retval, patch_path,(patch->'value')::jsonb, TRUE);
    WHEN 'delete' THEN
            retval = retval#-patch_path;
    WHEN 'append' THEN
            retval = {databaseSchema}.mt_jsonb_append(retval, patch_path,(patch->'value')::jsonb, FALSE);
    WHEN 'append_if_not_exists' THEN
            retval = {databaseSchema}.mt_jsonb_append(retval, patch_path,(patch->'value')::jsonb, TRUE);
    WHEN 'insert' THEN
            retval = {databaseSchema}.mt_jsonb_insert(retval, patch_path,(patch->'value')::jsonb,(patch->>'index')::integer, FALSE);
    WHEN 'insert_if_not_exists' THEN
            retval = {databaseSchema}.mt_jsonb_insert(retval, patch_path,(patch->'value')::jsonb,(patch->>'index')::integer, TRUE);
    WHEN 'remove' THEN
            retval = {databaseSchema}.mt_jsonb_remove(retval, patch_path,(patch->'value')::jsonb);
    WHEN 'duplicate' THEN
            retval = {databaseSchema}.mt_jsonb_duplicate(retval, patch_path,(patch->'targets')::jsonb);
    WHEN 'rename' THEN
            retval = {databaseSchema}.mt_jsonb_move(retval, patch_path,(patch->>'to')::text);
    WHEN 'increment' THEN
            retval = {databaseSchema}.mt_jsonb_increment(retval, patch_path,(patch->>'increment')::numeric);
    WHEN 'increment_float' THEN
            retval = {databaseSchema}.mt_jsonb_increment(retval, patch_path,(patch->>'increment')::numeric);
    ELSE NULL;
    END CASE;
    RETURN retval;
END;
$function$;
