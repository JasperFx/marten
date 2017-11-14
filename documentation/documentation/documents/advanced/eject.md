<!--title:Ejecting Documents from a Session-->

If for some reason you need to completely remove a document from a session's <[linkto:documentation/documents/advanced/identitymap;title=identity map]> and <[linkto:documentation/documents/basics/persisting;title=unit of work tracking]>, as of Marten 2.4.0 you can use the 
`IDocumentSession.Eject<T>(T document)` syntax shown below in one of the tests:


<[sample:ejecting_a_document]>