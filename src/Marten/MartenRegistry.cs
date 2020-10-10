using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Indexing.Unique;
using Marten.Storage;
using NpgsqlTypes;

namespace Marten
{

    internal interface IDocumentMappingBuilder
    {
        DocumentMapping Build(StoreOptions options);
    }

    internal class DocumentMappingBuilder<T> : IDocumentMappingBuilder
    {
        private readonly IList<Action<DocumentMapping<T>>> _alterations
            = new List<Action<DocumentMapping<T>>>();

        internal Action<DocumentMapping<T>> Alter
        {
            set
            {
                _alterations.Add(value);
            }
        }

        public DocumentMapping Build(StoreOptions options)
        {
            // TODO -- this is going to get fancier
            var mapping = new DocumentMapping<T>(options);
            foreach (var alteration in _alterations)
            {
                alteration(mapping);
            }

            return mapping;
        }
    }

    /// <summary>
    /// Used to customize or optimize the storage and retrieval of document types
    /// </summary>
    public class MartenRegistry
    {
        private readonly StoreOptions _storeOptions;

        public MartenRegistry(StoreOptions storeOptions)
        {
            _storeOptions = storeOptions;
        }

        /// <summary>
        /// Configure a single document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public DocumentMappingExpression<T> For<T>()
        {
            return new DocumentMappingExpression<T>(_storeOptions.Storage.BuilderFor<T>());
        }

        public class DocumentMappingExpression<T>
        {
            private DocumentMappingBuilder<T> _builder;

            internal DocumentMappingExpression(DocumentMappingBuilder<T> builder)
            {
                _builder = builder;
            }

            /// <summary>
            /// Specify the property searching mechanism for this document type. The default is
            /// JSON_Locator_Only
            /// </summary>
            /// <param name="searching"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> PropertySearching(PropertySearching searching)
            {
                _builder.Alter = m => m.PropertySearching = searching;
                return this;
            }

            /// <summary>
            /// Override the Postgresql schema alias for this document type in order
            /// to disambiguate similarly named document types. The default is just
            /// the document type name to lower case.
            /// </summary>
            /// <param name="alias"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> DocumentAlias(string alias)
            {
                _builder.Alter = m => m.Alias = alias;
                return this;
            }

            /// <summary>
            /// Marks a property or field on this document type as a searchable field that is also duplicated in the
            /// database document table
            /// </summary>
            /// <param name="expression"></param>
            /// <param name="pgType">Optional, overrides the Postgresql column type for the duplicated field</param>
            /// <param name="configure">Optional, allows you to customize the Postgresql database index configured for the duplicated field</param>
            /// <returns></returns>
            [Obsolete("Prefer Index() if you just want to optimize querying, or choose Duplicate() if you really want a duplicated field")]
            public DocumentMappingExpression<T> Searchable(Expression<Func<T, object>> expression, string pgType = null, NpgsqlDbType? dbType = null, Action<IndexDefinition> configure = null)
            {
                return Duplicate(expression, pgType, dbType, configure);
            }

            /// <summary>
            /// Marks a property or field on this document type as a searchable field that is also duplicated in the
            /// database document table
            /// </summary>
            /// <param name="expression"></param>
            /// <param name="pgType">Optional, overrides the Postgresql column type for the duplicated field</param>
            /// <param name="configure">Optional, allows you to customize the Postgresql database index configured for the duplicated field</param>
            /// <param name="dbType">Optional, overrides the Npgsql DbType for any parameter usage of this property</param>
            /// <returns></returns>
            public DocumentMappingExpression<T> Duplicate(Expression<Func<T, object>> expression, string pgType = null, NpgsqlDbType? dbType = null, Action<IndexDefinition> configure = null, bool notNull = false)
            {
                _builder.Alter = mapping =>
                {
                    mapping.Duplicate(expression, pgType, dbType, configure, notNull);
                };
                return this;
            }

            /// <summary>
            /// Creates a computed index on this data member within the JSON data storage
            /// </summary>
            /// <param name="expression"></param>
            /// <param name="configure"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> Index(Expression<Func<T, object>> expression, Action<ComputedIndex> configure = null)
            {
                _builder.Alter = m => m.Index(expression, configure);

                return this;
            }

            /// <summary>
            /// Creates a computed index on this data member within the JSON data storage
            /// </summary>
            /// <param name="expressions"></param>
            /// <param name="configure"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> Index(IReadOnlyCollection<Expression<Func<T, object>>> expressions, Action<ComputedIndex> configure = null)
            {
                _builder.Alter = m => m.Index(expressions, configure);

                return this;
            }

            /// <summary>
            /// Creates a unique index on this data member within the JSON data storage
            /// </summary>
            /// <param name="expressions"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> UniqueIndex(params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m => m.UniqueIndex(UniqueIndexType.Computed, null, expressions);

                return this;
            }

            /// <summary>
            /// Creates a unique index on this data member within the JSON data storage
            /// </summary>
            /// <param name="indexName">Name of the index</param>
            /// <param name="expressions"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> UniqueIndex(string indexName, params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m => m.UniqueIndex(UniqueIndexType.Computed, indexName, expressions);

                return this;
            }

            /// <summary>
            /// Creates a unique index on this data member within the JSON data storage
            /// </summary>
            /// <param name="indexType">Type of the index</param>
            /// <param name="expressions"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> UniqueIndex(UniqueIndexType indexType, params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m => m.UniqueIndex(indexType, null, expressions);

                return this;
            }

            /// <summary>
            /// Creates a unique index on this data member within the JSON data storage
            /// </summary>
            /// <param name="indexType">Type of the index</param>
            /// <param name="indexName">Name of the index</param>
            /// <param name="expressions"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> UniqueIndex(UniqueIndexType indexType, string indexName, params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m => m.UniqueIndex(indexType, indexName, expressions);

                return this;
            }

            /// <summary>
            /// Creates a unique index on this data member within the JSON data storage
            /// </summary>
            /// <param name="indexType">Type of the index</param>
            /// <param name="indexTenancyStyle">Style of tenancy</param>
            /// <param name="indexName">Name of the index</param>
            /// <param name="tenancyScope">Whether the unique index applies on a per tenant basis</param>
            /// <param name="expressions"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> UniqueIndex(UniqueIndexType indexType, string indexName, TenancyScope tenancyScope = TenancyScope.Global, params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m => m.UniqueIndex(indexType, indexName, tenancyScope, expressions);

                return this;
            }

            /// <summary>
            /// Creates an index on the predefined Last Modified column
            /// </summary>
            /// <param name="configure"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> IndexLastModified(Action<IndexDefinition> configure = null)
            {
                _builder.Alter = m => m.AddLastModifiedIndex(configure);

                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(string regConfig = Schema.FullTextIndex.DefaultRegConfig, Action<FullTextIndex> configure = null)
            {
                _builder.Alter = m => m.AddFullTextIndex(regConfig, configure);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(Action<FullTextIndex> configure)
            {
                _builder.Alter = m => m.AddFullTextIndex(Schema.FullTextIndex.DefaultRegConfig, configure);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(params Expression<Func<T, object>>[] expressions)
            {
                FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(string regConfig, params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m => m.FullTextIndex(regConfig, expressions);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(Action<FullTextIndex> configure, params Expression<Func<T, object>>[] expressions)
            {
                _builder.Alter = m =>
                {
                    var index = m.FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
                    configure(index);
                    FullTextIndex temp = index;
                };
                return this;
            }

            /// <summary>
            /// Add a foreign key reference to another document type
            /// </summary>
            /// <param name="expression"></param>
            /// <param name="foreignKeyConfiguration"></param>
            /// <param name="indexConfiguration"></param>
            /// <typeparam name="TReference"></typeparam>
            /// <returns></returns>
            public DocumentMappingExpression<T> ForeignKey<TReference>(
                Expression<Func<T, object>> expression,
                Action<ForeignKeyDefinition> foreignKeyConfiguration = null,
                Action<IndexDefinition> indexConfiguration = null)
            {
                _builder.Alter = m =>
                {
                    var visitor = new FindMembers();
                    visitor.Visit(expression);

                    var foreignKeyDefinition = m.AddForeignKey(visitor.Members.ToArray(), typeof(TReference));
                    foreignKeyConfiguration?.Invoke(foreignKeyDefinition);

                    var indexDefinition = m.AddIndex(foreignKeyDefinition.ColumnName);
                    indexConfiguration?.Invoke(indexDefinition);
                };

                return this;
            }

            public DocumentMappingExpression<T> ForeignKey(Expression<Func<T, object>> expression, string schemaName, string tableName, string columnName,
                                                           Action<ExternalForeignKeyDefinition> foreignKeyConfiguration = null)
            {
                _builder.Alter = m =>
                {
                    var schemaName1 = schemaName;
                    schemaName1 ??= m.DatabaseSchemaName;

                    var visitor = new FindMembers();
                    visitor.Visit(expression);

                    var duplicateField = m.DuplicateField(visitor.Members.ToArray());

                    var foreignKey =
                        new ExternalForeignKeyDefinition(duplicateField.ColumnName, m, schemaName1, tableName, columnName);
                    foreignKeyConfiguration?.Invoke(foreignKey);
                    m.ForeignKeys.Add(foreignKey);
                };

                return this;
            }

            /// <summary>
            /// Overrides the Hilo sequence increment and "maximum low" number for document types that
            /// use numeric id's and the Hilo Id assignment
            /// </summary>
            /// <param name="settings"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> HiloSettings(HiloSettings settings)
            {
                _builder.Alter = mapping => mapping.HiloSettings = settings;
                return this;
            }

            /// <summary>
            /// Overrides the database schema name used to store the documents.
            /// </summary>
            public DocumentMappingExpression<T> DatabaseSchemaName(string databaseSchemaName)
            {
                _builder.Alter = mapping => mapping.DatabaseSchemaName = databaseSchemaName;
                return this;
            }

            /// <summary>
            /// Overrides the stragtegy used for id generation.
            /// </summary>
            public DocumentMappingExpression<T> IdStrategy(IIdGeneration idStrategy)
            {
                _builder.Alter = mapping => mapping.IdStrategy = idStrategy;
                return this;
            }

            public DocumentMappingExpression<T> Identity(Expression<Func<T, object>> member)
            {
                _builder.Alter = mapping =>
                {
                    var members = FindMembers.Determine(member);
                    if (members.Length != 1)
                    {
                        throw new InvalidOperationException($"The expression {member} is not valid as an id column in Marten");
                    }

                    mapping.IdMember = members.Single();
                };

                return this;
            }

            /// <summary>
            /// Adds a Postgresql Gin index to the JSONB data column for this document type. Leads to faster
            /// querying, but does add overhead to storage and database writes
            /// </summary>
            /// <param name="configureIndex"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> GinIndexJsonData(Action<IndexDefinition> configureIndex = null)
            {
                _builder.Alter = mapping =>
                {
                    var index = mapping.AddGinIndexToData();

                    configureIndex?.Invoke(index);
                };

                return this;
            }

            /// <summary>
            /// Programmatically directs Marten to map this type to a hierarchy of types
            /// </summary>
            /// <param name="subclassType"></param>
            /// <param name="alias"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> AddSubClass(Type subclassType, string alias = null)
            {
                _builder.Alter = mapping => mapping.SubClasses.Add(subclassType, alias);
                return this;
            }

            /// <summary>
            /// Programmatically directs Marten to map all the subclasses of <cref name="T"/> to a hierarchy of types
            /// </summary>
            /// <param name="allSubclassTypes">All the subclass types of <cref name="T"/> that you wish to map.
            /// You can use either params of <see cref="Type"/> or <see cref="MappedType"/> or a mix, since Type can implicitly convert to MappedType (without an alias)</param>
            /// <returns></returns>
            public DocumentMappingExpression<T> AddSubClassHierarchy(params MappedType[] allSubclassTypes)
            {
                _builder.Alter = m => m.SubClasses.AddHierarchy(allSubclassTypes);
                return this;
            }

            /// <summary>
            /// Programmatically directs Marten to map all the subclasses of <cref name="T"/> to a hierarchy of types. <c>Unadvised in projects with many types.</c>
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> AddSubClassHierarchy()
            {
                _builder.Alter = m => m.SubClasses.AddHierarchy();

                return this;
            }

            public DocumentMappingExpression<T> AddSubClass<TSubclass>(string alias = null) where TSubclass : T
            {
                return AddSubClass(typeof(TSubclass), alias);
            }

            /// <summary>
            /// Directs Marten to use the optimistic versioning checks upon updates
            /// to this document type
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> UseOptimisticConcurrency(bool enabled)
            {
                _builder.Alter = m => m.UseOptimisticConcurrency = enabled;
                return this;
            }

            /// <summary>
            /// Directs Marten to apply "soft deletes" to this document type
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> SoftDeleted()
            {
                _builder.Alter = m => m.DeleteStyle = DeleteStyle.SoftDelete;
                return this;
            }

            public DocumentMappingExpression<T> SoftDeletedWithIndex(Action<IndexDefinition> configure = null)
            {
                SoftDeleted();
                _builder.Alter = m => m.AddDeletedAtIndex(configure);

                return this;
            }

            /// <summary>
            /// Direct this document type's DDL to be created with the named template
            /// </summary>
            /// <param name="templateName"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> DdlTemplate(string templateName)
            {
                _builder.Alter = m => m.DdlTemplate = templateName;
                return this;
            }

            /// <summary>
            /// Marks just this document type as being stored with conjoined multi-tenancy
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> MultiTenanted()
            {
                _builder.Alter = m => m.TenancyStyle = TenancyStyle.Conjoined;
                return this;
            }

            /// <summary>
            /// Opt into the identity key generation strategy
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> UseIdentityKey()
            {
                _builder.Alter = m => m.IdStrategy = new IdentityKeyGeneration(m, m.HiloSettings);
                return this;
            }

            /// <summary>
            /// Copy the document version metadata to the selected member
            /// </summary>
            /// <param name="memberExpression"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> VersionedWith(Expression<Func<T, Guid>> memberExpression)
            {
                var members = FindMembers.Determine(memberExpression);
                if (members.Length > 1)
                {
                    throw new ArgumentException($"The {nameof(VersionedWith)} member cannot be a nested property.", nameof(memberExpression));
                }

                _builder.Alter = m =>
                {
                    m.UseOptimisticConcurrency = true;
                    m.Metadata.Version.Member = members.First();
                };
                return this;
            }

            /// <summary>
            /// Copy the last modification date metadata to the selected member
            /// </summary>
            /// <param name="memberExpression"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> MapLastModifiedTo(Expression<Func<T, DateTimeOffset>> memberExpression)
            {
                var members = FindMembers.Determine(memberExpression);
                if (members.Length > 1)
                {
                    throw new ArgumentException($"The {nameof(MapLastModifiedTo)} member cannot be a nested property.", nameof(memberExpression));
                }
                _builder.Alter = m => m.Metadata.LastModified.Member = members.First();
                return this;
            }

            /// <summary>
            /// Copy the tenant id metadata to the selected member
            /// </summary>
            /// <param name="memberExpression"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> MapTenantIdTo(Expression<Func<T, string>> memberExpression)
            {
                var members = FindMembers.Determine(memberExpression);
                if (members.Length > 1)
                {
                    throw new ArgumentException($"The {nameof(MapTenantIdTo)} member cannot be a nested property.", nameof(memberExpression));
                }
                _builder.Alter = m => m.Metadata.TenantId.Member = members.First();
                return this;
            }

            /// <summary>
            /// Copy the is soft deleted metadata to the selected member
            /// </summary>
            /// <param name="memberExpression"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> MapIsSoftDeletedTo(Expression<Func<T, bool>> memberExpression)
            {
                var members = FindMembers.Determine(memberExpression);
                if (members.Length > 1)
                {
                    throw new ArgumentException($"The {nameof(MapIsSoftDeletedTo)} member cannot be a nested property.", nameof(memberExpression));
                }
                _builder.Alter = m => m.Metadata.IsSoftDeleted.Member = members.First();
                return this;
            }

            /// <summary>
            /// Copy the soft deleted timestamp to the selected member
            /// </summary>
            /// <param name="memberExpression"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> MapSoftDeletedAtTo(Expression<Func<T, DateTimeOffset?>> memberExpression)
            {
                var members = FindMembers.Determine(memberExpression);
                if (members.Length > 1)
                {
                    throw new ArgumentException($"The {nameof(MapSoftDeletedAtTo)} member cannot be a nested property.", nameof(memberExpression));
                }
                _builder.Alter = m => m.Metadata.SoftDeletedAt.Member = members.First();
                return this;
            }

            /// <summary>
            /// Copy the hierarchical document type to the selected member
            /// </summary>
            /// <param name="memberExpression"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> MapDocumentTypeTo(Expression<Func<T, string>> memberExpression)
            {
                var members = FindMembers.Determine(memberExpression);
                if (members.Length > 1)
                {
                    throw new ArgumentException($"The {nameof(MapDocumentTypeTo)} member cannot be a nested property.", nameof(memberExpression));
                }
                _builder.Alter = m => m.Metadata.DocumentType.Member = members.First();
                return this;
            }

        }
    }

    public class MappedType
    {
        public MappedType(Type type, string alias = null)
        {
            Type = type;
            Alias = alias;
        }

        public Type Type { get; set; }
        public string Alias { get; set; }

        public static implicit operator MappedType(Type type)
        {
            return new MappedType(type);
        }
    }
}
