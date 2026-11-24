# Marten Metadata

::: tip
Note that this content only applies to document storage and will have no impact on projected documents
created through live aggregations of event data.
:::

Marten supports a rich set of available metadata tracking for documents in the database, but note that some
of these columns are "opt in" and others are enabled by default, but allow for "opt out" for users who want
a leader database.

The available columns for document storage are:

| Column Name        | Description                                                                                       | Enabled by Default                                      |
| ------------------ | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| `mt_last_modified` | Timestamp of the last time the document record was modified                                       | Yes                                                     |
| `mt_version`       | `Guid` value that marks the current version of the document. This supports optimistic concurrency | Yes                                                     |
| `mt_dotnet_type`   | Assembly qualified name of the .Net type persisted to this row                                    | Yes                                                     |
| `correlation_id`   | User-supplied correlation identifier (`string`)                                                   | No, opt in                                              |
| `causation_id`     | User-supplied causation identifier (`string`)                                                     | No, opt in                                              |
| `headers`          | User-supplied key/value pairs for extensible metadata                                             | No, opt in                                              |
| `last_modified_by` | User name of the session that last changed the document (`string`)                                | No, opt in                                              |
| `mt_deleted`       | Boolean flag noting whether the document is soft-deleted                                          | Only if the document type is configured as soft-deleted |
| `mt_deleted_at`    | Timestamp marking when a document was soft-deleted                                                | Only if the document type is configured as soft-deleted |
| `mt_created_at`    | Timestamp marking when a document was originally created                                          | No, opt in                                              |
| `tenant_id`        | The identifier of the owning tenant if multi-tenancy is configured for this document.             | Yes, but only for multi-tenancy schemas                 |

## Correlation Id, Causation Id, Headers, and Last Modified By

::: tip
At this point, the Marten team thinks that using a custom `ISessionFactory` to set
correlation and causation data is the most likely usage for this feature. The Marten team
plans to build a sample application showing Marten being used with [Open Telemetry](https://opentelemetry.io/) tracing soon.
:::

These four columns are opt in, per document type or for all of them with a policy.

::: tip Why turn them on
[CritterWatch](https://critterwatch.jasperfx.net)'s document explorer filters documents by `last_modified_by`,
`correlation_id` and `causation_id`, and its saved filter watches use the same columns. Without them those filters have
nothing to match. They're also how you trace a document back to the request or message that last changed it.

What they cost: four more values on every document write, and four more columns on the table. On the
CritterStackScalability RC rig they were about half of what Marten 10 still cost on projection rebuilds that write
documents, which is why they're opt-in ([#6144](https://github.com/JasperFx/marten/issues/6144)).
:::

These are the settings:

<!-- snippet: sample_enabling_causation_fields -->
<a id='snippet-sample_enabling_causation_fields'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Optionally turn on metadata columns by document type
    opts.Schema.For<User>().Metadata(x =>
    {
        x.CorrelationId.Enabled = true;
        x.CausationId.Enabled = true;
        x.Headers.Enabled = true;
        x.LastModifiedBy.Enabled = true;

    });

    // Or just globally turn on columns for all document
    // types in one fell swoop
    opts.Policies.ForAllDocuments(x =>
    {
        x.Metadata.CausationId.Enabled = true;
        x.Metadata.CorrelationId.Enabled = true;
        x.Metadata.Headers.Enabled = true;
        x.Metadata.LastModifiedBy.Enabled = true;

        // This column is "opt in"
        x.Metadata.CreatedAt.Enabled = true;
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L26-L56' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enabling_causation_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Next, you relay the actual values for these fields at the document session level as shown below:

<!-- snippet: sample_setting_metadata_on_session -->
<a id='snippet-sample_setting_metadata_on_session'></a>
```cs
public void SettingMetadata(IDocumentSession session, string correlationId, string causationId)
{
    // These values will be persisted to any document changed
    // by the session when SaveChanges() is called
    session.CorrelationId = correlationId;
    session.CausationId = causationId;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L61-L71' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_metadata_on_session' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`IDocumentSession.LastModifiedBy` works the same way: once the `last_modified_by` column is turned on, whatever user
name the session carries is written to it for every document the session stores. If that is a person's name or login
and you have data-retention rules, decide whether you want it stored before you turn the column on. A policy that turns
it on for every document type can still leave it off for one:

```cs
opts.Schema.For<Customer>().Metadata(x => x.LastModifiedBy.Enabled = false);
```

Headers are a little bit different, with the ability to set individual header key/value pairs
as shown below:

<!-- snippet: sample_set_header -->
<a id='snippet-sample_set_header'></a>
```cs
public void SetHeader(IDocumentSession session, string sagaId)
{
    session.SetHeader("saga-id", sagaId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L73-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_set_header' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Tracking Metadata on Documents

Marten can now set metadata values directly on the documents persisted by Marten,
but this is an opt in behavior. You can explicitly map a public member of your document
type to a metadata value individually. Let's say that you have a document type like
this where you want to track metadata:

<!-- snippet: sample_docwithmetadata -->
<a id='snippet-sample_docwithmetadata'></a>
```cs
public class DocWithMetadata
{
    public Guid Id { get; set; }

    // other members

    public Guid Version { get; set; }
    public string Causation { get; set; }
    public bool IsDeleted { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L82-L95' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docwithmetadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To enable the Marten mapping to metadata values, use this syntax:

<!-- snippet: sample_explicitly_map_metadata -->
<a id='snippet-sample_explicitly_map_metadata'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Explicitly map the members on this document type
    // to metadata columns.
    opts.Schema.For<DocWithMetadata>().Metadata(m =>
    {
        m.Version.MapTo(x => x.Version);
        m.CausationId.MapTo(x => x.Causation);
        m.IsSoftDeleted.MapTo(x => x.IsDeleted);
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L99-L115' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_explicitly_map_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
Note that mapping a document member to a metadata column will implicitly enable that metadata column collection.
:::

### Mapping Metadata with Attributes

Instead of `MapTo()`, you can mark the member on the document type itself. Each attribute in the `Marten.Schema`
namespace maps one metadata column, and like `MapTo()` it turns that column on:

| Attribute                          | Column             | Member type        |
| ---------------------------------- | ------------------ | ------------------ |
| `[CreatedAtMetadata]`              | `mt_created_at`    | `DateTimeOffset`   |
| `[LastModifiedMetadata]`           | `mt_last_modified` | `DateTimeOffset`   |
| `[TenantIdMetadata]`               | `tenant_id`        | `string`           |
| `[IsSoftDeletedMetadataAttribute]` | `mt_deleted`       | `bool`             |
| `[SoftDeletedAtMetadata]`          | `mt_deleted_at`    | `DateTimeOffset?`  |
| `[DocumentTypeMetadata]`           | `mt_doc_type`      | `string`           |

<!-- snippet: sample_docs_metadata_attributes -->
<a id='snippet-sample_docs_metadata_attributes'></a>
```cs
[SoftDeleted]
public class ApiDocsInvoice
{
    public Guid Id { get; set; }

    // Filled in from mt_created_at. Mapping the member turns the column on
    [CreatedAtMetadata]
    public DateTimeOffset CreatedAt { get; set; }

    // Filled in from mt_last_modified
    [LastModifiedMetadata]
    public DateTimeOffset LastModified { get; set; }

    // Filled in from mt_deleted. The attribute class is IsSoftDeletedMetadataAttributeAttribute,
    // so it has to be written with its "Attribute" suffix
    [IsSoftDeletedMetadataAttribute]
    public bool IsDeleted { get; set; }

    // Filled in from mt_deleted_at
    [SoftDeletedAtMetadata]
    public DateTimeOffset? DeletedAt { get; set; }
}

// The tenant id column only exists on a multi-tenanted document type
[MultiTenanted]
public class ApiDocsTenantedInvoice
{
    public Guid Id { get; set; }

    // Filled in from tenant_id when the document is loaded
    [TenantIdMetadata]
    public string TenantId { get; set; } = string.Empty;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L44-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_metadata_attributes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Some things to know:

* The member has to be exactly the type in the table, or the mapping throws `ArgumentOutOfRangeException`.
* The attribute for `mt_deleted` is a class named `IsSoftDeletedMetadataAttributeAttribute`, so `[IsSoftDeletedMetadata]`
  does not compile. Write it as `[IsSoftDeletedMetadataAttribute]`.
* Marten checks the document type when it builds the mapping, and throws `InvalidDocumentException` when
  `[TenantIdMetadata]` is on a type that is not multi-tenanted, `[DocumentTypeMetadata]` is on a type that is not part of a
  [document hierarchy](/documents/hierarchies), or either soft-delete attribute is on a type that is not
  [soft-deleted](/documents/deletes#soft-deletes).
* The version and revision columns have their own attribute, `[Version]`, covered in
  [optimistic concurrency](/documents/concurrency).

For correlation, causation, and last modified tracking, an easy way to do this is to
just implement the Marten `ITracked` interface as shown below:

<!-- snippet: sample_mytrackeddoc -->
<a id='snippet-sample_mytrackeddoc'></a>
```cs
public class MyTrackedDoc: ITracked
{
    public Guid Id { get; set; }
    public string CorrelationId { get; set; }
    public string CausationId { get; set; }
    public string LastModifiedBy { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L163-L173' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mytrackeddoc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If your document type implements this interface, Marten will automatically enable the correlation and causation tracking, and set values for correlation, causation, and the last modified data on documents anytime they are loaded or persisted by Marten.

Likewise, version tracking directly on the document is probably easiest with the `IVersioned`
interface as shown below:

<!-- snippet: sample_myversioneddoc -->
<a id='snippet-sample_myversioneddoc'></a>
```cs
public class MyVersionedDoc: IVersioned
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/metadata_marker_interfaces.cs#L121-L129' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_myversioneddoc' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Implementing `IVersioned` will automatically opt your document type into optimistic concurrency
checking with mapping of the current version to the `IVersioned.Version` property.

## Disabling All Metadata

If you want Marten to run lean, you can omit the last modified, .NET type and version fields from Marten with this
configuration:

<!-- snippet: sample_disableallinformationalfields -->
<a id='snippet-sample_disableallinformationalfields'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // This will direct Marten to omit all informational
    // metadata fields
    opts.Policies.DisableInformationalFields();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L10-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_disableallinformationalfields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`DisableInformationalFields()` leaves the correlation id, causation id, headers and last modified by columns alone.
They are off by default; if you turned them on, turn them off with `opts.Policies.ForAllDocuments()` as shown above.

## Querying by Last Modified

Documents can be queried by the last modified time using these custom extension methods in Marten:

* `ModifiedSince(DateTimeOffset)` - Return only documents modified since specific date (not inclusive)
* `ModifiedBefore(DateTimeOffset)` - Return only documents modified before specific date (not inclusive)

Here is a sample usage:

<!-- snippet: sample_last_modified_queries -->
<a id='snippet-sample_last_modified_queries'></a>
```cs
public async Task sample_usage(IQuerySession session)
{
    var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
    var tenMinutesAgo = DateTime.UtcNow.AddMinutes(-10);

    // Query for documents modified between 5 and 10 minutes ago
    var recents = await session.Query<Target>()
        .Where(x => x.ModifiedSince(tenMinutesAgo))
        .Where(x => x.ModifiedBefore(fiveMinutesAgo))
        .ToListAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/last_modified_queries.cs#L35-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_last_modified_queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Querying by Created Time

The `mt_created_at` column has a matching pair of operators, in the `Marten.Linq.CreatedAt` namespace:

* `CreatedSince(DateTimeOffset)` - Return only documents created since a specific time (not inclusive)
* `CreatedBefore(DateTimeOffset)` - Return only documents created before a specific time (not inclusive)

`mt_created_at` is opt in, so turn the column on for the document type first. Without it, the query fails in
PostgreSQL because the column does not exist:

<!-- snippet: sample_docs_created_at_queries -->
<a id='snippet-sample_docs_created_at_queries'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // mt_created_at is opt in
    opts.Schema.For<ApiDocsOrder>().Metadata(m => m.CreatedAt.Enabled = true);
});

await using var session = store.QuerySession();

var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
var oneDayAgo = DateTimeOffset.UtcNow.AddDays(-1);

// Orders created between one day and one hour ago
var orders = await session.Query<ApiDocsOrder>()
    .Where(x => x.CreatedSince(oneDayAgo))
    .Where(x => x.CreatedBefore(oneHourAgo))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/DocumentApiDocsSamples.cs#L163-L184' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docs_created_at_queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Indexing

See [metadata index](/documents/indexing/metadata-indexes) for information on how to enable predefined
indexing
