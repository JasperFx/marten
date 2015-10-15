using System;
using Npgsql;

namespace Marten.Schema
{
    public interface IDocumentCleaner
    {
        void AllDocuments();
        void DocumentsFor(Type documentType);
        void DocumentsExcept(params Type[] documentTypes);

        void CompletelyRemove(Type documentType);
    }

    public class DevelopmentDocumentCleaner : IDocumentCleaner
    {
        public void AllDocuments()
        {
            throw new NotImplementedException();
        }

        public void DocumentsFor(Type documentType)
        {
            throw new NotImplementedException();
        }

        public void DocumentsExcept(params Type[] documentTypes)
        {
            throw new NotImplementedException();
        }

        public void CompletelyRemove(Type documentType)
        {
            throw new NotImplementedException();
        }
    }


}