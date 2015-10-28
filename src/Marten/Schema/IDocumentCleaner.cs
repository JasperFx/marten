using System;

namespace Marten.Schema
{
    public interface IDocumentCleaner
    {
        void AllDocuments();
        void DocumentsFor(Type documentType);
        void DocumentsExcept(params Type[] documentTypes);

        void CompletelyRemove(Type documentType);
    }
}