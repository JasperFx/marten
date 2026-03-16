CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_mark_event_progression_extended(name varchar, last_encountered bigint, p_heartbeat timestamp with time zone, p_agent_status varchar, p_pause_reason text, p_running_on_node integer) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
INSERT INTO {databaseSchema}.mt_event_progression (name, last_seq_id, last_updated, heartbeat, agent_status, pause_reason, running_on_node)
VALUES (name, last_encountered, transaction_timestamp(), p_heartbeat, p_agent_status, p_pause_reason, p_running_on_node)
ON CONFLICT ON CONSTRAINT pk_mt_event_progression
    DO
UPDATE SET last_seq_id = last_encountered, last_updated = transaction_timestamp(), heartbeat = p_heartbeat, agent_status = p_agent_status, pause_reason = p_pause_reason, running_on_node = p_running_on_node;

END;

$function$;
