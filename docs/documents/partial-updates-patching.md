# Partial updates/patching

Partial update or patching JSON involves making changes to specific parts of a JSON document without replacing the entire document. This is particularly useful when you only need to modify certain fields or elements within a large JSON object, rather than rewriting the entire structure.

Starting with Marten v7.x, there is native partial updates or patching support available in core library using a pure Postgres PL/pgSQL and JSON operators based implementation. Marten earlier supported a PLV8 based patching a separate opt-in plugin `Marten.PLv8`. This library will be deprecated and we recommend users to use this new native patching functionality.

## Patching API

Marten's Patching API is a mechanism to update persisted documents without having to first load the document into memory.
"Patching" can be much more efficient at runtime in some scenarios because you avoid the "deserialize from JSON, edit, serialize back to JSON" workflow.

The following are the supported operations:

- Set the value of a persisted field or property
- Add a new field or property with value
- Duplicate a field or property to one or more destinations
- Increment a numeric value by some increment (1 by default)
- Append an element to a child array, list, or collection at the end
- Insert an element into a child array, list, or collection at a given position
- Remove an element from a child array, list, or collection
- Rename a persisted field or property to a new name for structural document changes
- Delete a persisted field or property
- Patching multiple fields with the combination of the above operations using a fluent API. Note that the PLV8 based API was able to do only one patch operation per DB call.

To use the native patching include `using Marten.Patching;`. If you want to migrate from PLV8 based patching, change `using Marten.PLv8.Patching;` to `using Marten.Patching;` and all the API will work "as is" since we managed to retain the API the same.

The patch operation can be configured to either execute against a single document by supplying its id, or with a _Where_ clause expression.
In all cases, the property or field being updated can be a deep accessor like `Target.Inner.Color`.

## Patch by Where Expression

To apply a patch to all documents matching a given criteria, use the following syntax:

<!-- snippet: sample_patching_set_an_immediate_property_by_where_clause -->
<a id='snippet-sample_patching_set_an_immediate_property_by_where_clause'></a>
```cs
// Change every Target document where the Color is Blue
theSession.Patch<Target>(x => x.Color == Colors.Blue).Set(x => x.Number, 2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L107-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_set_an_immediate_property_by_where_clause' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Set a single Property/Field

The usage of `IDocumentSession.Patch().Set()` to change the value of a single persisted field is
shown below:

<!-- snippet: sample_patching_set_an_immediate_property_by_id -->
<a id='snippet-sample_patching_set_an_immediate_property_by_id'></a>
```cs
[Fact]
public async Task set_an_immediate_property_by_id()
{
    var target = Target.Random(true);
    target.Number = 5;

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Set(x => x.Number, 10);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(10);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L35-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_set_an_immediate_property_by_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Set a new Property/Field

To initialize a new property on existing documents:

<!-- snippet: sample_patching_initialise_a_new_property_by_expression -->
<a id='snippet-sample_patching_initialise_a_new_property_by_expression'></a>
```cs
const string where = "(data ->> 'UpdatedAt') is null";
theSession.Query<Target>(where).Count.ShouldBe(3);
theSession.Patch<Target>(new WhereFragment(where)).Set("UpdatedAt", DateTime.UtcNow);
await theSession.SaveChangesAsync();

using (var query = theStore.QuerySession())
{
    query.Query<Target>(where).Count.ShouldBe(0);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L63-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_initialise_a_new_property_by_expression' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Duplicate an existing Property/Field

To copy an existing value to a new location:

<!-- snippet: sample_patching_duplicate_to_new_field -->
<a id='snippet-sample_patching_duplicate_to_new_field'></a>
```cs
var target = Target.Random();
target.AnotherString = null;
theSession.Store(target);
await theSession.SaveChangesAsync();

theSession.Patch<Target>(target.Id).Duplicate(t => t.String, t => t.AnotherString);
await theSession.SaveChangesAsync();

using (var query = theStore.QuerySession())
{
    var result = query.Load<Target>(target.Id);
    result.AnotherString.ShouldBe(target.String);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L131-L145' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_duplicate_to_new_field' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The same value can be copied to multiple new locations:

<!-- snippet: sample_patching_duplicate_to_multiple_new_fields -->
<a id='snippet-sample_patching_duplicate_to_multiple_new_fields'></a>
```cs
theSession.Patch<Target>(target.Id).Duplicate(t => t.String,
    t => t.StringField,
    t => t.Inner.String,
    t => t.Inner.AnotherString);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L157-L162' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_duplicate_to_multiple_new_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The new locations need not exist in the persisted document, null or absent parents will be initialized

## Increment an Existing Value

To increment a persisted value in the persisted document, use this operation:

<!-- snippet: sample_patching_increment_for_int -->
<a id='snippet-sample_patching_increment_for_int'></a>
```cs
[Fact]
public async Task increment_for_int()
{
    var target = Target.Random();
    target.Number = 6;

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Increment(x => x.Number);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(7);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L176-L195' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_increment_for_int' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default, the `Patch.Increment()` operation will add 1 to the existing value. You can optionally override the increment:

<!-- snippet: sample_patching_increment_for_int_with_explicit_increment -->
<a id='snippet-sample_patching_increment_for_int_with_explicit_increment'></a>
```cs
[Fact]
public async Task increment_for_int_with_explicit_increment()
{
    var target = Target.Random();
    target.Number = 6;

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Increment(x => x.Number, 3);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(9);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L197-L216' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_increment_for_int_with_explicit_increment' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Append Element to a Child Collection

::: warning
Because the Patching API depends on comparisons to the underlying serialized JSON in the database, the `DateTime` or `DateTimeOffset` types will frequently miss on comparisons for timestamps because of insufficient precision.
:::

The `Patch.Append()` operation adds a new item to the end of a child collection:

<!-- snippet: sample_patching_append_complex_element -->
<a id='snippet-sample_patching_append_complex_element'></a>
```cs
[Fact]
public async Task append_complex_element()
{
    var target = Target.Random(true);
    var initialCount = target.Children.Length;

    var child = Target.Random();

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount + 1);

        target2.Children.Last().Id.ShouldBe(child.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L336-L360' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_append_complex_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Patch.AppendIfNotExists()` operation will treat the child collection as a set rather than a list and only append the element if it does not already exist within the collection

Marten can append either complex, value object values or primitives like numbers or strings.

### Insert an Element into a Child Collection

Instead of appending an item to the end of a child collection, the `Patch.Insert()` operation allows you
to insert a new item into a persisted collection with a given index -- with the default index
being 0 so that a new item would be inserted at the beginning of the child collection.

<!-- snippet: sample_patching_insert_first_complex_element -->
<a id='snippet-sample_patching_insert_first_complex_element'></a>
```cs
[Fact]
public async Task insert_first_complex_element()
{
    var target = Target.Random(true);
    var initialCount = target.Children.Length;

    var child = Target.Random();

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount + 1);

        target2.Children.Last().Id.ShouldBe(child.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L528-L552' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_insert_first_complex_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Patch.InsertIfNotExists()` operation will only insert the element if the element at the designated index does not already exist.

## Remove Element from a Child Collection

The `Patch.Remove()` operation removes the given item from a child collection:

<!-- snippet: sample_patching_remove_primitive_element -->
<a id='snippet-sample_patching_remove_primitive_element'></a>
```cs
[Fact]
public async Task remove_primitive_element()
{
    var random = new Random();
    var target = Target.Random();
    target.NumberArray = new[] { random.Next(0, 10), random.Next(0, 10), random.Next(0, 10) };
    target.NumberArray = target.NumberArray.Distinct().ToArray();

    var initialCount = target.NumberArray.Length;

    var child = target.NumberArray[random.Next(0, initialCount)];

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.NumberArray.Length.ShouldBe(initialCount - 1);

        target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.ExceptFirst(child));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L689-L718' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_remove_primitive_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Removing complex items can also be accomplished, matching is performed on all fields:

<!-- snippet: sample_patching_remove_complex_element -->
<a id='snippet-sample_patching_remove_complex_element'></a>
```cs
[Fact]
public async Task remove_complex_element()
{
    var target = Target.Random(true);
    var initialCount = target.Children.Length;

    var random = new Random();
    var child = target.Children[random.Next(0, initialCount)];

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Remove(x => x.Children, child);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount - 1);

        target2.Children.ShouldNotContain(t => t.Id == child.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L758-L783' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_remove_complex_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To remove reoccurring values from a collection specify `RemoveAction.RemoveAll`:

<!-- snippet: sample_patching_remove_repeated_primitive_element -->
<a id='snippet-sample_patching_remove_repeated_primitive_element'></a>
```cs
[Fact]
public async Task remove_repeated_primitive_elements()
{
    var random = new Random();
    var target = Target.Random();
    target.NumberArray = new[] { random.Next(0, 10), random.Next(0, 10), random.Next(0, 10) };
    target.NumberArray = target.NumberArray.Distinct().ToArray();

    var initialCount = target.NumberArray.Length;

    var child = target.NumberArray[random.Next(0, initialCount)];
    var occurances = target.NumberArray.Count(e => e == child);
    if (occurances < 2)
    {
        target.NumberArray = target.NumberArray.Concat(new[] { child }).ToArray();
        ++occurances;
        ++initialCount;
    }

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child, RemoveAction.RemoveAll);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.NumberArray.Length.ShouldBe(initialCount - occurances);

        target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.Except(new[] { child }));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L720-L756' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_remove_repeated_primitive_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Rename a Property/Field

In the case of changing the name of a property or field in your document type that's already persisted
in your Marten database, you have the option to apply a patch that will move the value from the
old name to the new name.

<!-- snippet: sample_patching_rename_deep_prop -->
<a id='snippet-sample_patching_rename_deep_prop'></a>
```cs
[Fact]
public async Task rename_deep_prop()
{
    var target = Target.Random(true);
    target.Inner.String = "Foo";
    target.Inner.AnotherString = "Bar";

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id).Rename("String", x => x.Inner.AnotherString);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Inner.AnotherString.ShouldBe("Foo");
        target2.Inner.String.ShouldBeNull();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L665-L687' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_rename_deep_prop' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Renaming can be used on nested values.

## Delete a Property/Field

The `Patch.Delete()` operation can be used to remove a persisted property or field without the need
to load, deserialize, edit and save all affected documents

To delete a redundant property no longer available on the class use the string overload:

<!-- snippet: sample_patching_delete_redundant_property -->
<a id='snippet-sample_patching_delete_redundant_property'></a>
```cs
theSession.Patch<Target>(target.Id).Delete("String");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L860-L862' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_delete_redundant_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To delete a redundant property nested on a child class specify a location lambda:

<!-- snippet: sample_patching_delete_redundant_nested_property -->
<a id='snippet-sample_patching_delete_redundant_nested_property'></a>
```cs
theSession.Patch<Target>(target.Id).Delete("String", t => t.Inner);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L880-L882' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_delete_redundant_nested_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A current property may be erased simply with a lambda:

<!-- snippet: sample_patching_delete_existing_property -->
<a id='snippet-sample_patching_delete_existing_property'></a>
```cs
theSession.Patch<Target>(target.Id).Delete(t => t.Inner);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L900-L902' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_delete_existing_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Many documents may be patched using a where expressions:

<!-- snippet: sample_patching_delete_property_from_many_documents -->
<a id='snippet-sample_patching_delete_property_from_many_documents'></a>
```cs
const string where = "(data ->> 'String') is not null";
theSession.Query<Target>(where).Count.ShouldBe(15);
theSession.Patch<Target>(new WhereFragment(where)).Delete("String");
await theSession.SaveChangesAsync();

using (var query = theStore.QuerySession())
{
    query.Query<Target>(where).Count(t => t.String != null).ShouldBe(0);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L922-L932' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_delete_property_from_many_documents' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Multi-field patching/chaining patch operations

<!-- snippet: sample_patching_multiple_fields -->
<a id='snippet-sample_patching_multiple_fields'></a>
```cs
[Fact]
public async Task able_to_chain_patch_operations()
{
    var target = Target.Random(true);
    target.Number = 5;

    theSession.Store(target);
    await theSession.SaveChangesAsync();

    theSession.Patch<Target>(target.Id)
        .Set(x => x.Number, 10)
        .Increment(x => x.Number, 10);
    await theSession.SaveChangesAsync();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(20);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L1196-L1218' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_patching_multiple_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
