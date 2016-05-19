using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Schema.Identity;
using Marten.Services.Includes;
using Marten.Util;

namespace Marten.Schema
{
    public class SubClassMapping : IDocumentMapping, IQueryableDocument
    {
        private readonly DocumentMapping _inner;

        public SubClassMapping(Type documentType, DocumentMapping parent, StoreOptions storeOptions, string alias = null)
        {
            DocumentType = documentType;
            _inner = new DocumentMapping(documentType, storeOptions);
            Parent = parent;
            Alias = alias ?? GetTypeMartenAlias(documentType);
            Aliases = new[] {Alias};
        }

        public SubClassMapping(Type documentType, DocumentMapping parent, StoreOptions storeOptions,
            IEnumerable<MappedType> otherSubclassTypes, string alias = null)
            : this(documentType, parent, storeOptions, alias)
        {
            Aliases = otherSubclassTypes
                .Where(
                    t =>
                        t.Type.IsSubclassOf(documentType) ||
                        (documentType.IsInterface && t.Type.GetInterfaces().Contains(documentType)) ||
                        t.Type == documentType)
                .Select(GetTypeMartenAlias).Concat(Aliases).ToArray();
        }


        public DocumentMapping Parent { get; }


        public string[] Aliases { get; }
        public string Alias { get; set; }

        public FunctionName UpsertName => Parent.UpsertFunction;

        public string DatabaseSchemaName
        {
            get { return Parent.DatabaseSchemaName; }
            set
            {
                throw new NotSupportedException(
                    "The DatabaseSchemaName of a sub class mapping can't be set. The DatabaseSchemaName of the parent will be used.");
            }
        }

        public IEnumerable<DuplicatedField> DuplicatedFields => Parent.DuplicatedFields;

        public Type DocumentType { get; }

        public TableName Table => Parent.Table;

        public PropertySearching PropertySearching => Parent.PropertySearching;

        public string[] SelectFields()
        {
            return _inner.SelectFields();
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return Parent.FieldFor(members) ?? _inner.FieldFor(members);
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return
                new WhereFragment(
                    Aliases.Select(a => $"d.{DocumentMapping.DocumentTypeColumn} = '{a}'").ToArray().Join(" or "));
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            var parentStorage = Parent.As<IDocumentMapping>().BuildStorage(schema);
            return typeof(SubClassDocumentStorage<,>).CloseAndBuildAs<IDocumentStorage>(parentStorage, DocumentType,
                Parent.DocumentType);
        }

        public IDocumentSchemaObjects SchemaObjects => Parent.SchemaObjects;


        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql(
                $"delete from {Parent.Table.QualifiedName} where {DocumentMapping.DocumentTypeColumn} = '{Alias}'");
        }

        public IdAssignment<T> ToIdAssignment<T>(IDocumentSchema schema)
        {
            return Parent.ToIdAssignment<T>(schema);
        }

        public IQueryableDocument ToQueryableDocument()
        {
            return this;
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members,
            Action<TOther> callback) where TOther : class
        {
            return Parent.JoinToInclude(joinType, other, members, callback);
        }

        private static string GetTypeMartenAlias(Type documentType)
        {
            return GetTypeMartenAlias(new MappedType(documentType));
        }

        private static string GetTypeMartenAlias(MappedType documentType)
        {
            return documentType.Alias ??
                   documentType.Type.GetTypeName()
                       .Replace(".", "_")
                       .SplitCamelCase()
                       .Replace(" ", "_")
                       .ToLowerInvariant();
        }
    }
}