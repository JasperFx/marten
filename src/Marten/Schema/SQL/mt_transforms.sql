DROP TABLE IF EXISTS {databaseSchema}.mt_transforms CASCADE;
CREATE TABLE {databaseSchema}.mt_transforms (
	name			varchar(100) CONSTRAINT pk_mt_transforms PRIMARY KEY,
	definition		varchar(30000) NOT NULL
);


CREATE OR REPLACE FUNCTION {databaseSchema}.mt_initialize_transforms() RETURNS void AS $$
	var transforms = {};
	var functions = plv8.execute("select name, definition from {databaseSchema}.mt_transforms");

	for (var i = 0; i++; i < functions.length){
		transforms[functions[i].name] = functions[i].definition;
	}

	plv8.transforms = transforms;


$$ LANGUAGE plv8;

CREATE OR REPLACE FUNCTION {databaseSchema}.mt_apply_transform(name varchar, doc JSON) RETURNS json AS $$
	if (!plv8.transforms){
		plv8.execute('select {databaseSchema}.mt_initialize_transforms()');
	}

	return plv8.transforms[name](doc);

$$ LANGUAGE plv8;
