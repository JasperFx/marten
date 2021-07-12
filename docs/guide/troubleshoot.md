# FAQ & Troubleshooting

## Types of document sessions

| From `IDocumentStore` | Characteristics | Use |
|-----------------------|-----------------------|---|
| `OpenSession`         | **Defaults** to session that tracks objects by their identity, `DocumentTracking.IdentityOnly`, with isolation level of *Read Committed*. | Reading & writing data. Objects within a session are cached by their identity. Updates to objects are explicitly controlled through session operations (`IDocumentSession.Update`, `IDocumentSession.Store`). With the defaults, incurs lower overhead than `DirtyTrackedSession`. |
| `LightweightSession`  | No change tracking, `DocumentTracking.None`, with the default isolation level of *Read Committed*. | Reading & writing data. No caching of objects is done within a session, e.g. repeated loads using the same document identity yield separate objects, each hydrated from the database. In case of updates to such objects, the last object to be stored will overwrite any pending changes from previously stored objects of the same identity. Can incur lower overhead than tracked sessions. |
| `DirtyTrackedSession` | Track all changes to objects, `DocumentTracking.DirtyTracking`, with the default isolation level of *Read Committed*. | Reading & writing data. Tracks all changes to objects loaded through a session. Upon save (`IDocumentSession.SaveChanges`), Marten updates the changed objects without requiring explicit calls to `IDocumentSession.Update` or `IDocumentSession.Store`. Incurs the largest overhead of tracked sessions.  |
| `QuerySession`        | No identity mapping with the default isolation level of *Read Committed*.   | Reading data, i.e. no insert or update operations are exposed. |

## Query throws `NotSupportedException` exception

Marten needs to translate LINQ queries to SQL in order to execute them against the database. This translation requires explicit support for all the query operators that are used. If your query operation is not covered, Marten will throw a `NotSupportedException`. In such a case, consider [filing a feature request](https://github.com/JasperFx/marten/issues/new). Lastly, as a mitigation, consider
<[linkto:documentation/documents/querying/sql;title=hand-crafting the required query]>.

## Serialize to camel case?

While it's possible to accommodate any serialization schemes by implementing a custom `ISerializer`, Marten's built-in serializer (Json.Net) can be set to serialize to Camel case through `StoreOptions.UseDefaultSerialization`:

<!-- snippet: sample_sample-serialize-to-camelcase -->
<a id='snippet-sample_sample-serialize-to-camelcase'></a>
```cs
var store = DocumentStore.For(storeOptions =>
{
    // Change default casing to CamelCase
    storeOptions.UseDefaultSerialization(casing: Casing.CamelCase);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/CamelCasing.cs#L11-L16' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sample-serialize-to-camelcase' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Disable PLV8

If you don't want PLV8 (required for JavaScript transformations) related items in your database schema, you can disable PLV8 altogether by setting `StoreOptions.PLV8Enabled` to false.

## More diagnostics data outside of Marten

If you cannot obtain the desired diagnostic data through Marten's [diagnostics](/guide/documents/diagnostics), consider using the [Npgsql logging facilities](https://www.npgsql.org/doc/logging.html), by hooking into `NpgsqlLogManager.Provider`, or by using the [performance counters exposed by Npgsql](https://www.npgsql.org/doc/performance.html).

Lastly, if you feel that exposing the data should be the responsibility of Marten, consider [filing a feature request](https://github.com/JasperFx/marten/issues/new).

## Full text search error `function to_tsvector(unknown, jsonb) does not exist`

Ensure, that you are running PostgreSQL 10 or higher that support full text searching JSON and JSONB.
