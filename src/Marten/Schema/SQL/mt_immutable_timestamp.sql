CREATE OR REPLACE FUNCTION {databaseSchema}.mt_immutable_timestamp(value text) RETURNS timestamp with time zone LANGUAGE sql IMMUTABLE AS $function$
    select value::timestamptz
$function$;