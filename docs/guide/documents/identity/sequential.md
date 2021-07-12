# Sequential Identifiers with Hilo

The _Hilo_ sequence generation can be customized with either global defaults or document type specific overrides. By default, the Hilo sequence generation in Marten increments by 1 and uses a "maximum lo" number of 1000.

To set different global defaults, use the `StoreOptions.HiloSequenceDefaults` property like this sample:

<!-- snippet: sample_configuring-global-hilo-defaults -->
<a id='snippet-sample_configuring-global-hilo-defaults'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Advanced.HiloSequenceDefaults.MaxLo = 55;
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "sequences";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L63-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-hilo-defaults' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It's also possible to use one sequence with multiple document types by specifying the same "sequence name".

<!-- snippet: sample_configuring-global-hilo-defaults-sequencename -->
<a id='snippet-sample_configuring-global-hilo-defaults-sequencename'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Advanced.HiloSequenceDefaults.SequenceName = "Entity";
    _.Connection(ConnectionSource.ConnectionString);

    _.DatabaseSchemaName = "sequences";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L149-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configuring-global-hilo-defaults-sequencename' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To override the Hilo configuration for a specific document type, you can decorate the document type with the `[HiloSequence]` attribute
as in this example:

<!-- snippet: sample_overriding-hilo-with-attribute -->
<a id='snippet-sample_overriding-hilo-with-attribute'></a>
```cs
[HiloSequence(MaxLo = 66, SequenceName = "Entity")]
public class OverriddenHiloDoc
{
    public int Id { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L187-L193' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding-hilo-with-attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also use the `MartenRegistry` fluent interface to override the Hilo configuration for a document type as in this example:

<!-- snippet: sample_overriding-hilo-with-marten-registry -->
<a id='snippet-sample_overriding-hilo-with-marten-registry'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Overriding the Hilo settings for the document type "IntDoc"
    _.Schema.For<IntDoc>()
        .HiloSettings(new HiloSettings {MaxLo = 66});

    _.Connection(ConnectionSource.ConnectionString);

    _.DatabaseSchemaName = "sequences";
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L79-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_overriding-hilo-with-marten-registry' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Set the Identifier Floor

Marten 1.2 adds a convenience method to reset the "floor" of the Hilo sequence for a single document type:

<!-- snippet: sample_ResetHiloSequenceFloor -->
<a id='snippet-sample_resethilosequencefloor'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection(ConnectionSource.ConnectionString);
    opts.DatabaseSchemaName = "sequences";
});

// Resets the minimum Id number for the IntDoc document
// type to 2500
await store.Tenancy.Default.ResetHiloSequenceFloor<IntDoc>(2500);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#L18-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_resethilosequencefloor' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This functionality was added specifically to aid in importing data from an existing data source. Do note that this functionality simply guarantees
that all new id's assigned for the document type will be higher than the new floor. It is perfectly possible and even likely that there will be some
gaps in the id sequence.
