CREATE
OR REPLACE FUNCTION {databaseSchema}.mt_mark_progression_with_skip(shard_name varchar, ending_sequence bigint, starting_sequence bigint) RETURNS bigint AS
$$
DECLARE
    current_value bigint;
BEGIN
    select last_seq_id into current_value from {databaseSchema}.mt_event_progression where name = shard_name;

    IF current_value is null then
        -- Bootstrap the high-water row on first invocation. Previously this branch
        -- returned 0, which left HighWaterAgent.CheckNowAsync polling forever because
        -- statistics.CurrentMark never advanced. Issue #4425.
        INSERT INTO {databaseSchema}.mt_event_progression (name, last_seq_id, last_updated)
        VALUES (shard_name, ending_sequence, transaction_timestamp())
        ON CONFLICT ON CONSTRAINT pk_mt_event_progression
            DO UPDATE SET last_seq_id = ending_sequence, last_updated = transaction_timestamp();
        IF ending_sequence > starting_sequence THEN
            insert into {databaseSchema}.mt_high_water_skips (ending_sequence, starting_sequence) values (ending_sequence, starting_sequence);
        END IF;
        return ending_sequence;
    ELSIF current_value = starting_sequence THEN
        update {databaseSchema}.mt_event_progression SET last_seq_id = ending_sequence, last_updated = transaction_timestamp() where shard_name = name;
        insert into {databaseSchema}.mt_high_water_skips (ending_sequence, starting_sequence) values (ending_sequence, starting_sequence);
        return ending_sequence;
    ELSE
        return current_value;
    END IF;
END
$$
LANGUAGE plpgsql;
