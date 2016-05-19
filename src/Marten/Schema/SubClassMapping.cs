using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;

namespace Marten.Schema
{
    public class SubClassMapping : IDocumentMapping
    {
        private readonly DocumentMapping _parent;
        private readonly DocumentMapping _inner;

        public SubClassMapping(Type documentType, DocumentMapping parent, StoreOptions storeOptions, string alias = null)
        {
            DocumentType = documentType;
            _inner = new DocumentMapping(documentType, storeOptions);
            _parent = parent;
            Alias = alias ?? GetTypeMartenAlias(documentType);
            Aliases = new[] {Alias};
        }

        public SubClassMapping(Type documentType, DocumentMapping parent, StoreOptions storeOptions, IEnumerable<MappedType> otherSubclassTypes, string alias = null)
            : this(documentType, parent, storeOptions, alias)
        {
            Aliases = otherSubclassTypes
                    .Where(t => t.Type.IsSubclassOf(documentType) || (documentType.IsInterface && t.Type.GetInterfaces().Contains(documentType)) || t.Type == documentType)
                    .Select(GetTypeMartenAlias).Concat(Aliases).ToArray();
        }

        private static string GetTypeMartenAlias(Type documentType)
        {
            return GetTypeMartenAlias(new MappedType(documentType));
        }

        private static string GetTypeMartenAlias(MappedType documentType)
        {
            return documentType.Alias ?? documentType.Type.GetTypeName().Replace(".", "_").SplitCamelCase().Replace(" ", "_").ToLowerInvariant();
        }


        public DocumentMapping Parent => _parent;


        public string[] Aliases { get; }
        public string Alias { get; set; }

        public FunctionName UpsertName => _parent.UpsertFunction;

        public Type DocumentType { get; }

        public TableName Table => _parent.Table;

        public string DatabaseSchemaName
        {
            get { return _parent.DatabaseSchemaName; }
            set { throw new NotSupportedException("The DatabaseSchemaName of a sub class mapping can't be set. The DatabaseSchemaName of the parent will be used."); }
        }

        public PropertySearching PropertySearching => _parent.PropertySearching;

        public IIdGeneration IdStrategy
        {
            get { return _parent.IdStrategy; }
            set { throw new NotSupportedException("The IdStrategy of a sub class mapping can't be set. The IdStrategy of the parent will be used."); }
        }

        public IEnumerable<DuplicatedField> DuplicatedFields => _parent.DuplicatedFields;
        public MemberInfo IdMember => _parent.IdMember;
        public string[] SelectFields()
        {
            return _inner.SelectFields();
        }

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
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
            return new WhereFragment(Aliases.Select(a=>$"d.{DocumentMapping.DocumentTypeColumn} = '{a}'").ToArray().Join(" or "));
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            var parentStorage = _parent.BuildStorage(schema);
            return typeof (SubClassDocumentStorage<,>).CloseAndBuildAs<IDocumentStorage>(parentStorage, DocumentType,
                _parent.DocumentType);
        }

        public IDocumentSchemaObjects SchemaObjects => _parent.SchemaObjects;



        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            factory.RunSql($"delete from {_parent.Table.QualifiedName} where {DocumentMapping.DocumentTypeColumn} = '{Alias}'");
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IDocumentMapping other, MemberInfo[] members, Action<TOther> callback) where TOther : class
        {
            return _parent.JoinToInclude<TOther>(joinType, other, members, callback);
        }

    }


}