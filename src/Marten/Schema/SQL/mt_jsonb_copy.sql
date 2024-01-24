CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_copy(jsonb, text[], text[])
    RETURNS jsonb
    LANGUAGE plpgsql
AS $function$
DECLARE
    retval ALIAS FOR $1;
    src_path ALIAS FOR $2;
    dst_path ALIAS FOR $3;
    tmp_value jsonb;
BEGIN
    tmp_value = retval #> src_path;
    retval = {databaseSchema}.mt_jsonb_fix_null_parent(retval, dst_path);
    RETURN jsonb_set(retval, dst_path, tmp_value::jsonb, TRUE);
END;
$function$;
