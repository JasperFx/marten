# Unique Indexes

[Unique Indexes](https://www.postgresql.org/docs/current/static/indexes-unique.html) are used to enforce uniqueness of property value. They can be combined to handle also uniqueness of multiple properties.

Marten supports both [duplicate fields](/guide/documents/configuration/duplicated-fields) and [calculated indexes](/guide/documents/configuration/computed-indexes) uniqueness. Using Duplicated Field brings benefits to queries but brings additional complexity, while Computed Index reuses current JSON structure without adding additional db column.

## Defining Unique Index through Store options

Unique Indexes can be created using the fluent interface of `StoreOptions` like this:

1. **Computed**:

- single property

<!-- snippet: sample_using_a_single_property_computed_unique_index_through_store_options -->
<a id='snippet-sample_using_a_single_property_computed_unique_index_through_store_options'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "unique_text";

    // This creates
    _.Schema.For<User>().UniqueIndex(UniqueIndexType.Computed, x => x.UserName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L70-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_computed_unique_index_through_store_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_computed_unique_index_through_store_options -->
<a id='snippet-sample_using_a_multiple_properties_computed_unique_index_through_store_options'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "unique_text";

    // This creates
    _.Schema.For<User>().UniqueIndex(UniqueIndexType.Computed, x => x.FirstName, x => x.FullName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L100-L109' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_multiple_properties_computed_unique_index_through_store_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
If you don't specify first parameter (index type) - by default it will be created as computed index.
:::

1. **Duplicated field**:

- single property

<!-- snippet: sample_using_a_single_property_duplicate_field_unique_index_through_store_options -->
<a id='snippet-sample_using_a_single_property_duplicate_field_unique_index_through_store_options'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "unique_text";

    // This creates
    _.Schema.For<User>().UniqueIndex(UniqueIndexType.DuplicatedField, x => x.UserName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L85-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_duplicate_field_unique_index_through_store_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_duplicate_field_unique_index_through_store_options -->
<a id='snippet-sample_using_a_multiple_properties_duplicate_field_unique_index_through_store_options'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "unique_text";

    // This creates
    _.Schema.For<User>().UniqueIndex(UniqueIndexType.DuplicatedField, x => x.FirstName, x => x.FullName);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L115-L124' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_multiple_properties_duplicate_field_unique_index_through_store_options' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Defining Unique Index through Attribute

Unique Indexes can be created using the `[UniqueIndex]` attribute like this:

1. **Computed**:

- single property

<!-- snippet: sample_using_a_single_property_computed_unique_index_through_attribute -->
<a id='snippet-sample_using_a_single_property_computed_unique_index_through_attribute'></a>
```cs
public class Account
{
    public Guid Id { get; set; }

    [UniqueIndex(IndexType = UniqueIndexType.Computed)]
    public string Number { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L13-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_computed_unique_index_through_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_computed_unique_index_through_store_attribute -->
<a id='snippet-sample_using_a_multiple_properties_computed_unique_index_through_store_attribute'></a>
```cs
public class Address
{
    private const string UniqueIndexName = "sample_uidx_person";

    public Guid Id { get; set; }

    [UniqueIndex(IndexType = UniqueIndexType.Computed, IndexName = UniqueIndexName)]
    public string Street { get; set; }

    [UniqueIndex(IndexType = UniqueIndexType.Computed, IndexName = UniqueIndexName)]
    public string Number { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L35-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_multiple_properties_computed_unique_index_through_store_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
If you don't specify IndexType parameter - by default it will be created as computed index.
:::

1. **Duplicated field**:

- single property

<!-- snippet: sample_using_a_single_property_duplicate_field_unique_index_through_store_attribute -->
<a id='snippet-sample_using_a_single_property_duplicate_field_unique_index_through_store_attribute'></a>
```cs
public class Client
{
    public Guid Id { get; set; }

    [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField)]
    public string Name { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L24-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_single_property_duplicate_field_unique_index_through_store_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_duplicate_field_unique_index_through_attribute -->
<a id='snippet-sample_using_a_multiple_properties_duplicate_field_unique_index_through_attribute'></a>
```cs
public class Person
{
    private const string UniqueIndexName = "sample_uidx_person";

    public Guid Id { get; set; }

    [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField, IndexName = UniqueIndexName)]
    public string FirstName { get; set; }

    [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField, IndexName = UniqueIndexName)]
    public string SecondName { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L51-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_a_multiple_properties_duplicate_field_unique_index_through_attribute' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip INFO
To group multiple properties into single index you need to specify the same values in `IndexName` parameters.
:::

## Defining Unique Index through Index customization

You have some ability to extend to Computed Index definition to be unique index by passing a second Lambda `Action` into
the `Index()` method and definining `IsUnique` property as `true` as shown below:

<!-- snippet: sample_customizing-calculated-index -->
<a id='snippet-sample_customizing-calculated-index'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    // The second, optional argument to Index()
    // allows you to customize the calculated index
    _.Schema.For<Target>().Index(x => x.Number, x =>
            {
                // Change the index method to "brin"
                x.Method = IndexMethod.brin;

                // Force the index to be generated with casing rules
                x.Casing = ComputedIndex.Casings.Lower;

                // Override the index name if you want
                x.Name = "mt_my_name";

                // Toggle whether or not the index is concurrent
                // Default is false
                x.IsConcurrent = true;

                // Toggle whether or not the index is a UNIQUE
                // index
                x.IsUnique = true;

                // Toggle whether index value will be constrained unique in scope of whole document table (Global)
                // or in a scope of a single tenant (PerTenant)
                // Default is Global
                x.TenancyScope = Schema.Indexing.Unique.TenancyScope.PerTenant;

                // Partial index by supplying a condition
                x.Predicate = "(data ->> 'Number')::int > 10";
            });

    // For B-tree indexes, it's also possible to change
    // the sort order from the default of "ascending"
    _.Schema.For<User>().Index(x => x.LastName, x =>
            {
                // Change the index method to "brin"
                x.SortOrder = SortOrder.Desc;
            });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/computed_indexes.cs#L80-L123' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_customizing-calculated-index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Same can be configured for Duplicated Field:

<!-- snippet: sample_IndexExamples -->
<a id='snippet-sample_indexexamples'></a>
```cs
var store = DocumentStore.For(options =>
{
    // Add a gin index to the User document type
    options.Schema.For<User>().GinIndexJsonData();

    // Adds a basic btree index to the duplicated
    // field for this property that also overrides
    // the Postgresql database type for the column
    options.Schema.For<User>().Duplicate(x => x.FirstName, pgType: "varchar(50)");

    // Defining a duplicate column with not null constraint
    options.Schema.For<User>().Duplicate(x => x.Department, pgType: "varchar(50)", notNull: true);

    // Customize the index on the duplicated field
    // for FirstName
    options.Schema.For<User>().Duplicate(x => x.FirstName, configure: idx =>
    {
        idx.Name = "idx_special";
        idx.Method = IndexMethod.hash;
    });

    // Customize the index on the duplicated field
    // for UserName to be unique
    options.Schema.For<User>().Duplicate(x => x.UserName, configure: idx =>
    {
        idx.IsUnique = true;
    });

    // Customize the index on the duplicated field
    // for LastName to be in descending order
    options.Schema.For<User>().Duplicate(x => x.LastName, configure: idx =>
    {
        idx.SortOrder = SortOrder.Desc;
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/MartenRegistryExamples.cs#L51-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_indexexamples' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Unique Index per Tenant

For tables which have been configured for [tenancy](/guide/documents/tenancy), index definitions may also be scoped per tenant.

<!-- snippet: sample_per-tenant-unique-index -->
<a id='snippet-sample_per-tenant-unique-index'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);
    _.DatabaseSchemaName = "unique_text";

    // This creates a duplicated field unique index on firstname, lastname and tenant_id
    _.Schema.For<User>().MultiTenanted().UniqueIndex(UniqueIndexType.DuplicatedField, "index_name", TenancyScope.PerTenant, x => x.FirstName, x => x.LastName);

    // This creates a computed unique index on client name and tenant_id
    _.Schema.For<Client>().MultiTenanted().UniqueIndex(UniqueIndexType.Computed, "index_name", TenancyScope.PerTenant, x => x.Name);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Acceptance/unique_indexes.cs#L130-L142' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_per-tenant-unique-index' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
