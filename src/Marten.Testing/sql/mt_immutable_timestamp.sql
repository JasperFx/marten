DO LANGUAGE plpgsql $tran$
BEGIN

CREATE OR REPLACE FUNCTION public.mt_immutable_timestamp(value text) RETURNS timestamp with time zone LANGUAGE sql IMMUTABLE AS $function$
    select value::timestamptz
$function$;



END;
$tran$;
