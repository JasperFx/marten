using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Generation;
using Marten.Linq;

namespace Marten.Schema.Hierarchies
{
    public class HierarchyMapping : DocumentMapping
    {
        public static readonly string DocumentTypeColumn = "mt_doc_type";

        public HierarchyMapping(Type documentType, StoreOptions options) : base(documentType, options)
        {
            
        }

        public override string SelectFields(string tableAlias)
        {
            return base.SelectFields(tableAlias);
        }

        public override string ToResolveMethod(string typeName)
        {
            return base.ToResolveMethod(typeName);
        }

        public override TableDefinition ToTable(IDocumentSchema schema)
        {
            return base.ToTable(schema);
        }
    }

    public class SubClassMapping : IDocumentMapping
    {
        private readonly HierarchyMapping _parent;
        private readonly DocumentMapping _inner;

        public SubClassMapping(Type documentType, HierarchyMapping parent)
        {
            DocumentType = documentType;
            _inner = new DocumentMapping(documentType);
            _parent = parent;
        }

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

        public UpsertFunction ToUpsertFunction(IDocumentSchema schema)
        {
            throw new NotImplementedException();
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return _parent.FieldFor(members) ?? _inner.FieldFor(members);
        }

        public string ToResolveMethod(string typeName)
        {
            return _inner.ToResolveMethod(typeName);
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            throw new NotImplementedException();
        }
    }
}