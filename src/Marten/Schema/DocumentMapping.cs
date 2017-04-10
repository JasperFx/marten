using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Baseline;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services.Includes;
using Marten.Util;
using Remotion.Linq;

namespace Marten.Schema
{
    public class DocumentMapping : FieldCollection, IDocumentMapping, IQueryableDocument
    {
        public const string BaseAlias = "BASE";
        public const string TablePrefix = "mt_doc_";
        public const string UpsertPrefix = "mt_upsert_";
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
        private readonly DocumentSchemaObjects _schemaObjects;
        private MemberInfo _idMember;


        public DocumentMapping(Type documentType, StoreOptions storeOptions) : base("d.data", documentType, storeOptions)
        {
            if (documentType == null) throw new ArgumentNullException(nameof(documentType));
            if (storeOptions == null) throw new ArgumentNullException(nameof(storeOptions));

            _schemaObjects = new DocumentSchemaObjects(this);

            _storeOptions = storeOptions;

            DocumentType = documentType;
            Alias = defaultDocumentAliasName(documentType);

            IdMember = FindIdMember(documentType);

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

        public IList<IIndexDefinition> Indexes { get; } = new List<IIndexDefinition>();

        public IList<ForeignKeyDefinition> ForeignKeys { get; } = new List<ForeignKeyDefinition>();

        public IEnumerable<SubClassMapping> SubClasses => _subClasses;

        public FunctionName UpsertFunction => new FunctionName(DatabaseSchemaName, $"{UpsertPrefix}{_alias}");

        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName ?? _storeOptions.DatabaseSchemaName; }
            set { _databaseSchemaName = value; }
        }

        public DuplicatedField[] DuplicatedFields => fields().OfType<DuplicatedField>().ToArray();

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
            if (DeleteStyle == DeleteStyle.Remove) return null;

            return ExcludeSoftDeletedDocuments();
        }

        public static IWhereFragment ExcludeSoftDeletedDocuments()
        {
            return new WhereFragment($"d.{DeletedColumn} = False");
        }

        IDocumentStorage IDocumentMapping.BuildStorage(IDocumentSchema schema)
        {
            var resolverType = IsHierarchy() ? typeof(HierarchicalDocumentStorage<>) : typeof(DocumentStorage<>);

            var closedType = resolverType.MakeGenericType(DocumentType);

            return Activator.CreateInstance(closedType, schema.StoreOptions.Serializer(), this, schema.StoreOptions.UseCharBufferPooling)
                .As<IDocumentStorage>();
        }

        public virtual IDocumentSchemaObjects SchemaObjects => _schemaObjects;

        public IList<string> DependentScripts => _schemaObjects.DependentScripts;
        public IList<Type> DependentTypes => _schemaObjects.DependentTypes;

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
            return this.As<IDocumentMapping>().BuildStorage(schema).As<IDocumentUpsert>();
        }

        public Type IdType => IdMember?.GetMemberType();

        public IncludeJoin<TOther> JoinToInclude<TOther>(JoinType joinType, IQueryableDocument other, MemberInfo[] members, Action<TOther> callback)
        {
            var tableAlias = members.ToTableAlias();

            return new IncludeJoin<TOther>(other, FieldFor(members), tableAlias, callback, joinType);
        }

        public IIdGeneration IdStrategy { get; set; }

        public Type DocumentType { get; }

        public virtual TableName Table => new TableName(DatabaseSchemaName, $"{TablePrefix}{_alias}");

        public MemberInfo IdMember
        {
            get { return _idMember; }
            set
            {
                _idMember = value;

                if (_idMember != null && !_idMember.GetMemberType().IsOneOf(typeof(int), typeof(Guid), typeof(long), typeof(string)))
                {
                    throw new ArgumentOutOfRangeException(nameof(IdMember),"Id members must be an int, long, Guid, or string");
                }

                if (_idMember != null)
                {
                    var idField = new IdField(IdMember);
                    setField(IdMember.Name, idField);

                    IdStrategy = defineIdStrategy(DocumentType, _storeOptions);
                }
            }
        }

        public virtual string[] SelectFields()
        {
            return IsHierarchy()
                ? new[] {"data", "id", DocumentTypeColumn, VersionColumn}
                : new[] {"data", "id", VersionColumn};
        }

        public PropertySearching PropertySearching { get; set; } = PropertySearching.JSON_Locator_Only;
        public DeleteStyle DeleteStyle { get; set; } = DeleteStyle.Remove;
        public bool StructuralTyped { get; set; }

        public string DdlTemplate { get; set; }



        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            if (DeleteStyle == DeleteStyle.Remove) return query;

            if (query.Contains(DeletedColumn)) return query;

            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

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
            return (MemberInfo) GetProperties(documentType).FirstOrDefault(x => x.Name.EqualsIgnoreCase("id") || x.HasAttribute<IdentityAttribute>())
                   ?? documentType.GetFields().FirstOrDefault(x => x.Name.EqualsIgnoreCase("id") || x.HasAttribute<IdentityAttribute>());
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            return type.GetTypeInfo().IsInterface
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

            var index = new IndexDefinition(this, DeletedAtColumn) {Modifier = $"WHERE {DeletedColumn}"};
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

        public ForeignKeyDefinition AddForeignKey(string memberName, Type referenceType)
        {
            var field = FieldFor(memberName);
            return AddForeignKey(field.Members, referenceType);
        }


        public ForeignKeyDefinition AddForeignKey(MemberInfo[] members, Type referenceType)
        {
            var referenceMapping = _storeOptions.Storage.MappingFor(referenceType);

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
            return _subClasses.Any() || DocumentType.GetTypeInfo().IsAbstract || DocumentType.GetTypeInfo().IsInterface;
        }


        private static string defaultDocumentAliasName(Type documentType)
        {
            var nameToAlias = documentType.Name;
            if (documentType.GetTypeInfo().IsGenericType)
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






        public DuplicatedField DuplicateField(string memberName, string pgType = null)
        {
            var field = FieldFor(memberName);
            var duplicate = new DuplicatedField(_storeOptions.Serializer().EnumStorage, field.Members);
            if (pgType.IsNotEmpty())
            {
                duplicate.PgType = pgType;
            }

            setField(memberName, duplicate);

            return duplicate;
        }

        public DuplicatedField DuplicateField(MemberInfo[] members, string pgType = null, string columnName = null)
        {
            var memberName = members.Select(x => x.Name).Join("");

            var duplicatedField = new DuplicatedField(_storeOptions.Serializer().EnumStorage, members);
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

        public override string ToString()
        {
            return $"Storage for {DocumentType}, Table: {Table}";
        }

    }

    public class DocumentMapping<T> : DocumentMapping
    {
        public DocumentMapping(StoreOptions storeOptions) : base(typeof(T), storeOptions)
        {
            var configure = typeof(T).GetMethod("ConfigureMarten", BindingFlags.Static | BindingFlags.Public);
            configure?.Invoke(null, new object[] {this});

        }

        /// <summary>
        /// Marks a property or field on this document type as a searchable field that is also duplicated in the 
        /// database document table
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="pgType">Optional, overrides the Postgresql column type for the duplicated field</param>
        /// <param name="configure">Optional, allows you to customize the Postgresql database index configured for the duplicated field</param>
        /// <returns></returns>
        public void Duplicate(Expression<Func<T, object>> expression, string pgType = null, Action<IndexDefinition> configure = null)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);

            var duplicateField = DuplicateField(visitor.Members.ToArray(), pgType);
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
            var visitor = new FindMembers();
            visitor.Visit(expression);

            var index = new ComputedIndex(this, visitor.Members.ToArray());
            configure?.Invoke(index);
            Indexes.Add(index);
        }

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
    }
}
