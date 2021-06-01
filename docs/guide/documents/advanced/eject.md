# Ejecting Documents from a Session

If for some reason you need to completely remove a document from a session's [identity map](/guide/documents/advanced/identity-map) and [unit of work tracking](/guide/documents/basics/persisting), as of Marten 2.4.0 you can use the
`IDocumentSession.Eject<T>(T document)` syntax shown below in one of the tests:

<!-- snippet: sample_ejecting_a_document -->
<!-- endSnippet -->
