<!--Title:The Patching API-->

<div class="alert alert-info">
Using the Patching API in Marten requires the usage of Postgresql's <a href="https://github.com/plv8/plv8">PLV8 extension</a>. 
</div>

Marten's Patching API is a mechanism to update persisted documents without having to first load the document into memory.
"Patching" can be much more efficient at runtime in some scenarios because you avoid the "deserialize from JSON, edit, serialize
back to JSON" workflow.

As of 1.0, Marten supports mechanisms to:

1. Set the value of a persisted field or property
1. Increment a numeric value by some increment (1 by default)
1. Append an element to a child array, list, or collection at the end
1. Insert an element into a child array, list, or collection at a given position
1. Rename a persisted field or property to a new name for structural document changes

The patch operation can be configured to either execute against a single document by supplying its id, or with a _Where_ clause expression.
In all cases, the property or field being updated can be a deep accessor like `Target.Inner.Color`.

## Patching by Where Expression

To apply a patch to all documents matching a given criteria, use the following syntax:

<[sample:set_an_immediate_property_by_where_clause]>

## Setting a single Property/Field

The usage of `IDocumentSession.Patch().Set()` to change the value of a single persisted field is 
shown below:

<[sample:set_an_immediate_property_by_id]>


## Incrementing an Existing Value

To increment a persisted value in the persisted document, use this operation:

<[sample:increment_for_int]>

By default, the `Increment()` operation will add 1 to the existing value. You can optionally override the increment:

<[sample:increment_for_int_with_explicit_increment]>

## Append an Element to a Child Collection

The `Patch.Append()` operation adds a new item to the end of a child collection:

<[sample:append_complex_element]>

Marten can append either complex, value object values or primitives like numbers or strings.

## Insert an Element into a Child Collection

Instead of adding an item to the end of a child collection, the `Insert()` operation allows you
to put a new item into a persisted collection with a given index -- with the default index
being 0 so that a new item would be inserted at the beginning of the child collection.

<[sample:insert_first_complex_element]>

## Rename a Property/Field

In the case of changing the name of a property or field in your document type that's already persisted 
in your Marten database, you have the option to apply a patch that will move the value from the 
old name to the new name. 

<[sample:rename_deep_prop]>

Renaming can be used on nested values.

