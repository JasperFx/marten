# The Patching API

::: tip INFO
Using the Patching API in Marten requires the usage of Postgresql's [PLV8 extension](https://github.com/plv8/plv8).
:::

Marten's Patching API is a mechanism to update persisted documents without having to first load the document into memory.
"Patching" can be much more efficient at runtime in some scenarios because you avoid the "deserialize from JSON, edit, serialize
back to JSON" workflow.

As of 1.2, Marten supports mechanisms to:

1. Set the value of a persisted field or property
1. Add a field or property with value
1. Duplicate a field or property to one or more destinations
1. Increment a numeric value by some increment (1 by default)
1. Append an element to a child array, list, or collection at the end
1. Insert an element into a child array, list, or collection at a given position
1. Remove an element from a child array, list, or collection
1. Rename a persisted field or property to a new name for structural document changes
1. Delete a persisted field or property

The patch operation can be configured to either execute against a single document by supplying its id, or with a _Where_ clause expression.
In all cases, the property or field being updated can be a deep accessor like `Target.Inner.Color`.

## Patching by Where Expression

To apply a patch to all documents matching a given criteria, use the following syntax:

<!-- snippet: sample_set_an_immediate_property_by_where_clause -->
<!-- endSnippet -->

## Setting a single Property/Field

The usage of `IDocumentSession.Patch().Set()` to change the value of a single persisted field is
shown below:

<!-- snippet: sample_set_an_immediate_property_by_id -->
<!-- endSnippet -->

## Initialize a new Property/Field

To initialize a new property on existing documents:

<!-- snippet: sample_initialise_a_new_property_by_expression -->
<!-- endSnippet -->

## Duplicating an existing Property/Field

To copy an existing value to a new location:

<!-- snippet: sample_duplicate_to_new_field -->
<!-- endSnippet -->

The same value can be copied to multiple new locations:

<!-- snippet: sample_duplicate_to_multiple_new_fields -->
<!-- endSnippet -->

The new locations need not exist in the persisted document, null or absent parents will be initialized

## Incrementing an Existing Value

To increment a persisted value in the persisted document, use this operation:

<!-- snippet: sample_increment_for_int -->
<!-- endSnippet -->

By default, the `Patch.Increment()` operation will add 1 to the existing value. You can optionally override the increment:

<!-- snippet: sample_increment_for_int_with_explicit_increment -->
<!-- endSnippet -->

## Append an Element to a Child Collection

<[warning]>
Because the Patching API depends on comparisons to the underlying serialized JSON in the database, the `DateTime` or `DateTimeOffset` types will frequently miss on comparisons for timestamps because of insufficient precision.
<[/warning]>

The `Patch.Append()` operation adds a new item to the end of a child collection:

<!-- snippet: sample_append_complex_element -->
<!-- endSnippet -->

The `Patch.AppendIfNotExists()` operation will treat the child collection as a set rather than a list and only append the element if it does not already exist within the collection

Marten can append either complex, value object values or primitives like numbers or strings.

## Insert an Element into a Child Collection

Instead of appending an item to the end of a child collection, the `Patch.Insert()` operation allows you
to insert a new item into a persisted collection with a given index -- with the default index
being 0 so that a new item would be inserted at the beginning of the child collection.

<!-- snippet: sample_insert_first_complex_element -->
<!-- endSnippet -->

The `Patch.InsertIfNotExists()` operation will only insert the element if the element at the designated index does not already exist.

## Remove an Element from a Child Collection

The `Patch.Remove()` operation removes the given item from a child collection:

<!-- snippet: sample_remove_primitive_element -->
<!-- endSnippet -->

Removing complex items can also be accomplished, matching is performed on all fields:

<!-- snippet: sample_remove_complex_element -->
<!-- endSnippet -->

To remove reoccurring values from a collection specify `RemoveAction.RemoveAll`:

<!-- snippet: sample_remove_repeated_primitive_element -->
<!-- endSnippet -->

## Rename a Property/Field

In the case of changing the name of a property or field in your document type that's already persisted
in your Marten database, you have the option to apply a patch that will move the value from the
old name to the new name.

<!-- snippet: sample_rename_deep_prop -->
<!-- endSnippet -->

Renaming can be used on nested values.

## Delete a Property/Field

The `Patch.Delete()` operation can be used to remove a persisted property or field without the need
to load, deserialize, edit and save all affected documents

To delete a redundant property no longer available on the class use the string overload:

<!-- snippet: sample_delete_redundant_property -->
<!-- endSnippet -->

To delete a redundant property nested on a child class specify a location lambda:

<!-- snippet: sample_delete_redundant_nested_property -->
<!-- endSnippet -->

A current property may be erased simply with a lambda:

<!-- snippet: sample_delete_existing_property -->
<!-- endSnippet -->

Many documents may be patched using a where expressions:

<!-- snippet: sample_delete_property_from_many_documents -->
<!-- endSnippet -->
