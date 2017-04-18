using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Schema.Identity;
using Marten.Services.Includes;
using Marten.Storage;
using Marten.Util;
using Remotion.Linq;

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
                        t.Type.GetTypeInfo().IsSubclassOf(documentType) ||
                        (documentType.GetTypeInfo().IsInterface && t.Type.GetInterfaces().Contains(documentType)) ||
                        t.Type == documentType)
                .Select(GetTypeMartenAlias).Concat(Aliases).ToArray();
        }


        public DocumentMapping Parent { get; }


        public string[] Aliases { get; }
        public string Alias { get; set; }

        public DbObjectName UpsertName => Parent.UpsertFunction;

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
        public DeleteStyle DeleteStyle => Parent.DeleteStyle;

        public Type DocumentType { get; }

        public DbObjectName Table => Parent.Table;

        DuplicatedField[] IQueryableDocument.DuplicatedFields
        {
            get { throw new NotImplementedException(); }
        }

        public PropertySearching PropertySearching => Parent.PropertySearching;

        public string[] SelectFields()
        {
            return new[] {"data", "id", DocumentMapping.DocumentTypeColumn, DocumentMapping.VersionColumn };
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return Parent.FieldFor(members) ?? _inner.FieldFor(members);
        }

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            if (Parent.DeleteStyle == DeleteStyle.Remove)
            {
                return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
            }

            if (query.Contains(DocumentMapping.DeletedColumn))
            {
                return new CompoundWhereFragment("and", toBasicWhere(), query);
            }

            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            var basicWhere = toBasicWhere();

            if (Parent.DeleteStyle == DeleteStyle.Remove)
            {
                return basicWhere;
            }
            else
            {
                return new CompoundWhereFragment(" and ", basicWhere, DocumentMapping.ExcludeSoftDeletedDocuments());
            }
        }

        private WhereFragment toBasicWhere()
        {
            var aliasValues = Aliases.Select(a => $"d.{DocumentMapping.DocumentTypeColumn} = '{a}'").ToArray().Join(" or ");
            var basicWhere = new WhereFragment(aliasValues);
            return basicWhere;
        }

        public IDocumentStorage BuildStorage(StoreOptions options)
        {
            var parentStorage = Parent.As<IDocumentMapping>().BuildStorage(options);
            return typeof(SubClassDocumentStorage<,>).CloseAndBuildAs<IDocumentStorage>(parentStorage, this, DocumentType,
                Parent.DocumentType);
        }

        public IDocumentSchemaObjects SchemaObjects => Parent.SchemaObjects;


        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql(
                $"delete from {Parent.Table.QualifiedName} where {DocumentMapping.DocumentTypeColumn} = '{Alias}'");
        }

        public IdAssignment<T> ToIdAssignment<T>(ITenant tenant)
        {
            return Parent.ToIdAssignment<T>(tenant);
        }

        public IQueryableDocument ToQueryableDocument()
        {
            return this;
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members,
            Action<TOther> callback)
        {
            return Parent.JoinToInclude(joinType, other, members, callback);
        }

        public Type TypeFor(string alias)
        {
            return Parent.TypeFor(alias);
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

        public Type IdType => Parent.IdType;
    }
}
