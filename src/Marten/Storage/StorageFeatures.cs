using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;

namespace Marten.Storage
{
    public class StorageFeatures
    {
        private readonly StoreOptions _parent;

        private readonly ConcurrentDictionary<Type, DocumentMapping> _documentMappings =
            new ConcurrentDictionary<Type, DocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentMapping> _mappings =
            new ConcurrentDictionary<Type, IDocumentMapping>();

        public StorageFeatures(StoreOptions parent)
        {
            _parent = parent;
            SystemFunctions = new SystemFunctions(parent);
        }

        public SystemFunctions SystemFunctions { get; }

        public IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Values;

        public DocumentMapping MappingFor(Type documentType)
        {
            return _documentMappings.GetOrAdd(documentType, type =>
            {
                var mapping = typeof(DocumentMapping<>).CloseAndBuildAs<DocumentMapping>(_parent, documentType);

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

        public IFeatureSchema FindFeature(Type featureType)
        {
            if (featureType == typeof(SystemFunctions))
            {
                return SystemFunctions;
            }

            throw new NotImplementedException();
        }


    }
}