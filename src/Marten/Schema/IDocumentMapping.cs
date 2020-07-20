using System;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        IDocumentMapping Root { get; }

        Type DocumentType { get; }

        DbObjectName Table { get; }

        void DeleteAllDocuments(ITenant factory);

        IdAssignment<T> ToIdAssignment<T>(ITenant tenant);

        IQueryableDocument ToQueryableDocument();

        Type IdType { get; }
        TenancyStyle TenancyStyle { get; }
    }

}
