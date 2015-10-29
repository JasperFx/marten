using System;

namespace Marten.Schema
{
    public interface IDocumentCleaner
    {
        void DeleteAllDocuments();
        void DocumentsFor(Type documentType);
        void DeleteDocumentsExcept(params Type[] documentTypes);

        void CompletelyRemove(Type documentType);
    }
}