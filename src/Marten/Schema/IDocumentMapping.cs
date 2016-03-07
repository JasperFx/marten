using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Events;
using Marten.Linq;
using Marten.Services;

namespace Marten.Schema
{
    public interface IDocumentMapping
    {
        string Alias { get; }
        Type DocumentType { get; }
        string TableName { get; }
        PropertySearching PropertySearching { get; }
        IIdGeneration IdStrategy { get; }
        MemberInfo IdMember { get; }
        string SelectFields(string tableAlias);

        void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql);

        IField FieldFor(IEnumerable<MemberInfo> members);

        IWhereFragment FilterDocuments(IWhereFragment query);


        IWhereFragment DefaultWhereFragment();
        IDocumentStorage BuildStorage(IDocumentSchema schema);

        void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer);

        void RemoveSchemaObjects(IManagedConnection connection);
        void DeleteAllDocuments(IConnectionFactory factory);

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