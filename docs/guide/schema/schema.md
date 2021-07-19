# Configuring the Database Schema

By default, Marten will put all database schema objects into the main _public_ schema. If you want to override this behavior,
use the `StoreOptions.DocumentSchemaName` property when configuring your `IDocumentStore`:

<!-- snippet: sample_override_schema_per_table -->
<a id='snippet-sample_override_schema_per_table'></a>
```cs
StoreOptions(_ =>
{
    _.Storage.MappingFor(typeof(User)).DatabaseSchemaName = "other";
    _.Storage.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
    _.Storage.MappingFor(typeof(Company));
    _.Storage.MappingFor(typeof(IntDoc));

    // this will tell marten to use the default 'public' schema name.
    _.DatabaseSchemaName = SchemaConstants.DefaultSchema;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/DocumentSchemaTests.cs#L335-L348' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_override_schema_per_table' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

As you can see, you can also choose to configure the schema storage for each document type individually.
