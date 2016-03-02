using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Generation;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Schema.Sequences;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentMapping : IDocumentMapping
    {
        public const string BaseAlias = "BASE";
        public const string TablePrefix = "mt_doc_";
        public const string UpsertPrefix = "mt_upsert_";
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
                prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop));
            });

            documentType.GetFields().Where(x => TypeMappings.HasTypeMapping(x.FieldType)).Each(fieldInfo =>
            {
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
            if (subclassType == DocumentType) return BaseAlias;

            var type = _subClasses.FirstOrDefault(x => x.DocumentType == subclassType);
            if (type == null)
            {
                throw new ArgumentOutOfRangeException($"Unknown subclass type '{subclassType.FullName}' for Document Hierarchy {DocumentType.FullName}");
            }

            return type.Alias;
        }


        public Type TypeFor(string alias)
        {
            if (alias == BaseAlias) return DocumentType;

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

            if (IsHierarchy())
            {
                yield return new HierarchyArgument(this);
            }
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return null;
        }

        public bool IsHierarchy()
        {
            return _subClasses.Any() || DocumentType.IsAbstract || DocumentType.IsInterface;
        }

        public IDocumentStorage BuildStorage(IDocumentSchema schema)
        {
            return DocumentStorageBuilder.Build(schema, this);
        }

        public void WriteSchemaObjects(IDocumentSchema schema, StringWriter writer)
        {
            var table = ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            ToUpsertFunction().WriteFunctionSql(schema?.UpsertType ?? PostgresUpsertType.Legacy, writer);

            Indexes.Each(x =>
            {
                writer.WriteLine();
                writer.WriteLine(x.ToDDL());
            });

            writer.WriteLine();
            writer.WriteLine();
        }

        public void RemoveSchemaObjects(IManagedConnection connection)
        {
            connection.Execute($"DROP TABLE IF EXISTS {TableName} CASCADE;");

            var dropTargets = DocumentCleaner.DropFunctionSql.ToFormat(UpsertName);

            var drops = connection.GetStringList(dropTargets);
            drops.Each(drop => connection.Execute(drop));
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            var sql = "truncate {0} cascade".ToFormat(TableName);
            factory.RunSql(sql);
        }

        public IEnumerable<SubClassMapping> SubClasses => _subClasses;

        public IIdGeneration IdStrategy { get; set; } = new StringIdGeneration();

        public string UpsertName { get; private set; }

        public Type DocumentType { get; }

        public string TableName { get; private set; }

        public MemberInfo IdMember { get; set; }

        public virtual string SelectFields(string tableAlias)
        {
            if (IsHierarchy())
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
            return _fields.GetOrAdd(member.Name, name => new JsonLocatorField(member));
        }

        public IField FieldFor(string memberName)
        {
            return _fields.GetOrAdd(memberName, name =>
            {
                var member = DocumentType.GetProperties().FirstOrDefault(x => x.Name == name).As<MemberInfo>() ??
                             DocumentType.GetFields().FirstOrDefault(x => x.Name == name);

                if (member == null) return null;

                return new JsonLocatorField(member);
            });
        }

        public virtual TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
            // can do foreign keys
        {
            var pgIdType = TypeMappings.GetPgType(IdMember.GetMemberType());
            var table = new TableDefinition(TableName, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb") {Directive = "NOT NULL"});

            _fields.Values.OfType<DuplicatedField>().Select(x => x.ToColumn(schema)).Each(x => table.Columns.Add(x));


            if (IsHierarchy())
            {
                table.Columns.Add(new TableColumn(DocumentTypeColumn, "varchar"));
            }

            return table;
        }

        public virtual UpsertFunction ToUpsertFunction()
        {
            var function = new UpsertFunction(this);
            function.Arguments.AddRange(DuplicatedFields.Select(x => x.UpsertArgument));

            if (IsHierarchy())
            {
                function.Arguments.Add(new UpsertArgument
                {
                    Arg = "docType",
                    Column = DocumentTypeColumn,
                    DbType = NpgsqlDbType.Varchar,
                    PostgresType = "varchar",
                    BatchUpdatePattern = ".Param(\"docType\", _hierarchy.AliasFor(document.GetType()), NpgsqlDbType.Varchar)",
                    BulkInsertPattern = "writer.Write(_hierarchy.AliasFor(x.GetType()), NpgsqlDbType.Varchar);"
                });
            }


            return function;
        }

        private readonly object _lock = new object();
        private bool _hasCheckedSchema = false;

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            if (_hasCheckedSchema) return;

            try
            {
                var expected = ToTable(schema);

                var existing = schema.TableSchema(TableName);
                if (existing != null && expected.Equals(existing))
                {
                    _hasCheckedSchema = true;
                    return;
                }

                lock (_lock)
                {
                    existing = schema.TableSchema(TableName);
                    if (existing == null || !expected.Equals(existing))
                    {
                        buildSchemaObjects(existing, expected, autoCreateSchemaObjectsMode, schema, executeSql);
                    }
                }
            }
            finally
            {
                _hasCheckedSchema = true;
            }


        }

        private void buildSchemaObjects(TableDefinition existing, TableDefinition expected, AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            // TODO -- this will change w/ the enum later
            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                var className = nameof(StoreOptions);
                var propName = nameof(StoreOptions.AutoCreateSchemaObjects);

                string message = $"No document storage exists for type {DocumentType.FullName} and cannot be created dynamically unless the {className}.{propName} = true. See http://jasperfx.github.io/marten/documentation/documents/ for more information";
                throw new InvalidOperationException(message);
            }

            // TODO -- this will need to get fancier. Right now it just drops
            // and replaces the tables. Later needs to add the extra fields and indexes
            var writer = new StringWriter();
            WriteSchemaObjects(schema, writer);

            var sql = writer.ToString();
            executeSql(sql);
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
            return _fields.GetOrAdd(key, _ => new JsonLocatorField(members.ToArray()));
        }

        public virtual string ToResolveMethod(string typeName)
        {
            if (IsHierarchy())
            {
                return $@"
BLOCK:public {typeName} Resolve(DbDataReader reader, IIdentityMap map)
var json = reader.GetString(0);
var id = reader[1];
var typeAlias = reader.GetString(2);

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