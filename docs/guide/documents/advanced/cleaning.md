# Tearing Down Document Storage

For the purpose of automated testing where you need to carefully control the state of the document storage, Marten supplies the
`IDocumentCleaner` service to quickly remove persisted document state or even to completely tear down the entire document storage.

This service is exposed as the `IDocumentStore.Advanced.Clean` property. You can see the usages of the document cleaner below:

<!-- snippet: sample_clean_out_documents -->
<!-- endSnippet -->
