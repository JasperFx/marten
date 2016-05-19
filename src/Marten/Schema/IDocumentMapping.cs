using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Services.Includes;

namespace Marten.Schema
{

    public interface IQueryableDocument
    {
        IWhereFragment FilterDocuments(IWhereFragment query);

        IWhereFragment DefaultWhereFragment();

        IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IDocumentMapping other, MemberInfo[] members, Action<TOther> callback) where TOther : class;

        IField FieldFor(IEnumerable<MemberInfo> members);

        string[] SelectFields();

        PropertySearching PropertySearching { get; }

        TableName Table { get; }
    }


    public interface IDocumentMapping : IQueryableDocument
    {
        /*
        // These 3 need to move out
        string Alias { get; }
        IIdGeneration IdStrategy { get; set; }
        MemberInfo IdMember { get; }
        */


        Type DocumentType { get; }

        IDocumentStorage BuildStorage(IDocumentSchema schema);

        IDocumentSchemaObjects SchemaObjects { get; }

        void DeleteAllDocuments(IConnectionFactory factory);

        IdAssignment<T> ToIdAssignment<T>(IDocumentSchema schema);

        // More methods for creating a deleter? Queryable document?

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