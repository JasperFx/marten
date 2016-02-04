using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Generation;
using Marten.Linq;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema.Hierarchies
{

    // TODO --- asser that there are no duplicate aliases or subclass types
    public class HierarchyMapping : DocumentMapping
    {
        public static readonly string DocumentTypeColumn = "mt_doc_type";
        private readonly IList<SubClassMapping> _subClasses = new List<SubClassMapping>(); 

        public HierarchyMapping(Type documentType, StoreOptions options) : base(documentType, options)
        {
            
        }

        public string AliasFor(Type subclassType)
        {
            var type = _subClasses.FirstOrDefault(x => x.DocumentType == subclassType);
            if (type == null)
            {
                throw new ArgumentOutOfRangeException($"Unknown subclass type '{subclassType.FullName}' for Document Hierarchy {DocumentType.FullName}");
            }

            return type.Alias;
        }



        public override string SelectFields(string tableAlias)
        {
            return $"{tableAlias}.data, {tableAlias}.id, {tableAlias}.{DocumentTypeColumn}";
        }

        public override string ToResolveMethod(string typeName)
        {
            var subclassStatements = _subClasses
                .Select(x => x.ToResolveStatement())
                .Join("\n");

            return $@"
BLOCK:public {typeName} Resolve(DbDataReader reader, IIdentityMap map)
var json = reader.GetString(0);
var id = reader[1];
var typeAlias = reader.GetString(1);

{subclassStatements}
            
return map.Get<{typeName}>(id, json);
END
";
        }

        public override IEnumerable<StorageArgument> ToArguments()
        {
            foreach (var argument in base.ToArguments())
            {
                yield return argument;
            }

            yield return new HierarchyArgument(this);
        }


        public override TableDefinition ToTable(IDocumentSchema schema)
        {
            var table = base.ToTable(schema);
            table.Columns.Add(new TableColumn(DocumentTypeColumn, "varchar"));

            return table;
        }

        public override UpsertFunction ToUpsertFunction()
        {
            var function = base.ToUpsertFunction();
            function.Arguments.Add(new UpsertArgument
            {
                Arg = "docType",
                Column = DocumentTypeColumn,
                DbType = NpgsqlDbType.Varchar,
                PostgresType = "varchar"
            });

            return function;
        }

        public void AddSubClass(Type subclassType, string alias = null)
        {
            if (!subclassType.CanBeCastTo(DocumentType))
            {
                throw new ArgumentOutOfRangeException(nameof(subclassType), 
                    $"Type '{subclassType.GetFullName()}' cannot be cast to '{DocumentType.GetFullName()}'");
            }

            var subclass = new SubClassMapping(subclassType, this, alias);
            _subClasses.Add(subclass);
        }

        public override string ToString()
        {
            return "HierarchyMapping for " + DocumentType.GetFullName();
        }

        public Type TypeFor(string alias)
        {
            var subClassMapping = _subClasses.FirstOrDefault(x => x.Alias.EqualsIgnoreCase(alias));
            if (subClassMapping == null)
            {
                throw new ArgumentOutOfRangeException(nameof(alias),$"No subclass in the hierarchy '{DocumentType.FullName}' matches the alias '{alias}'");
            }

            return subClassMapping.DocumentType;
        }
    }

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