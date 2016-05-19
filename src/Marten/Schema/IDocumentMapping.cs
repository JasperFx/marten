using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Services.Includes;

namespace Marten.Schema
{
    public interface IDocumentSchemaObjects
    {
        void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql);

        void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer);

        void RemoveSchemaObjects(IManagedConnection connection);

        void ResetSchemaExistenceChecks();
    }

    public interface IDocumentMapping
    {
        string Alias { get; }
        Type DocumentType { get; }

        TableName Table { get; }

        PropertySearching PropertySearching { get; }
        IIdGeneration IdStrategy { get; set; }
        MemberInfo IdMember { get; }
        string[] SelectFields();

        IField FieldFor(IEnumerable<MemberInfo> members);

        IWhereFragment FilterDocuments(IWhereFragment query);

        IWhereFragment DefaultWhereFragment();
        IDocumentStorage BuildStorage(IDocumentSchema schema);

        IDocumentSchemaObjects SchemaObjects { get; }

        void DeleteAllDocuments(IConnectionFactory factory);

        IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IDocumentMapping other, MemberInfo[] members, Action<TOther> callback) where TOther : class;
    }

    public static class DocumentMappingExtensions
    {
        public static string JsonLocator(this IDocumentMapping mapping, Expression expression)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);


            var field = mapping.FieldFor(visitor.Members);

            return field.SqlLocator;
        }
    }
}