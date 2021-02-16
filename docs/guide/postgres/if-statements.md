# If Statements

When ever you write SQL in SQL Server Management Studio, you're actually writing TSQL PostgreSQL supports SQL by default only and has key words for breaking out into other supported sub languages, the most common being PL/pgSQL (PostgresSQL) and plv8 (JavaScript run on the v8 engine)

If statement's are not actually part of the SQL Spec *(The supported version of PostgreSQL 9.5 onwards supports most of the 2011 spec)*, and so in order to write if statements you need to use an anonymous code block with the language PL/pgSQL.

For example:

```sql
DO %%
BEGIN

    // your sql here…

END
%%;
```

Everything inside the begin/end will use PL/pgSQL by default as an assumed language.

You can be explicit about the language by defining the language at the end of the block like so:

```sql
DO %%
BEGIN

    // your sql here…

END
%% LANGUAGE plpgsql;
```

Note: `DO` is not part of the SQL Standard and is unique to PostgreSQL.

An actual example, lets say we wanted to create a new schema called `inventory` if it didn’t exist already.

```sql
DO $$
BEGIN
    IF NOT EXISTS(
        SELECT schema_name
          FROM information_schema.schemata
          WHERE schema_name = 'inventory'
      )
    THEN
      EXECUTE 'CREATE SCHEMA inventory';
    END IF;
END
$$;
```

It is worth noting that Postgres offers helpful syntax for scenarios you would usually use IF statements in SQL Server for.

For example in SQL Server we might do the following:

```sql
DROP PROCEDURE CreateProduct…
CREATE PROCEDURE CreateProduct…
```

or

```sql
IF NOT EXISTS (….)
BEGIN
    CREATE TABLE Product (…
```

These would be written in Postgres like so:

```sql
CREATE OR REPLACE FUNCTION create_product

CREATE TABLE IF NOT EXISTS Product (….
```

These don't require you to drop down to PL/pgSQ in order to execute.
