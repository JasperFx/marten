using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Baseline.Reflection;
using Marten.Exceptions;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Indexing.Unique;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Schema
{
    public interface IDocumentType
    {
        IDocumentType Root { get; }

        Type DocumentType { get; }

        Type IdType { get; }
        DbObjectName TableName { get; }

        DocumentMetadataCollection Metadata { get; }
        bool UseOptimisticConcurrency { get;}
        IList<IndexDefinition> Indexes { get; }
        IList<ForeignKey> ForeignKeys { get; }
        SubClasses SubClasses { get; }
        DbObjectName UpsertFunction { get; }
        DbObjectName InsertFunction { get; }
        DbObjectName UpdateFunction { get; }
        DbObjectName OverwriteFunction { get; }
        string DatabaseSchemaName { get; set; }
        EnumStorage EnumStorage { get; }
        Casing Casing { get; }
        string Alias { get; set; }
        IIdGeneration IdStrategy { get;}
        MemberInfo IdMember { get; }
        bool StructuralTyped { get; }
        string DdlTemplate { get; }
        IReadOnlyHiloSettings HiloSettings { get; }
        TenancyStyle TenancyStyle { get; }
        DuplicatedField[] DuplicatedFields { get; }
        bool IsHierarchy();
        IEnumerable<DocumentIndex> IndexesFor(string column);
        string AliasFor(Type subclassType);
        Type TypeFor(string alias);
        IField FieldFor(string memberName);


    }

    public class DocumentMapping: FieldMapping, IDocumentMapping, IDocumentType
    {
        private static readonly Regex _aliasSanitizer = new Regex("<|>", RegexOptions.Compiled);

        private readonly StoreOptions _storeOptions;


        private string _alias;
        private string _databaseSchemaName;


        private HiloSettings _hiloSettings;
        private MemberInfo _idMember;
        private readonly Lazy<DocumentSchema> _schema;

        public DocumentMapping(Type documentType, StoreOptions storeOptions): base("d.data", documentType, storeOptions)
        {
            _storeOptions = storeOptions ?? throw new ArgumentNullException(nameof(storeOptions));

            DocumentType = documentType ?? throw new ArgumentNullException(nameof(documentType));
            Alias = defaultDocumentAliasName(documentType);

            IdMember = FindIdMember(documentType);

            Metadata = new DocumentMetadataCollection(this);

            SubClasses = new SubClasses(this, storeOptions);

            _storeOptions.applyPolicies(this);

            applyAnyMartenAttributes(documentType);

            _schema = new Lazy<DocumentSchema>(() => new DocumentSchema(this));
        }

        internal DocumentSchema Schema => _schema.Value;

        public DocumentMetadataCollection Metadata { get; }


        public bool UseOptimisticConcurrency { get; set; }

        private void applyAnyMartenAttributes(Type documentType)
        {
            documentType.ForAttribute<MartenAttribute>(att => att.Modify(this));

            documentType.GetProperties()
                .Where(x => !x.HasAttribute<DuplicateFieldAttribute>() && PostgresqlProvider.Instance.HasTypeMapping(x.PropertyType))
                .Each(prop => { prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop)); });

            documentType.GetFields()
                .Where(x => !x.HasAttribute<DuplicateFieldAttribute>() && PostgresqlProvider.Instance.HasTypeMapping(x.FieldType))
                .Each(fieldInfo => { fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo)); });

            // DuplicateFieldAttribute does not require TypeMappings check
            documentType.GetProperties()
                .Where(x => x.HasAttribute<DuplicateFieldAttribute>())
                .Each(prop => { prop.ForAttribute<DuplicateFieldAttribute>(att => att.Modify(this, prop)); });

            documentType.GetFields()
                .Where(x => x.HasAttribute<DuplicateFieldAttribute>())
                .Each(fieldInfo => { fieldInfo.ForAttribute<DuplicateFieldAttribute>(att => att.Modify(this, fieldInfo)); });
        }

        public IList<IndexDefinition> Indexes { get; } = new List<IndexDefinition>();

        public IList<ForeignKey> ForeignKeys { get; } = new List<ForeignKey>();

        /// <summary>
        /// Access to all other document types that are linked to by foreign keys
        /// from this document type
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Type> ReferencedTypes()
        {
            return ForeignKeys.OfType<DocumentForeignKey>().Select(x => x.ReferenceDocumentType).Where(x => x != DocumentType);
        }

        public SubClasses SubClasses { get; }

        public DbObjectName UpsertFunction => new DbObjectName(DatabaseSchemaName, $"{SchemaConstants.UpsertPrefix}{_alias}");
        public DbObjectName InsertFunction => new DbObjectName(DatabaseSchemaName, $"{SchemaConstants.InsertPrefix}{_alias}");
        public DbObjectName UpdateFunction => new DbObjectName(DatabaseSchemaName, $"{SchemaConstants.UpdatePrefix}{_alias}");
        public DbObjectName OverwriteFunction => new DbObjectName(DatabaseSchemaName, $"{SchemaConstants.OverwritePrefix}{_alias}");

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? _storeOptions.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public EnumStorage EnumStorage
        {
            get { return _storeOptions.EnumStorage; }
        }

        public Casing Casing
        {
            get { return _storeOptions.Serializer().Casing; }
        }

        public string Alias
        {
            get => _alias;
            set
            {
                if (value.IsEmpty())
                    throw new ArgumentNullException(nameof(value));

                _alias = value.ToLower();
            }
        }

        public IIdGeneration IdStrategy { get; set; }

        public MemberInfo IdMember
        {
            get => _idMember;
            set
            {
                _idMember = value;

                if (_idMember != null && !_idMember.GetMemberType()
                    .IsOneOf(typeof(int), typeof(Guid), typeof(long), typeof(string)))
                    throw new ArgumentOutOfRangeException(nameof(IdMember),
                        "Id members must be an int, long, Guid, or string");

                if (_idMember != null)
                {
                    removeIdField();

                    var idField = new IdField(_idMember);
                    setField(_idMember.Name, idField);
                    IdStrategy = defineIdStrategy(DocumentType, _storeOptions);
                }
            }
        }

        public bool StructuralTyped { get; set; }

        public string DdlTemplate { get; set; }
        IReadOnlyHiloSettings IDocumentType.HiloSettings { get; }

        public HiloSettings HiloSettings
        {
            get => _hiloSettings;
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

        public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

        public Type IdType => IdMember?.GetMemberType();

        IDocumentMapping IDocumentMapping.Root => this;

        IDocumentType IDocumentType.Root => this;

        public Type DocumentType { get; }

        public virtual DbObjectName TableName => new DbObjectName(DatabaseSchemaName, $"{SchemaConstants.TablePrefix}{_alias}");

        public DuplicatedField[] DuplicatedFields => fields().OfType<DuplicatedField>().ToArray();

        public static DocumentMapping<T> For<T>(string databaseSchemaName = SchemaConstants.DefaultSchema)
        {
            var storeOptions = new StoreOptions
            {
                DatabaseSchemaName = databaseSchemaName
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
                   ?? (MemberInfo)GetProperties(documentType)
                       .FirstOrDefault(x => x.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                   ?? documentType.GetFields()
                       .FirstOrDefault(x => x.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            return type.GetTypeInfo().IsInterface
                ? new[] {type}
                    .Concat(type.GetInterfaces())
                    .SelectMany(i => i.GetProperties()).ToArray()
                : type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .OrderByDescending(x => x.DeclaringType == type).ToArray();
        }


        public DocumentIndex AddGinIndexToData()
        {
            var index = AddIndex("data");
            index.ToGinWithJsonbPathOps();

            PropertySearching = PropertySearching.ContainmentOperator;

            return index;
        }

        public DocumentIndex AddLastModifiedIndex(Action<DocumentIndex> configure = null)
        {
            var index = new DocumentIndex(this, SchemaConstants.LastModifiedColumn);
            configure?.Invoke(index);
            Indexes.Add(index);

            return index;
        }

        public DocumentIndex AddDeletedAtIndex(Action<DocumentIndex> configure = null)
        {
            if (DeleteStyle != DeleteStyle.SoftDelete)
                throw new InvalidOperationException(
                    $"DocumentMapping for {DocumentType.FullName} is not configured to use Soft Delete");

            var index = new DocumentIndex(this, SchemaConstants.DeletedAtColumn)
            {
                Predicate = SchemaConstants.DeletedColumn
            };

            configure?.Invoke(index);
            Indexes.Add(index);

            return index;
        }

        public DocumentIndex AddIndex(params string[] columns)
        {
            var existing = Indexes.OfType<DocumentIndex>().FirstOrDefault(x => x.Columns.SequenceEqual(columns));
            if (existing != null) return existing;

            var index = new DocumentIndex(this, columns);
            Indexes.Add(index);

            return index;
        }

        public IndexDefinition AddUniqueIndex(MemberInfo[][] members,
            UniqueIndexType indexType = UniqueIndexType.Computed, string indexName = null,
            IndexMethod indexMethod = IndexMethod.btree, TenancyScope tenancyScope = TenancyScope.Global)
        {
            if (indexType == UniqueIndexType.DuplicatedField)
            {
                var fields = members.Select(memberPath => DuplicateField(memberPath)).ToList();

                var index = AddIndex(fields.Select(m => m.ColumnName).ToArray());
                index.Name = indexName;
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
                    Method = indexMethod, Name = indexName, IsUnique = true, TenancyScope = tenancyScope
                };

                var existing = Indexes.OfType<ComputedIndex>().FirstOrDefault(x => x.Name == index.Name);
                if (existing != null) return existing;
                Indexes.Add(index);

                return index;
            }
        }

        /// <summary>
        ///     Adds a full text index
        /// </summary>
        /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
        /// <param name="configure">Optional action to further configure the full text index</param>
        /// <remarks>
        ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex AddFullTextIndex(string regConfig = FullTextIndex.DefaultRegConfig,
            Action<FullTextIndex> configure = null)
        {
            var index = new FullTextIndex(this, regConfig);
            configure?.Invoke(index);

            return AddFullTextIndexIfDoesNotExist(index);
        }

        /// <summary>
        ///     Adds a full text index
        /// </summary>
        /// <param name="members">Document fields that should be use by full text index</param>
        /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
        /// <remarks>
        ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex AddFullTextIndex(MemberInfo[][] members, string regConfig = FullTextIndex.DefaultRegConfig,
            string indexName = null)
        {
            var index = new FullTextIndex(this, regConfig, members) {Name = indexName};

            return AddFullTextIndexIfDoesNotExist(index);
        }

        private FullTextIndex AddFullTextIndexIfDoesNotExist(FullTextIndex index)
        {
            var existing = Indexes.OfType<FullTextIndex>().FirstOrDefault(x => x.Name == index.Name);
            if (existing != null) return existing;
            Indexes.Add(index);

            return index;
        }

        public DocumentForeignKey AddForeignKey(string memberName, Type referenceType)
        {
            var field = FieldFor(memberName);
            return AddForeignKey(field.Members, referenceType);
        }

        public DocumentForeignKey AddForeignKey(MemberInfo[] members, Type referenceType)
        {
            var referenceMapping =
                referenceType != DocumentType ? _storeOptions.Storage.MappingFor(referenceType) : this;

            var duplicateField = DuplicateField(members);

            var foreignKey = new DocumentForeignKey(duplicateField.ColumnName, this, referenceMapping);
            ForeignKeys.Add(foreignKey);

            return foreignKey;
        }

        private IIdGeneration defineIdStrategy(Type documentType, StoreOptions options)
        {
            if (!idMemberIsSettable()) return new NoOpIdGeneration();

            var idType = IdMember.GetMemberType();
            if (idType == typeof(string)) return new StringIdGeneration();
            if (idType == typeof(Guid)) return new CombGuidIdGeneration();
            if (idType == typeof(int) || idType == typeof(long))
                return new HiloIdGeneration(documentType, options.Advanced.HiloSequenceDefaults);

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


        public bool IsHierarchy()
        {
            return SubClasses.Any() || DocumentType.GetTypeInfo().IsAbstract || DocumentType.GetTypeInfo().IsInterface;
        }

        private static string defaultDocumentAliasName(Type documentType)
        {
            var nameToAlias = documentType.Name;
            if (documentType.GetTypeInfo().IsGenericType)
                nameToAlias = _aliasSanitizer.Replace(documentType.GetPrettyName(), string.Empty).Replace(",", "_");
            var parts = new List<string> {nameToAlias.ToLower()};
            if (documentType.IsNested) parts.Insert(0, documentType.DeclaringType.Name.ToLower());

            return string.Join("_", parts);
        }

        public DuplicatedField DuplicateField(string memberName, string pgType = null, bool notNull = false)
        {
            var field = FieldFor(memberName);

            var duplicate = new DuplicatedField(_storeOptions.Advanced.DuplicatedFieldEnumStorage, field,
                _storeOptions.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime, notNull);

            if (pgType.IsNotEmpty()) duplicate.PgType = pgType;

            setField(memberName, duplicate);

            return duplicate;
        }

        public DuplicatedField DuplicateField(MemberInfo[] members, string pgType = null, string columnName = null,
            bool notNull = false)
        {
            var field = FieldFor(members);
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(_storeOptions.Advanced.DuplicatedFieldEnumStorage, field,
                _storeOptions.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime, notNull);

            if (pgType.IsNotEmpty()) duplicatedField.PgType = pgType;

            if (columnName.IsNotEmpty()) duplicatedField.ColumnName = columnName;

            setField(memberName, duplicatedField);

            return duplicatedField;
        }

        public IEnumerable<DocumentIndex> IndexesFor(string column)
        {
            return Indexes.OfType<DocumentIndex>().Where(x => x.Columns.Contains(column));
        }

        internal void CompileAndValidate()
        {
            if (IdMember == null)
                throw new InvalidDocumentException(
                    $"Could not determine an 'id/Id' field or property for requested document type {DocumentType.FullName}");

            if (Metadata.TenantId.Member != null && TenancyStyle != TenancyStyle.Conjoined)
            {
                throw new InvalidDocumentException($"Tenancy style must be set to {nameof(TenancyStyle.Conjoined)} to map tenant id metadata for {DocumentType.FullName}.");
            }

            if (Metadata.DocumentType.Member != null && !IsHierarchy())
            {
                throw new InvalidDocumentException($"{DocumentType.FullName} must be part of a document hierarchy to map document type metadata.");
            }

            if ((Metadata.IsSoftDeleted.Member != null || Metadata.SoftDeletedAt.Member != null) && DeleteStyle != DeleteStyle.SoftDelete)
            {
                throw new InvalidDocumentException($"{DocumentType.FullName} must be configured for soft deletion to map soft deleted metadata.");
            }


            var idField = new IdField(IdMember);
            setField(IdMember.Name, idField);
        }




        public string AliasFor(Type subclassType)
        {
            if (subclassType == DocumentType)
                return SchemaConstants.BaseAlias;

            var type = SubClasses.FirstOrDefault(x => x.DocumentType == subclassType);
            if (type == null)
                throw new ArgumentOutOfRangeException(
                    $"Unknown subclass type '{subclassType.FullName}' for Document Hierarchy {DocumentType.FullName}");

            return type.Alias;
        }

        // This method is used in generated code, so please don't delete this!!!!
        public Type TypeFor(string alias)
        {
            if (alias == SchemaConstants.BaseAlias)
                return DocumentType;

            var subClassMapping = SubClasses.FirstOrDefault(x => x.Alias.EqualsIgnoreCase(alias));
            if (subClassMapping == null)
                throw new ArgumentOutOfRangeException(nameof(alias),
                    $"No subclass in the hierarchy '{DocumentType.FullName}' matches the alias '{alias}'");

            return subClassMapping.DocumentType;
        }



        public override string ToString()
        {
            return $"Storage for {DocumentType}, Table: {TableName}";
        }




    }

    public class DocumentMapping<T>: DocumentMapping
    {
        public DocumentMapping(StoreOptions storeOptions): base(typeof(T), storeOptions)
        {
            var configure = typeof(T).GetMethod("ConfigureMarten", BindingFlags.Static | BindingFlags.Public);
            configure?.Invoke(null, new object[] {this});
        }

        /// <summary>
        ///     Find a field by lambda expression representing a property or field
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        public IField FieldFor(Expression<Func<T, object>> expression)
        {
            return FieldFor(FindMembers.Determine(expression));
        }

        /// <summary>
        ///     Marks a property or field on this document type as a searchable field that is also duplicated in the
        ///     database document table
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="pgType">Optional, overrides the Postgresql column type for the duplicated field</param>
        /// <param name="configure">
        ///     Optional, allows you to customize the Postgresql database index configured for the duplicated
        ///     field
        /// </param>
        /// <returns></returns>
        public void Duplicate(Expression<Func<T, object>> expression, string pgType = null, NpgsqlDbType? dbType = null,
            Action<DocumentIndex> configure = null, bool notNull = false)
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
        ///     Adds a computed index
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="configure"></param>
        public void Index(Expression<Func<T, object>> expression, Action<ComputedIndex> configure = null)
        {
            Index(new[] {expression}, configure);
        }

        /// <summary>
        ///     Adds a computed index
        /// </summary>
        /// <param name="expressions"></param>
        /// <param name="configure"></param>
        public void Index(IReadOnlyCollection<Expression<Func<T, object>>> expressions,
            Action<ComputedIndex> configure = null)
        {
            var members = expressions
                .Select(FindMembers.Determine).ToArray();

            var index = new ComputedIndex(this, members);
            configure?.Invoke(index);
            Indexes.Add(index);
        }

        public void UniqueIndex(UniqueIndexType indexType, string indexName,
            params Expression<Func<T, object>>[] expressions)
        {
            UniqueIndex(indexType, indexName, TenancyScope.Global, expressions);
        }

        public void UniqueIndex(UniqueIndexType indexType, string indexName,
            TenancyScope tenancyScope = TenancyScope.Global, params Expression<Func<T, object>>[] expressions)
        {
            var members = expressions
                .Select(e =>
                {
                    var visitor = new FindMembers();
                    visitor.Visit(e);
                    return visitor.Members.ToArray();
                })
                .ToArray();

            if (members.Length == 0)
            {
                throw new InvalidOperationException($"Unique index on {typeof(T)} requires at least one property/field");
            }

            AddUniqueIndex(
                members,
                indexType,
                indexName,
                IndexMethod.btree,
                tenancyScope);
        }

        /// <summary>
        ///     Adds a full text index
        /// </summary>
        /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
        /// <param name="expressions">Document fields that should be use by full text index</param>
        /// <remarks>
        ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
        /// </remarks>
        public FullTextIndex FullTextIndex(string regConfig, params Expression<Func<T, object>>[] expressions)
        {
            return AddFullTextIndex(
                expressions
                    .Select(FindMembers.Determine)
                    .ToArray(),
                regConfig);
        }




    }
}
