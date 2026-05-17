using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Core;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Core.Migrations;

namespace Marten.Schema;

internal class DocumentSchema: IFeatureSchema
{
    private readonly DocumentMapping _mapping;

    public DocumentSchema(DocumentMapping mapping)
    {
        _mapping = mapping;

        Table = new DocumentTable(_mapping);

        foreach (var metadataColumn in Table.Columns.OfType<MetadataColumn>())
            metadataColumn.RegisterForLinqSearching(mapping);
    }

    public DocumentTable Table { get; }

    public IEnumerable<Type> DependentTypes()
    {
        // #4404: SystemFunctions had been required by the old
        // mt_upsert_* / mt_insert_* / mt_update_* / mt_overwrite_*
        // Postgres functions emitted by the document codegen. The
        // closed-shape path uses raw SQL operations, so document
        // schemas no longer depend on it.
        foreach (var referencedType in _mapping.ReferencedTypes()) yield return referencedType;
    }

    public ISchemaObject[] Objects => toSchemaObjects().ToArray();
    public Type StorageType => _mapping.DocumentType;
    public string Identifier => _mapping.Alias.ToLowerInvariant();
    public Migrator Migrator => _mapping.StoreOptions.Advanced.Migrator;

    public void WritePermissions(Migrator rules, TextWriter writer)
    {
        var template = _mapping.DdlTemplate.IsNotEmpty()
            ? rules.Templates[_mapping.DdlTemplate.ToLower()]
            : rules.Templates["default"];

        Table.WriteTemplate(template, writer);
    }

    public bool IsActive(StoreOptions options)
    {
        return true;
    }

    private IEnumerable<ISchemaObject> toSchemaObjects()
    {
        yield return Table;

        if (_mapping.TenancyStyle == TenancyStyle.Conjoined)
        {
            var (enabled, settingName) = _mapping.ResolveRowLevelSecurity();
            yield return new RlsPolicySchemaObject(
                _mapping.TableName,
                enabled ? settingName : null);
        }
    }
}
