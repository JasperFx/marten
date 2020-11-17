CREATE OR REPLACE FUNCTION {databaseSchema}.mt_mark_event_progression(name varchar, last_encountered bigint) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
INSERT INTO {databaseSchema}.mt_event_progression (name, last_seq_id) VALUES (name, last_encountered)
  ON CONFLICT ON CONSTRAINT pk_mt_event_progression
  DO UPDATE SET last_seq_id = last_encountered;

END;

$function$;
