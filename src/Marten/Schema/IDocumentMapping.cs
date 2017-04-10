using System;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema.Identity;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        Type DocumentType { get; }

        IDocumentStorage BuildStorage(IDocumentSchema schema);

        IDocumentSchemaObjects SchemaObjects { get; }
        DbObjectName Table { get; }

        void DeleteAllDocuments(IConnectionFactory factory);

        IdAssignment<T> ToIdAssignment<T>(IDocumentSchema schema);

        IQueryableDocument ToQueryableDocument();

        IDocumentUpsert BuildUpsert(IDocumentSchema schema);

        // More methods for creating a deleter? Queryable document?


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