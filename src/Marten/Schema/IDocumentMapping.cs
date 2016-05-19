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
        string Alias { get; }
        Type DocumentType { get; }

        IIdGeneration IdStrategy { get; set; }
        MemberInfo IdMember { get; }

        IDocumentStorage BuildStorage(IDocumentSchema schema);

        IDocumentSchemaObjects SchemaObjects { get; }

        void DeleteAllDocuments(IConnectionFactory factory);
        
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