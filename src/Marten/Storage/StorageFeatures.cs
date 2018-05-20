using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Util;

namespace Marten.Storage
{
    public class StorageFeatures
    {
        private readonly StoreOptions _options;
        private readonly DocumentStorageFeatures _documentStorageFeatures = new DocumentStorageFeatures();
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
#if NETSTANDARD1_3
            var ctor = typeof(T).GetConstructor(new Type[] { typeof(StoreOptions) });
#else
            var ctor = typeof(T).GetTypeInfo().GetConstructor(new Type[]{typeof(StoreOptions)});
#endif

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

        public IEnumerable<DocumentMapping> AllDocumentMappings => _documentStorageFeatures.AllDocumentMappings;

        public IEnumerable<IDocumentMapping> AllMappings => _documentStorageFeatures.AllMappings;

        public DocumentMapping MappingFor(Type documentType) => _documentStorageFeatures.MappingFor(documentType, _options);

        internal IDocumentMapping FindDocumentMapping(Type documentType) => _documentStorageFeatures.FindMapping(documentType, _options);

        internal void AddDocumentMapping(IDocumentMapping mapping) => _documentStorageFeatures.AddMapping(mapping);

        public IDocumentStorage StorageFor(Type documentType) => _documentStorageFeatures.StorageFor(documentType, _options);
        
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

            Add(SystemFunctions);

            Add(Transforms.As<IFeatureSchema>());

            Add(_options.Events);
            _features[typeof(StreamState)] = _options.Events;
            _features[typeof(EventStream)] = _options.Events;
            _features[typeof(IEvent)] = _options.Events;

            _documentStorageFeatures.HoldEventQueryMapping(_options);
            
            foreach (var mapping in AllDocumentMappings)
            {
                foreach (var subClass in mapping.SubClasses)
                {
                    _documentStorageFeatures.HoldSubClassMapping(subClass.DocumentType, subClass);
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

            var mappings = AllDocumentMappings
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

            if (_documentStorageFeatures.SequenceIsRequired())
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

    }

}