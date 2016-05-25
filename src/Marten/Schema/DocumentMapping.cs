using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services.Includes;
using Marten.Util;

namespace Marten.Schema
{

    public class DocumentMapping : IDocumentMapping, IQueryableDocument
    {
        public const string BaseAlias = "BASE";
        public const string TablePrefix = "mt_doc_";
        public const string UpsertPrefix = "mt_upsert_";
        public const string DocumentTypeColumn = "mt_doc_type";
        public const string MartenPrefix = "mt_";
        public const string LastModifiedColumn = "mt_last_modified";
        public const string DotNetTypeColumn = "mt_dotnet_type";
        public const string VersionColumn = "mt_version";

        private static readonly Regex _aliasSanitizer = new Regex("<|>", RegexOptions.Compiled);
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private readonly StoreOptions _storeOptions;

        private readonly IList<SubClassMapping> _subClasses = new List<SubClassMapping>();
        private string _alias;
        private string _databaseSchemaName;


        public DocumentMapping(Type documentType, StoreOptions storeOptions)
        {
            if (documentType == null) throw new ArgumentNullException(nameof(documentType));
            if (storeOptions == null) throw new ArgumentNullException(nameof(storeOptions));

            SchemaObjects = new DocumentSchemaObjects(this);

            _storeOptions = storeOptions;

            DocumentType = documentType;
            Alias = defaultDocumentAliasName(documentType);

            IdMember = FindIdMember(documentType);

            if (IdMember != null)
            {
                _fields[IdMember.Name] = new IdField(IdMember);
                IdStrategy = defineIdStrategy(documentType, storeOptions);
            }

            applyAnyMartenAttributes(documentType);
        }

        private void applyAnyMartenAttributes(Type documentType)
        {
            documentType.ForAttribute<MartenAttribute>(att => att.Modify(this));

            documentType.GetProperties()
                .Where(x => TypeMappings.HasTypeMapping(x.PropertyType))
                .Each(prop => { prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop)); });

            documentType.GetFields()
                .Where(x => TypeMappings.HasTypeMapping(x.FieldType))
                .Each(fieldInfo => { fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo)); });
        }

        public bool UseOptimisticConcurrency { get; set; } = false;

        public IList<IndexDefinition> Indexes { get; } = new List<IndexDefinition>();

        public IList<ForeignKeyDefinition> ForeignKeys { get; } = new List<ForeignKeyDefinition>();

        public IEnumerable<SubClassMapping> SubClasses => _subClasses;

        public FunctionName UpsertFunction => new FunctionName(DatabaseSchemaName, $"{UpsertPrefix}{_alias}");

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? _storeOptions.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public IEnumerable<DuplicatedField> DuplicatedFields => _fields.Values.OfType<DuplicatedField>();

        public string Alias
        {
            get { return _alias; }
            set
            {
                if (value.IsEmpty()) throw new ArgumentNullException(nameof(value));

                _alias = value.ToLower();
            }
        }


        public IWhereFragment DefaultWhereFragment()
        {
            return null;
        }

        IDocumentStorage IDocumentMapping.BuildStorage(IDocumentSchema schema)
        {
            var resolverType = IsHierarchy() ? typeof(HierarchicalResolver<>) : typeof(Resolver<>);

            var closedType = resolverType.MakeGenericType(DocumentType);

            return Activator.CreateInstance(closedType, schema.StoreOptions.Serializer(), this)
                .As<IDocumentStorage>();
        }

        public IDocumentSchemaObjects SchemaObjects { get; }

        void IDocumentMapping.DeleteAllDocuments(IConnectionFactory factory)
        {
            var sql = "truncate {0} cascade".ToFormat(Table.QualifiedName);
            factory.RunSql(sql);
        }

        public IdAssignment<T> ToIdAssignment<T>(IDocumentSchema schema)
        {
            var idType = IdMember.GetMemberType();

            var assignerType = typeof(IdAssigner<,>).MakeGenericType(typeof(T), idType);

            return (IdAssignment<T>) Activator.CreateInstance(assignerType, IdMember, IdStrategy, schema);
        }

        public IQueryableDocument ToQueryableDocument()
        {
            return this;
        }

        public IDocumentUpsert BuildUpsert(IDocumentSchema schema)
        {
            // TODO -- temporary
            return this.As<IDocumentMapping>().BuildStorage(schema).As<IDocumentUpsert>();
        }

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback) where TOther : class
        {
            var joinOperator = joinType == JoinType.Inner ? "INNER JOIN" : "LEFT OUTER JOIN";

            var tableAlias = members.ToTableAlias();
            var locator = FieldFor(members).SqlLocator;

            var joinText = $"{joinOperator} {other.Table.QualifiedName} as {tableAlias} ON {locator} = {tableAlias}.id";

            return new IncludeJoin<TOther>(other, joinText, tableAlias, callback);
        }

        public IIdGeneration IdStrategy { get; set; }

        public Type DocumentType { get; }

        public TableName Table => new TableName(DatabaseSchemaName, $"{TablePrefix}{_alias}");

        public MemberInfo IdMember { get; }

        public virtual string[] SelectFields()
        {
            return IsHierarchy()
                ? new[] {"data", "id", DocumentTypeColumn, VersionColumn}
                : new[] {"data", "id", VersionColumn};
        }

        public PropertySearching PropertySearching { get; set; } = PropertySearching.JSON_Locator_Only;

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            if (members.Count() == 1)
            {
                return FieldFor(members.Single());
            }

            var key = members.Select(x => x.Name).Join("");
            return _fields.GetOrAdd(key,
                _ => new JsonLocatorField(_storeOptions.Serializer().EnumStorage, members.ToArray()));
        }

        public IWhereFragment FilterDocuments(IWhereFragment query)
        {
            return query;
        }

        public static DocumentMapping For<T>(string databaseSchemaName = StoreOptions.DefaultDatabaseSchemaName,
            Func<IDocumentMapping, StoreOptions, IIdGeneration> idGeneration = null)
        {
            var storeOptions = new StoreOptions
            {
                DatabaseSchemaName = databaseSchemaName,
                DefaultIdStrategy = idGeneration
            };

            return new DocumentMapping(typeof(T), storeOptions);
        }

        public static MemberInfo FindIdMember(Type documentType)
        {
            return (MemberInfo) GetProperties(documentType).FirstOrDefault(x => x.Name.EqualsIgnoreCase("id") || x.HasAttribute<IdentityAttribute>())
                   ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id") || x.HasAttribute<IdentityAttribute>());
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            return type.IsInterface
                ? new[] {type}
                    .Concat(type.GetInterfaces())
                    .SelectMany(i => i.GetProperties()).ToArray()
                : type.GetProperties();
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

        public string AliasFor(Type subclassType)
        {
            if (subclassType == DocumentType) return BaseAlias;

            var type = _subClasses.FirstOrDefault(x => x.DocumentType == subclassType);
            if (type == null)
            {
                throw new ArgumentOutOfRangeException(
                    $"Unknown subclass type '{subclassType.FullName}' for Document Hierarchy {DocumentType.FullName}");
            }

            return type.Alias;
        }

        public Type TypeFor(string alias)
        {
            if (alias == BaseAlias) return DocumentType;

            var subClassMapping = _subClasses.FirstOrDefault(x => x.Alias.EqualsIgnoreCase(alias));
            if (subClassMapping == null)
            {
                throw new ArgumentOutOfRangeException(nameof(alias),
                    $"No subclass in the hierarchy '{DocumentType.FullName}' matches the alias '{alias}'");
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
            if (property != null) return property.CanWrite && property.SetMethod != null;
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

        public bool IsHierarchy()
        {
            return _subClasses.Any() || DocumentType.IsAbstract || DocumentType.IsInterface;
        }


        private static string defaultDocumentAliasName(Type documentType)
        {
            var nameToAlias = documentType.Name;
            if (documentType.IsGenericType)
            {
                nameToAlias = _aliasSanitizer.Replace(documentType.GetPrettyName(), string.Empty);
            }
            var parts = new List<string> {nameToAlias.ToLower()};
            if (documentType.IsNested)
            {
                parts.Insert(0, documentType.DeclaringType.Name.ToLower());
            }

            return string.Join("_", parts);
        }

        public IField FieldFor(MemberInfo member)
        {
            return _fields.GetOrAdd(member.Name,
                name => new JsonLocatorField(_storeOptions.Serializer().EnumStorage, member));
        }

        public IField FieldFor(string memberName)
        {
            return _fields.GetOrAdd(memberName, name =>
            {
                var member = DocumentType.GetProperties().FirstOrDefault(x => x.Name == name).As<MemberInfo>() ??
                             DocumentType.GetFields().FirstOrDefault(x => x.Name == name);

                if (member == null) return null;

                return new JsonLocatorField(_storeOptions.Serializer().EnumStorage, member);
            });
        }

        public IField FieldForColumn(string columnName)
        {
            return _fields.Values.FirstOrDefault(x => x.ColumnName == columnName);
        }


        public DuplicatedField DuplicateField(string memberName, string pgType = null)
        {
            var field = FieldFor(memberName);
            var duplicate = new DuplicatedField(_storeOptions.Serializer().EnumStorage, field.Members);
            if (pgType.IsNotEmpty())
            {
                duplicate.PgType = pgType;
            }

            _fields[memberName] = duplicate;

            return duplicate;
        }

        public DuplicatedField DuplicateField(MemberInfo[] members, string pgType = null)
        {
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(_storeOptions.Serializer().EnumStorage, members);
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
}