CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_mark_event_progression_extended(name varchar, last_encountered bigint, p_heartbeat timestamp with time zone, p_agent_status varchar, p_pause_reason text, p_running_on_node integer) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
-- Telemetry-only: decorate an EXISTING progression row's extended columns and nothing else.
-- The progression row (name / last_seq_id / last_updated) is owned by the projection batch commit,
-- so this write must never INSERT and never touch last_seq_id / last_updated:
--   * a pre-emptive INSERT here races the batch's own first progress insert into a duplicate-key
--     failure on pk_mt_event_progression (and a Started publication carries last_encountered = 0, which
--     would seed the row's progress backwards);
--   * updating last_seq_id on a throttled heartbeat would roll committed progress backwards -- the
--     "clobber" defect.
-- If no progression row exists yet the telemetry is simply skipped until the first commit creates it.
-- The table-qualified column disambiguates it from the same-named `name` parameter (plpgsql's
-- default variable_conflict = error would otherwise reject the bare `name`), while `$1` references
-- that parameter positionally. The parameter is left named `name` so CREATE OR REPLACE never trips
-- the "cannot rename input parameter" rule against a function an older release already installed.
-- CritterWatch #750 / marten#4981.
UPDATE {databaseSchema}.mt_event_progression
SET heartbeat = p_heartbeat,
    agent_status = p_agent_status,
    pause_reason = p_pause_reason,
    running_on_node = p_running_on_node
WHERE mt_event_progression.name = $1;

END;

$function$;
