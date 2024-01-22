/*
 * Function: mt_jsonb_patch
 * Description: This function applies a JSON patch to a JSONB object and returns the updated JSONB object.
 * Parameters:
 *   - retval: The original JSONB object.
 *   - patch: The JSONB patch to apply.
 * Returns: The updated JSONB object after applying the patch.
 */
CREATE OR REPLACE FUNCTION {databaseSchema}.mt_jsonb_patch(JSONB, JSONB) RETURNS JSONB AS $$
DECLARE
retval ALIAS FOR $1;
patch ALIAS FOR $2;
patch_path TEXT[];
value JSONB;
BEGIN

-- Inner helper functions
CREATE OR REPLACE FUNCTION mt_jsonb_path_to_array(TEXT, CHAR(1) DEFAULT '\.') RETURNS TEXT[] AS $helper$
DECLARE
location ALIAS FOR $1;
regex_pattern ALIAS FOR $2;
BEGIN
RETURN regexp_split_to_array(location, regex_pattern)::TEXT[];
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_set_null_parent(JSONB, TEXT[]) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
dst_path ALIAS FOR $2;
dst_path_segment TEXT[] = ARRAY[]::TEXT[];
dst_path_array_length INTEGER;
i INTEGER = 1;
BEGIN
dst_path_array_length = array_length(dst_path, 1);
WHILE i <= (dst_path_array_length - 1) LOOP
  dst_path_segment = dst_path_segment || ARRAY[dst_path[i]];
  IF retval#>dst_path_segment = 'null'::jsonb THEN
    retval = jsonb_set(retval, dst_path_segment, '{}'::JSONB, true);
  END IF;
  i = i + 1;
END LOOP;
RETURN retval;
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_copy(JSONB, TEXT[], TEXT[]) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
src_path ALIAS FOR $2;
dst_path ALIAS FOR $3;
tmp_value JSONB;
BEGIN
  tmp_value = retval#>src_path;
  retval = mt_jsonb_set_null_parent(retval, dst_path);
  RETURN jsonb_set(retval, dst_path, tmp_value::JSONB, true);
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_set(JSONB, TEXT[], JSONB) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
location ALIAS FOR $2;
val ALIAS FOR $3;
BEGIN
RETURN jsonb_set(retval, location, val, true);
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_delete(JSONB, TEXT[]) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
location ALIAS FOR $2;
BEGIN
RETURN retval #- location;
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_append(JSONB, TEXT[], JSONB, BOOLEAN DEFAULT FALSE) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
location ALIAS FOR $2;
val ALIAS FOR $3;
if_not_exists ALIAS FOR $4;
tmp_value JSONB;
BEGIN
  tmp_value = retval#>location;
  IF tmp_value IS NOT NULL AND jsonb_typeof(tmp_value) = 'array' THEN
    CASE
      WHEN NOT if_not_exists THEN
        retval = jsonb_set(retval, location, tmp_value || val, false);
      WHEN jsonb_typeof(val) = 'object' and NOT tmp_value @> jsonb_build_array(val) THEN
        retval = jsonb_set(retval, location, tmp_value || val, false);
      WHEN jsonb_typeof(val) <> 'object' and NOT tmp_value @> val THEN
        retval = jsonb_set(retval, location, tmp_value || val, false);
      ELSE NULL;
    END CASE;
  END IF;
RETURN retval;
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_insert(JSONB, TEXT[], JSONB, INTEGER DEFAULT NULL, BOOLEAN DEFAULT FALSE) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
location ALIAS FOR $2;
val ALIAS FOR $3;
elm_index ALIAS FOR $4;
if_not_exists ALIAS FOR $5;
tmp_value JSONB;
BEGIN
  tmp_value = retval#>location;
  IF tmp_value IS NOT NULL AND jsonb_typeof(tmp_value) = 'array' THEN
    IF elm_index IS NULL THEN
      elm_index = jsonb_array_length(tmp_value) + 1;
    END IF;
    CASE
      WHEN NOT if_not_exists THEN
        retval = jsonb_insert(retval, location || elm_index::text , val);
      WHEN jsonb_typeof(val) = 'object' and NOT tmp_value @> jsonb_build_array(val) THEN
        retval = jsonb_insert(retval, location || elm_index::text , val);
      WHEN jsonb_typeof(val) <> 'object' and NOT tmp_value @> val THEN
        retval = jsonb_insert(retval, location || elm_index::text , val);
    ELSE NULL;
  END CASE;
END IF;
RETURN retval;
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_remove(JSONB, TEXT[], JSONB) RETURNS JSONB AS $helper$
DECLARE
  retval ALIAS FOR $1;
  location ALIAS FOR $2;
  val ALIAS FOR $3;
  tmp_value JSONB;
BEGIN
  tmp_value = retval#>location;
  IF tmp_value IS NOT NULL AND jsonb_typeof(tmp_value) = 'array' THEN
    tmp_value = (
      SELECT jsonb_agg(elem)
      FROM jsonb_array_elements(tmp_value) AS elem
      WHERE elem <> val
    );
    IF tmp_value IS NULL THEN
      tmp_value = '[]'::jsonb;
    END IF;
  END IF;
  RETURN jsonb_set(retval, location, tmp_value, false);
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_duplicate(JSONB, TEXT[], JSONB) RETURNS JSONB AS $helper$
DECLARE
  retval ALIAS FOR $1;
  location ALIAS FOR $2;
  targets ALIAS FOR $3;
  tmp_value JSONB;
  target_path TEXT[];
  target TEXT;
BEGIN
  FOR target IN SELECT jsonb_array_elements_text(targets)
  LOOP
    target_path = mt_jsonb_path_to_array(target);
    retval = mt_jsonb_copy(retval, location, target_path);
  END LOOP;
  RETURN retval;
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_move(JSONB, TEXT[], TEXT) RETURNS JSONB AS $helper$
DECLARE
retval ALIAS FOR $1;
src_path ALIAS FOR $2;
dst_name ALIAS FOR $3;
dst_path TEXT[];
tmp_value JSONB;
BEGIN
  tmp_value = retval#>src_path;
  retval = retval #- src_path;
  dst_path = src_path;
  dst_path[array_length(dst_path, 1)] = dst_name;
  retval = mt_jsonb_set_null_parent(retval, dst_path);
  RETURN jsonb_set(retval, dst_path, tmp_value, true);
END;
$helper$ LANGUAGE PLPGSQL;

CREATE OR REPLACE FUNCTION mt_jsonb_increment(JSONB, TEXT[], NUMERIC) RETURNS JSONB AS $helper$
DECLARE
  retval ALIAS FOR $1;
  location ALIAS FOR $2;
  increment_value ALIAS FOR $3;
  tmp_value JSONB;
BEGIN
  tmp_value = retval#>location;
  IF tmp_value IS NULL THEN
    tmp_value = to_jsonb(0);
  END IF;
  RETURN jsonb_set(retval, location, to_jsonb(tmp_value::NUMERIC + increment_value), true);
END;
$helper$ LANGUAGE PLPGSQL;
-- END: Inner helper functions

patch_path = mt_jsonb_path_to_array((patch->>'path')::TEXT);
CASE patch->>'type'
  WHEN 'set' THEN
    retval = mt_jsonb_set(retval, patch_path, (patch->'value')::JSONB);
  WHEN 'delete' THEN
    retval = mt_jsonb_delete(retval, patch_path);
  WHEN 'append' THEN
    retval = mt_jsonb_append(retval, patch_path, (patch->'value')::JSONB);
  WHEN 'append_if_not_exists' THEN
    retval = mt_jsonb_append(retval, patch_path, (patch->'value')::JSONB, true);
  WHEN 'insert' THEN
    retval = mt_jsonb_insert(retval, patch_path, (patch->'value')::JSONB, (patch->>'index')::INTEGER);
  WHEN 'insert_if_not_exists' THEN
    retval = mt_jsonb_insert(retval, patch_path, (patch->'value')::JSONB, (patch->>'index')::INTEGER, true);
  WHEN 'remove' THEN
    retval = mt_jsonb_remove(retval, patch_path, (patch->'value')::JSONB);
  WHEN 'duplicate' THEN
    retval = mt_jsonb_duplicate(retval, patch_path, (patch->'targets')::JSONB);
  WHEN 'rename' THEN
    retval = mt_jsonb_move(retval, patch_path, (patch->>'to')::TEXT);
  WHEN 'increment' THEN
    retval = mt_jsonb_increment(retval, patch_path, (patch->>'increment')::NUMERIC);
  WHEN 'increment_float' THEN
    retval = mt_jsonb_increment(retval, patch_path, (patch->>'increment')::NUMERIC);
  ELSE NULL;
END CASE;
RETURN retval;
END;
$$ LANGUAGE PLPGSQL;
