using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class DocumentCleanerExamples
    {
        // SAMPLE: clean_out_documents
        public void clean_out_documents(IDocumentStore store)
        {
            // Completely remove all the database schema objects related
            // to the User document type
            store.Advanced.Clean.CompletelyRemove(typeof(User));

            // Tear down and remove all Marten related database schema objects
            store.Advanced.Clean.CompletelyRemoveAll();

            // Deletes all the documents stored in a Marten database
            store.Advanced.Clean.DeleteAllDocuments();

            // Deletes all of the persisted User documents
            store.Advanced.Clean.DeleteDocumentsFor(typeof(User));

            // For cases where you may want to keep some document types,
            // but eliminate everything else. This is here specifically to support
            // automated testing scenarios where you have some static data that can
            // be safely reused across tests
            store.Advanced.Clean.DeleteDocumentsExcept(typeof(Company), typeof(User));
        } 
        // ENDSAMPLE
    }
}