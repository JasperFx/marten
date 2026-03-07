using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;

namespace MultiTenancyTests;

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class using_bucketed_database_sharding_and_document_partitioning: IAsyncLifetime
{
    private const int NumberOfPartitions = 4;
    private IHost _host = null!;
    private IDocumentStore _store = null!;
    private string[] _dbNames = null!;
    private Dictionary<string, string> _connectionStrings = null!;
    private BucketRegistry _registry = null!;

    private static readonly string TenantAlpha = "tenant_alpha";
    private static readonly string TenantBeta = "tenant_beta";
    private static readonly string TenantGamma = "tenant_gamma";

    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;
        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        _dbNames = Enumerable.Range(1, 6).Select(i => $"marten_shard_{i:00}").ToArray();
        _connectionStrings = new(StringComparer.OrdinalIgnoreCase);

        foreach (var name in _dbNames)
        {
            _connectionStrings[name] = await CreateDatabaseIfNotExists(conn, name);
        }

        _registry = BucketRegistry.EvenlySpreadOver(_dbNames);

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        // Custom tenancy that spreads tenants across the shard databases based on a hash of the tenant id
                        opts.Tenancy = new BucketedStaticTenancy(opts, _registry, _connectionStrings);

                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                        opts.Events.AddEventType<DummyEvent>();

                        opts.Schema.For<Target>()
                            .MultiTenantedWithPartitioning(x =>
                            {
                                x.ByHash(Enumerable.Range(0, NumberOfPartitions)
                                    .Select(i => $"h{i:000}")
                                    .ToArray());
                            });
                    })
                    .ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        _store = _host.Services.GetRequiredService<IDocumentStore>();
    }

    public record DummyEvent;

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _store.Dispose();
    }

    [Fact]
    public async Task describes_the_configured_shard_databases()
    {
        _store.Options.Tenancy.Cardinality.ShouldBe(DatabaseCardinality.StaticMultiple);

        var description = await _store.Options.Tenancy.DescribeDatabasesAsync(CancellationToken.None);

        description.Cardinality.ShouldBe(DatabaseCardinality.StaticMultiple);
        description.MainDatabase.ShouldBeNull();

        description.Databases.Select(x => x.DatabaseName).OrderBy(x => x)
            .ShouldBe(_dbNames.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task creates_all_shard_databases()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        foreach (var name in _dbNames)
        {
            (await conn.DatabaseExists(name)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task applies_schema_changes_to_each_shard_database()
    {
        await using var store = _host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        var databases = await store.Tenancy.BuildDatabases();

        foreach (var db in databases)
        {
            var database = (IMartenDatabase)db;

            await using var conn = database.CreateConnection();
            await conn.OpenAsync();

            var tables = await conn.ExistingTablesAsync();

            tables.Any(x => x.QualifiedName == "public.mt_doc_user").ShouldBeTrue();
            for (var i = 1; i < NumberOfPartitions; i++)
            {
                tables.Any(x => x.QualifiedName == $"public.mt_doc_target_h00{i}").ShouldBeTrue();
            }

            tables.Any(x => x.QualifiedName == "public.mt_events").ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData("tenant_alpha")]
    [InlineData("tenant_beta")]
    [InlineData("tenant_gamma")]
    public async Task routes_each_tenant_to_the_expected_database(string tenantId)
    {
        var expectedDatabase = _registry.DatabaseForTenant(tenantId);

        await using var session = _store.LightweightSession(new SessionOptions { TenantId = tenantId });

        session.Connection.Database.ShouldBe(expectedDatabase);
    }

    [Fact]
    public async Task can_bulk_insert_and_query_per_tenant()
    {
        await _store.Advanced.Clean.DeleteAllDocumentsAsync();

        var alphaTargets = Target.GenerateRandomData(40).ToArray();
        var gammaTargets = Target.GenerateRandomData(25).ToArray();

        await _store.BulkInsertDocumentsAsync(TenantAlpha, alphaTargets);
        await _store.BulkInsertDocumentsAsync(TenantGamma, gammaTargets);

        await using (var queryAlpha = _store.QuerySession(TenantAlpha))
        {
            var count = await queryAlpha.Query<Target>().CountAsync();
            count.ShouldBe(alphaTargets.Length);
        }

        await using (var queryGamma = _store.QuerySession(TenantGamma))
        {
            var count = await queryGamma.Query<Target>().CountAsync();
            count.ShouldBe(gammaTargets.Length);
        }
    }

    [Fact]
    public async Task clean_deletes_documents_across_all_shard_databases()
    {
        var alphaTargets = Target.GenerateRandomData(10).ToArray();
        var betaTargets = Target.GenerateRandomData(10).ToArray();

        await _store.BulkInsertDocumentsAsync(TenantAlpha, alphaTargets);
        await _store.BulkInsertDocumentsAsync(TenantBeta, betaTargets);

        await _store.Advanced.Clean.DeleteAllDocumentsAsync();

        await using (var q1 = _store.QuerySession(TenantAlpha))
        {
            (await q1.Query<Target>().AnyAsync()).ShouldBeFalse();
        }

        await using (var q2 = _store.QuerySession(TenantBeta))
        {
            (await q2.Query<Target>().AnyAsync()).ShouldBeFalse();
        }
    }

    [Fact]
    public void tenant_ids_are_spread_over_multiple_databases()
    {
        var dbs = new[]
            {
                _registry.DatabaseForTenant("tenant_alpha"), _registry.DatabaseForTenant("tenant_beta"),
                _registry.DatabaseForTenant("tenant_gamma")
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        dbs.Length.ShouldBeGreaterThan(1);
    }

    private string FindTenantIdForDatabase(string databaseName)
    {
        for (var i = 0; i < 100_000; i++)
        {
            var tenantId = $"tenant_{i:000000}";
            if (_registry.DatabaseForTenant(tenantId).Equals(databaseName, StringComparison.OrdinalIgnoreCase))
                return tenantId;
        }

        throw new($"Could not find a tenant id for {databaseName}");
    }

    [Fact]
    public async Task writes_go_to_multiple_shards()
    {
        var tenant1 = FindTenantIdForDatabase(_dbNames[0]);
        var tenant2 = FindTenantIdForDatabase(_dbNames[1]);
        var tenant3 = FindTenantIdForDatabase(_dbNames[2]);

        await _store.Advanced.Clean.DeleteAllDocumentsAsync();

        await _store.BulkInsertDocumentsAsync(tenant1, Target.GenerateRandomData(5).ToArray());
        await _store.BulkInsertDocumentsAsync(tenant2, Target.GenerateRandomData(5).ToArray());
        await _store.BulkInsertDocumentsAsync(tenant3, Target.GenerateRandomData(5).ToArray());

        await using var s1 = _store.QuerySession(tenant1);
        await using var s2 = _store.QuerySession(tenant2);
        await using var s3 = _store.QuerySession(tenant3);

        s1.Connection.Database.ShouldBe(_registry.DatabaseForTenant(tenant1));
        s2.Connection.Database.ShouldBe(_registry.DatabaseForTenant(tenant2));
        s3.Connection.Database.ShouldBe(_registry.DatabaseForTenant(tenant3));

        s1.Connection.Database.ShouldNotBe(s2.Connection.Database);
        s2.Connection.Database.ShouldNotBe(s3.Connection.Database);
    }
}

public sealed class BucketedStaticTenancy: ITenancy
{
    private readonly StoreOptions _options;
    private readonly BucketRegistry _registry;

    private readonly Dictionary<string, IMartenDatabase> _databasesByName;
    private readonly ConcurrentDictionary<string, Tenant> _tenants = new();

    public BucketedStaticTenancy(
        StoreOptions options,
        BucketRegistry registry,
        IReadOnlyDictionary<string, string> databaseNameToConnectionString
    )
    {
        _options = options;
        _registry = registry;

        Cleaner = new CompositeDocumentCleaner(this, options);

        _databasesByName = databaseNameToConnectionString.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                var dataSource = new NpgsqlDataSourceBuilder(kvp.Value).Build();
                return (IMartenDatabase)new MartenDatabase(_options, dataSource, kvp.Key);
            },
            StringComparer.OrdinalIgnoreCase
        );
    }

    public DatabaseCardinality Cardinality => DatabaseCardinality.StaticMultiple;

    public Tenant Default => null!;

    public IDocumentCleaner Cleaner { get; }

    public Tenant GetTenant(string tenantId)
        => _tenants.GetOrAdd(_options.TenantIdStyle.MaybeCorrectTenantId(tenantId), BuildTenant);

    public ValueTask<Tenant> GetTenantAsync(string tenantId)
        => new(GetTenant(tenantId));

    public ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        var tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier);
        var dbName = _registry.DatabaseForTenant(tenantId);
        return new(_databasesByName[dbName]);
    }

    public ValueTask<IMartenDatabase> FindDatabase(DatabaseId id)
    {
        var db = _databasesByName.Values.FirstOrDefault(x => x.Id == id);
        if (db is null)
            throw new ArgumentOutOfRangeException(nameof(id), $"Database not found: {id.Identity}");

        return new(db);
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        var expected = _registry.DatabaseForTenant(tenantId);
        return database.Id.Name.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
        => ValueTask.FromResult<IReadOnlyList<IDatabase>>([.. _databasesByName.Values]);

    public ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        var usage = new DatabaseUsage
        {
            Cardinality = Cardinality,
            MainDatabase = null,
            Databases = _databasesByName.Values
                .Select(db => new DatabaseDescriptor { DatabaseName = db.Id.Name })
                .OrderBy(x => x.DatabaseName)
                .ToList()
        };

        return new(usage);
    }

    private Tenant BuildTenant(string tenantId)
    {
        var dbName = _registry.DatabaseForTenant(tenantId);
        var database = _databasesByName[dbName];
        return new(tenantId, database);
    }

    public void Dispose()
    {
        foreach (var db in _databasesByName.Values)
            db.Dispose();
    }
}

public sealed class BucketRegistry
{
    private readonly string[] _bucketToDatabaseName;

    public BucketRegistry(string[] bucketToDatabaseName)
    {
        if (bucketToDatabaseName.Length != 128)
            throw new ArgumentException("bucketToDatabaseName must have length 128");

        _bucketToDatabaseName = bucketToDatabaseName;
    }

    public string DatabaseForTenant(string tenantId)
    {
        var bucket = TenantHashing.DbBucket128(tenantId);
        return _bucketToDatabaseName[bucket];
    }

    public static BucketRegistry EvenlySpreadOver(string[] databaseNames)
    {
        var map = new string[128];

        for (var bucket = 0; bucket < 128; bucket++)
        {
            var idx = (bucket * databaseNames.Length) / 128;
            map[bucket] = databaseNames[idx];
        }

        return new(map);
    }
}

public static class TenantHashing
{
    public static ulong Hash64(string tenantId)
    {
        var bytes = Encoding.UTF8.GetBytes(tenantId);
        return XxHash64.HashToUInt64(bytes);
    }

    public static int DbBucket128(string tenantId)
        => (int)(Hash64(tenantId) % 128);

    public static int Partition32(string tenantId)
        => (int)(Hash64(tenantId) % 32);
}
