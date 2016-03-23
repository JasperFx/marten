DROP TABLE IF EXISTS {databaseSchema}.mt_hilo CASCADE;
CREATE TABLE {databaseSchema}.mt_hilo (
	entity_name			varchar CONSTRAINT pk_mt_hilo PRIMARY KEY,
	hi_value			bigint default 0
);

CREATE OR REPLACE FUNCTION {databaseSchema}.mt_get_next_hi(entity varchar) RETURNS int AS $$
DECLARE
	current_value bigint;
	next_value bigint;
BEGIN
	select hi_value into current_value from {databaseSchema}.mt_hilo where entity_name = entity;
	IF current_value is null THEN
		insert into {databaseSchema}.mt_hilo (entity_name, hi_value) values (entity, 0);
		next_value := 0;
	ELSE
		next_value := current_value + 1;
		update {databaseSchema}.mt_hilo set hi_value = next_value where entity_name = entity;
	END IF;

	return next_value;
END
$$ LANGUAGE plpgsql;
