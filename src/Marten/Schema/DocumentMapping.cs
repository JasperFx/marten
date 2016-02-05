using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Generation;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Schema.Sequences;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentMapping : IDocumentMapping
    {
        public const string TablePrefix = "mt_doc_";
        private const string UpsertPrefix = "mt_upsert_";
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private PropertySearching _propertySearching = PropertySearching.JSON_Locator_Only;
        private string _alias;

        public static readonly string DocumentTypeColumn = "mt_doc_type";
        private readonly IList<SubClassMapping> _subClasses = new List<SubClassMapping>();


        public DocumentMapping(Type documentType) : this(documentType, new StoreOptions())
        {
        }

        public DocumentMapping(Type documentType, StoreOptions options)
        {
            DocumentType = documentType;
            Alias = defaultDocumentAliasName(documentType);

            IdMember = (MemberInfo) documentType.GetProperties().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"))
                       ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"));

            if (IdMember == null)
            {
                throw new InvalidDocumentException(
                    $"Could not determine an 'id/Id' field or property for requested document type {documentType.FullName}");
            }


            assignIdStrategy(documentType, options);

            documentType.ForAttribute<MartenAttribute>(att => att.Modify(this));

            documentType.GetProperties().Where(x => TypeMappings.HasTypeMapping(x.PropertyType)).Each(prop =>
            {
                var field = new LateralJoinField(prop);
                _fields[field.MemberName] = field;

                prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop));
            });

            documentType.GetFields().Where(x => TypeMappings.HasTypeMapping(x.FieldType)).Each(fieldInfo =>
            {
                var field = new LateralJoinField(fieldInfo);
                _fields.AddOrUpdate(field.MemberName, field, (key, f) => f);

                fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo));
            });
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

        public string Alias
        {
            get { return _alias; }
            set
            {
                if (value.IsEmpty()) throw new ArgumentNullException(nameof(value));

                _alias = value;
                TableName = TablePrefix + _alias;
                UpsertName = UpsertPrefix + _alias;
            }
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


        public Type TypeFor(string alias)
        {
            var subClassMapping = _subClasses.FirstOrDefault(x => x.Alias.EqualsIgnoreCase(alias));
            if (subClassMapping == null)
            {
                throw new ArgumentOutOfRangeException(nameof(alias), $"No subclass in the hierarchy '{DocumentType.FullName}' matches the alias '{alias}'");
            }

            return subClassMapping.DocumentType;
        }


        public IndexDefinition AddGinIndexToData()
        {
            var index = AddIndex("data");
            index.Method = IndexMethod.gin;
            index.Expression = "? jsonb_path_ops";

            PropertySearching = PropertySearching.ContainmentOperator;

            return index;
        }

        public IndexDefinition AddIndex(params string[] columns)
        {
            var index = new IndexDefinition(this, columns);
            Indexes.Add(index);

            return index;
        }


        public IList<IndexDefinition> Indexes { get; } = new List<IndexDefinition>();

        private void assignIdStrategy(Type documentType, StoreOptions options)
        {
            var idType = IdMember.GetMemberType();
            if (idType == typeof (string))
            {
                IdStrategy = new StringIdGeneration();
            }
            else if (idType == typeof (Guid))
            {
                IdStrategy = new GuidIdGeneration();
            }
            else if (idType == typeof (int) || idType == typeof (long))
            {
                IdStrategy = new HiloIdGeneration(documentType, options.HiloSequenceDefaults);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(documentType),
                    $"Marten cannot use the type {idType.FullName} as the Id for a persisted document. Use int, long, Guid, or string");
            }
        }

        public void HiloSettings(HiloSettings hilo)
        {
            if (IdStrategy is HiloIdGeneration)
            {
                IdStrategy = new HiloIdGeneration(DocumentType, hilo);
            }
            else
            {
                throw new InvalidOperationException(
                    $"DocumentMapping for {DocumentType.FullName} is using {IdStrategy.GetType().FullName} as its Id strategy so cannot override Hilo sequence configuration");
            }
        }

        public virtual IEnumerable<StorageArgument> ToArguments()
        {
            foreach (var argument in IdStrategy.ToArguments())
            {
                yield return argument;
            }

            if (_subClasses.Any())
            {
                yield return new HierarchyArgument(this);
            }
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return null;
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return DocumentStorageBuilder.Build(schema, this);
        }

        public IEnumerable<SubClassMapping> SubClasses => _subClasses;

        public IIdGeneration IdStrategy { get; set; } = new StringIdGeneration();

        public string UpsertName { get; private set; }

        public Type DocumentType { get; }

        public string TableName { get; private set; }

        public MemberInfo IdMember { get; set; }

        public virtual string SelectFields(string tableAlias)
        {
            if (_subClasses.Any())
            {
                return $"{tableAlias}.data, {tableAlias}.id, {tableAlias}.{DocumentTypeColumn}";
            }


            return $"{tableAlias}.data, {tableAlias}.id";
        }

        public PropertySearching PropertySearching
        {
            get { return _propertySearching; }
            set
            {
                _propertySearching = value;

                if (_propertySearching == PropertySearching.JSONB_To_Record)
                {
                    var fields = _fields.Values.Where(x => x.Members.Length == 1).OfType<JsonLocatorField>().ToArray();
                    fields.Each(x => { _fields[x.MemberName] = new LateralJoinField(x.Members.Last()); });
                }
                else
                {
                    var fields = _fields.Values.Where(x => x.Members.Length == 1).OfType<LateralJoinField>().ToArray();
                    fields.Each(x => { _fields[x.MemberName] = new JsonLocatorField(x.Members.Last()); });
                }
            }
        }

        public IEnumerable<DuplicatedField> DuplicatedFields => _fields.Values.OfType<DuplicatedField>();

        private static string defaultDocumentAliasName(Type documentType)
        {
            var parts = new List<string> {documentType.Name.ToLower()};
            if (documentType.IsNested)
            {
                parts.Insert(0, documentType.DeclaringType.Name.ToLower());
            }

            return string.Join("_", parts);
        }

        public IField FieldFor(MemberInfo member)
        {
            return _fields.GetOrAdd(member.Name, name =>
            {
                return PropertySearching == PropertySearching.JSONB_To_Record
                    ? (IField) new LateralJoinField(member)
                    : new JsonLocatorField(member);
            });
        }

        public IField FieldFor(string memberName)
        {
            return _fields[memberName];
        }

        public virtual TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
            // can do foreign keys
        {
            var pgIdType = TypeMappings.GetPgType(IdMember.GetMemberType());
            var table = new TableDefinition(TableName, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb") {Directive = "NOT NULL"});

            _fields.Values.OfType<DuplicatedField>().Select(x => x.ToColumn(schema)).Each(x => table.Columns.Add(x));


            if (_subClasses.Any())
            {
                table.Columns.Add(new TableColumn(DocumentTypeColumn, "varchar"));
            }

            return table;
        }

        public virtual UpsertFunction ToUpsertFunction()
        {
            var function = new UpsertFunction(this);
            function.Arguments.AddRange(DuplicatedFields.Select(x => x.UpsertArgument));

            if (_subClasses.Any())
            {
                function.Arguments.Add(new UpsertArgument
                {
                    Arg = "docType",
                    Column = DocumentTypeColumn,
                    DbType = NpgsqlDbType.Varchar,
                    PostgresType = "varchar",
                    BatchUpdatePattern = ".Param(_hierarchy.AliasFor(document.GetType()), NpgsqlDbType.Varchar)",
                    BulkInsertPattern = "writer.Write(_hierarchy.AliasFor(x.GetType()), NpgsqlDbType.Varchar);"
                });
            }


            return function;
        }


        public static DocumentMapping For<T>()
        {
            return new DocumentMapping(typeof (T));
        }

        public DuplicatedField DuplicateField(string memberName, string pgType = null)
        {
            var field = FieldFor(memberName);
            var duplicate = new DuplicatedField(field.Members);
            if (pgType.IsNotEmpty())
            {
                duplicate.PgType = pgType;
            }

            _fields[memberName] = duplicate;

            return duplicate;
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            if (members.Count() == 1)
            {
                return FieldFor(members.Single());
            }

            var key = members.Select(x => x.Name).Join("");
            return _fields.GetOrAdd(key, _ => { return new JsonLocatorField(members.ToArray()); });
        }

        public virtual string ToResolveMethod(string typeName)
        {
            if (_subClasses.Any())
            {
                return $@"
BLOCK:public {typeName} Resolve(DbDataReader reader, IIdentityMap map)
var json = reader.GetString(0);
var id = reader[1];
var typeAlias = reader.GetString(1);

return map.Get<{typeName}>(id, _hierarchy.TypeFor(typeAlias), json);
END
";
            }

            return $@"
BLOCK:public {typeName} Resolve(DbDataReader reader, IIdentityMap map)
var json = reader.GetString(0);
var id = reader[1];
            
return map.Get<{typeName}>(id, json);
END
";
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            return query;
        }

        public IndexDefinition DuplicateField(MemberInfo[] members, string pgType = null)
        {
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(members);
            if (pgType.IsNotEmpty())
            {
                duplicatedField.PgType = pgType;
            }

            _fields[memberName] = duplicatedField;

            return AddIndex(duplicatedField.ColumnName);
        }

        public IEnumerable<IndexDefinition> IndexesFor(string column)
        {
            return Indexes.Where(x => x.Columns.Contains(column));
        }
    }
}