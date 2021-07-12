# Structural Typing

Let's say that you have some kind of complicated document type that represents the single source of truth for that
information within your system, but you frequently need to fetch only a subset of that document. In that
particular case you can opt into Marten's (limited) support for [structural typing](https://en.wikipedia.org/wiki/Structural_type_system).

See these two document types:

<!-- snippet: sample_structural_typing_classes -->
<a id='snippet-sample_structural_typing_classes'></a>
```cs
namespace Area1
{
    public class Product
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal Price { get; set; }
    }
}

namespace Area2
{
    [StructuralTyped]
    public class Product
    {
        public int Id { get; set; }

        public string Name { get; set; }
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Bugs/Bug_113_same_named_class_different_namespaces.cs#L30-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_structural_typing_classes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `Area2.Product` has a subset of the properties that `Area1.Product` has. By marking `Area2.Product`
with the `[StructuralTyped]` attribute, we are directing Marten to pull `Area2.Product` data
from the underlying storage of the bigger `Area1.Product` document.

You can see this in action inside of a unit test from Marten:

<!-- snippet: sample_can_select_from_the_same_table -->
<a id='snippet-sample_can_select_from_the_same_table'></a>
```cs
[Fact]
public void can_select_from_the_same_table()
{
    using (var session = theStore.OpenSession())
    {
        var product1 = new Area1.Product { Name = "Paper", Price = 10 };
        session.Store(product1);
        session.SaveChanges();

        var product2 = session.Load<Area2.Product>(product1.Id);

        product2.Name.ShouldBe(product1.Name);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Bugs/Bug_113_same_named_class_different_namespaces.cs#L10-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_can_select_from_the_same_table' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The caveats here are that the document types must have the same name, cannot be inner classes, must be in
separate namespaces, and the underlying JSON serializer must be able to resolve the structural typed
subset documents from the raw JSON of the master document.
