# Schema Feature Extensions

New in Marten 2.4.0 is the ability to add additional features with custom database schema objects that simply plug into Marten's
[schema management facilities)[/guide/schema/migrations). The key abstraction is the `IFeatureSchema` interface shown below:

<!-- snippet: sample_IFeatureSchema -->
<a id='snippet-sample_ifeatureschema'></a>
```cs
/// <summary>
/// Defines the database objects for a named feature within your
/// Marten application
/// </summary>
public interface IFeatureSchema
{
    /// <summary>
    /// Any document or feature types that this feature depends on. Used
    /// to intelligently order the creation and scripting of database
    /// schema objects
    /// </summary>
    /// <returns></returns>
    IEnumerable<Type> DependentTypes();

    /// <summary>
    /// All the schema objects in this feature
    /// </summary>
    ISchemaObject[] Objects { get; }

    /// <summary>
    /// Identifier by type for this feature. Used along with the DependentTypes()
    /// collection to control the proper ordering of object creation or scripting
    /// </summary>
    Type StorageType { get; }

    /// <summary>
    /// Really just the filename when the SQL is exported
    /// </summary>
    string Identifier { get; }

    /// <summary>
    /// Write any permission SQL when this feature is exported to a SQL
    /// file
    /// </summary>
    /// <param name="rules"></param>
    /// <param name="writer"></param>
    void WritePermissions(DdlRules rules, TextWriter writer);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Storage/IFeatureSchema.cs#L11-L51' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ifeatureschema' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Not to worry though, Marten comes with a base class that makes it a bit simpler to build out new features. Here's a very simple
example that defines a custom table with one column:

<!-- snippet: sample_creating-a-fake-schema-feature -->
<a id='snippet-sample_creating-a-fake-schema-feature'></a>
```cs
public class FakeStorage : FeatureSchemaBase
{
    private readonly StoreOptions _options;

    public FakeStorage(StoreOptions options) : base("fake")
    {
        _options = options;
    }

    protected override IEnumerable<ISchemaObject> schemaObjects()
    {
        var table = new Table(new DbObjectName(_options.DatabaseSchemaName, "mt_fake_table"));
        table.AddColumn("name", "varchar");

        yield return table;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/ability_to_add_custom_storage_features.cs#L50-L69' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_creating-a-fake-schema-feature' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, to actually apply this feature to your Marten applications, use this syntax:

<!-- snippet: sample_adding-schema-feature -->
<a id='snippet-sample_adding-schema-feature'></a>
```cs
var store = DocumentStore.For(_ =>
{
    // Creates a new instance of FakeStorage and
    // passes along the current StoreOptions
    _.Storage.Add<FakeStorage>();

    // or

    _.Storage.Add(new FakeStorage(_));
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/ability_to_add_custom_storage_features.cs#L31-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_adding-schema-feature' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that when you use the `Add<T>()` syntax, Marten will pass along the current `StoreOptions` to the constructor function if there is a constructor with that signature. Otherwise, it uses the no-arg constructor.

While you *can* directly implement the `ISchemaObject` interface for something Marten doesn't already support, it's probably far easier to just configure one of the existing implementations shown in the following sections.

* `Table`
* `Function`
* `Sequence`

## Table

Postgresql tables can be modeled with the `Table` class as shown in this example from the event store inside of Marten:

<!-- snippet: sample_EventsTable -->
<a id='snippet-sample_eventstable'></a>
```cs
internal class EventsTable: Table
{
    public EventsTable(EventGraph events): base(new DbObjectName(events.DatabaseSchemaName, "mt_events"))
    {
        AddColumn(new EventTableColumn("seq_id", x => x.Sequence)).AsPrimaryKey();
        AddColumn(new EventTableColumn("id", x => x.Id)).NotNull();
        AddColumn(new StreamIdColumn(events));

        AddColumn(new EventTableColumn("version", x => x.Version)).NotNull();
        AddColumn<EventJsonDataColumn>();
        AddColumn<EventTypeColumn>();
        AddColumn(new EventTableColumn("timestamp", x => x.Timestamp))
            .NotNull().DefaultValueByString("(now())");

        AddColumn<TenantIdColumn>();

        AddColumn<DotNetTypeColumn>().AllowNulls();

        AddIfActive(events.Metadata.CorrelationId);
        AddIfActive(events.Metadata.CausationId);
        AddIfActive(events.Metadata.Headers);

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_tenant_id")
            {
                ColumnNames = new string[]{"stream_id", TenantIdColumn.Name},
                LinkedNames = new string[]{"id", TenantIdColumn.Name},
                LinkedTable = new DbObjectName(events.DatabaseSchemaName, "mt_streams")
            });

            Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
            {
                IsUnique = true,
                Columns = new string[]{"stream_id", TenantIdColumn.Name, "version"}
            });
        }
        else
        {
            ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id")
            {
                ColumnNames = new string[]{"stream_id"},
                LinkedNames = new string[]{"id"},
                LinkedTable = new DbObjectName(events.DatabaseSchemaName, "mt_streams"),
                OnDelete = CascadeAction.Cascade
            });

            Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
            {
                IsUnique = true,
                Columns = new string[]{"stream_id", "version"}
            });
        }

        Indexes.Add(new IndexDefinition("pk_mt_events_id_unique")
        {
            Columns = new string[]{"id"},
            IsUnique = true
        });

        AddColumn<IsArchivedColumn>();
    }

    internal IList<IEventTableColumn> SelectColumns()
    {
        var columns = new List<IEventTableColumn>();
        columns.AddRange(Columns.OfType<IEventTableColumn>());

        var data = columns.OfType<EventJsonDataColumn>().Single();
        var typeName = columns.OfType<EventTypeColumn>().Single();
        var dotNetTypeName = columns.OfType<DotNetTypeColumn>().Single();

        columns.Remove(data);
        columns.Insert(0, data);
        columns.Remove(typeName);
        columns.Insert(1, typeName);
        columns.Remove(dotNetTypeName);
        columns.Insert(2, dotNetTypeName);

        return columns;
    }

    private void AddIfActive(MetadataColumn column)
    {
        if (column.Enabled)
        {
            AddColumn(column);
        }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Schema/EventsTable.cs#L13-L107' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_eventstable' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Function

Postgresql functions can be managed by creating a subclass of the `Function` base class as shown below from the big "append event" function in the event store:

// TODO: Add sample

## Sequence

[Postgresql sequences](https://www.postgresql.org/docs/10/static/sql-createsequence.html) can be managed with this usage:

<!-- snippet: sample_using-sequence -->
<a id='snippet-sample_using-sequence'></a>
```cs
var sequence = new Sequence(new DbObjectName(DatabaseSchemaName, "mt_events_sequence"))
{
    Owner = eventsTable.Identifier,
    OwnerColumn = "seq_id"
};
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/EventGraph.FeatureSchema.cs#L32-L38' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using-sequence' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
