# Exporting the Schema Definition

In production, you
may either not have rights to generate new tables at runtime or simply not wish to do that. In that case, Marten exposes some ability to dump all
the SQL for creating these objects for *all the known document types* from `IDocumentStore` like this:

<!-- snippet: sample_export-ddl -->
<a id='snippet-sample_export-ddl'></a>
```cs
public void export_ddl()
{
    var store = DocumentStore.For(_ =>
    {
        _.Connection("some connection string");

        // If you are depending upon attributes for customization,
        // you have to help DocumentStore "know" what the document types
        // are
        _.Schema.For<User>();
        _.Schema.For<Company>();
        _.Schema.For<Issue>();
    });

    // Export the SQL to a file
    store.Schema.WriteDatabaseCreationScriptFile("my_database.sql");

    // Or instead, write a separate sql script
    // to the named directory
    // for each type of document
    store.Schema.WriteDatabaseCreationScriptByType("sql");

    // or just see it
    var sql = store.Schema.ToDatabaseScript();
    Debug.WriteLine(sql);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ExportingDDL.cs#L8-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_export-ddl' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As of v0.8, this export will include the Hilo id generation table and all the objects necessary to use the event store functionality if that is enabled.

For the moment, Marten is not directly supporting any kind of database migration strategy. To be honest, we're waiting to see how Marten plays out in production before investing in anything like that.

The code above creates the following SQL script below, with these elements:

1. For each document type, there is a table systematically named `mt_doc_[document type name]` that consists of an id column and a
   JSONB column called "data"
1. For each document type, there is a function called `mt_upsert_[document type name]` that performs inserts or updates of that document type
1. You'll see an index named `mt_doc_user_idx_user_name` that is on a duplicated, searchable field for the `User` document (it's configured by attribute)
1. `mt_hilo` and `mt_get_next_hi` that support Marten's HiLo numeric identifier strategy.

The full DDL exported from above is:

```sql
DROP TABLE IF EXISTS mt_doc_user CASCADE;
CREATE TABLE mt_doc_user (
    id           uuid CONSTRAINT pk_mt_doc_user PRIMARY KEY,
    data         jsonb NOT NULL ,
    user_name    varchar
);


CREATE OR REPLACE FUNCTION mt_upsert_user(docId uuid, doc JSONB, arg_user_name varchar) RETURNS VOID AS
$$
BEGIN
INSERT INTO mt_doc_user VALUES (docId, doc, arg_user_name)
  ON CONFLICT ON CONSTRAINT pk_mt_doc_user
  DO UPDATE SET data = doc, user_name = arg_user_name;
END;
$$ LANGUAGE plpgsql;

CREATE INDEX mt_doc_user_idx_user_name ON mt_doc_user (user_name)


DROP TABLE IF EXISTS mt_doc_company CASCADE;
CREATE TABLE mt_doc_company (
    id      uuid CONSTRAINT pk_mt_doc_company PRIMARY KEY,
    data    jsonb NOT NULL
);


CREATE OR REPLACE FUNCTION mt_upsert_company(docId uuid, doc JSONB) RETURNS VOID AS
$$
BEGIN
INSERT INTO mt_doc_company VALUES (docId, doc)
  ON CONFLICT ON CONSTRAINT pk_mt_doc_company
  DO UPDATE SET data = doc;
END;
$$ LANGUAGE plpgsql;

CREATE INDEX mt_doc_company_idx_data ON mt_doc_company USING gin (data jsonb_path_ops)


DROP TABLE IF EXISTS mt_doc_issue CASCADE;
CREATE TABLE mt_doc_issue (
    id      uuid CONSTRAINT pk_mt_doc_issue PRIMARY KEY,
    data    jsonb NOT NULL
);


CREATE OR REPLACE FUNCTION mt_upsert_issue(docId uuid, doc JSONB) RETURNS VOID AS
$$
BEGIN
INSERT INTO mt_doc_issue VALUES (docId, doc)
  ON CONFLICT ON CONSTRAINT pk_mt_doc_issue
  DO UPDATE SET data = doc;
END;
$$ LANGUAGE plpgsql;


DROP TABLE IF EXISTS mt_hilo CASCADE;
CREATE TABLE mt_hilo (
	entity_name			varchar CONSTRAINT pk_mt_hilo PRIMARY KEY,
	hi_value			bigint default 0
);

CREATE OR REPLACE FUNCTION mt_get_next_hi(entity varchar) RETURNS int AS $$
DECLARE
	current_value bigint;
	next_value bigint;
BEGIN
	select hi_value into current_value from mt_hilo where entity_name = entity;
	IF current_value is null THEN
		insert into mt_hilo (entity_name, hi_value) values (entity, 0);
		next_value := 0;
	ELSE
		next_value := current_value + 1;
		update mt_hilo set hi_value = next_value where entity_name = entity;
	END IF;

	return next_value;
END
$$ LANGUAGE plpgsql;
```
