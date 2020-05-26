using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Indexing.Unique;
using Marten.Services.Includes;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;
using Remotion.Linq;

namespace Marten.Schema
{
    public class DocumentMapping: FieldCollection, IDocumentMapping, IQueryableDocument, IFeatureSchema
    {
        public const string BaseAlias = "BASE";
        public const string TablePrefix = "mt_doc_";
        public const string UpsertPrefix = "mt_upsert_";
        public const string InsertPrefix = "mt_insert_";
        public const string UpdatePrefix = "mt_update_";
        public const string OverwritePrefix = "mt_overwrite_";
        public const string DocumentTypeColumn = "mt_doc_type";
        public const string MartenPrefix = "mt_";
        public const string LastModifiedColumn = "mt_last_modified";
        public const string DotNetTypeColumn = "mt_dotnet_type";
        public const string VersionColumn = "mt_version";
        public const string DeletedColumn = "mt_deleted";
        public const string DeletedAtColumn = "mt_deleted_at";

        private static readonly Regex _aliasSanitizer = new Regex("<|>", RegexOptions.Compiled);

        private readonly StoreOptions _storeOptions;

        private readonly IList<SubClassMapping> _subClasses = new List<SubClassMapping>();
        private string _alias;
        private string _databaseSchemaName;
        private MemberInfo _idMember;

        public DocumentMapping(Type documentType, StoreOptions storeOptions) : base("d.data", documentType, storeOptions)
        {
            _storeOptions = storeOptions ?? throw new ArgumentNullException(nameof(storeOptions));

            DocumentType = documentType ?? throw new ArgumentNullException(nameof(documentType));
            Alias = defaultDocumentAliasName(documentType);

            IdMember = FindIdMember(documentType);

            _storeOptions.applyPolicies(this);

            applyAnyMartenAttributes(documentType);
        }

        public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

        public MemberInfo VersionMember
        {
            get => _versionMember;
            set
            {
                if (value == null)
                {
                    UseOptimisticConcurrency = false;
                }
                else
                {
                    if (value.GetMemberType() != typeof(Guid))
                        throw new ArgumentOutOfRangeException(nameof(value), "The Version member has to be of type Guid");
                    UseOptimisticConcurrency = true;
                    _versionMember = value;
                }
            }
        }

        private void applyAnyMartenAttributes(Type documentType)
        {
            documentType.ForAttribute<MartenAttribute>(att => att.Modify(this));

            documentType.GetProperties()
                .Where(x => !x.HasAttribute<DuplicateFieldAttribute>() && TypeMappings.HasTypeMapping(x.PropertyType))
                .Each(prop => { prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop)); });

            documentType.GetFields()
                .Where(x => !x.HasAttribute<DuplicateFieldAttribute>() && TypeMappings.HasTypeMapping(x.FieldType))
                .Each(fieldInfo => { fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo)); });

            // DuplicateFieldAttribute does not require TypeMappings check
            documentType.GetProperties()
                .Where(x => x.HasAttribute<DuplicateFieldAttribute>())
                .Each(prop => { prop.ForAttribute<DuplicateFieldAttribute>(att => att.Modify(this, prop)); });

            documentType.GetFields()
                .Where(x => x.HasAttribute<DuplicateFieldAttribute>())
                .Each(fieldInfo => { fieldInfo.ForAttribute<DuplicateFieldAttribute>(att => att.Modify(this, fieldInfo)); });
        }

        public bool UseOptimisticConcurrency { get; set; } = false;

        public IList<IIndexDefinition> Indexes { get; } = new List<IIndexDefinition>();

        public IList<ForeignKeyDefinition> ForeignKeys { get; } = new List<ForeignKeyDefinition>();

        public IEnumerable<SubClassMapping> SubClasses => _subClasses;

        public DbObjectName UpsertFunction => new DbObjectName(DatabaseSchemaName, $"{UpsertPrefix}{_alias}");
        public DbObjectName InsertFunction => new DbObjectName(DatabaseSchemaName, $"{InsertPrefix}{_alias}");
        public DbObjectName UpdateFunction => new DbObjectName(DatabaseSchemaName, $"{UpdatePrefix}{_alias}");
        public DbObjectName OverwriteFunction => new DbObjectName(DatabaseSchemaName, $"{OverwritePrefix}{_alias}");

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? _storeOptions.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public EnumStorage EnumStorage
        {
            get { return _storeOptions.EnumStorage; }
        }

        public EnumStorage DuplicatedFieldEnumStorage
        {
            get { return _storeOptions.DuplicatedFieldEnumStorage; }
        }

        public DuplicatedField[] DuplicatedFields => fields().OfType<DuplicatedField>().ToArray();

        public string Alias
        {
            get { return _alias; }
            set
            {
                if (value.IsEmpty())
                    throw new ArgumentNullException(nameof(value));

                _alias = value.ToLower();
            }
        }

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            var extras = extraFilters(query).ToList();

            if (extras.Count > 0)
            {
                extras.Add(query);
                return new CompoundWhereFragment("and", extras.ToArray());
            }

            return query;
        }

        private IEnumerable<IWhereFragment> extraFilters(IWhereFragment query)
        {
            if (DeleteStyle == DeleteStyle.SoftDelete && !query.Contains(DeletedColumn))
            {
                yield return ExcludeSoftDeletedDocuments();
            }

            if (TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
            {
                yield return new TenantWhereFragment();
            }
        }

        public IWhereFragment DefaultWhereFragment()
        {
            var defaults = defaultFilters().ToArray();
            switch (defaults.Length)
            {
                case 0:
                    return null;

                case 1:
                    return defaults[0];

                default:
                    return new CompoundWhereFragment("and", defaults);
            }
        }

        private IEnumerable<IWhereFragment> defaultFilters()
        {
            if (DeleteStyle == DeleteStyle.SoftDelete)
            {
                yield return ExcludeSoftDeletedDocuments();
            }

            if (TenancyStyle == TenancyStyle.Conjoined)
            {
                yield return new TenantWhereFragment();
            }
        }

        public static IWhereFragment ExcludeSoftDeletedDocuments()
        {
            return new WhereFragment($"d.{DeletedColumn} = False");
        }

        IDocumentStorage IDocumentMapping.BuildStorage(StoreOptions options)
        {
            var resolverType = IsHierarchy() ? typeof(HierarchicalDocumentStorage<>) : typeof(DocumentStorage<>);

            var closedType = resolverType.MakeGenericType(DocumentType);

            return Activator.CreateInstance(closedType, this)
                .As<IDocumentStorage>();
        }

        void IDocumentMapping.DeleteAllDocuments(ITenant factory)
        {
            var sql = "truncate {0} cascade".ToFormat(Table.QualifiedName);
            factory.RunSql(sql);
        }

        // TODO -- see if you can eliminate the tenant argument here
        public IdAssignment<T> ToIdAssignment<T>(ITenant tenant)
        {
            var idType = IdMember.GetMemberType();

            var assignerType = typeof(IdAssigner<,>).MakeGenericType(typeof(T), idType);

            return (IdAssignment<T>)Activator.CreateInstance(assignerType, IdMember, IdStrategy);
        }

        public IQueryableDocument ToQueryableDocument()
        {
            return this;
        }

        public Type IdType => IdMember?.GetMemberType();

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback)
        {
            var tableAlias = members.ToTableAlias();

            return new IncludeJoin<TOther>(other, FieldFor(members), tableAlias, callback, joinType);
        }

        public IIdGeneration IdStrategy { get; set; }

        public IDocumentMapping Root => this;
        public Type DocumentType { get; }

        public virtual DbObjectName Table => new DbObjectName(DatabaseSchemaName, $"{TablePrefix}{_alias}");

        public MemberInfo IdMember
        {
            get { return _idMember; }
            set
            {
                _idMember = value;

                if (_idMember != null && !_idMember.GetMemberType().IsOneOf(typeof(int), typeof(Guid), typeof(long), typeof(string)))
                {
                    throw new ArgumentOutOfRangeException(nameof(IdMember), "Id members must be an int, long, Guid, or string");
                }

                if (_idMember != null)
                {
                    removeIdField();

                    var idField = new IdField(_idMember);
                    setField(_idMember.Name, idField);
                    IdStrategy = defineIdStrategy(DocumentType, _storeOptions);
                }
            }
        }

        public virtual string[] SelectFields()
        {
            return IsHierarchy()
                ? new[] { "data", "id", DocumentTypeColumn, VersionColumn }
                : new[] { "data", "id", VersionColumn };
        }

        public PropertySearching PropertySearching { get; set; } = PropertySearching.JSON_Locator_Only;
        public DeleteStyle DeleteStyle { get; set; } = DeleteStyle.Remove;
        public bool StructuralTyped { get; set; }

        public string DdlTemplate { get; set; }

        public static DocumentMapping<T> For<T>(string databaseSchemaName = StoreOptions.DefaultDatabaseSchemaName,
            Func<IDocumentMapping, StoreOptions, IIdGeneration> idGeneration = null)
        {
            var storeOptions = new StoreOptions
            {
                DatabaseSchemaName = databaseSchemaName,
                DefaultIdStrategy = idGeneration
            };

            return new DocumentMapping<T>(storeOptions);
        }

        public static MemberInfo FindIdMember(Type documentType)
        {
            // Order of finding id member should be
            // 1) IdentityAttribute on property
            // 2) IdentityAttribute on field
            // 3) Id Property
            // 4) Id field
            return GetProperties(documentType).FirstOrDefault(x => x.HasAttribute<IdentityAttribute>())
                   ?? documentType.GetFields().FirstOrDefault(x => x.HasAttribute<IdentityAttribute>())
                   ?? (MemberInfo)GetProperties(documentType).FirstOrDefault(x => x.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                   ?? documentType.GetFields().FirstOrDefault(x => x.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            return type.GetTypeInfo().IsInterface
                ? new[] { type }
                    .Concat(type.GetInterfaces())
                    .SelectMany(i => i.GetProperties()).ToArray()
                : type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderByDescending(x => x.DeclaringType == type).ToArray();
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
            if (subclassType == DocumentType)
                return BaseAlias;

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
            if (alias == BaseAlias)
                return DocumentType;

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

        public IndexDefinition AddLastModifiedIndex(Action<IndexDefinition> configure = null)
        {
            var index = new IndexDefinition(this, LastModifiedColumn);
            configure?.Invoke(index);
            Indexes.Add(index);

            return index;
        }

        public IndexDefinition AddDeletedAtIndex(Action<IndexDefinition> configure = null)
        {
            if (DeleteStyle != DeleteStyle.SoftDelete)
                throw new InvalidOperationException($"DocumentMapping for {DocumentType.FullName} is not configured to use Soft Delete");

            var index = new IndexDefinition(this, DeletedAtColumn) { Modifier = $"WHERE {DeletedColumn}" };
            configure?.Invoke(index);
            Indexes.Add(index);

            return index;
        }

        public IndexDefinition AddIndex(params string[] columns)
        {
            var existing = Indexes.OfType<IndexDefinition>().FirstOrDefault(x => x.Columns.SequenceEqual(columns));
            if (existing != null)
            {
                return existing;
            }

            var index = new IndexDefinition(this, columns);
            Indexes.Add(index);

            return index;
        }

        public IIndexDefinition AddUniqueIndex(MemberInfo[][] members, UniqueIndexType indexType = UniqueIndexType.Computed, string indexName = null, IndexMethod indexMethod = IndexMethod.btree, TenancyScope tenancyScope = TenancyScope.Global)
        {
            if (indexType == UniqueIndexType.DuplicatedField)
            {
                var fields = members.Select(memberPath => DuplicateField(memberPath)).ToList();

                var index = AddIndex(fields.Select(m => m.ColumnName).ToArray());
                index.IndexName = indexName;
                index.Method = indexMethod;
                index.IsUnique = true;
                index.TenancyScope = tenancyScope;

                return index;
            }
            else
            {
                var index = new ComputedIndex(
                    this,
                    members)
                {
                    Method = indexMethod,
                    IndexName = indexName,
                    IsUnique = true,
                    TenancyScope = tenancyScope
                };

                var existing = Indexes.OfType<ComputedIndex>().FirstOrDefault(x => x.IndexName == index.IndexName);
                if (existing != null)
                {
                    return existing;
                }
                Indexes.Add(index);

                return index;
            }
        }

        /// <summary>
        /// Adds a full text index
        /// </summary>
        /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
        /// <param name="configure">Optional action to further configure the full text index</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex AddFullTextIndex(string regConfig = FullTextIndex.DefaultRegConfig, Action<FullTextIndex> configure = null)
        {
            var index = new FullTextIndex(this, regConfig);
            configure?.Invoke(index);

            return AddFullTextIndexIfDoesNotExist(index);
        }

        /// <summary>
        /// Adds a full text index
        /// </summary>
        /// <param name="members">Document fields that should be use by full text index</param>
        /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex AddFullTextIndex(MemberInfo[][] members, string regConfig = FullTextIndex.DefaultRegConfig, string indexName = null)
        {
            var index = new FullTextIndex(this, regConfig, members)
            {
                IndexName = indexName
            };

            return AddFullTextIndexIfDoesNotExist(index);
        }

        private FullTextIndex AddFullTextIndexIfDoesNotExist(FullTextIndex index)
        {
            var existing = Indexes.OfType<FullTextIndex>().FirstOrDefault(x => x.IndexName == index.IndexName);
            if (existing != null)
            {
                return existing;
            }
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
            var referenceMapping = referenceType != DocumentType ?
                _storeOptions.Storage.MappingFor(referenceType) : this;

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
                return new CombGuidIdGeneration();
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
            if (field != null)
                return field.IsPublic;
            var property = IdMember as PropertyInfo;
            if (property != null)
                return property.CanWrite && property.SetMethod != null;
            return false;
        }

        private HiloSettings _hiloSettings;
        private MemberInfo _versionMember;

        public HiloSettings HiloSettings
        {
            get { return _hiloSettings; }
            set
            {
                if (IdStrategy is HiloIdGeneration)
                {
                    IdStrategy = new HiloIdGeneration(DocumentType, value);
                    _hiloSettings = value;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"DocumentMapping for {DocumentType.FullName} is using {IdStrategy.GetType().FullName} as its Id strategy so cannot override Hilo sequence configuration");
                }
            }
        }

        public bool IsHierarchy()
        {
            return _subClasses.Any() || DocumentType.GetTypeInfo().IsAbstract || DocumentType.GetTypeInfo().IsInterface;
        }

        private static string defaultDocumentAliasName(Type documentType)
        {
            var nameToAlias = documentType.Name;
            if (documentType.GetTypeInfo().IsGenericType)
            {
                nameToAlias = _aliasSanitizer.Replace(documentType.GetPrettyName(), string.Empty).Replace(",", "_");
            }
            var parts = new List<string> { nameToAlias.ToLower() };
            if (documentType.IsNested)
            {
                parts.Insert(0, documentType.DeclaringType.Name.ToLower());
            }

            return string.Join("_", parts);
        }

        public DuplicatedField DuplicateField(string memberName, string pgType = null, bool notNull = false)
        {
            var field = FieldFor(memberName);

            var duplicate = new DuplicatedField(_storeOptions.DuplicatedFieldEnumStorage, field, _storeOptions.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime, notNull);

            if (pgType.IsNotEmpty())
            {
                duplicate.PgType = pgType;
            }

            setField(memberName, duplicate);

            return duplicate;
        }

        public DuplicatedField DuplicateField(MemberInfo[] members, string pgType = null, string columnName = null, bool notNull = false)
        {
            var field = FieldFor(members);
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(_storeOptions.DuplicatedFieldEnumStorage, field,
                _storeOptions.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime, notNull);

            if (pgType.IsNotEmpty())
            {
                duplicatedField.PgType = pgType;
            }

            if (columnName.IsNotEmpty())
            {
                duplicatedField.ColumnName = columnName;
            }

            setField(memberName, duplicatedField);

            return duplicatedField;
        }

        public IEnumerable<IndexDefinition> IndexesFor(string column)
        {
            return Indexes.OfType<IndexDefinition>().Where(x => x.Columns.Contains(column));
        }

        internal void Validate()
        {
            if (IdMember == null)
            {
                throw new InvalidDocumentException(
                    $"Could not determine an 'id/Id' field or property for requested document type {DocumentType.FullName}");
            }

            var idField = new IdField(IdMember);
            setField(IdMember.Name, idField);
        }

        public override string ToString()
        {
            return $"Storage for {DocumentType}, Table: {Table}";
        }

        IEnumerable<Type> IFeatureSchema.DependentTypes()
        {
            yield return typeof(SystemFunctions);

            foreach (var foreignKey in ForeignKeys)
            {
                // ExternalForeignKeyDefinition's will have a null ReferenceDocumentType, so we can skip it
                if (foreignKey.ReferenceDocumentType == null)
                    continue;

                yield return foreignKey.ReferenceDocumentType;
            }
        }

        bool IFeatureSchema.IsActive(StoreOptions options) => true;

        ISchemaObject[] IFeatureSchema.Objects => toSchemaObjects().ToArray();

        private IEnumerable<ISchemaObject> toSchemaObjects()
        {
            yield return new DocumentTable(this);
            yield return new UpsertFunction(this);
            yield return new InsertFunction(this);
            yield return new UpdateFunction(this);

            if (UseOptimisticConcurrency)
            {
                yield return new OverwriteFunction(this);
            }
        }

        Type IFeatureSchema.StorageType => DocumentType;
        public string Identifier => Alias.ToLowerInvariant();

        public void WritePermissions(DdlRules rules, StringWriter writer)
        {
            var template = DdlTemplate.IsNotEmpty()
                ? rules.Templates[DdlTemplate.ToLower()]
                : rules.Templates["default"];

            new DocumentTable(this).WriteTemplate(template, writer);
            new UpsertFunction(this).WriteTemplate(rules, template, writer);
        }
    }

    public class DocumentMapping<T>: DocumentMapping
    {
        public DocumentMapping(StoreOptions storeOptions) : base(typeof(T), storeOptions)
        {
            var configure = typeof(T).GetMethod("ConfigureMarten", BindingFlags.Static | BindingFlags.Public);
            configure?.Invoke(null, new object[] { this });
        }

        /// <summary>
        /// Find a field by lambda expression representing a property or field
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public IField FieldFor(Expression<Func<T, object>>  expression)
        {
            return FieldFor(FindMembers.Determine(expression));
        }

        /// <summary>
        /// Marks a property or field on this document type as a searchable field that is also duplicated in the
        /// database document table
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="pgType">Optional, overrides the Postgresql column type for the duplicated field</param>
        /// <param name="configure">Optional, allows you to customize the Postgresql database index configured for the duplicated field</param>
        /// <returns></returns>
        public void Duplicate(Expression<Func<T, object>> expression, string pgType = null, NpgsqlDbType? dbType = null, Action<IndexDefinition> configure = null, bool notNull = false)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);

            var duplicateField = DuplicateField(visitor.Members.ToArray(), pgType, notNull: notNull);

            if (dbType.HasValue)
                duplicateField.DbType = dbType.Value;

            var indexDefinition = AddIndex(duplicateField.ColumnName);
            configure?.Invoke(indexDefinition);
        }

        /// <summary>
        /// Programmatically directs Marten to map all the subclasses of <cref name="T"/> to a hierarchy of types
        /// </summary>
        /// <param name="allSubclassTypes">All the subclass types of <cref name="T"/> that you wish to map.
        /// You can use either params of <see cref="Type"/> or <see cref="MappedType"/> or a mix, since Type can implicitly convert to MappedType (without an alias)</param>
        /// <returns></returns>
        public void AddSubClassHierarchy(params MappedType[] allSubclassTypes)
        {
            allSubclassTypes.Each(subclassType =>
                AddSubClass(
                    subclassType.Type,
                    allSubclassTypes.Except(new[] { subclassType }),
                    subclassType.Alias
                )
            );
        }

        /// <summary>
        /// Programmatically directs Marten to map all the subclasses of <cref name="T"/> to a hierarchy of types. <c>Unadvised in projects with many types.</c>
        /// </summary>
        /// <returns></returns>
        public void AddSubClassHierarchy()
        {
            var baseType = typeof(T);
            var allSubclassTypes = baseType.GetTypeInfo().Assembly.GetTypes()
                .Where(t => t.GetTypeInfo().IsSubclassOf(baseType) || baseType.GetTypeInfo().IsInterface && t.GetInterfaces().Contains(baseType))
                .Select(t => (MappedType)t).ToList();

            allSubclassTypes.Each<MappedType>(subclassType =>
                AddSubClass(
                    subclassType.Type,
                    allSubclassTypes.Except<MappedType>(new[] { subclassType }),
                    null
                )
            );
        }

        /// <summary>
        /// Adds a computed index
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="configure"></param>
        public void Index(Expression<Func<T, object>> expression, Action<ComputedIndex> configure = null)
        {
            Index(new[] { expression }, configure);
        }

        /// <summary>
        /// Adds a computed index
        /// </summary>
        /// <param name="expressions"></param>
        /// <param name="configure"></param>
        public void Index(IReadOnlyCollection<Expression<Func<T, object>>> expressions, Action<ComputedIndex> configure = null)
        {
            MemberInfo[][] members = expressions
                .Select(e =>
                {
                    var visitor = new FindMembers();
                    visitor.Visit(e);
                    return visitor.Members.ToArray();
                }).ToArray();

            var index = new ComputedIndex(this, members);
            configure?.Invoke(index);
            Indexes.Add(index);
        }

        public void UniqueIndex(params Expression<Func<T, object>>[] expressions)
        {
            UniqueIndex(UniqueIndexType.Computed, null, expressions);
        }

        public void UniqueIndex(string indexName, params Expression<Func<T, object>>[] expressions)
        {
            UniqueIndex(UniqueIndexType.Computed, indexName, expressions);
        }

        public void UniqueIndex(UniqueIndexType indexType, params Expression<Func<T, object>>[] expressions)
        {
            UniqueIndex(indexType, null, expressions);
        }

        public void UniqueIndex(UniqueIndexType indexType, string indexName, params Expression<Func<T, object>>[] expressions)
        {
            UniqueIndex(indexType, indexName, tenancyScope: TenancyScope.Global, expressions);
        }

        public void UniqueIndex(UniqueIndexType indexType, string indexName, TenancyScope tenancyScope = TenancyScope.Global, params Expression<Func<T, object>>[] expressions)
        {
            AddUniqueIndex(
                expressions
                    .Select(e =>
                    {
                        var visitor = new FindMembers();
                        visitor.Visit(e);
                        return visitor.Members.ToArray();
                    })
                    .ToArray(),
                indexType,
                indexName,
                IndexMethod.btree,
                tenancyScope);
        }

        /// <summary>
        /// Adds a full text index with default region config set to 'english'
        /// </summary>
        /// <param name="expressions">Document fields that should be use by full text index</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex FullTextIndex(params Expression<Func<T, object>>[] expressions)
        {
            return FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
        }

        /// <summary>
        /// Adds a full text index with default region config set to 'english'
        /// </summary>
        /// <param name="expressions">Document fields that should be use by full text index</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex FullTextIndex(Action<FullTextIndex> configure, params Expression<Func<T, object>>[] expressions)
        {
            var index = FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
            configure(index);
            return index;
        }

        /// <summary>
        /// Adds a full text index
        /// </summary>
        /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
        /// <param name="expressions">Document fields that should be use by full text index</param>
        /// <remarks>
        /// See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex FullTextIndex(string regConfig, params Expression<Func<T, object>>[] expressions)
        {
            return AddFullTextIndex(
                expressions
                .Select(e =>
                {
                    var visitor = new FindMembers();
                    visitor.Visit(e);
                    return visitor.Members.ToArray();
                })
                .ToArray(),
                regConfig);
        }

        /// <summary>
        /// Adds foreign key index to other marten document
        /// </summary>
        /// <typeparam name="TReference">Document type</typeparam>
        /// <param name="expression">Field selector</param>
        /// <param name="foreignKeyConfiguration">customize foreign key configuration</param>
        /// <param name="indexConfiguration">customize index configuration</param>
        public void ForeignKey<TReference>(
            Expression<Func<T, object>> expression,
            Action<ForeignKeyDefinition> foreignKeyConfiguration = null,
            Action<IndexDefinition> indexConfiguration = null)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);

            var foreignKeyDefinition = AddForeignKey(visitor.Members.ToArray(), typeof(TReference));
            foreignKeyConfiguration?.Invoke(foreignKeyDefinition);

            var indexDefinition = AddIndex(foreignKeyDefinition.ColumnName);
            indexConfiguration?.Invoke(indexDefinition);
        }

        /// <summary>
        /// Adds foreign key index to non-marten table
        /// </summary>
        /// <param name="expression">Field selector</param>
        /// <param name="tableName">external table name</param>
        /// <param name="columnName">referenced column to external table</param>
        /// <param name="schemaName">external table schema name, if not provided then DatabaseSchemaName from store options will be used</param>
        /// <param name="foreignKeyConfiguration">customize foreign key configuration</param>
        public void ForeignKey(
            Expression<Func<T, object>> expression,
            string tableName,
            string columnName,
            string schemaName = null,
            Action<ExternalForeignKeyDefinition> foreignKeyConfiguration = null)
        {
            schemaName = schemaName ?? DatabaseSchemaName;

            var visitor = new FindMembers();
            visitor.Visit(expression);

            var duplicateField = DuplicateField(visitor.Members.ToArray());

            var foreignKey = new ExternalForeignKeyDefinition(duplicateField.ColumnName, this, schemaName, tableName, columnName);
            foreignKeyConfiguration?.Invoke(foreignKey);
            ForeignKeys.Add(foreignKey);
        }
    }
}
