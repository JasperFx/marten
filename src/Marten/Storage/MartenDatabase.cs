using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Storage;

public partial class MartenDatabase: PostgresqlDatabase, IMartenDatabase
{
    private readonly StorageFeatures _features;

    private Lazy<SequenceFactory> _sequences;

    public MartenDatabase(
        StoreOptions options,
        NpgsqlDataSource npgsqlDataSource,
        string identifier
    ): base(options, options.AutoCreateSchemaObjects, options.Advanced.Migrator, identifier, npgsqlDataSource)
    {
        _features = options.Storage;
        Options = options;

        resetSequences();

        Providers = options.Providers;

        Tracker = new ShardStateTracker(options.LogFactory?.CreateLogger<MartenDatabase>() ?? options.DotNetLogger ??
            NullLogger<MartenDatabase>.Instance);
    }

    public StoreOptions Options { get; }

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
        return Options.Storage.AllActiveFeatures(this).ToArray();
    }

    public override void ResetSchemaExistenceChecks()
    {
        base.ResetSchemaExistenceChecks();
        resetSequences();
    }

    public override IFeatureSchema FindFeature(Type featureType)
    {
        return _features.FindFeature(featureType);
    }

    private void resetSequences()
    {
        _sequences = new Lazy<SequenceFactory>(() =>
        {
            var sequences = new SequenceFactory(Options, this);

            generateOrUpdateFeature(typeof(SequenceFactory), sequences, default).AsTask().GetAwaiter().GetResult();

            return sequences;
        });
    }

    public void Dispose()
    {
        DataSource?.Dispose();
        ((IDisposable)Tracker)?.Dispose();
    }

    string IProjectionStorage.StorageIdentifier => Identifier;
}
