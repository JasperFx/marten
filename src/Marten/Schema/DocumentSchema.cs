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
        // Document tables still depend on the mt_immutable_* helper
        // functions (used by computed indexes, duplicated date fields,
        // etc.). The old mt_upsert_* / mt_insert_* / mt_update_* /
        // mt_overwrite_* Postgres functions are gone (#4404) but
        // SystemFunctions is broader than just that family.
        yield return typeof(SystemFunctions);
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
