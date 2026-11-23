# Exporting the Schema Definition

In production, you
may either not have rights to generate new tables at runtime or simply not wish to do that. In that case, Marten exposes some ability to dump all
the SQL for creating these objects for *all the known document types* from `IDocumentStore` like this:

<!-- snippet: sample_export-ddl -->
<a id='snippet-sample_export-ddl'></a>
```cs
public async Task export_ddl()
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
    await store.Storage.WriteCreationScriptToFile("my_database.sql");

    // Or instead, write a separate sql script
    // to the named directory
    // for each type of document
    await store.Storage.WriteScriptsByType("sql");

    // or just see it
    var sql = store.Storage.ToDatabaseScript();
    Debug.WriteLine(sql);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ExportingDDL.cs#L9-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_export-ddl' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
Exporting is offline. `ToDatabaseScript()`, `WriteCreationScriptToFile()`, `WriteScriptsByType()` and the
`db-dump` command all build the script from your configuration alone — they neither connect to the database nor
create anything in it. That holds whatever id strategies your documents use, so `db-dump` is safe to run on a
build machine that has no database in reach.

Everything else in the `db-` family does need a database, because it works out the difference between your
configuration and what is already there: `db-patch` and `db-assert` connect and read, and `db-apply` connects
and writes.
:::

To generate migration scripts for an existing database, rather than the complete script, see
[Schema Migrations and Patches](/schema/migrations).

The code above creates a SQL script with these elements:

1. A block that creates the `public` schema if it is missing, and the same for any other schema your configuration uses
2. Marten's shared helper functions, created once per schema: the `mt_immutable_*` functions that computed indexes on
   date and time members use, the `mt_grams_*` and `mt_safe_unaccent` functions behind
   [n-gram search](/documents/full-text#partial-text-search-in-a-multi-word-text-ngram-search), and the `mt_jsonb_*` functions behind
   [partial updates](/documents/partial-updates-patching)
3. For each document type, a table systematically named `mt_doc_[document alias]`, with the `id` column, the `data`
   JSONB column, and the `mt_last_modified`, `mt_version` and `mt_dotnet_type` metadata columns. Any indexes, duplicated
   fields or foreign keys you configure for a document type are created with its table.
4. When any document type uses numeric identifiers, the `mt_hilo` table and `mt_get_next_hi` function behind Marten's
   [HiLo identifier strategy](/documents/identity)

Marten writes documents with ordinary SQL statements against these tables, so there is no function per document type.
The document tables exported from above are:

```sql
CREATE TABLE IF NOT EXISTS public.mt_doc_company (
    id                  uuid                        NOT NULL,
    data                jsonb                       NOT NULL,
    mt_last_modified    timestamp with time zone    NULL DEFAULT (transaction_timestamp()),
    mt_version          uuid                        NOT NULL DEFAULT (md5(random()::text || clock_timestamp()::text)::uuid),
    mt_dotnet_type      varchar                     NULL,
CONSTRAINT pkey_mt_doc_company_id PRIMARY KEY (id)
);
CREATE TABLE IF NOT EXISTS public.mt_doc_issue (
    id                  uuid                        NOT NULL,
    data                jsonb                       NOT NULL,
    mt_last_modified    timestamp with time zone    NULL DEFAULT (transaction_timestamp()),
    mt_version          uuid                        NOT NULL DEFAULT (md5(random()::text || clock_timestamp()::text)::uuid),
    mt_dotnet_type      varchar                     NULL,
CONSTRAINT pkey_mt_doc_issue_id PRIMARY KEY (id)
);
CREATE TABLE IF NOT EXISTS public.mt_doc_user (
    id                  uuid                        NOT NULL,
    data                jsonb                       NOT NULL,
    mt_last_modified    timestamp with time zone    NULL DEFAULT (transaction_timestamp()),
    mt_version          uuid                        NOT NULL DEFAULT (md5(random()::text || clock_timestamp()::text)::uuid),
    mt_dotnet_type      varchar                     NULL,
CONSTRAINT pkey_mt_doc_user_id PRIMARY KEY (id)
);
```

All three of those document types use `Guid` identifiers. Add a document type with an `int` or `long` identifier, and
the script also contains the HiLo support:

```sql
CREATE TABLE IF NOT EXISTS public.mt_hilo (
    entity_name    varchar    NOT NULL,
    hi_value       bigint     NULL DEFAULT 0,
CONSTRAINT pkey_mt_hilo_entity_name PRIMARY KEY (entity_name)
);

CREATE OR REPLACE FUNCTION public.mt_get_next_hi(entity varchar) RETURNS bigint AS
$$
DECLARE
    next_value bigint;
BEGIN
    update public.mt_hilo
    set hi_value = hi_value + 1
    where entity_name = entity
    returning hi_value into next_value;

    IF NOT FOUND THEN
        insert into public.mt_hilo (entity_name, hi_value) values (entity, 0)
        on conflict (entity_name) do update set hi_value = public.mt_hilo.hi_value + 1
        returning hi_value into next_value;
    END IF;

    return next_value;
END
$$
LANGUAGE plpgsql;
```
