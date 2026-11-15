CREATE OR REPLACE FUNCTION {databaseSchema}.mt_get_next_hi(entity varchar) RETURNS bigint AS
$$
DECLARE
    next_value bigint;
BEGIN
    -- #5938: one atomic increment. Concurrent callers queue on the row lock and each one gets a hi of
    -- its own. The old read-then-compare-and-set returned -1 to every caller that overlapped another
    -- and made it retry, and under load a caller could lose every one of its attempts.
    update {databaseSchema}.mt_hilo
    set hi_value = hi_value + 1
    where entity_name = entity
    returning hi_value into next_value;

    IF NOT FOUND THEN
        -- The first block for this entity. A concurrent first call that inserted the row before this
        -- one could see it lands on the conflict and increments instead.
        insert into {databaseSchema}.mt_hilo (entity_name, hi_value) values (entity, 0)
        on conflict (entity_name) do update set hi_value = {databaseSchema}.mt_hilo.hi_value + 1
        returning hi_value into next_value;
    END IF;

    return next_value;
END
$$
LANGUAGE plpgsql;
