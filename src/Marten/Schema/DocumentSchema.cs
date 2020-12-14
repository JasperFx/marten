using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Storage;
using Marten.Storage.Metadata;

namespace Marten.Schema
{
    internal class DocumentSchema : IFeatureSchema
    {
        private readonly DocumentMapping _mapping;

        public DocumentSchema(DocumentMapping mapping)
        {
            _mapping = mapping;

            Table = new DocumentTable(_mapping);

            foreach (var metadataColumn in Table.Columns.OfType<MetadataColumn>())
            {
                metadataColumn.RegisterForLinqSearching(mapping);
            }

            Upsert = new UpsertFunction(_mapping);
            Insert = new InsertFunction(_mapping);
            Update = new UpdateFunction(_mapping);

            if (_mapping.UseOptimisticConcurrency) Overwrite = new OverwriteFunction(_mapping);
        }

        public OverwriteFunction Overwrite { get; }

        public UpdateFunction Update { get;  }

        public InsertFunction Insert { get; }

        public UpsertFunction Upsert { get;  }

        public DocumentTable Table { get; }

        public IEnumerable<Type> DependentTypes()
        {
            yield return typeof(SystemFunctions);

            foreach (var foreignKey in _mapping.ForeignKeys)
            {
                // ExternalForeignKeyDefinition's will have a null ReferenceDocumentType, so we can skip it
                if (foreignKey.ReferenceDocumentType == null)
                    continue;

                yield return foreignKey.ReferenceDocumentType;
            }
        }

        public bool IsActive(StoreOptions options)
        {
            return true;
        }

        private IEnumerable<ISchemaObject> toSchemaObjects()
        {
            yield return Table;
            yield return Upsert;
            yield return Insert;
            yield return Update;

            if (Overwrite != null) yield return Overwrite;
        }

        public ISchemaObject[] Objects => toSchemaObjects().ToArray();
        public Type StorageType => _mapping.DocumentType;
        public string Identifier => _mapping.Alias.ToLowerInvariant();
        public void WritePermissions(DdlRules rules, StringWriter writer)
        {
            var template = _mapping.DdlTemplate.IsNotEmpty()
                ? rules.Templates[_mapping.DdlTemplate.ToLower()]
                : rules.Templates["default"];

            Table.WriteTemplate(template, writer);
            Upsert.WriteTemplate(rules, template, writer);
            Update.WriteTemplate(rules, template, writer);
            Insert.WriteTemplate(rules, template, writer);
            Overwrite?.WriteTemplate(rules, template, writer);
        }
    }
}
