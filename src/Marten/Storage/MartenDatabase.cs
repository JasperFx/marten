using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Internal;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Storage;

public partial class MartenDatabase: PostgresqlDatabase, IMartenDatabase, IProjectionDatabase, IAsyncDisposable
{
    private readonly StorageFeatures _features;

    private Lazy<SequenceFactory> _sequences;

    public MartenDatabase(
        StoreOptions options,
        NpgsqlDataSource npgsqlDataSource,
        string identifier
        // #4874: flow the data-source ownership into the Weasel base (weasel#345/#346) so the inherited
        // async PostgresqlDatabase.DisposeAsync() also skips disposing a caller-owned NpgsqlDataSource —
        // the sync MartenDatabase.Dispose() below was already guarded by #4903, but the async path was not.
    ): base(options, options.AutoCreateSchemaObjects, options.Advanced.Migrator, identifier, npgsqlDataSource,
        options.OwnsPrimaryDataSource)
    {
        _features = options.Storage;
        Options = options;

        // #4863: EVERY database of the store — not just DefaultTenancy's single database —
        // must hydrate the Marten-managed tenant partition state from its OWN
        // mt_tenant_partitions registry before migrations run against it. Without this,
        // a table created lazily on a shard by a fresh store instance was created
        // partitioned with zero partitions and every write failed with 23514.
        // (Options.TenantPartitions is always set before any MartenDatabase exists:
        // at config time for sharded tenancy, in StoreOptions.Validate() otherwise.)
        if (options.TenantPartitions != null)
        {
            AddInitializer(new TenantPartitionsDatabaseInitializer(this, options.TenantPartitions.Partitions));
        }

        resetSequences();

        Providers = options.Providers;

        Tracker = new ShardStateTracker(options.LogFactory?.CreateLogger<MartenDatabase>() ?? options.DotNetLogger ??
            NullLogger<MartenDatabase>.Instance);

        // Subscribe before any downstream observer (daemon, CritterWatch) so the
        // Skipped state's SkippedEventsCount is populated in-place before they see it.
        Tracker.Subscribe(new SkippedEventsCountObserver());
    }

    public StoreOptions Options { get; }

    // #4500-family dedupe: IProjectionDatabase contract for the lifted JasperFx.Events
    // projection distributors. Identifier comes from the Weasel DatabaseBase; the URI is
    // the canonical descriptor URI used for telemetry — never hit on the daemon hot path.
    Uri IProjectionDatabase.DatabaseUri => Describe().DatabaseUri();

    public ISequences Sequences => _sequences.Value;

    public IProviderGraph Providers { get; }

    /// <summary>
    ///     Set the minimum sequence number for a Hilo sequence for a specific document type
    ///     to the specified floor. Useful for migrating data between databases
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="floor"></param>
    public Task ResetHiloSequenceFloor<T>(long floor)
    {
        var sequence = Sequences.SequenceFor(typeof(T));
        return sequence.SetFloor(floor);
    }

    public async Task<IReadOnlyList<DbObjectName>> DocumentTables()
    {
        var tables = await SchemaTables().ConfigureAwait(false);
        return tables.Where(x => x.Name.StartsWith(SchemaConstants.TablePrefix)).ToList();
    }

    public async Task<IReadOnlyList<DbObjectName>> Functions()
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        var schemaNames = AllSchemaNames();

        return await conn.ExistingFunctionsAsync("mt_%", schemaNames).ConfigureAwait(false);
    }

    public async Task<Table> ExistingTableFor(Type type)
    {
        var mapping = _features.MappingFor(type).As<DocumentMapping>();
        var expected = mapping.Schema.Table;

        await using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        return await expected.FetchExistingAsync(conn).ConfigureAwait(false);
    }


    public override IFeatureSchema[] BuildFeatureSchemas()
    {
        markTenantPartitionScope();
        return Options.Storage.AllActiveFeatures(this).ToArray();
    }

    /// <summary>
    /// #4863/#4855: stamp the ambient "database being migrated" scope for
    /// <see cref="Marten.Schema.DatabaseScopedTenantPartitions"/>. Weasel's DatabaseBase calls
    /// <c>BuildFeatureSchemas()</c> / <c>FindFeature()</c> synchronously at the head of every
    /// migration operation on this database, and AsyncLocal writes from a synchronous callee flow
    /// onward through the rest of that operation — so the partition manager's expected set can be
    /// resolved per database even from Weasel internals that have no database parameter.
    /// </summary>
    private void markTenantPartitionScope()
    {
        if (Options.TenantPartitions != null && !ReferenceEquals(TenantPartitionDatabaseScope.Current, this))
        {
            TenantPartitionDatabaseScope.Current = this;
        }
    }

    public void Dispose()
    {
        // #4874: never dispose a data source the caller owns (supplied via Connection(NpgsqlDataSource)
        // / UseNpgsqlDataSource). Disposing it aborts every connection rented from it — fatal when the
        // caller shares one across stores or a running daemon still holds connections. OwnsDataSource is
        // the Weasel base flag we set from Options.OwnsPrimaryDataSource in the constructor, so the sync
        // and async (base DisposeAsync) teardown paths read the exact same ownership signal.
        if (OwnsDataSource)
        {
            DataSource?.Dispose();
        }

        ((IDisposable)Tracker)?.Dispose();
    }

    // #4874: DocumentStore.DisposeAsync() disposes the tenancy through JasperFx's MaybeDisposeAllAsync,
    // which prefers IAsyncDisposable. Before this, MartenDatabase only exposed the inherited
    // PostgresqlDatabase.DisposeAsync() (which disposes the data source but not Marten's tracker), and no
    // tenancy implemented IAsyncDisposable — so async store teardown always fell back to the synchronous
    // Dispose() chain and blocked on NpgsqlDataSource.Dispose(). Re-implement IAsyncDisposable here so the
    // async path releases the tracker AND runs the base's OwnsDataSource-aware async data-source disposal
    // (weasel#346), matching Dispose() above.
    public new async ValueTask DisposeAsync()
    {
        ((IDisposable)Tracker)?.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    public override DatabaseDescriptor Describe()
    {
        var descriptor = base.Describe();
        descriptor.SubjectUri = MartenSystemPart.MartenStoreUri;
        if (descriptor.SchemaOrNamespace.IsEmpty())
        {
            descriptor.SchemaOrNamespace = Options?.DatabaseSchemaName ?? "public";
        }

        foreach (var tenantId in TenantIds)
        {
            descriptor.TenantIds.Fill(tenantId);
        }

        return descriptor;
    }

    public override void ResetSchemaExistenceChecks()
    {
        base.ResetSchemaExistenceChecks();
        resetSequences();
    }

    public override IFeatureSchema FindFeature(Type featureType)
    {
        markTenantPartitionScope();
        return _features.FindFeature(featureType);
    }

    private void resetSequences()
    {
        _sequences = new Lazy<SequenceFactory>(() =>
        {
            var sequences = new SequenceFactory(Options, this);

            generateOrUpdateFeature(typeof(SequenceFactory), sequences, default, true).AsTask().GetAwaiter()
                .GetResult();

            return sequences;
        });
    }
}
