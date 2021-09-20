# Document Hierarchies

Marten now allows you to specify that hierarchies of document types should be stored in one table and allow you
to query for either the base class or any of the subclasses.

## One Level Hierarchies

To make that concrete, let's say you have a document type named `User` that has a pair of specialized subclasses
called `SuperUser` and `AdminUser`. To use the document hierarchy storage, we need to tell Marten that
`SuperUser` and `AdminUser` should just be stored as subclasses of `User` like this:

<!-- snippet: sample_configure-hierarchy-of-types -->
<a id='snippet-sample_configure-hierarchy-of-types'></a>
```cs
var store = DocumentStore.For(_ =>
{
    _.Connection("connection to your database");

    _.Schema.For<User>()
        // generic version
        .AddSubClass<AdminUser>()

        // By document type object
        .AddSubClass(typeof (SuperUser));
});

using (var session = store.QuerySession())
{
    // query for all types of User and User itself
    session.Query<User>().ToList();

    // query for only SuperUser
    session.Query<SuperUser>().ToList();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs#L110-L131' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure-hierarchy-of-types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With the configuration above, you can now query by `User` and get `AdminUser` and `SuperUser` documents as part of the results,
or query directly for any of the subclasses to limit the query.

The best description of what is possible with hierarchical storage is to read the [acceptance tests for this feature](https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Services/BatchedQuerying/batched_querying_acceptance_Tests.cs).

There's a couple things to be aware of with type hierarchies:

* A document type that is either abstract or an interface is automatically assumed to be a hierarchy
* If you want to use a concrete type as the base class for a hierarchy, you will need to explicitly configure
  that by adding the subclasses as shown above
* At this point, you can only specify "Searchable" fields on the top, base type
* The subclass document types must be convertable to the top level type. As of right now, Marten does not support "structural typing",
  but may in the future
* Internally, the subclass type documents are also stored as the parent type in the Identity Map mechanics. Many, many hours of
  banging my head on my desk were required to add this feature.

## Multi Level Hierarchies

Say you have a document type named `ISmurf` that is implemented by `Smurf`. Now, say the latter has a pair of specialized
subclasses called `PapaSmurf` and `PapySmurf` and that both implement `IPapaSmurf` and that `PapaSmurf` has the subclass
`BrainySmurf` like so:

<!-- snippet: sample_smurfs-hierarchy -->
<a id='snippet-sample_smurfs-hierarchy'></a>
```cs
public interface ISmurf
{
    string Ability { get; set; }
    Guid Id { get; set; }
}

public class Smurf: ISmurf
{
    public string Ability { get; set; }
    public Guid Id { get; set; }
}

public interface IPapaSmurf: ISmurf
{
}

public class PapaSmurf: Smurf, IPapaSmurf
{
}

public class PapySmurf: Smurf, IPapaSmurf
{
}

public class BrainySmurf: PapaSmurf
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_inheritance.cs#L12-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_smurfs-hierarchy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you wish to query over one of hierarchy classes and be able to get all of its documents as well as its subclasses,
first you will need to map the hierarchy like so:

<!-- snippet: sample_add-subclass-hierarchy -->
<a id='snippet-sample_add-subclass-hierarchy'></a>
```cs
public query_with_inheritance(DefaultStoreFixture fixture): base(fixture)
{
    StoreOptions(_ =>
    {
        _.Schema.For<ISmurf>()
            .AddSubClassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(PapySmurf), typeof(IPapaSmurf),
                typeof(BrainySmurf));

        // Alternatively, you can use the following:
        // _.Schema.For<ISmurf>().AddSubClassHierarchy();
        // this, however, will use the assembly
        // of type ISmurf to get all its' subclasses/implementations.
        // In projects with many types, this approach will be undvisable.

        _.Connection(ConnectionSource.ConnectionString);
        _.AutoCreateSchemaObjects = AutoCreate.All;

        _.Schema.For<ISmurf>().GinIndexJsonData();
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_inheritance.cs#L86-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_add-subclass-hierarchy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that if you wish to use aliases on certain subclasses, you could pass a `MappedType`, which contains the type to map
and its alias. Since `Type` implicitly converts to `MappedType` and the methods takes in `params MappedType[]`, you could
use a mix of both like so:

<!-- snippet: sample_add-subclass-hierarchy-with-aliases -->
<a id='snippet-sample_add-subclass-hierarchy-with-aliases'></a>
```cs
_.Schema.For<ISmurf>()
    .AddSubClassHierarchy(
        typeof(Smurf),
        new MappedType(typeof(PapaSmurf), "papa"),
        typeof(PapySmurf),
        typeof(IPapaSmurf),
        typeof(BrainySmurf)
    );
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_inheritance.cs#L50-L61' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_add-subclass-hierarchy-with-aliases' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now you can query the "complex" hierarchy in the following ways:

<!-- snippet: sample_query-subclass-hierarchy -->
<a id='snippet-sample_query-subclass-hierarchy'></a>
```cs
[Fact]
public void get_all_subclasses_of_a_subclass()
{
    var smurf = new Smurf {Ability = "Follow the herd"};
    var papa = new PapaSmurf {Ability = "Lead"};
    var brainy = new BrainySmurf {Ability = "Invent"};
    theSession.Store(smurf, papa, brainy);

    theSession.SaveChanges();

    theSession.Query<Smurf>().Count().ShouldBe(3);
}

[Fact]
public void get_all_subclasses_of_a_subclass2()
{
    var smurf = new Smurf {Ability = "Follow the herd"};
    var papa = new PapaSmurf {Ability = "Lead"};
    var brainy = new BrainySmurf {Ability = "Invent"};
    theSession.Store(smurf, papa, brainy);

    theSession.SaveChanges();

    theSession.Query<PapaSmurf>().Count().ShouldBe(2);
}

[Fact]
public void get_all_subclasses_of_a_subclass_with_where()
{
    var smurf = new Smurf {Ability = "Follow the herd"};
    var papa = new PapaSmurf {Ability = "Lead"};
    var brainy = new BrainySmurf {Ability = "Invent"};
    theSession.Store(smurf, papa, brainy);

    theSession.SaveChanges();

    theSession.Query<PapaSmurf>().Count(s => s.Ability == "Invent").ShouldBe(1);
}

[Fact]
public void get_all_subclasses_of_a_subclass_with_where_with_camel_casing()
{
    StoreOptions(_ =>
    {
        _.Schema.For<ISmurf>()
            .AddSubClassHierarchy(typeof(Smurf), typeof(PapaSmurf), typeof(PapySmurf), typeof(IPapaSmurf),
                typeof(BrainySmurf));

        // Alternatively, you can use the following:
        // _.Schema.For<ISmurf>().AddSubClassHierarchy();
        // this, however, will use the assembly
        // of type ISmurf to get all its' subclasses/implementations.
        // In projects with many types, this approach will be undvisable.

        _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

        _.Connection(ConnectionSource.ConnectionString);
        _.AutoCreateSchemaObjects = AutoCreate.All;

        _.Schema.For<ISmurf>().GinIndexJsonData();
    });

    var smurf = new Smurf {Ability = "Follow the herd"};
    var papa = new PapaSmurf {Ability = "Lead"};
    var brainy = new BrainySmurf {Ability = "Invent"};
    theSession.Store(smurf, papa, brainy);

    theSession.SaveChanges();

    theSession.Query<PapaSmurf>().Count(s => s.Ability == "Invent").ShouldBe(1);
}

[Fact]
public void get_all_subclasses_of_an_interface()
{
    var smurf = new Smurf {Ability = "Follow the herd"};
    var papa = new PapaSmurf {Ability = "Lead"};
    var papy = new PapySmurf {Ability = "Lead"};
    var brainy = new BrainySmurf {Ability = "Invent"};
    theSession.Store(smurf, papa, brainy, papy);

    theSession.SaveChanges();

    theSession.Query<IPapaSmurf>().Count().ShouldBe(3);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Linq/query_with_inheritance.cs#L144-L234' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query-subclass-hierarchy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
