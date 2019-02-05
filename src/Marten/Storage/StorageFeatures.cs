using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    public class StorageFeatures
    {
        private readonly StoreOptions _options;

        private readonly Ref<ImHashMap<Type, DocumentMapping>> _documentMappings =
            Ref.Of(ImHashMap<Type, DocumentMapping>.Empty);

        private readonly Ref<ImHashMap<Type, IDocumentMapping>> _mappings =
            Ref.Of(ImHashMap<Type, IDocumentMapping>.Empty);

        private readonly Ref<ImHashMap<Type, IDocumentStorage>> _documentTypes =
            Ref.Of(ImHashMap<Type, IDocumentStorage>.Empty);

        private readonly Dictionary<Type, IFeatureSchema> _features = new Dictionary<Type, IFeatureSchema>();

        public StorageFeatures(StoreOptions options)
        {
            _options = options;

            SystemFunctions = new SystemFunctions(options);

            Transforms = options.Transforms.As<Transforms.Transforms>();
        }

        public Transforms.Transforms Transforms { get; }

        /// <summary>
        /// Register custom storage features
        /// </summary>
        /// <param name="feature"></param>
        public void Add(IFeatureSchema feature)
        {
            _features.Add(feature.StorageType, feature);
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

        public SystemFunctions SystemFunctions { get; }

        public IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Value.Enumerate().Select(x => x.Value);

        public IEnumerable<IDocumentMapping> AllMappings => _documentMappings.Value.Enumerate().Select(x => x.Value).Union(_mappings.Value.Enumerate().Select(x => x.Value));

        public DocumentMapping MappingFor(Type documentType)
        {            
            if (!_documentMappings.Value.TryFind(documentType, out var value))
            {
                value = typeof(DocumentMapping<>).CloseAndBuildAs<DocumentMapping>(_options, documentType);
                _documentMappings.Swap(d => d.AddOrUpdate(documentType, value));
            }

            return value;
        }

        internal IDocumentMapping FindMapping(Type documentType)
        {
            if (!_mappings.Value.TryFind(documentType, out var value))
            {
                var subclass = AllDocumentMappings.SelectMany(x => x.SubClasses)
                    .FirstOrDefault(x => x.DocumentType == documentType) as IDocumentMapping;

                value = subclass ?? MappingFor(documentType);
                _mappings.Swap(d => d.AddOrUpdate(documentType, value));
            }

            return value;
        }

        internal void AddMapping(IDocumentMapping mapping)
        {                        
            _mappings.Swap(d => d.AddOrUpdate(mapping.DocumentType, mapping));
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            if (!_documentTypes.Value.TryFind(documentType, out var value))
            {
                var mapping = FindMapping(documentType);

                assertNoDuplicateDocumentAliases();

                value = mapping.BuildStorage(_options);

                _documentTypes.Swap(d => d.AddOrUpdate(documentType, value));
            }
            return value;
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
                        $"Document types {group.Select(x => x.DocumentType.Name).Join(", ")} all have the same document alias '{group.Key}'. You must explicitly make document type aliases to disambiguate the database schema objects";
                }).Join("\n");

                throw new AmbiguousDocumentTypeAliasesException(message);
            }
        }

        public IFeatureSchema FindFeature(Type featureType)
        {
            if (_features.ContainsKey(featureType))
            {
                return _features[featureType];
            }

            if (_options.Events.AllEvents().Any(x => x.DocumentType == featureType))
            {
                return _options.Events;
            }

            return MappingFor(featureType);
        }

        internal void PostProcessConfiguration()
        {
            SystemFunctions.AddSystemFunction(_options, "mt_immutable_timestamp", "text");
            SystemFunctions.AddSystemFunction(_options, "mt_immutable_timestamptz", "text");

            Add(SystemFunctions);

            Add(Transforms.As<IFeatureSchema>());

            Add(_options.Events);
            _features[typeof(StreamState)] = _options.Events;
            _features[typeof(EventStream)] = _options.Events;
            _features[typeof(IEvent)] = _options.Events;
                        
            _mappings.Swap(d => d.AddOrUpdate(typeof(IEvent), new EventQueryMapping(_options)));

            foreach (var mapping in _documentMappings.Value.Enumerate().Select(x => x.Value))
            {
                foreach (var subClass in mapping.SubClasses)
                {                                        
                    _mappings.Swap(d => d.AddOrUpdate(subClass.DocumentType, subClass));
                    _features[subClass.DocumentType] = subClass.Parent;
                }
            }
        }

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

        public IEnumerable<IFeatureSchema> AllActiveFeatures(ITenant tenant)
        {
            yield return SystemFunctions;

            var mappings = _documentMappings.Value
                .Enumerate().Select(x => x.Value)
                .OrderBy(x => x.DocumentType.Name)
                .TopologicalSort(m =>
                {
                    return m.ForeignKeys
                        .Where(x => x.ReferenceDocumentType != m.DocumentType)
                        .Select(keyDefinition => keyDefinition.ReferenceDocumentType)
                        .Select(MappingFor);
                });

            foreach (var mapping in mappings)
            {
                yield return mapping;
            }

            if (SequenceIsRequired())
            {
                yield return tenant.Sequences;
            }

            if (Transforms.IsActive(_options))
            {
                yield return Transforms;
            }

            if (_options.Events.IsActive(_options))
            {
                yield return _options.Events;
            }

            var custom = _features.Values
                .Where(x => x.GetType().GetTypeInfo().Assembly != GetType().GetTypeInfo().Assembly).ToArray();

            foreach (var featureSchema in custom)
            {
                yield return featureSchema;
            }
        }

        internal bool SequenceIsRequired()
        {
            return _documentMappings.Value.Enumerate().Select(x => x.Value).Any(x => x.IdStrategy.RequiresSequences);
        }
    }
}