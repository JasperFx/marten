#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.Events;
using Marten.Schema;
using Weasel.Core.Migrations;

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
        };

        // Code-generation snapshot — wrapped as a child descriptor (rather than
        // flattened) so the four obsolete-on-StoreOptions properties stay
        // cohesive even after they ride out the deprecation window.
#pragma warning disable CS0618 // intentional: building a diagnostic snapshot of obsolete settings
        usage.CodeGeneration = new CodeGenerationDescriptor
        {
            ApplicationAssembly = Options.ApplicationAssembly?.GetName().FullName,
            SourceCodeWritingEnabled = Options.SourceCodeWritingEnabled,
            GeneratedCodeOutputPath = Options.GeneratedCodeOutputPath,
            GeneratedCodeMode = Options.GeneratedCodeMode.ToString(),
        };
#pragma warning restore CS0618

        // Per-document-type mappings — Documents collection. Skip mappings that
        // don't emit schema (structural-typed, internal-only) so the snapshot
        // matches what an operator would see in the database.
        foreach (var mapping in Options.Storage.DocumentMappingsWithSchema.OrderBy(x => x.Alias))
        {
            usage.Documents.Add(BuildMappingDescriptor(mapping));
        }

        // Flat OptionValues lifted up onto Properties bag — populated via the
        // OptionsDescription auto-property reader through the base ctor, but
        // we override several to coerce non-default shapes (enums-as-strings,
        // Configured/Default masking, etc.).
        ApplyFlatOptionValues(usage);

        return usage;
    }

    private DocumentMappingDescriptor BuildMappingDescriptor(DocumentMapping mapping)
    {
        var ddl = WriteSchemaCreationDdl(mapping);

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
            PartitioningStrategy = mapping.Partitioning?.GetType().Name,
            Ddl = ddl,
        };
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
