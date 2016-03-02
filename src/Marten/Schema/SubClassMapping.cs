using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Baseline;
using Marten.Generation;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Schema
{
    public class SubClassMapping : IDocumentMapping
    {
        private readonly DocumentMapping _parent;
        private readonly DocumentMapping _inner;

        public SubClassMapping(Type documentType, DocumentMapping parent, string alias = null)
        {
            DocumentType = documentType;
            _inner = new DocumentMapping(documentType);
            _parent = parent;
            Alias = alias ?? documentType.GetTypeName().Replace(".", "_").SplitCamelCase().Replace(" ", "_").ToLowerInvariant();
        }


        public DocumentMapping Parent => _parent;


        public string Alias { get; set; }

        public string UpsertName => _parent.UpsertName;
        public Type DocumentType { get; }

        public string TableName => _parent.TableName;
        public PropertySearching PropertySearching => _parent.PropertySearching;
        public IIdGeneration IdStrategy => _parent.IdStrategy;
        public IEnumerable<DuplicatedField> DuplicatedFields => _parent.DuplicatedFields;
        public MemberInfo IdMember => _parent.IdMember;
        public string SelectFields(string tableAlias)
        {
            return _inner.SelectFields(tableAlias);
        }

        public void GenerateSchemaObjectsIfNecessary(bool autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            _parent.GenerateSchemaObjectsIfNecessary(autoCreateSchemaObjectsMode, schema, executeSql);
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return _parent.FieldFor(members) ?? _inner.FieldFor(members);
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return new WhereFragment($"d.{DocumentMapping.DocumentTypeColumn} = '{Alias}'");
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            var parentStorage = _parent.BuildStorage(schema);
            return typeof (SubClassDocumentStorage<,>).CloseAndBuildAs<IDocumentStorage>(parentStorage, DocumentType,
                _parent.DocumentType);
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            _parent.WriteSchemaObjects(schema, writer);
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            throw new NotSupportedException($"Invalid to remove schema objects for {DocumentType}, Use the parent {_parent.DocumentType} instead");
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql($"delete from {_parent.TableName} where {DocumentMapping.DocumentTypeColumn} = '{Alias}'");
        }
    }


}