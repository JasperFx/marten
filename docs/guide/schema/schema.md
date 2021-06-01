# Configuring the Database Schema

By default, Marten will put all database schema objects into the main _public_ schema. If you want to override this behavior,
use the `StoreOptions.DocumentSchemaName` property when configuring your `IDocumentStore`:

<!-- snippet: sample_override_schema_per_table -->
<!-- endSnippet -->

As you can see, you can also choose to configure the schema storage for each document type individually.
