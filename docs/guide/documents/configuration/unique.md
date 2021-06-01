# Unique Indexes

[Unique Indexes](https://www.postgresql.org/docs/current/static/indexes-unique.html) are used to enforce uniqueness of property value. They can be combined to handle also uniqueness of multiple properties.

Marten supports both [duplicate fields](/guide/documents/configuration/duplicated-fields) and [calculated indexes](/guide/documents/configuration/computed-indexes) uniqueness. Using Duplicated Field brings benefits to queries but brings additional complexity, while Computed Index reuses current JSON structure without adding additional db column.

## Defining Unique Index through Store options

Unique Indexes can be created using the fluent interface of `StoreOptions` like this:

1. **Computed**:

- single property

<!-- snippet: sample_using_a_single_property_computed_unique_index_through_store_options -->
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_computed_unique_index_through_store_options -->
<!-- endSnippet -->

::: tip INFO
If you don't specify first parameter (index type) - by default it will be created as computed index.
:::

1. **Duplicated field**:

- single property

<!-- snippet: sample_using_a_single_property_duplicate_field_unique_index_through_store_options -->
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_duplicate_field_unique_index_through_store_options -->
<!-- endSnippet -->

## Defining Unique Index through Attribute

Unique Indexes can be created using the `[UniqueIndex]` attribute like this:

1. **Computed**:

- single property

<!-- snippet: sample_using_a_single_property_computed_unique_index_through_attribute -->
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_computed_unique_index_through_store_attribute -->
<!-- endSnippet -->

::: tip INFO
If you don't specify IndexType parameter - by default it will be created as computed index.
:::

1. **Duplicated field**:

- single property

<!-- snippet: sample_using_a_single_property_duplicate_field_unique_index_through_store_attribute -->
<!-- endSnippet -->

- multiple properties

<!-- snippet: sample_using_a_multiple_properties_duplicate_field_unique_index_through_attribute -->
<!-- endSnippet -->

::: tip INFO
To group multiple properties into single index you need to specify the same values in `IndexName` parameters.
:::

## Defining Unique Index through Index customization

You have some ability to extend to Computed Index definition to be unique index by passing a second Lambda `Action` into
the `Index()` method and definining `IsUnique` property as `true` as shown below:

<!-- snippet: sample_customizing-calculated-index -->
<!-- endSnippet -->

Same can be configured for Duplicated Field:

<!-- snippet: sample_IndexExamples -->
<!-- endSnippet -->

## Unique Index per Tenant

For tables which have been configured for [tenancy](/guide/documents/tenancy), index definitions may also be scoped per tenant.

<!-- snippet: sample_per-tenant-unique-index -->
<!-- endSnippet -->
