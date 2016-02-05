using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using Marten.Generation;
using Marten.Linq;
using Marten.Schema.Hierarchies;
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

        public IEnumerable<StorageArgument> ToArguments()
        {
            return _parent.ToArguments();
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
        public IList<IndexDefinition> Indexes => _parent.Indexes;
        public string SelectFields(string tableAlias)
        {
            return _inner.SelectFields(tableAlias);
        }

        public TableDefinition ToTable(IDocumentSchema schema)
        {
            return _parent.ToTable(schema);
        }

        public UpsertFunction ToUpsertFunction()
        {
            return _parent.ToUpsertFunction();
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return _parent.FieldFor(members) ?? _inner.FieldFor(members);
        }

        public string ToResolveMethod(string typeName)
        {
            throw new NotSupportedException();
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
    }


}