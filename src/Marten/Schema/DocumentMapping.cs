using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Baseline.Reflection;
using Marten.Generation;
using Marten.Linq;
using Marten.Schema.Hierarchies;
using Marten.Schema.Sequences;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DocumentMapping : IDocumentMapping
    {
        public static DocumentMapping For<T>(string databaseSchemaName = StoreOptions.DefaultDatabaseSchemaName, Func<IDocumentMapping, StoreOptions, IIdGeneration> idGeneration = null)
        {
            var storeOptions = new StoreOptions { DatabaseSchemaName = databaseSchemaName, DefaultIdStrategy = idGeneration };

            return new DocumentMapping(typeof(T), storeOptions);
        }

        public const string BaseAlias = "BASE";
        public const string TablePrefix = "mt_doc_";
        public const string UpsertPrefix = "mt_upsert_";
        public const string DocumentTypeColumn = "mt_doc_type";
        public const string MartenPrefix = "mt_";

        private readonly StoreOptions _storeOptions;
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private string _alias;

        private readonly object _lock = new object();
        private bool _hasCheckedSchema = false;
        private string _databaseSchemaName;

        private readonly IList<SubClassMapping> _subClasses = new List<SubClassMapping>();

		public DocumentMapping(Type documentType, StoreOptions storeOptions)
        {
            if (documentType == null) throw new ArgumentNullException(nameof(documentType));
            if (storeOptions == null) throw new ArgumentNullException(nameof(storeOptions));

            _storeOptions = storeOptions;

            DocumentType = documentType;
            Alias = defaultDocumentAliasName(documentType);

            IdMember = determineId(documentType);

		    if (IdMember != null)
		    {
                _fields[IdMember.Name] = new IdField(IdMember);
                IdStrategy = defineIdStrategy(documentType, storeOptions);
            }

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

        private static MemberInfo determineId(Type documentType)
        {
            return (MemberInfo) GetProperties(documentType).FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"))
                   ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id"));
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            return type.IsInterface ? (new [] { type })
                .Concat(type.GetInterfaces())
                .SelectMany(i => i.GetProperties()).ToArray() : type.GetProperties();
        }

        public void AddSubClass(Type subclassType, IEnumerable<MappedType> otherSubclassTypes, string alias)
        {
            VerifyIsSubclass(subclassType);

            var subclass = new SubClassMapping(subclassType, this, _storeOptions, otherSubclassTypes, alias);
            _subClasses.Add(subclass);
        }

        public void AddSubClass(Type subclassType, string alias = null)
        {
            VerifyIsSubclass(subclassType);

            var subclass = new SubClassMapping(subclassType, this, _storeOptions, alias);
            _subClasses.Add(subclass);
        }

        private void VerifyIsSubclass(Type subclassType)
        {
            if (!subclassType.CanBeCastTo(DocumentType))
            {
                throw new ArgumentOutOfRangeException(nameof(subclassType),
                    $"Type '{subclassType.GetFullName()}' cannot be cast to '{DocumentType.GetFullName()}'");
            }
        }

        public string Alias
        {
            get { return _alias; }
            set
            {
                if (value.IsEmpty()) throw new ArgumentNullException(nameof(value));

                _alias = value.ToLower();

                TableName = $"{TablePrefix}{_alias}";
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
            var existing = Indexes.FirstOrDefault(x => x.Columns.SequenceEqual(columns));
            if (existing != null)
            {
                return existing;
            }

            var index = new IndexDefinition(this, columns);
            Indexes.Add(index);

            return index;
        }

        public ForeignKeyDefinition AddForeignKey(string memberName, Type referenceType)
        {
            var field = FieldFor(memberName);
            return AddForeignKey(field.Members, referenceType);
        }

        public ForeignKeyDefinition AddForeignKey(MemberInfo[] members, Type referenceType)
        {
            var referenceMapping = _storeOptions.MappingFor(referenceType);

            var duplicateField = DuplicateField(members);

            var foreignKey = new ForeignKeyDefinition(duplicateField.ColumnName, this, referenceMapping);
            ForeignKeys.Add(foreignKey);

            return foreignKey;
        }

        public IList<IndexDefinition> Indexes { get; } = new List<IndexDefinition>();

        public IList<ForeignKeyDefinition> ForeignKeys { get; } = new List<ForeignKeyDefinition>();

        private IIdGeneration defineIdStrategy(Type documentType, StoreOptions options)
        {
            if (!idMemberIsSettable())
            {
                return new NoOpIdGeneration();
            }

            var strategy = options.DefaultIdStrategy?.Invoke(this, options);
            if (strategy != null)
            {
                return strategy;
            }
            
            var idType = IdMember.GetMemberType();
            if (idType == typeof(string))
            {
                return new StringIdGeneration();
            }
            if (idType == typeof(Guid))
            {
                return new GuidIdGeneration();
            }
            if (idType == typeof(int) || idType == typeof(long))
            {
                return new HiloIdGeneration(documentType, options.HiloSequenceDefaults);
            }

            throw new ArgumentOutOfRangeException(nameof(documentType),
                $"Marten cannot use the type {idType.FullName} as the Id for a persisted document. Use int, long, Guid, or string");
        }

        private bool idMemberIsSettable()
        {
            var field = IdMember as FieldInfo;
            if (field != null) return field.IsPublic;
            var property = IdMember as PropertyInfo;
            if (property != null) return property.CanWrite && property.GetSetMethod(false) != null;
            return false;
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
            EnsureDatabaseSchema.WriteSql(DatabaseSchemaName, writer);

            var table = ToTable(schema);
            table.Write(writer);
            writer.WriteLine();
            writer.WriteLine();

            ToUpsertFunction().WriteFunctionSql(schema?.UpsertType ?? PostgresUpsertType.Legacy, writer);

            ForeignKeys.Each(x =>
            {
                writer.WriteLine();
                writer.WriteLine(x.ToDDL());
            });

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
            _hasCheckedSchema = false;

            connection.Execute($"DROP TABLE IF EXISTS {QualifiedTableName} CASCADE;");

            var dropTargets = DocumentCleaner.DropFunctionSql.ToFormat(UpsertName, DatabaseSchemaName);

            var drops = connection.GetStringList(dropTargets);
            drops.Each(drop => connection.Execute(drop));
        }

        public void DeleteAllDocuments(IConnectionFactory factory)
        {
            var sql = "truncate {0} cascade".ToFormat(QualifiedTableName);
            factory.RunSql(sql);
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IDocumentMapping other, MemberInfo[] members, Action<TOther> callback) where TOther : class
        {
            var joinOperator = joinType == JoinType.Inner ? "INNER JOIN" : "LEFT OUTER JOIN";

            var tableAlias = members.ToTableAlias();
            var locator = FieldFor(members).SqlLocator;

            var joinText = $"{joinOperator} {other.QualifiedTableName} as {tableAlias} ON {locator} = {tableAlias}.id";

            return new IncludeJoin<TOther>(other, joinText, tableAlias, callback);
        }

        public IEnumerable<SubClassMapping> SubClasses => _subClasses;

        public IIdGeneration IdStrategy { get; set; }

        public string QualifiedUpsertName => $"{DatabaseSchemaName}.{UpsertName}";

        public string UpsertName => $"{UpsertPrefix}{_alias}";

        public Type DocumentType { get; }

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? _storeOptions.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public string QualifiedTableName => $"{DatabaseSchemaName}.{TableName}";

        public string TableName { get; private set; }

        public MemberInfo IdMember { get; set; }

        public virtual string[] SelectFields()
        {
            return IsHierarchy() 
                ? new [] {"data", "id", DocumentTypeColumn} 
                : new[] {"data", "id"};
        }

        public PropertySearching PropertySearching { get; set; } = PropertySearching.JSON_Locator_Only;

        public IEnumerable<DuplicatedField> DuplicatedFields => _fields.Values.OfType<DuplicatedField>();

        private static readonly Regex _aliasSanitizer = new Regex("<|>", RegexOptions.Compiled);

        private static string defaultDocumentAliasName(Type documentType)
        {
            var nameToAlias = documentType.Name;
            if (documentType.IsGenericType)
            {
                nameToAlias = _aliasSanitizer.Replace(documentType.GetPrettyName(), string.Empty);
            }
            var parts = new List<string> { nameToAlias.ToLower() };
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

        public IField FieldForColumn(string columnName)
        {
            return _fields.Values.FirstOrDefault(x => x.ColumnName == columnName);
        }

        public virtual TableDefinition ToTable(IDocumentSchema schema) // take in schema so that you
                                                                       // can do foreign keys
        {
            var pgIdType = TypeMappings.GetPgType(IdMember.GetMemberType());
            var table = new TableDefinition(QualifiedTableName, TableName, new TableColumn("id", pgIdType));
            table.Columns.Add(new TableColumn("data", "jsonb") { Directive = "NOT NULL" });

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

        public void GenerateSchemaObjectsIfNecessary(AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            if (_hasCheckedSchema) return;

            try
            {
                var expected = ToTable(schema);

                var existing = schema.TableSchema(this);
                if (existing != null && expected.Equals(existing))
                {
                    _hasCheckedSchema = true;
                    return;
                }

                lock (_lock)
                {
                    existing = schema.TableSchema(this);
                    if (existing == null || !expected.Equals(existing))
                    {
                        buildOrModifySchemaObjects(existing, expected, autoCreateSchemaObjectsMode, schema, executeSql);
                    }
                }
            }
            finally
            {
                _hasCheckedSchema = true;
            }


        }

        private void buildOrModifySchemaObjects(TableDefinition existing, TableDefinition expected, AutoCreate autoCreateSchemaObjectsMode, IDocumentSchema schema, Action<string> executeSql)
        {
            if (autoCreateSchemaObjectsMode == AutoCreate.None)
            {
                var className = nameof(StoreOptions);
                var propName = nameof(StoreOptions.AutoCreateSchemaObjects);

                string message = $"No document storage exists for type {DocumentType.FullName} and cannot be created dynamically unless the {className}.{propName} = true. See http://jasperfx.github.io/marten/documentation/documents/ for more information";
                throw new InvalidOperationException(message);
            }

            if (existing == null)
            {
                rebuildTableAndUpsertFunction(schema, executeSql);
                return;
            }

            if (autoCreateSchemaObjectsMode == AutoCreate.CreateOnly)
            {
                throw new InvalidOperationException($"The table for document type {DocumentType.FullName} is different than the current schema table, but AutoCreateSchemaObjects = '{nameof(AutoCreate.CreateOnly)}'");
            }

            var diff = new TableDiff(expected, existing);
            if (diff.CanPatch())
            {
                diff.CreatePatch(this, executeSql);
            }
            else if (autoCreateSchemaObjectsMode == AutoCreate.All)
            {
                // TODO -- better evaluation here against the auto create mode
                rebuildTableAndUpsertFunction(schema, executeSql);
            }
            else
            {
                throw new InvalidOperationException($"The table for document type {DocumentType.FullName} is different than the current schema table, but AutoCreateSchemaObjects = '{autoCreateSchemaObjectsMode}'");
            }

        }

        private void rebuildTableAndUpsertFunction(IDocumentSchema schema, Action<string> executeSql)
        {
            var writer = new StringWriter();
            WriteSchemaObjects(schema, writer);

            var sql = writer.ToString();
            executeSql(sql);
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

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            return query;
        }

        public DuplicatedField DuplicateField(MemberInfo[] members, string pgType = null)
        {
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(members);
            if (pgType.IsNotEmpty())
            {
                duplicatedField.PgType = pgType;
            }

            _fields[memberName] = duplicatedField;

            return duplicatedField;
        }

        public IEnumerable<IndexDefinition> IndexesFor(string column)
        {
            return Indexes.Where(x => x.Columns.Contains(column));
        }
    }

    public class IdField : IField
    {
        private readonly MemberInfo _idMember;

        public IdField(MemberInfo idMember)
        {
            _idMember = idMember;
        }

        public MemberInfo[] Members => new[] {_idMember};
        public string MemberName => _idMember.Name;
        public string SqlLocator { get; } = "d.id";
        public string ColumnName { get; } = "id";
        public void WritePatch(DocumentMapping mapping, Action<string> writer)
        {
            // Nothing
        }
    }
}