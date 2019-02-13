using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using NpgsqlTypes;

namespace Marten
{
    /// <summary>
    /// Used to customize or optimize the storage and retrieval of document types
    /// </summary>
    public class MartenRegistry
    {
        private readonly IList<Action<StoreOptions>> _alterations = new List<Action<StoreOptions>>();

        public MartenRegistry()
        {
        }

        /// <summary>
        /// Configure a single document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public DocumentMappingExpression<T> For<T>()
        {
            return new DocumentMappingExpression<T>(this);
        }

        private Action<StoreOptions> alter
        {
            set
            {
                _alterations.Add(value);
            }
        }

        internal void Apply(StoreOptions options)
        {
            foreach (var alteration in _alterations)
            {
                alteration(options);
            }
        }

        /// <summary>
        /// Include the declarations from another MartenRegistry type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void Include<T>() where T : MartenRegistry, new()
        {
            alter = x =>
            {
                var registry = new T();
                registry.Apply(x);
            };
        }

        /// <summary>
        /// Include the declarations from another MartenRegistry object
        /// </summary>
        /// <param name="registry"></param>
        public void Include(MartenRegistry registry)
        {
            alter = registry.Apply;
        }

        /// <summary>
        /// Overrides the strategy used to generate the ids.
        /// </summary>
        public void DefaultIdStrategy(Func<IDocumentMapping, StoreOptions, IIdGeneration> strategy)
        {
            alter = x => x.DefaultIdStrategy = strategy;
        }

        public class DocumentMappingExpression<T>
        {
            private readonly MartenRegistry _parent;

            public DocumentMappingExpression(MartenRegistry parent)
            {
                _parent = parent;

                _parent.alter = options => options.Storage.MappingFor(typeof(T));
            }

            /// <summary>
            /// Specify the property searching mechanism for this document type. The default is
            /// JSON_Locator_Only
            /// </summary>
            /// <param name="searching"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> PropertySearching(PropertySearching searching)
            {
                alter = m => m.PropertySearching = searching;
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
                alter = m => m.Alias = alias;
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
            public DocumentMappingExpression<T> Duplicate(Expression<Func<T, object>> expression, string pgType = null, NpgsqlDbType? dbType = null, Action<IndexDefinition> configure = null)
            {
                alter = mapping =>
                {
                    mapping.Duplicate(expression, pgType, dbType, configure);
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
                alter = m => m.Index(expression, configure);

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
                alter = m => m.Index(expressions, configure);

                return this;
            }

            /// <summary>
            /// Creates a unique index on this data member within the JSON data storage
            /// </summary>
            /// <param name="expressions"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> UniqueIndex(params Expression<Func<T, object>>[] expressions)
            {
                alter = m => m.UniqueIndex(expressions);

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
                alter = m => m.UniqueIndex(indexName, expressions);

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
                alter = m => m.UniqueIndex(indexType, expressions);

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
                alter = m => m.UniqueIndex(indexType, indexName, expressions);

                return this;
            }

            /// <summary>
            /// Creates an index on the predefined Last Modified column
            /// </summary>
            /// <param name="configure"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> IndexLastModified(Action<IndexDefinition> configure = null)
            {
                alter = m => m.AddLastModifiedIndex(configure);

                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(string regConfig = Schema.FullTextIndex.DefaultRegConfig, Action<FullTextIndex> configure = null)
            {
                alter = m => m.AddFullTextIndex(regConfig, configure);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(Action<FullTextIndex> configure)
            {
                alter = m => m.AddFullTextIndex(Schema.FullTextIndex.DefaultRegConfig, configure);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(params Expression<Func<T, object>>[] expressions)
            {
                FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(string regConfig, params Expression<Func<T, object>>[] expressions)
            {
                alter = m => m.FullTextIndex(regConfig, expressions);
                return this;
            }

            public DocumentMappingExpression<T> FullTextIndex(Action<FullTextIndex> configure, params Expression<Func<T, object>>[] expressions)
            {
                alter = m => m.FullTextIndex(configure, expressions);
                return this;
            }

            public DocumentMappingExpression<T> ForeignKey<TReference>(
                Expression<Func<T, object>> expression,
                Action<ForeignKeyDefinition> foreignKeyConfiguration = null,
                Action<IndexDefinition> indexConfiguration = null)
            {
                alter = m => m.ForeignKey<TReference>(expression, foreignKeyConfiguration, indexConfiguration);

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
                alter = mapping => mapping.HiloSettings = settings;
                return this;
            }

            /// <summary>
            /// Overrides the database schema name used to store the documents.
            /// </summary>
            public DocumentMappingExpression<T> DatabaseSchemaName(string databaseSchemaName)
            {
                alter = mapping => mapping.DatabaseSchemaName = databaseSchemaName;
                return this;
            }

            /// <summary>
            /// Overrides the stragtegy used for id generation.
            /// </summary>
            public DocumentMappingExpression<T> IdStrategy(IIdGeneration idStrategy)
            {
                alter = mapping => mapping.IdStrategy = idStrategy;
                return this;
            }

            public DocumentMappingExpression<T> Identity(Expression<Func<T, object>> member)
            {
                alter = mapping =>
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

            private Action<DocumentMapping<T>> alter
            {
                set
                {
                    Action<StoreOptions> alteration = o =>
                    {
                        value((DocumentMapping<T>)o.Storage.MappingFor(typeof(T)));
                    };

                    _parent.alter = alteration;
                }
            }

            /// <summary>
            /// Adds a Postgresql Gin index to the JSONB data column for this document type. Leads to faster
            /// querying, but does add overhead to storage and database writes
            /// </summary>
            /// <param name="configureIndex"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> GinIndexJsonData(Action<IndexDefinition> configureIndex = null)
            {
                alter = mapping =>
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
                alter = mapping => mapping.AddSubClass(subclassType, alias);
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
                alter = m => m.AddSubClassHierarchy(allSubclassTypes);
                return this;
            }

            /// <summary>
            /// Programmatically directs Marten to map all the subclasses of <cref name="T"/> to a hierarchy of types. <c>Unadvised in projects with many types.</c>
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> AddSubClassHierarchy()
            {
                alter = m => m.AddSubClassHierarchy();

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
                alter = m => m.UseOptimisticConcurrency = enabled;
                return this;
            }

            /// <summary>
            /// Directs Marten to apply "soft deletes" to this document type
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> SoftDeleted()
            {
                alter = m => m.DeleteStyle = DeleteStyle.SoftDelete;
                return this;
            }

            public DocumentMappingExpression<T> SoftDeletedWithIndex(Action<IndexDefinition> configure = null)
            {
                SoftDeleted();
                alter = m => m.AddDeletedAtIndex(configure);

                return this;
            }

            /// <summary>
            /// Direct this document type's DDL to be created with the named template
            /// </summary>
            /// <param name="templateName"></param>
            /// <returns></returns>
            public DocumentMappingExpression<T> DdlTemplate(string templateName)
            {
                alter = m => m.DdlTemplate = templateName;
                return this;
            }

            /// <summary>
            /// Marks just this document type as being stored with conjoined multi-tenancy
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> MultiTenanted()
            {
                alter = m => m.TenancyStyle = TenancyStyle.Conjoined;
                return this;
            }

            /// <summary>
            /// Opt into the identity key generation strategy
            /// </summary>
            /// <returns></returns>
            public DocumentMappingExpression<T> UseIdentityKey()
            {
                alter = m => m.IdStrategy = new IdentityKeyGeneration(m, m.HiloSettings);
                return this;
            }

            public DocumentMappingExpression<T> VersionedWith(Expression<Func<T, Guid>> memberExpression)
            {
                alter = m => m.VersionMember = FindMembers.Determine(memberExpression).Single();
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