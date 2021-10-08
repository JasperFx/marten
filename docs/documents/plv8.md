# Marten.PLv8

Marten provides **Marten.PLv8** plugin to use [PLV8](https://plv8.github.io/) based Patch and Transform operations on stored documents. Install it through the [NuGet package](https://www.nuget.org/packages/Marten.PLv8).

```powershell
Install-Package Marten.PLv8
```

Call `UseJavascriptTransformsAndPatching()` as part of `DocumentStore` setup to enable it. You can also optionally configure custom JavaScript transformations as part of this method.

A sample setup is shown below:
<!-- snippet: sample_loading_js_transform_files -->
<a id='snippet-sample_loading_js_transform_files'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.UseJavascriptTransformsAndPatching(transforms =>
    {
        // Let Marten derive the transform name from the filename
        transforms.LoadFile("get_fullname.js");

        // Explicitly define the transform name yourself
        transforms.LoadFile("default_username.js", "set_default_username");
    });

});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Transforms/document_transforms.cs#L36-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_loading_js_transform_files' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To enable these features, Marten provides **Marten.PLv8** plugin. 

Install it through the [Nuget package](https://www.nuget.org/packages/Marten.PLv8/).

```powershell
PM> Install-Package Marten.PLv8
```


## The Patching API

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

### Patching by Where Expression

To apply a patch to all documents matching a given criteria, use the following syntax:

<!-- snippet: sample_set_an_immediate_property_by_where_clause -->
<a id='snippet-sample_set_an_immediate_property_by_where_clause'></a>
```cs
// Change every Target document where the Color is Blue
theSession.Patch<Target>(x => x.Color == Colors.Blue).Set(x => x.Number, 2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L131-L134' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_set_an_immediate_property_by_where_clause' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Setting a single Property/Field

The usage of `IDocumentSession.Patch().Set()` to change the value of a single persisted field is
shown below:

<!-- snippet: sample_set_an_immediate_property_by_id -->
<a id='snippet-sample_set_an_immediate_property_by_id'></a>
```cs
[Fact]
public void set_an_immediate_property_by_id()
{
    var target = Target.Random(true);
    target.Number = 5;

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Set(x => x.Number, 10);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(10);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L59-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_set_an_immediate_property_by_id' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Initialize a new Property/Field

To initialize a new property on existing documents:

<!-- snippet: sample_initialise_a_new_property_by_expression -->
<a id='snippet-sample_initialise_a_new_property_by_expression'></a>
```cs
const string where = "where (data ->> 'UpdatedAt') is null";
theSession.Query<Target>(where).Count.ShouldBe(3);
theSession.Patch<Target>(new WhereFragment(where)).Set("UpdatedAt", DateTime.UtcNow);
theSession.SaveChanges();

using (var query = theStore.QuerySession())
{
    query.Query<Target>(where).Count.ShouldBe(0);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L87-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_initialise_a_new_property_by_expression' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Duplicating an existing Property/Field

To copy an existing value to a new location:

<!-- snippet: sample_duplicate_to_new_field -->
<a id='snippet-sample_duplicate_to_new_field'></a>
```cs
var target = Target.Random();
target.AnotherString = null;
theSession.Store(target);
theSession.SaveChanges();

theSession.Patch<Target>(target.Id).Duplicate(t => t.String, t => t.AnotherString);
theSession.SaveChanges();

using (var query = theStore.QuerySession())
{
    var result = query.Load<Target>(target.Id);
    result.AnotherString.ShouldBe(target.String);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L155-L169' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_duplicate_to_new_field' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The same value can be copied to multiple new locations:

<!-- snippet: sample_duplicate_to_multiple_new_fields -->
<a id='snippet-sample_duplicate_to_multiple_new_fields'></a>
```cs
theSession.Patch<Target>(target.Id).Duplicate(t => t.String,
    t => t.StringField,
    t => t.Inner.String,
    t => t.Inner.AnotherString);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L181-L186' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_duplicate_to_multiple_new_fields' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The new locations need not exist in the persisted document, null or absent parents will be initialized

### Incrementing an Existing Value

To increment a persisted value in the persisted document, use this operation:

<!-- snippet: sample_increment_for_int -->
<a id='snippet-sample_increment_for_int'></a>
```cs
[Fact]
public void increment_for_int()
{
    var target = Target.Random();
    target.Number = 6;

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Increment(x => x.Number);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(7);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L200-L219' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_increment_for_int' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

By default, the `Patch.Increment()` operation will add 1 to the existing value. You can optionally override the increment:

<!-- snippet: sample_increment_for_int_with_explicit_increment -->
<a id='snippet-sample_increment_for_int_with_explicit_increment'></a>
```cs
[Fact]
public void increment_for_int_with_explicit_increment()
{
    var target = Target.Random();
    target.Number = 6;

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Increment(x => x.Number, 3);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        query.Load<Target>(target.Id).Number.ShouldBe(9);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L221-L240' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_increment_for_int_with_explicit_increment' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Append an Element to a Child Collection

::: warning
Because the Patching API depends on comparisons to the underlying serialized JSON in the database, the `DateTime` or `DateTimeOffset` types will frequently miss on comparisons for timestamps because of insufficient precision.
:::

The `Patch.Append()` operation adds a new item to the end of a child collection:

<!-- snippet: sample_append_complex_element -->
<a id='snippet-sample_append_complex_element'></a>
```cs
[Fact]
public void append_complex_element()
{
    var target = Target.Random(true);
    var initialCount = target.Children.Length;

    var child = Target.Random();

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount + 1);

        target2.Children.Last().Id.ShouldBe(child.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L343-L367' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_append_complex_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Patch.AppendIfNotExists()` operation will treat the child collection as a set rather than a list and only append the element if it does not already exist within the collection

Marten can append either complex, value object values or primitives like numbers or strings.

### Insert an Element into a Child Collection

Instead of appending an item to the end of a child collection, the `Patch.Insert()` operation allows you
to insert a new item into a persisted collection with a given index -- with the default index
being 0 so that a new item would be inserted at the beginning of the child collection.

<!-- snippet: sample_insert_first_complex_element -->
<a id='snippet-sample_insert_first_complex_element'></a>
```cs
[Fact]
public void insert_first_complex_element()
{
    var target = Target.Random(true);
    var initialCount = target.Children.Length;

    var child = Target.Random();

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount + 1);

        target2.Children.First().Id.ShouldBe(child.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L499-L523' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_insert_first_complex_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Patch.InsertIfNotExists()` operation will only insert the element if the element at the designated index does not already exist.

### Remove an Element from a Child Collection

The `Patch.Remove()` operation removes the given item from a child collection:

<!-- snippet: sample_remove_primitive_element -->
<a id='snippet-sample_remove_primitive_element'></a>
```cs
[Fact]
public void remove_primitive_element()
{
    var target = Target.Random();
    var initialCount = target.NumberArray.Length;

    var random = new Random();
    var child = target.NumberArray[random.Next(0, initialCount)];

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.NumberArray.Length.ShouldBe(initialCount - 1);

        target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.ExceptFirst(child));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L615-L640' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_remove_primitive_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Removing complex items can also be accomplished, matching is performed on all fields:

<!-- snippet: sample_remove_complex_element -->
<a id='snippet-sample_remove_complex_element'></a>
```cs
[Fact]
public void remove_complex_element()
{
    var target = Target.Random(true);
    var initialCount = target.Children.Length;

    var random = new Random();
    var child = target.Children[random.Next(0, initialCount)];

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Remove(x => x.Children, child);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Children.Length.ShouldBe(initialCount - 1);

        target2.Children.ShouldNotContain(t => t.Id == child.Id);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L676-L701' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_remove_complex_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To remove reoccurring values from a collection specify `RemoveAction.RemoveAll`:

<!-- snippet: sample_remove_repeated_primitive_element -->
<a id='snippet-sample_remove_repeated_primitive_element'></a>
```cs
[Fact]
public void remove_repeated_primitive_elements()
{
    var target = Target.Random();
    var initialCount = target.NumberArray.Length;

    var random = new Random();
    var child = target.NumberArray[random.Next(0, initialCount)];
    var occurences = target.NumberArray.Count(e => e == child);
    if (occurences < 2)
    {
        target.NumberArray = target.NumberArray.Concat(new[] { child }).ToArray();
        ++occurences;
        ++initialCount;
    }

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child, RemoveAction.RemoveAll);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.NumberArray.Length.ShouldBe(initialCount - occurences);

        target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.Except(new[] { child }));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L642-L674' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_remove_repeated_primitive_element' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Rename a Property/Field

In the case of changing the name of a property or field in your document type that's already persisted
in your Marten database, you have the option to apply a patch that will move the value from the
old name to the new name.

<!-- snippet: sample_rename_deep_prop -->
<a id='snippet-sample_rename_deep_prop'></a>
```cs
[Fact]
public void rename_deep_prop()
{
    var target = Target.Random(true);
    target.Inner.String = "Foo";
    target.Inner.AnotherString = "Bar";

    theSession.Store(target);
    theSession.SaveChanges();

    theSession.Patch<Target>(target.Id).Rename("String", x => x.Inner.AnotherString);
    theSession.SaveChanges();

    using (var query = theStore.QuerySession())
    {
        var target2 = query.Load<Target>(target.Id);
        target2.Inner.AnotherString.ShouldBe("Foo");
        SpecificationExtensions.ShouldBeNull(target2.Inner.String);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L591-L613' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rename_deep_prop' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Renaming can be used on nested values.

### Delete a Property/Field

The `Patch.Delete()` operation can be used to remove a persisted property or field without the need
to load, deserialize, edit and save all affected documents

To delete a redundant property no longer available on the class use the string overload:

<!-- snippet: sample_delete_redundant_property -->
<a id='snippet-sample_delete_redundant_property'></a>
```cs
theSession.Patch<Target>(target.Id).Delete("String");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L710-L712' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delete_redundant_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To delete a redundant property nested on a child class specify a location lambda:

<!-- snippet: sample_delete_redundant_nested_property -->
<a id='snippet-sample_delete_redundant_nested_property'></a>
```cs
theSession.Patch<Target>(target.Id).Delete("String", t => t.Inner);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L730-L732' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delete_redundant_nested_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A current property may be erased simply with a lambda:

<!-- snippet: sample_delete_existing_property -->
<a id='snippet-sample_delete_existing_property'></a>
```cs
theSession.Patch<Target>(target.Id).Delete(t => t.Inner);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L750-L752' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delete_existing_property' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Many documents may be patched using a where expressions:

<!-- snippet: sample_delete_property_from_many_documents -->
<a id='snippet-sample_delete_property_from_many_documents'></a>
```cs
const string where = "(data ->> 'String') is not null";
theSession.Query<Target>(where).Count.ShouldBe(15);
theSession.Patch<Target>(new WhereFragment(where)).Delete("String");
theSession.SaveChanges();

using (var query = theStore.QuerySession())
{
    query.Query<Target>(where).Count(t => t.String != null).ShouldBe(0);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L772-L782' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_delete_property_from_many_documents' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->



## Javascript Transformations

The patch API provides some out of the box recipes for common document transforms and the [document projections](/documents/querying/linq/projections) in the Linq support gives you the ability to do some basic transformations of the persisted JSON data in the database as part of querying. If your needs fall outside of these simple built in mechanism, you're still in luck because you can resort to using custom Javascript functions that will run inside of Postgresql itself to do more advanced document transformations.

At this point, Marten supports these use cases:

1. Transforming the data in one or more documents to apply some kind of structural migration to persisted documents, like you would need to do if
   the application code no longer matches the JSON previously stored
2. Creating a "read-side" view of a persisted document as part of a Linq query.
3. Transform the raw document data to a completely different .Net type as part of a Linq query

### Creating and Loading a Javascript Function

Javascript transformations work in Marten by first allowing you to write your Javascript function into a single file like this one:

<[sample:sample_get_fullname]>

You'll notice a couple things:

* Marten requires you to export the transformation function with the `module.exports =` syntax familiar from [CommonJS](http://wiki.commonjs.org/wiki/CommonJS) or Node.js development.
  ES6 modules are not supported at this time.
* Marten expects the transformation function to take in a single argument for the current JSON data and return the new JSON data

::: tip INFO
There is some thought and even infrastructure for doing Javascript transformations with multiple, related documents, but that feature will not likely make it into Marten 1.0.
:::

To load a Javascript function into your Marten-ized Postgresql database, use this syntax as part of bootstrapping
your Marten `IDocumentStore`:

<!-- snippet: sample_loading_js_transform_files -->
<a id='snippet-sample_loading_js_transform_files'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection(ConnectionSource.ConnectionString);

    _.UseJavascriptTransformsAndPatching(transforms =>
    {
        // Let Marten derive the transform name from the filename
        transforms.LoadFile("get_fullname.js");

        // Explicitly define the transform name yourself
        transforms.LoadFile("default_username.js", "set_default_username");
    });

});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Transforms/document_transforms.cs#L36-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_loading_js_transform_files' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Behind the scenes, Marten creates a `TransformFunction` object in the document store that knows how governs the construction and update
of a [PLV8 function](http://pgxn.org/dist/plv8/doc/plv8.html) that will wrap your raw Javascript function to expose it to Postgresql:

```sql
CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc jsonb)
  RETURNS jsonb AS
$BODY$

  var module = {export: {}};

module.exports = function (doc) {
    return {fullname: doc.FirstName + ' ' + doc.LastName};
}

  var func = module.exports;

  return func(doc);

$BODY$
  LANGUAGE plv8 IMMUTABLE STRICT
  COST 100;
```

The Javascript functions are managed roughly the same way as all other schema objects. If you are running your `IDocumentStore` with
the `StoreOptions.AutoCreateSchemaObjects` option set to anything but `None`, Marten will attempt to automatically update
your database schema with the current version of the Javascript wrapper function. It does this on only the first usage of the
named transform, and works by just doing a `string.Contains()` check against the existing function in the database schema.

In the case of `StoreOptions.AutoCreateSchemaObjects = None`, the Javascript transform functions are evaluated and output through
the [schema migrations](/schema/migrations).

### Using a Javascript Transform in Linq Queries

Once you have a Javascript transform loaded into the `IDocumentStore`, you can do live transformations inside
of Linq queries. If you only care about the transformed JSON, you use this syntax:

<!-- snippet: sample_using_transform_to_json -->
<a id='snippet-sample_using_transform_to_json'></a>
```cs
[Fact]
public void can_select_a_string_field_in_compiled_query()
{
    var user = new User { FirstName = "Eric", LastName = "Berry" };

    using var session = theStore.OpenSession();
    session.Store(user);
    session.SaveChanges();

    var name = session.Query<User>().Select(x => x.FirstName)
        .Single();

    name.ShouldBe("Eric");
}

[Fact]
public async Task can_transform_to_json()
{
    var user = new User { FirstName = "Eric", LastName = "Berry" };

    using var session = theStore.OpenSession();
    session.Store(user);
    await session.SaveChangesAsync();

    var json = await session.Query<User>()
        .Where(x => x.Id == user.Id)
        .TransformOneToJson("get_fullname");

    json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Transforms/select_with_transformation.cs#L27-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_transform_to_json' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you want to retrieve the results deserialized to another type, you can use the `TransformTo<T>(transformName)`
method shown below:

<!-- snippet: sample_transform_to_another_type -->
<a id='snippet-sample_transform_to_another_type'></a>
```cs
public class FullNameView
{
    public string fullname { get; set; }
}

[Fact]
public async Task can_transform_to_another_doc()
{
    var user = new User { FirstName = "Eric", LastName = "Berry" };

    using var session = theStore.OpenSession();
    session.Store(user);
    await session.SaveChangesAsync();

    var view = await session.Query<User>()
        .Where(x => x.Id == user.Id)
        .TransformOneTo<FullNameView>("get_fullname");

    view.fullname.ShouldBe("Eric Berry");
}

[Fact]
public async Task can_write_many_to_json()
{
    var user1 = new User { FirstName = "Eric", LastName = "Berry" };
    var user2 = new User { FirstName = "Derrick", LastName = "Johnson" };

    using var session = theStore.OpenSession();
    session.Store(user1, user2);
    await session.SaveChangesAsync();

    var view = await session.Query<User>()

        .TransformManyToJson("get_fullname");

    view.ShouldBe("[{\"fullname\": \"Eric Berry\"},{\"fullname\": \"Derrick Johnson\"}]");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Transforms/select_with_transformation.cs#L79-L118' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_transform_to_another_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

You can also use `TransformToJson()` inside of a [compiled query](/documents/querying/compiled-queries):

<[sample:sample_transform_to_json_in_compiled_query]>

### Document Transformations

The persisted JSON documents in Marten are a reflection of your .Net classes. Great, that makes it absurdly easy to keep the database schema
in synch with your application code at development time -- especially compared to the typical development process against a relational database.
However, what happens when you really do need to make breaking changes or additions to a document type but you already have loads of
persisted documents in your Marten database with the old structure?

To that end, Marten allows you to use Javascript functions to alter the existing documents in the database. As an example,
let's go back to the User document type and assume for some crazy reason that we didn't immediately issue a user name to some subset of users.
As a default, we might just assign their user names by combining their first and last names like so:

<[sample:sample_default_username]>

To apply this transformation to existing rows in the database, Marten exposes this syntax:

<!-- snippet: sample_transform_example -->
<a id='snippet-sample_transform_example'></a>
```cs
private static void transform_example(IDocumentStore store)
{
    store.Transform(x =>
    {
        // Transform User documents with a filter
        x.Where<User>("default_username", x => x.UserName == null);

        // Transform a single User document by Id
        x.Document<User>("default_username", Guid.NewGuid());

        // Transform all User documents
        x.All<User>("default_username");

        // Only transform documents from the "tenant1" tenant
        x.Tenant<User>("default_username", "tenant1");

        // Only transform documents from the named tenants
        x.Tenants<User>("default_username", "tenant1", "tenant2");
    });

}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Transforms/document_transforms.cs#L59-L83' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_transform_example' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->



## Installing plv8 on Windows

In order to use the JavaScript functions, you need to install plv8. The Windows install of PostgreSQL 9.5 / 9.6, and possibly future versions, do not come with plv8 installed.

If you attempt to install the extension for your database:

```sql
CREATE EXTENSION plv8;
```

You may be greeted with the following:

    sql> create extension plv8
    [2016-12-06 22:53:22] [58P01] ERROR: could not open extension control file "C:/Program Files/PostgreSQL/9.5/share/extension/plv8.control": No such file or directory

This means that the plv8 extension isn't installed so you're unable to create it in the database for usage.

## Download

You can download the appropriate binaries for PostgreSQL 9.5/PostgreSQL 9.6 from:

[PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit](http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html)

[PLV8-binaries-for-PostgreSQL-9.6beta1-windows-both-32-bit-and-64-bit](http://www.postgresonline.com/journal/archives/367-PLV8-binaries-for-PostgreSQL-9.6beta1-windows-both-32-bit-and-64-bit.html)

[PLV8-binaries-for-PostgreSQL-10-windows-both-32-bit-and-64-bit](http://www.postgresonline.com/journal/archives/379-PLV8-binaries-for-PostgreSQL-10-windows-both-32-bit-and-64-bit.htmll)

[xTuple-PLV8-binaries-for-PostgreSQL-9.4-to-12-windows-64-bit](http://updates.xtuple.com/updates/plv8/win/xtuple_plv8.zip)

Download the version that corresponds to the version of PostgreSQL you installed (32 or 64 bit)

## Install

### Distributions from Postgres Online

The zip should contain 3 folders:

- bin
- lib
- share

Move the contents of bin to:

> C:\Program Files\PostgreSQL\9.5\bin

Move the contents of lib to:

> C:\Program Files\PostgreSQL\9.5\lib

Move the contents of share/extension to:

> C:\Program Files\PostgreSQL\9.5\share\extension

The `Program Files` and `9.5` folders should correspond to the bit and version of PostgreSQL you installed. For example if you installed the 32 bit version of 9.6 then your path would be:

> C:\Program Files (x86)\PostgreSQL\9.6\

### Distributions from xTuple

The zip contains the folders for all the supported versions and the install_plv8.bat file.

Run the batch file from a command window running in administrative mode and provide the path for your Postgres installation.

## Create Extension

Once you have moved all the files to the correct folder, you can now call the create extension again:

```sql
CREATE EXTENSION plv8;
```

This time you should get the message like:

    sql> create extension plv8
    [2016-12-06 23:12:10] completed in 2s 271ms

If you get the below error while using the xTuple distribution

> ERROR:  syntax error in file "path_to_/plv8.control" line 1, near token ""
> SQL state: 42601

You need to ensure that the plv8.control is encoded with UTF-8. This is easiest to do with Notepad++.

## Testing out the extension

To test out the extension you can create a super basic function which manipulates a string input and returns the value.

```sql
create or replace function test_js_func(value text) returns text as $$

    var thing = 'I\' a JavaScript string';

    var result = thing.replace(/JavaScript/g, value);

    return result;

$$ language plv8;
```

Then we can invoke it:

```sql
select test_js_func('Manipulated');
```

And we should get a result that reads:

> I' a Manipulated string
