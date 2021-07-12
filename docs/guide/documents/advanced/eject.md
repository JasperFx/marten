# Ejecting Documents from a Session

If for some reason you need to completely remove a document from a session's [identity map](/guide/documents/advanced/identity-map) and [unit of work tracking](/guide/documents/basics/persisting), as of Marten 2.4.0 you can use the
`IDocumentSession.Eject<T>(T document)` syntax shown below in one of the tests:

<!-- snippet: sample_ejecting_a_document -->
<a id='snippet-sample_ejecting_a_document'></a>
```cs
[Fact]
public void demonstrate_eject()
{
    var target1 = Target.Random();
    var target2 = Target.Random();

    using (var session = theStore.OpenSession())
    {
        session.Store(target1, target2);

        // Both documents are in the identity map
        session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
        session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

        // Eject the 2nd document
        session.Eject(target2);

        // Now that 2nd document is no longer in the identity map
        SpecificationExtensions.ShouldBeNull(session.Load<Target>(target2.Id));

        session.SaveChanges();
    }

    using (var session = theStore.QuerySession())
    {
        // The 2nd document was ejected before the session
        // was saved, so it was never persisted
        SpecificationExtensions.ShouldBeNull(session.Load<Target>(target2.Id));
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/CoreFunctionality/ejecting_a_document.cs#L11-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_ejecting_a_document' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
