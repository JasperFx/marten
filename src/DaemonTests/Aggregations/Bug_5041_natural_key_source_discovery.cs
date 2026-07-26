using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Aggregations;

/// <summary>
/// #5041, from the repro in https://github.com/JasperFx/marten/pull/5042 (thanks @ytqsl).
///
/// Both of these hang on [NaturalKeySource] discovery in JasperFx.Events, not on anything Marten
/// owns — see https://github.com/JasperFx/jasperfx/issues/569:
///
///   * a handler whose first parameter is IEvent&lt;T&gt; yields no usable extractor, so
///     NaturalKeyDefinition.EventMappings never gains an entry for that event type and the
///     mt_natural_key_X table is silently never written for it;
///   * an instance Apply(TEvent) handler is invoked reflectively against a fabricated blank
///     aggregate (Expression.New(TDoc), which also bypasses `required` member enforcement), so a
///     handler body that touches any other state throws — out of NaturalKeyProjection.ApplyAsync
///     and out of the caller's SaveChangesAsync.
///
/// Unskip both when the JasperFx.Events dependency picks up the fix.
/// </summary>
public class Bug_5041_natural_key_source_discovery: DaemonContext
{
    private const string schemaName = "bug_5041_natural_key_discovery";

    public Bug_5041_natural_key_source_discovery(ITestOutputHelper output) : base(output)
    {
    }

    public sealed record ProductCode(string Value);

    public sealed record ProductRegistered(Guid ProductId, string ProductCode);

    public sealed record ProductCodeChangedByEventWrapper(Guid ProductId, string NewProductCode);

    public sealed record ProductCodeChangedByInstanceMethod(Guid ProductId, string NewProductCode);

    public sealed record Product
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public ProductCode Code { get; set; }

        public required IEnumerable<ProductCode> KnownCodes { get; set; }

        [NaturalKeySource]
        public static Product Create(ProductRegistered e)
        {
            return new Product
            {
                Id = e.ProductId,
                Code = new ProductCode(e.ProductCode),
                KnownCodes = [new ProductCode(e.ProductCode)]
            };
        }

        [NaturalKeySource]
        public static Product Apply(IEvent<ProductCodeChangedByEventWrapper> e, Product product)
        {
            return product with
            {
                Code = new ProductCode(e.Data.NewProductCode),
                KnownCodes = product.KnownCodes
                    .Where(c => c.Value != e.Data.NewProductCode)
                    .Append(new ProductCode(e.Data.NewProductCode))
            };
        }

        [NaturalKeySource]
        public void Apply(ProductCodeChangedByInstanceMethod e)
        {
            Code = new ProductCode(e.NewProductCode);
            KnownCodes = KnownCodes
                .Where(c => c.Value != e.NewProductCode)
                .Append(new ProductCode(e.NewProductCode));
        }
    }

    private static void ConfigureStore(StoreOptions opts)
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = schemaName;
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Projections.Snapshot<Product>(SnapshotLifecycle.Async);
    }

    [Fact(Skip = "Blocked on JasperFx/jasperfx#569 -- IEvent<T> [NaturalKeySource] handlers yield no extractor")]
    public async Task natural_key_is_maintained_when_the_handler_takes_IEvent()
    {
        await runRenameScenario(streamId => new ProductCodeChangedByEventWrapper(streamId, "PROD-999"));
    }

    [Fact(Skip = "Blocked on JasperFx/jasperfx#569 -- instance [NaturalKeySource] handlers run against a blank aggregate")]
    public async Task natural_key_is_maintained_when_the_handler_is_an_instance_method()
    {
        await runRenameScenario(streamId => new ProductCodeChangedByInstanceMethod(streamId, "PROD-999"));
    }

    private async Task runRenameScenario(Func<Guid, object> renameEvent)
    {
        StoreOptions(ConfigureStore);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Product>(streamId, new ProductRegistered(streamId, "PROD-001"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, renameEvent(streamId));
            await session.SaveChangesAsync();
        }

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        await using var query = theStore.LightweightSession();
        var product = await query.Events.FetchLatest<Product, ProductCode>(new ProductCode("PROD-999"));
        product.ShouldNotBeNull();
        product.Code.Value.ShouldBe("PROD-999");
        product.KnownCodes.ShouldContain(new ProductCode("PROD-001"));
        product.KnownCodes.ShouldContain(new ProductCode("PROD-999"));
    }
}
