using System;
using System.Collections.Generic;
using System.Reflection;
using Baseline;
using Marten.Generation;
using Marten.Linq;
using Marten.Util;

namespace Marten.Schema.Hierarchies
{
    public class SubClassMapping : IDocumentMapping
    {
        private readonly HierarchyMapping _parent;
        private readonly DocumentMapping _inner;

        public SubClassMapping(Type documentType, HierarchyMapping parent, string alias = null)
        {
            DocumentType = documentType;
            _inner = new DocumentMapping(documentType);
            _parent = parent;
            Alias = alias ?? documentType.GetTypeName().Replace(".", "_").SplitCamelCase().Replace(" ", "_").ToLowerInvariant();
        }

        public string ToResolveStatement()
        {
            return $"if (typeAlias == `{Alias}`) return map.Get<{DocumentType.GetFullName()}>(id, json);";
        }

        public IEnumerable<StorageArgument> ToArguments()
        {
            return _parent.ToArguments();
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

        public UpsertFunction ToUpsertFunction()
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