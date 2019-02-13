<!--title:Unique Indexes-->

[Unique Indexes](https://www.postgresql.org/docs/current/static/indexes-unique.html) are used to enforce uniqueness of property value. They can be combined to handle also uniqueness of multiple properties.

Marten supports both <[linkto:documentation/documents/configuration/duplicated_fields]> and <[linkto:documentation/documents/configuration/computed_index]> uniqueness. Using Duplicated Field brings benefits to queries but brings additional complexity, while Computed Index reuses current JSON structure without adding additional db column.

## Definining Unique Index through Store options

Unique Indexes can be created using the fluent interface of `StoreOptions` like this: 

1. **Computed**:
* single property

<[sample:using_a_single_property_computed_unique_index_through_store_options]>

* multiple properties

<[sample:using_a_multiple_properties_computed_unique_index_through_store_options]>

<div class="alert alert-info">
If you don't specify first parameter (index type) - by default it will be created as computed index.
</div>

2. **Duplicated field**:
* single property

<[sample:using_a_single_property_duplicate_field_unique_index_through_store_options]>

* multiple properties

<[sample:using_a_multiple_properties_duplicate_field_unique_index_through_store_options]>

## Defining Unique Index through Attribute

Unique Indexes can be created using the `[UniqueIndex]` attribute like this: 

1. **Computed**:
* single property

<[sample:using_a_single_property_computed_unique_index_through_attribute]>

* multiple properties

<[sample:using_a_multiple_properties_computed_unique_index_through_store_attribute]>


<div class="alert alert-info">
If you don't specify IndexType parameter - by default it will be created as computed index.
</div>


2. **Duplicated field**:
* single property

<[sample:using_a_single_property_duplicate_field_unique_index_through_store_attribute]>

* multiple properties

<[sample:using_a_multiple_properties_duplicate_field_unique_index_through_attribute]>

<div class="alert alert-info">
To group multiple properties into single index you need to specify the same values in `IndexName` parameters.
</div>


## Defining Unique Index through Index customization

You have some ability to extend to Computed Index definition to be unique index  by passing a second Lambda `Action` into
the `Index()` method and definining `IsUnique` property as `true` as shown below:

<[sample:customizing-calculated-index]>

Same can be configured for Duplicated Field:

<[sample:IndexExamples]>