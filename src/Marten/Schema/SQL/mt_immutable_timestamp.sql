CREATE OR REPLACE FUNCTION {databaseSchema}.mt_immutable_timestamp(value text) RETURNS timestamp without time zone LANGUAGE sql IMMUTABLE AS $function$
    select value::timestamp
$function$;