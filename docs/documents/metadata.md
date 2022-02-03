# Marten Metadata

A major goal of the Marten V4 release was to enable much richer document and event metadata collection based
on user requests. To that end, Marten still supports the same basic metadata columns
as Marten V2/V3, but adds other **opt in** columns.

The available columns for document storage are:

|Column Name|Description|Enabled by Default|
|-----------|-----------|------------------|
|`mt_last_modified`|Timestamp of the last time the document record was modified|Yes|
|`mt_version`|`Guid` value that marks the current version of the document. This supports optimistic concurrency|Yes|
|`mt_dotnet_type`|Assembly qualified name of the .Net type persisted to this row|Yes|
|`correlation_id`|User-supplied correlation identifier (`string`)|No, opt in|
|`causation_id`|User-supplied causation identifier (`string`)|No, opt in|
|`headers`|User-supplied key/value pairs for extensible metadata|No, opt in|
|`mt_deleted`|Boolean flag noting whether the document is soft-deleted|Only if the document type is configured as soft-deleted|
|`mt_deleted_at`|Timestamp marking when a document was soft-deleted|Only if the document type is configured as soft-deleted|

## Correlation Id, Causation Id, and Headers

::: tip
At this point, the Marten team thinks that using a custom `ISessionFactory` to set
correlation and causation data is the most likely usage for this feature. The Marten team
plans to build a sample application showing Marten being used with [Open Telemetry](https://opentelemetry.io/) tracing soon.
:::

The first step is to enable these columns on the document types in your system:

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
    });

    // Or just globally turn on columns for all document
    // types in one fell swoop
    opts.Policies.ForAllDocuments(x =>
    {
        x.Metadata.CausationId.Enabled = true;
        x.Metadata.CorrelationId.Enabled = true;
        x.Metadata.Headers.Enabled = true;
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L27-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enabling_causation_fields' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L56-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_setting_metadata_on_session' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L68-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_set_header' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Tracking Metadata on Documents

Marten can now set metadata values directly on the documents persisted by Marten,
but this is an opt in behavior. You can explicitly map a public member of your document
type to a metadata value individually. Let's say that you have a document type like
this where you want to track metadata:

<!-- snippet: sample_DocWithMetadata -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L77-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_docwithmetadata' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L94-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_explicitly_map_metadata' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
Note that mapping a document member to a metadata column will implicitly enable that metadata column collection.
:::

For correlation, causation, and last modified tracking, an easy way to do this is to
just implement the Marten `ITracked` interface as shown below:

<!-- snippet: sample_MyTrackedDoc -->
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

<!-- snippet: sample_MyVersionedDoc -->
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

If you want Marten to run lean, you can omit all metadata fields from Marten with this configuration:

<!-- snippet: sample_DisableAllInformationalFields -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MetadataUsage.cs#L11-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_disableallinformationalfields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Metadata/last_modified_queries.cs#L16-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_last_modified_queries' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Indexing

See [metadata index](/documents/indexing/metadata-indexes) for information on how to enable predefined
indexing
