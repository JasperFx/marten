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

        Upsert = new UpsertFunction(_mapping);
        Insert = new InsertFunction(_mapping);
        Update = new UpdateFunction(_mapping);

        if (_mapping.UseOptimisticConcurrency || _mapping.UseNumericRevisions)
        {
            Overwrite = new OverwriteFunction(_mapping);
        }
    }

    public OverwriteFunction Overwrite { get; }

    public UpdateFunction Update { get; }

    public InsertFunction Insert { get; }

    public UpsertFunction Upsert { get; }

    public DocumentTable Table { get; }

    public IEnumerable<Type> DependentTypes()
    {
        yield return typeof(SystemFunctions);
        yield return typeof(RequiredExtensions);
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

        Upsert.WriteTemplate(rules, template, writer);
        Update.WriteTemplate(rules, template, writer);
        Insert.WriteTemplate(rules, template, writer);
        Overwrite?.WriteTemplate(rules, template, writer);
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

        if (Overwrite != null)
        {
            yield return Overwrite;
        }
    }
}
