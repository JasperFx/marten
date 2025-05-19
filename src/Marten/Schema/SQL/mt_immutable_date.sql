CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_immutable_date(value text) RETURNS date LANGUAGE sql IMMUTABLE AS
$function$
select value::date

$function$;
