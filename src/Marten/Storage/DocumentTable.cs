using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

internal class DocumentTable: Table
{
    private readonly DocumentMapping _mapping;

    public DocumentTable(DocumentMapping mapping) : base(mapping.TableName)
    {
        // validate to ensure document has an Identity field or property
        mapping.CompileAndValidate();

        _mapping = mapping;

        foreach (var index in mapping.IgnoredIndexes)
            IgnoredIndexes.Add(index);

        var idColumn = new IdColumn(mapping);

        // Per https://github.com/JasperFx/marten/issues/2430 the tenant_id needs to be first in
        // PK
        if (mapping.TenancyStyle == TenancyStyle.Conjoined && mapping.PrimaryKeyTenancyOrdering == PrimaryKeyTenancyOrdering.TenantId_Then_Id)
        {
            AddColumn(mapping.Metadata.TenantId).AsPrimaryKey();
        }

        AddColumn(idColumn).AsPrimaryKey();

        if (mapping.TenancyStyle == TenancyStyle.Conjoined && mapping.PrimaryKeyTenancyOrdering == PrimaryKeyTenancyOrdering.Id_Then_TenantId)
        {
            AddColumn(mapping.Metadata.TenantId).AsPrimaryKey();

            Indexes.Add(new DocumentIndex(mapping, TenantIdColumn.Name));
        }

        AddColumn<DataColumn>();

        AddIfActive(_mapping.Metadata.LastModified);
        AddIfActive(_mapping.Metadata.Version);
        AddIfActive(_mapping.Metadata.DotNetType);
        AddIfActive(_mapping.Metadata.CreatedAt);

        AddIfActive(_mapping.Metadata.CorrelationId);
        AddIfActive(_mapping.Metadata.CausationId);
        AddIfActive(_mapping.Metadata.LastModifiedBy);
        AddIfActive(_mapping.Metadata.Headers);

        AddIfActive(_mapping.Metadata.Revision);

        foreach (var field in mapping.DuplicatedFields.Where(x => !x.OnlyForSearching))
        {
            AddColumn(new DuplicatedFieldColumn(field));
        }

        if (mapping.IsHierarchy())
        {
            Indexes.Add(new DocumentIndex(_mapping, SchemaConstants.DocumentTypeColumn));
            AddColumn(_mapping.Metadata.DocumentType);
        }

        if (mapping.DeleteStyle == DeleteStyle.SoftDelete)
        {
            AddColumn(_mapping.Metadata.IsSoftDeleted);

            Indexes.Add(new DocumentIndex(mapping, SchemaConstants.DeletedColumn));

            AddColumn(_mapping.Metadata.SoftDeletedAt);
        }

        Indexes.AddRange(mapping.Indexes);

        // tenant_id should always be first
        foreach (var foreignKey in mapping.ForeignKeys)
        {
            foreignKey.TryMoveTenantIdFirst(mapping);
        }

        ForeignKeys.AddRange(mapping.ForeignKeys);

        Partitioning = mapping.Partitioning;

        if (mapping.Partitioning != null && !mapping.IgnorePartitions)
        {
            if (mapping.Partitioning.Columns.All(HasColumn))
            {
                Partitioning = mapping.Partitioning;
            }
            else
            {
                Console.WriteLine($"Warning: Table {Identifier} is missing columns specified in the Partitioning scheme. This is probably an error in configuration");
            }
        }

        // Any column referred to in the partitioning has to be
        // part of the primary key
        if (Partitioning != null)
        {
            IgnorePartitionsInMigration = mapping.IgnorePartitions;
            foreach (var columnName in Partitioning.Columns)
            {
                var column = ModifyColumn(columnName);
                column.AsPrimaryKey();
            }
        }
    }

    public Type DocumentType => _mapping.DocumentType;

    public void AddIfActive(MetadataColumn column)
    {
        if (column.Enabled)
        {
            AddColumn(column);
        }
    }

    public string BuildTemplate(string template)
    {
        return template
            .Replace(Migrator.SCHEMA, Identifier.Schema)
            .Replace(Migrator.TABLENAME, Identifier.Name)
            .Replace(Migrator.COLUMNS, Columns.Select(x => x.Name).Join(", "))
            .Replace(Migrator.NON_ID_COLUMNS,
                Columns.Where(x => !x.Name.EqualsIgnoreCase("id")).Select(x => x.Name).Join(", "))
            .Replace(Migrator.METADATA_COLUMNS, Columns.OfType<MetadataColumn>().Select(x => x.Name).Join(", "));
    }

    public void WriteTemplate(SqlTemplate template, TextWriter writer)
    {
        var text = template?.TableCreation;
        if (text.IsNotEmpty())
        {
            writer.WriteLine();
            writer.WriteLine(BuildTemplate(text));
        }
    }

    protected bool Equals(DocumentTable other)
    {
        return base.Equals(other);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((DocumentTable)obj);
    }

    public override int GetHashCode()
    {
        return Identifier.QualifiedName.GetHashCode();
    }

    internal ISelectableColumn[] SelectColumns(StorageStyle style)
    {
        // There's some hokey stuff going here, but older code assumes that the
        // order of the selection is data, id, everything else
        var columns = Columns.OfType<ISelectableColumn>().Where(x => x.ShouldSelect(_mapping, style)).ToList();

        var id = columns.OfType<IdColumn>().SingleOrDefault();
        var data = columns.OfType<DataColumn>().Single();
        var type = columns.OfType<DocumentTypeColumn>().SingleOrDefault();
        var version = columns.OfType<VersionColumn>().SingleOrDefault();

        var answer = new List<ISelectableColumn>();

        if (id != null)
        {
            columns.Remove(id);
            answer.Add(id);
        }

        columns.Remove(data);
        answer.Add(data);

        if (type != null)
        {
            columns.Remove(type);

            // Old code might depend on this exact ordering
            answer.Add(type);
        }

        if (version != null)
        {
            columns.Remove(version);
            answer.Add(version);
        }

        answer.AddRange(columns);

        return answer.ToArray();
    }
}
