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
    internal class DocumentStorageFeatures
    {
        private readonly ConcurrentDictionary<Type, DocumentMapping> _documentMappings =
            new ConcurrentDictionary<Type, DocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentMapping> _mappings =
            new ConcurrentDictionary<Type, IDocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentStorage> _documentTypes =
            new ConcurrentDictionary<Type, IDocumentStorage>();

        internal IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Values;
        internal IEnumerable<IDocumentMapping> AllMappings => _documentMappings.Values.Union(_mappings.Values);

        internal bool SequenceIsRequired()
        {
            return _documentMappings.Values.Any(x => x.IdStrategy.RequiresSequences);
        }
        internal DocumentMapping MappingFor(Type documentType, StoreOptions options)
        {
            return _documentMappings.GetOrAdd(documentType, type => typeof(DocumentMapping<>).CloseAndBuildAs<DocumentMapping>(options, documentType));
        }

        internal void AddMapping(IDocumentMapping mapping)
        {
            _mappings[mapping.DocumentType] = mapping;
        }

        internal IDocumentMapping FindMapping(Type documentType, StoreOptions options)
        {

            return _mappings.GetOrAdd(documentType, type =>
            {
                var subclass = AllDocumentMappings.SelectMany(x => x.SubClasses)
                    .FirstOrDefault(x => x.DocumentType == type) as IDocumentMapping;

                return subclass ?? MappingFor(documentType, options);
            });
        }

        public IDocumentStorage StorageFor(Type documentType, StoreOptions options) {
            return _documentTypes.GetOrAdd(documentType, type =>
            {
                var mapping = FindMapping(documentType, options);

                assertNoDuplicateDocumentAliases();

                return mapping.BuildStorage(options);
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

        public void HoldEventQueryMapping(StoreOptions options)
        {
            _mappings[typeof(IEvent)] = new EventQueryMapping(options);
        }

        public void HoldSubClassMapping(Type subClassDocumentType, SubClassMapping subClass)
        {
            _mappings[subClass.DocumentType] = subClass;
        }
    }
}