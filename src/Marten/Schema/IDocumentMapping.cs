using System;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        IDocumentMapping Root { get; }

        Type DocumentType { get; }

        DbObjectName TableName { get; }

        void DeleteAllDocuments(ITenant factory);

        IdAssignment<T> ToIdAssignment<T>(ITenant tenant);

        Type IdType { get; }
    }

}
