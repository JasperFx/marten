using System;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        Type DocumentType { get; }

        IDocumentStorage BuildStorage(StoreOptions options);

        IDocumentSchemaObjects SchemaObjects { get; }
        DbObjectName Table { get; }

        void DeleteAllDocuments(IConnectionFactory factory);

        IdAssignment<T> ToIdAssignment<T>(ITenant tenant);

        IQueryableDocument ToQueryableDocument();

        Type IdType { get; }

    }

    public static class DocumentMappingExtensions
    {
        public static string JsonLocator(this IQueryableDocument mapping, Expression expression)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);


            var field = mapping.FieldFor(visitor.Members);

            return field.SqlLocator;
        }
    }
}