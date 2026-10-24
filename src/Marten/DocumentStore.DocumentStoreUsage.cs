#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten.Schema;
using Marten.Storage;
using Weasel.Core.Migrations;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten;

public partial class DocumentStore : IDocumentStoreUsageSource
{
    /// <summary>
    /// Build a <see cref="DocumentStoreUsage"/> snapshot of this store for
    /// monitoring tools (CritterWatch). Mirrors the structure of
    /// <c>IEventStore.TryCreateUsage</c> on the document side: hand-built
    /// first-class properties for the operationally-interesting bits, flat
    /// OptionValues for the secondary settings, and a per-document-type
    /// <see cref="DocumentMappingDescriptor"/> for each mapping that emits
    /// schema (skips structural-typed and skip-generation mappings).
    /// </summary>
    async Task<DocumentStoreUsage?> IDocumentStoreUsageSource.TryCreateUsage(CancellationToken token)
    {
        var usage = new DocumentStoreUsage(Subject, this)
        {
            Database = await Options.Tenancy.DescribeDatabasesAsync(token).ConfigureAwait(false),
            StoreName = Options.StoreName,
            DatabaseSchemaName = Options.DatabaseSchemaName,
            AutoCreateSchemaObjects = Options.AutoCreateSchemaObjects.ToString(),
            EnumStorage = Options.EnumStorage.ToString(),

            // #5543 / jasperfx#870 §5. A console building a member path into the raw JSON, or explaining why
            // a filter on `Status` matched nothing in a document stored as `status`, cannot work it out from
            // the JSON alone -- absence of a member and a differently-cased member look the same. The three
            // values Casing spells (Default, CamelCase, SnakeCase) are exactly the three the contract names.
            SerializerCasing = Serializer.Casing.ToString(),
        };

        // Per-document-type mappings — Documents collection. Skip mappings that
        // don't emit schema (structural-typed, internal-only) so the snapshot
        // matches what an operator would see in the database.
        //
        // 9.0: BuildAllMappings forces materialization of every registered type
        // builder into a concrete DocumentMapping. After #4303 made mapping
        // materialization lazy (built on first MappingFor(type) call rather
        // than eagerly during ApplyConfiguration), DocumentMappingsWithSchema
        // would otherwise enumerate an empty set when called pre-session.
        // The descriptor snapshot is exactly that "pre-session" caller.
        Options.Storage.BuildAllMappings();

        foreach (var mapping in Options.Storage.DocumentMappingsWithSchema.OrderBy(x => x.Alias))
        {
            usage.Documents.Add(BuildMappingDescriptor(mapping));
        }

        // Flat OptionValues lifted up onto Properties bag — populated via the
        // OptionsDescription auto-property reader through the base ctor, but
        // we override several to coerce non-default shapes (enums-as-strings,
        // Configured/Default masking, etc.).
        ApplyFlatOptionValues(usage);

        // jasperfx#475 — advertise which document metadata Marten captures so
        // store-aware consumers (CritterWatch) gate document-query facets by what is
        // actually persisted. Version / last-modified / tenant / soft-delete are
        // universal facets in Marten and keep the descriptor's default of true; the
        // opt-in columns (correlation/causation/last-modified-by) are only queryable
        // where some document mapping has enabled them.
        var mappings = Options.Storage.DocumentMappingsWithSchema.ToList();
        usage.DocumentMetadata = new DocumentMetadataCapabilities
        {
            StoreType = "Marten",
            CorrelationId = mappings.Any(m => m.Metadata.CorrelationId.Enabled),
            CausationId = mappings.Any(m => m.Metadata.CausationId.Enabled),
            LastModifiedBy = mappings.Any(m => m.Metadata.LastModifiedBy.Enabled)
        };

        return usage;
    }

    private DocumentMappingDescriptor BuildMappingDescriptor(DocumentMapping mapping)
    {
        var ddl = WriteSchemaCreationDdl(mapping);
        var indexesReported = TryBuildIndexes(mapping, out var indexes);

        return new DocumentMappingDescriptor
        {
            DocumentType = TypeDescriptor.For(mapping.DocumentType),
            DatabaseSchemaName = mapping.DatabaseSchemaName,
            Alias = mapping.Alias,
            IdStrategy = mapping.IdStrategy?.GetType().Name ?? "None",
            TenancyStyle = mapping.TenancyStyle.ToString(),
            DeleteStyle = mapping.DeleteStyle.ToString(),
            UseOptimisticConcurrency = mapping.UseOptimisticConcurrency,
            UseNumericRevisions = mapping.UseNumericRevisions,
            SubClassCount = mapping.SubClasses.Count(),
            SubClasses = mapping.SubClasses.Select(x => TypeDescriptor.For(x.DocumentType)).ToArray(),
            PartitioningStrategy = mapping.Partitioning?.GetType().Name,
            Partitioning = BuildPartitioning(mapping.Partitioning),
            Ddl = ddl,
            DuplicatedFields = BuildDuplicatedFields(mapping),

            // jasperfx#1081: mapping.DuplicatedFields is the whole set by construction -- it is what the
            // table's columns are built from -- so an empty list here means "nothing is duplicated".
            DuplicatedFieldsReported = true,
            Indexes = indexes,
            IndexesReported = indexesReported,
            Metadata = BuildMetadataCapabilities(mapping),
        };
    }

    /// <summary>
    /// jasperfx#1077: which metadata columns THIS type's table has, so a console offers only the metadata
    /// filters <c>QueryDocumentsAsync</c> will accept for it. The store-wide
    /// <see cref="DocumentStoreUsage.DocumentMetadata" /> answers "does any type capture it"; a filter on a type
    /// that does not is refused.
    /// </summary>
    /// <remarks>
    /// Read off the same mapping metadata <c>DocumentTable</c> builds its columns from. <c>Version</c> is either
    /// metadata column that lands in <c>mt_version</c> (a Guid version or a numeric revision), and
    /// <c>TenantId</c> is the <c>tenant_id</c> column, which only a conjoined type has. A sub-class shares its
    /// root's table, so it is described by the root's descriptor.
    /// </remarks>
    internal static DocumentMetadataCapabilities BuildMetadataCapabilities(DocumentMapping mapping)
    {
        var metadata = mapping.Metadata;
        return new DocumentMetadataCapabilities
        {
            StoreType = "Marten",
            Version = metadata.Version.Enabled || metadata.Revision.Enabled,
            LastModified = metadata.LastModified.Enabled,
            TenantId = mapping.TenancyStyle == TenancyStyle.Conjoined,
            SoftDelete = mapping.DeleteStyle == DeleteStyle.SoftDelete,
            CorrelationId = metadata.CorrelationId.Enabled,
            CausationId = metadata.CausationId.Enabled,
            LastModifiedBy = metadata.LastModifiedBy.Enabled
        };
    }

    /// <summary>
    /// #5543 / jasperfx#870 §5: the duplicated fields in structured form, so a console can say that a filter
    /// on this member reads a real column rather than the JSON body.
    /// </summary>
    /// <remarks>
    /// <c>OnlyForSearching</c> fields are included deliberately, unlike in <see cref="DocumentTable"/>: they
    /// have no column of their own there, but they ARE the members Marten can index and search efficiently,
    /// which is the question this list exists to answer.
    /// </remarks>
    private static List<DuplicatedFieldDescriptor> BuildDuplicatedFields(DocumentMapping mapping)
        => mapping.DuplicatedFields
            .Select(x => new DuplicatedFieldDescriptor
            {
                MemberPath = string.Join(".", x.Members.Select(m => m.Name)),
                ColumnName = x.ColumnName,
                DbType = x.PgType
            })
            .ToList();

    /// <summary>
    /// #5543 / jasperfx#870 §5: the table's indexes in structured form, so a console can tell whether a
    /// filter on a member can use one without parsing <c>Ddl</c>, which stays the canonical view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the table model (<c>DocumentTable</c>), not from <c>mapping.Indexes</c> alone: the table adds
    /// indexes of its own -- <c>mt_doc_type</c> for a hierarchy, <c>mt_deleted</c> for a soft-deleted type, and
    /// <c>tenant_id</c> under <c>PrimaryKeyTenancyOrdering.Id_Then_TenantId</c> -- and a list without them is not
    /// the whole set jasperfx#1081's <see cref="DocumentMappingDescriptor.IndexesReported" /> promises.
    /// </para>
    /// <para>
    /// <c>Members</c> is filled where Marten knows the member an index was declared over: a duplicated
    /// field's own column, and a computed index (<c>Index(x =&gt; x.ShipTo.City)</c>), which keeps its member
    /// chains (marten#5768 / CritterWatch#1469 -- before that a computed index reported no members, so a console
    /// read a filter it covers as a scan). Paths are dotted, in declaration casing. Indexes over metadata
    /// columns or raw expressions name no member, and are not given one.
    /// </para>
    /// <para>
    /// Answers false -- not reported -- when the model cannot be built, or when the mapping names indexes to
    /// ignore (<c>IgnoredIndexes</c>): those live on the table but outside Marten's model, so the list would be
    /// partial.
    /// </para>
    /// </remarks>
    internal static bool TryBuildIndexes(DocumentMapping mapping, out List<DocumentIndexDescriptor> indexes)
    {
        try
        {
            indexes = BuildIndexes(mapping);
        }
        catch (Exception)
        {
            // Same stance as WriteSchemaCreationDdl: one mapping's model failing must not poison the snapshot,
            // and an empty list must not then read as "no indexes".
            indexes = new List<DocumentIndexDescriptor>();
            return false;
        }

        return mapping.IgnoredIndexes.Count == 0;
    }

    private static List<DocumentIndexDescriptor> BuildIndexes(DocumentMapping mapping)
    {
        var membersByColumn = mapping.DuplicatedFields
            .GroupBy(x => x.ColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                g => string.Join(".", g.First().Members.Select(m => m.Name)),
                StringComparer.OrdinalIgnoreCase);

        return mapping.Schema.Table.Indexes
            .OfType<IndexDefinition>()
            .Select(index =>
            {
                var columns = index.Columns ?? Array.Empty<string>();

                var members = index is ComputedIndex computed
                    ? computed.MemberPaths.ToArray()
                    : columns
                        .Select(c => membersByColumn.TryGetValue(c, out var member) ? member : null)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToArray();

                return new DocumentIndexDescriptor
                {
                    Name = index.Name,
                    Columns = columns,
                    Members = members,
                    IsUnique = index.IsUnique,
                    Method = index.Method.ToString().ToLowerInvariant(),
                    Predicate = index.Predicate
                };
            })
            .ToList();
    }

    private static PartitioningDescriptor? BuildPartitioning(IPartitionStrategy? partitioning)
    {
        if (partitioning == null)
        {
            return null;
        }

        // "ListPartitioning" -> "List", "HashPartitioning" -> "Hash", etc.
        var strategy = partitioning.GetType().Name.Replace("Partitioning", "");

        var names = partitioning switch
        {
            ListPartitioning list => list.Partitions.Select(x => x.Suffix).ToArray(),
            RangePartitioning range => range.Ranges.Select(x => x.Suffix).ToArray(),
            HashPartitioning hash => hash.Suffixes,
            _ => Array.Empty<string>()
        };

        return new PartitioningDescriptor { Strategy = strategy, PartitionNames = names };
    }

    private string WriteSchemaCreationDdl(DocumentMapping mapping)
    {
        try
        {
            using var writer = new StringWriter();
            mapping.Schema.WriteFeatureCreation(Options.Advanced.Migrator, writer);
            return writer.ToString();
        }
        catch (Exception ex)
        {
            // Don't let a schema-generation hiccup poison the whole snapshot —
            // worst case the operator sees an explanatory error string instead
            // of DDL on this one mapping.
            return $"-- Failed to generate DDL: {ex.Message}";
        }
    }

    private void ApplyFlatOptionValues(DocumentStoreUsage usage)
    {
        // Cluster A: TenantIdStyle, DefaultTenantUsageEnabled, RlsTenantSessionSetting
        usage.AddValue(nameof(Options.TenantIdStyle), Options.TenantIdStyle.ToString());
        usage.AddValue(nameof(Options.Advanced.DefaultTenantUsageEnabled), Options.Advanced.DefaultTenantUsageEnabled);
        usage.AddValue(
            "RlsTenantSessionSetting",
            Options.RlsTenantSessionSetting != null ? "Configured" : "Default");

        // Cluster B: NameDataLength, ApplyChangesLockId
        usage.AddValue(nameof(Options.NameDataLength), Options.NameDataLength);
        usage.AddValue(nameof(Options.ApplyChangesLockId), Options.ApplyChangesLockId);

        // Cluster C: CommandTimeout, UpdateBatchSize, UseStickyConnectionLifetimes
        usage.AddValue(nameof(Options.CommandTimeout), Options.CommandTimeout);
        usage.AddValue(nameof(Options.UpdateBatchSize), Options.UpdateBatchSize);
        usage.AddValue(nameof(Options.UseStickyConnectionLifetimes), Options.UseStickyConnectionLifetimes);

        // Cluster D: DuplicatedFieldEnumStorage (lifted from Advanced)
        usage.AddValue("DuplicatedFieldEnumStorage", Options.Advanced.DuplicatedFieldEnumStorage.ToString());

        // Cluster F: OpenTelemetryTrackConnections (flattened from OpenTelemetry child),
        // DisableNpgsqlLogging
        usage.AddValue("OpenTelemetryTrackConnections", Options.OpenTelemetry.TrackConnections.ToString());
        usage.AddValue(nameof(Options.DisableNpgsqlLogging), Options.DisableNpgsqlLogging);

        // Cluster H6: HiloMaxLo / HiloMaxAdvanceToNextHiAttempts (lifted from
        // HiloSequenceDefaults). MaxLo is the canonical chunk-size knob;
        // MaxAdvanceToNextHiAttempts bounds retry behaviour during sequence
        // contention. SequenceName is omitted (it's a per-document override
        // that lives on individual mappings, not the store-wide default).
        usage.AddValue("HiloMaxLo", Options.Advanced.HiloSequenceDefaults.MaxLo);
        usage.AddValue("HiloMaxAdvanceToNextHiAttempts", Options.Advanced.HiloSequenceDefaults.MaxAdvanceToNextHiAttempts);

        // Cluster H7: ReadSessionPreference / WriteSessionPreference
        // (lifted from MultiHostSettings)
        usage.AddValue(
            "ReadSessionPreference",
            Options.Advanced.MultiHostSettings.ReadSessionPreference.ToString());
        usage.AddValue(
            "WriteSessionPreference",
            Options.Advanced.MultiHostSettings.WriteSessionPreference.ToString());
    }
}
