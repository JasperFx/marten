CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_immutable_time(value text) RETURNS time without time zone LANGUAGE sql IMMUTABLE AS
$function$
select value::time

$function$;
