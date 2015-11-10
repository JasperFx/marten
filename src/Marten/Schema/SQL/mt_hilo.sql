DROP TABLE IF EXISTS mt_hilo CASCADE;
CREATE TABLE mt_hilo (
	entity_name			varchar CONSTRAINT pk_mt_hilo PRIMARY KEY,
	hi					int default 0
);

CREATE OR REPLACE FUNCTION mt_get_next_hi(entity varchar) RETURNS int AS $$
DECLARE
	current int;
	next int;
BEGIN
	select hi into current from mt_hilo where entity_name = entity;
	IF hi is null THEN
		insert into mt_hilo (entity_name, hi) values (entity, 1);
		next := 1;
	ELSE
		next := current + 1;
		update mt_hilo set hi = next where entity_name = entity;
	END IF;

	return next;
END
$$ LANGUAGE plpgsql;
