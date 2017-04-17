using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;

namespace Marten.Storage
{
    public class StorageFeatures
    {
        private readonly StoreOptions _options;

        private readonly ConcurrentDictionary<Type, DocumentMapping> _documentMappings =
            new ConcurrentDictionary<Type, DocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentMapping> _mappings =
            new ConcurrentDictionary<Type, IDocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes =
            new ConcurrentDictionary<Type, IDocumentStorage>();


        private readonly Dictionary<Type, IFeatureSchema> _features = new Dictionary<Type, IFeatureSchema>();

        public StorageFeatures(StoreOptions options)
        {
            _options = options;

            SystemFunctions = new SystemFunctions(options);
            Sequences = new SequenceFactory(options);

            store(SystemFunctions);
            store(options.Transforms.As<IFeatureSchema>());
            store(Sequences);

            store(options.Events);

            _mappings[typeof(IEvent)] = new EventQueryMapping(_options);
        }

        public SequenceFactory Sequences { get;}

        private void store(IFeatureSchema feature)
        {
            _features.Add(feature.StorageType, feature);
        }

        

        public SystemFunctions SystemFunctions { get; }

        public IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Values;

        public DocumentMapping MappingFor(Type documentType)
        {
            return _documentMappings.GetOrAdd(documentType, type =>
            {
                var mapping = typeof(DocumentMapping<>).CloseAndBuildAs<DocumentMapping>(_options, documentType);

                if (mapping.IdMember == null)
                {
                    throw new InvalidDocumentException(
                        $"Could not determine an 'id/Id' field or property for requested document type {documentType.FullName}");
                }

                return mapping;
            });
        }



        internal IDocumentMapping FindMapping(Type documentType)
        {
            return _mappings.GetOrAdd(documentType, type =>
            {
                var subclass =  AllDocumentMappings.SelectMany(x => x.SubClasses)
                    .FirstOrDefault(x => x.DocumentType == type) as IDocumentMapping;

                return subclass ?? MappingFor(documentType);
            });
        }

        internal void AddMapping(IDocumentMapping mapping)
        {
            _mappings[mapping.DocumentType] = mapping;
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _documentTypes.GetOrAdd(documentType, type =>
            {
                var mapping = FindMapping(documentType);

                assertNoDuplicateDocumentAliases();

                return mapping.BuildStorage(_options);
            });
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
            throw new NotImplementedException();
        }


        internal void CompileSubClasses()
        {
            foreach (var mapping in _documentMappings.Values)
            {
                foreach (var subClass in mapping.SubClasses)
                {
                    _mappings[subClass.DocumentType] = subClass;
                }
            }
        }
    }
}