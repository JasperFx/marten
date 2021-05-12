using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Baseline.ImTools;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Exceptions;
using Marten.Schema;

#nullable enable
namespace Marten.Storage
{
    public class StorageFeatures
    {
        private readonly StoreOptions _options;

        private readonly Ref<ImHashMap<Type, DocumentMapping>> _documentMappings =
            Ref.Of(ImHashMap<Type, DocumentMapping>.Empty);

        private readonly Ref<ImHashMap<Type, IDocumentMapping>> _mappings =
            Ref.Of(ImHashMap<Type, IDocumentMapping>.Empty);

        private readonly Dictionary<Type, IFeatureSchema> _features = new Dictionary<Type, IFeatureSchema>();

        internal StorageFeatures(StoreOptions options)
        {
            _options = options;

            SystemFunctions = new SystemFunctions(options);

            Transforms = options.Transforms.As<Transforms.Transforms>();
        }

        private readonly IDictionary<Type, IDocumentMappingBuilder> _builders
            = new Dictionary<Type, IDocumentMappingBuilder>();

        private readonly IList<Type> _buildingList = new List<Type>();

        internal DocumentMapping Build(Type type, StoreOptions options)
        {
            if (_buildingList.Contains(type))
            {
                throw new InvalidOperationException($"Cyclic dependency between documents detected. The types are: {_buildingList.Select(x => x.FullNameInCode()).Join(", ")}");
            }

            _buildingList.Add(type);

            if (_builders.TryGetValue(type, out var builder))
            {
                var mapping = builder.Build(options);
                _buildingList.Remove(type);
                return mapping;
            }

            _buildingList.Remove(type);
            return new DocumentMapping(type, options);
        }

        internal void RegisterDocumentType(Type documentType)
        {
            if (!_builders.ContainsKey(documentType))
            {
                _builders[documentType] =
                    typeof(DocumentMappingBuilder<>).CloseAndBuildAs<IDocumentMappingBuilder>(documentType);
            }
        }

        internal DocumentMappingBuilder<T> BuilderFor<T>()
        {
            if (_builders.TryGetValue(typeof(T), out var builder))
            {
                return (DocumentMappingBuilder<T>) builder;
            }

            builder = new DocumentMappingBuilder<T>();
            _builders[typeof(T)] = builder;

            return (DocumentMappingBuilder<T>) builder;
        }

        internal void BuildAllMappings()
        {
            foreach (var pair in _builders.ToArray())
            {
                // Just forcing them all to be built
                var mapping = MappingFor(pair.Key);
            }
        }

        public Transforms.Transforms Transforms { get; }

        /// <summary>
        /// Register custom storage features
        /// </summary>
        /// <param name="feature"></param>
        public void Add(IFeatureSchema feature)
        {
            if (!_features.ContainsKey(feature.StorageType))
            {
                _features[feature.StorageType] = feature;
            }
        }

        /// <summary>
        /// Register custom storage features by type. Type must have either a no-arg, public
        /// constructor or a constructor that takes in a single StoreOptions parameter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void Add<T>() where T : IFeatureSchema
        {
            var ctor = typeof(T).GetTypeInfo().GetConstructor(new Type[] { typeof(StoreOptions) });

            IFeatureSchema feature;
            if (ctor != null)
            {
                feature = Activator.CreateInstance(typeof(T), _options)
                    .As<IFeatureSchema>();
            }
            else
            {
                feature = Activator.CreateInstance(typeof(T)).As<IFeatureSchema>();
            }

            Add(feature);
        }

        internal SystemFunctions SystemFunctions { get; }

        internal IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Value.Enumerate().Select(x => x.Value);

        internal DocumentMapping MappingFor(Type documentType)
        {
            if (!_documentMappings.Value.TryFind(documentType, out var value))
            {
                value = Build(documentType, _options);
                _documentMappings.Swap(d => d.AddOrUpdate(documentType, value));
            }

            return value;
        }

        internal IDocumentMapping FindMapping(Type documentType)
        {
            if (documentType == null) throw new ArgumentNullException(nameof(documentType));

            if (!_mappings.Value.TryFind(documentType, out var value))
            {
                var subclass = AllDocumentMappings.SelectMany(x => x.SubClasses)
                    .FirstOrDefault(x => x.DocumentType == documentType) as IDocumentMapping;

                value = subclass ?? MappingFor(documentType);
                _mappings.Swap(d => d.AddOrUpdate(documentType, value));

                assertNoDuplicateDocumentAliases();
            }

            return value;
        }

        internal void AddMapping(IDocumentMapping mapping)
        {
            _mappings.Swap(d => d.AddOrUpdate(mapping.DocumentType, mapping));
        }


        private void assertNoDuplicateDocumentAliases()
        {
            var duplicates =
                AllDocumentMappings.Where(x => !x.StructuralTyped)
                    .GroupBy(x => x.Alias)
                    .Where(x => x.Count() > 1)
                    .ToArray();
            if (duplicates.Any())
            {
                var message = duplicates.Select(group =>
                {
                    return
                        $"Document types {group.Select(x => x.DocumentType.FullName).Join(", ")} all have the same document alias '{group.Key}'. You must explicitly make document type aliases to disambiguate the database schema objects";
                }).Join("\n");

                throw new AmbiguousDocumentTypeAliasesException(message);
            }
        }

        /// <summary>
        /// Retrieve an IFeatureSchema for the designated type
        /// </summary>
        /// <param name="featureType"></param>
        /// <returns></returns>
        public IFeatureSchema FindFeature(Type featureType)
        {
            if (_features.TryGetValue(featureType, out var schema))
            {
                return schema;
            }

            if (_options.EventGraph.AllEvents().Any(x => x.DocumentType == featureType))
            {
                return _options.EventGraph;
            }

            return MappingFor(featureType).Schema;
        }

        internal void PostProcessConfiguration()
        {
            SystemFunctions.AddSystemFunction(_options, "mt_immutable_timestamp", "text");
            SystemFunctions.AddSystemFunction(_options, "mt_immutable_timestamptz", "text");

            Add(SystemFunctions);

            Add(Transforms.As<IFeatureSchema>());

            Add(_options.EventGraph);
            _features[typeof(StreamState)] = _options.EventGraph;
            _features[typeof(StreamAction)] = _options.EventGraph;
            _features[typeof(IEvent)] = _options.EventGraph;

            _mappings.Swap(d => d.AddOrUpdate(typeof(IEvent), new EventQueryMapping(_options)));

            foreach (var mapping in _documentMappings.Value.Enumerate().Select(x => x.Value))
            {
                foreach (var subClass in mapping.SubClasses)
                {
                    _mappings.Swap(d => d.AddOrUpdate(subClass.DocumentType, subClass));
                    _features[subClass.DocumentType] = subClass.Parent.Schema;
                }
            }
        }

        /// <summary>
        /// All referenced schema names by this DocumentStore
        /// </summary>
        /// <returns></returns>
        public string[] AllSchemaNames()
        {
            var schemas = AllDocumentMappings
                .Select(x => x.DatabaseSchemaName)
                .Distinct()
                .ToList();

            schemas.Fill(_options.DatabaseSchemaName);
            schemas.Fill(_options.Events.DatabaseSchemaName);

            return schemas.Select(x => x.ToLowerInvariant()).ToArray();
        }


        internal IEnumerable<IFeatureSchema> AllActiveFeatures(ITenant tenant)
        {
            yield return SystemFunctions;

            var mappings = _documentMappings.Value
                .Enumerate().Select(x => x.Value)
                .OrderBy(x => x.DocumentType.Name)
                .TopologicalSort(m => m.ReferencedTypes()
                    .Select(MappingFor));

            foreach (var mapping in mappings)
            {
                yield return mapping.Schema;
            }

            if (SequenceIsRequired())
            {
                yield return tenant.Sequences;
            }

            if (Transforms.IsActive(_options))
            {
                yield return Transforms;
            }

            if (_options.Events.As<EventGraph>().IsActive(_options))
            {
                yield return _options.EventGraph;
            }

            var custom = _features.Values
                .Where(x => x.GetType().Assembly != GetType().Assembly).ToArray();

            foreach (var featureSchema in custom)
            {
                yield return featureSchema;
            }
        }

        internal bool SequenceIsRequired()
        {
            return _documentMappings.Value.Enumerate().Select(x => x.Value).Any(x => x.IdStrategy.RequiresSequences);
        }

        private ImHashMap<Type, IEnumerable<Type>> _typeDependencies = ImHashMap<Type, IEnumerable<Type>>.Empty;

        internal IEnumerable<Type> GetTypeDependencies(Type type)
        {
            if (_typeDependencies.TryFind(type, out var deps))
            {
                return deps;
            }

            deps = determineTypeDependencies(type);
            _typeDependencies = _typeDependencies.AddOrUpdate(type, deps);

            return deps;
        }

        private IEnumerable<Type> determineTypeDependencies(Type type)
        {
            var mapping = FindMapping(type);
            var documentMapping = mapping as DocumentMapping ?? (mapping as SubClassMapping)?.Parent;
            if (documentMapping == null)
                return Enumerable.Empty<Type>();


            return documentMapping.ReferencedTypes()
                .SelectMany(keyDefinition =>
                {
                    var results = new List<Type>();
                    // If the reference type has sub-classes, also need to insert/update them first too
                    if (FindMapping(keyDefinition) is DocumentMapping referenceMappingType && referenceMappingType.SubClasses.Any())
                    {
                        results.AddRange(referenceMappingType.SubClasses.Select(s => s.DocumentType));
                    }

                    results.Add(keyDefinition);
                    return results;
                });
        }


        /// <summary>
        /// Used to support MartenRegistry.Include()
        /// </summary>
        /// <param name="includedStorage"></param>
        internal void IncludeDocumentMappingBuilders(StorageFeatures includedStorage)
        {
            foreach (var builder in includedStorage._builders.Values)
            {
                if (_builders.TryGetValue(builder.DocumentType, out var existing))
                {
                    existing.Include(builder);
                }
                else
                {
                    _builders.Add(builder.DocumentType, builder);
                }
            }
        }
    }
}
