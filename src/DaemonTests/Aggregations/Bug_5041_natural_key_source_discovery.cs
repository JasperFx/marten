using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Aggregations;

/// <summary>
/// #5041, from the repro in https://github.com/JasperFx/marten/pull/5042 (thanks @ytqsl), closed out
/// by #5052 on JasperFx.Events 2.36.0.
///
/// Both halves of the original report hung on [NaturalKeySource] discovery in JasperFx.Events rather
/// than on anything Marten owns — see https://github.com/JasperFx/jasperfx/issues/569:
///
///   * a handler whose first parameter is IEvent&lt;T&gt; yielded no usable extractor, so
///     NaturalKeyDefinition.EventMappings never gained an entry for that event type and the
///     mt_natural_key_X table was silently never written for it;
///   * an instance Apply(TEvent) handler was invoked reflectively against a fabricated blank
///     aggregate (Expression.New(TDoc), which also bypasses `required` member enforcement), so a
///     handler body that touched any other state threw — out of NaturalKeyProjection.ApplyAsync and
///     out of the caller's SaveChangesAsync.
///
/// jasperfx#571 widened the extraction contract from the event DATA to the whole IEvent, which is what
/// makes an IEvent&lt;T&gt; source bindable at all (see NaturalKeyProjection for Marten's two call
/// sites), and made an unbindable source a loud configuration-time error instead of a mapping that
/// silently never existed. The repro's own aggregate shape — an instance handler on a type with
/// `required` members — is one of those unbindable cases BY DESIGN now, so it is pinned as such here,
/// alongside the two supported ways to express the same rename.
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

    /// <summary>
    /// The reporter's aggregate, verbatim. Every [NaturalKeySource] here other than Create needs a
    /// prior aggregate to derive the key, and `required IEnumerable&lt;ProductCode&gt; KnownCodes` means
    /// no blank one can be safely fabricated.
    /// </summary>
    public sealed record Product
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public ProductCode Code { get; set; }

        // Materialised deliberately: a lazy LINQ iterator assigned here is written by
        // Newtonsoft's TypeNameHandling.Auto as  = the iterator's concrete type, which
        // cannot be reconstructed, so the document writes fine and then fails every read.
        // See #5076.
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
                    .ToArray()
            };
        }

        [NaturalKeySource]
        public void Apply(ProductCodeChangedByInstanceMethod e)
        {
            Code = new ProductCode(e.NewProductCode);
            KnownCodes = KnownCodes
                .Where(c => c.Value != e.NewProductCode)
                .Append(new ProductCode(e.NewProductCode))
                .ToArray();
        }
    }

    /// <summary>
    /// The same aggregate, with the key derived from the event ALONE — a static [NaturalKeySource]
    /// returning the key type and taking IEvent&lt;T&gt;. This is the shape that could not bind before
    /// jasperfx#571 and is now the highest-ranked strategy: nothing is fabricated and no user
    /// aggregation code runs to work out the key.
    /// </summary>
    public sealed record KeyFromEventProduct
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public ProductCode Code { get; set; }

        // Materialised deliberately: a lazy LINQ iterator assigned here is written by
        // Newtonsoft's TypeNameHandling.Auto as  = the iterator's concrete type, which
        // cannot be reconstructed, so the document writes fine and then fails every read.
        // See #5076.
        public required IEnumerable<ProductCode> KnownCodes { get; set; }

        [NaturalKeySource]
        public static ProductCode KeyOnRegistration(IEvent<ProductRegistered> e)
            => new(e.Data.ProductCode);

        [NaturalKeySource]
        public static ProductCode KeyOnRename(IEvent<ProductCodeChangedByEventWrapper> e)
            => new(e.Data.NewProductCode);

        public static KeyFromEventProduct Create(ProductRegistered e)
        {
            return new KeyFromEventProduct
            {
                Id = e.ProductId,
                Code = new ProductCode(e.ProductCode),
                KnownCodes = [new ProductCode(e.ProductCode)]
            };
        }

        public static KeyFromEventProduct Apply(IEvent<ProductCodeChangedByEventWrapper> e,
            KeyFromEventProduct product)
        {
            return product with
            {
                Code = new ProductCode(e.Data.NewProductCode),
                KnownCodes = product.KnownCodes
                    .Where(c => c.Value != e.Data.NewProductCode)
                    .Append(new ProductCode(e.Data.NewProductCode))
                    .ToArray()
            };
        }
    }

    private static void configureStore(StoreOptions opts, Action<ProjectionBase>? configureProjection = null)
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = schemaName;
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Projections.Snapshot<Product>(SnapshotLifecycle.Async, configureProjection!);
    }

    // The original bug was that NOTHING happened: no mapping, no log, no error, and the user found out
    // when the natural key lookup returned null at runtime. Silence is the regression to guard against.
    [Fact]
    public void an_unbindable_natural_key_source_fails_loudly_at_configuration_time()
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            StoreOptions(opts => configureStore(opts));
        });

        // Names the offending methods, the reason, and both supported ways out
        ex.Message.ShouldContain(nameof(ProductCodeChangedByEventWrapper));
        ex.Message.ShouldContain(nameof(ProductCodeChangedByInstanceMethod));
        ex.Message.ShouldContain("required members");
        ex.Message.ShouldContain("NaturalKeyFor");
    }

    // #5042's failing test. The key source takes IEvent<T>, which yielded no extractor at all before
    // the contract widened from the event data to the event.
    [Fact]
    public async Task natural_key_is_maintained_when_the_handler_takes_IEvent()
    {
        StoreOptions(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schemaName;
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Projections.Snapshot<KeyFromEventProduct>(SnapshotLifecycle.Async);
        });

        var streamId = await appendRenameAsync<KeyFromEventProduct>(
            id => new ProductCodeChangedByEventWrapper(id, "PROD-999"));

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<KeyFromEventProduct>(CancellationToken.None);

        await using var query = theStore.LightweightSession();
        var product = await query.Events.FetchLatest<KeyFromEventProduct, ProductCode>(new ProductCode("PROD-999"), TestContext.Current.CancellationToken);
        product.ShouldNotBeNull();
        product.Id.ShouldBe(streamId);
        product.Code.Value.ShouldBe("PROD-999");
        product.KnownCodes.ShouldContain(new ProductCode("PROD-001"));
        product.KnownCodes.ShouldContain(new ProductCode("PROD-999"));

        // #5041 item 2 on a source shape that could not bind at all before jasperfx#571
        (await naturalKeysForStreamAsync("mt_natural_key_keyfromeventproduct", streamId))
            .ShouldBe(["PROD-999"]);
        (await query.Events.FetchLatest<KeyFromEventProduct, ProductCode>(new ProductCode("PROD-001"), TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    // The escape hatch for the reporter's own aggregate: NaturalKeyBuilder.SetBy/SetByEvent were dead
    // code (internal constructor, nothing ever built one) until jasperfx#571 made them reachable. An
    // explicit registration replaces whatever discovery found AND clears the configuration-time error.
    [Fact]
    public async Task natural_key_is_maintained_through_an_explicit_registration()
    {
        StoreOptions(opts => configureStore(opts, p =>
            ((SingleStreamProjection<Product, Guid>)p).NaturalKeyFor(x => x
                .SetBy<ProductRegistered>(e => new ProductCode(e.ProductCode))
                .SetByEvent<ProductCodeChangedByEventWrapper>(e => new ProductCode(e.Data.NewProductCode))
                .SetBy<ProductCodeChangedByInstanceMethod>(e => new ProductCode(e.NewProductCode)))));

        var streamId = await appendRenameAsync<Product>(
            id => new ProductCodeChangedByInstanceMethod(id, "PROD-999"));

        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Product>(CancellationToken.None);

        await using var query = theStore.LightweightSession();
        var product = await query.Events.FetchLatest<Product, ProductCode>(new ProductCode("PROD-999"), TestContext.Current.CancellationToken);
        product.ShouldNotBeNull();
        product.Id.ShouldBe(streamId);
        product.Code.Value.ShouldBe("PROD-999");

        // #5041 item 2 through an explicitly registered extractor
        (await naturalKeysForStreamAsync("mt_natural_key_product", streamId)).ShouldBe(["PROD-999"]);
        (await query.Events.FetchLatest<Product, ProductCode>(new ProductCode("PROD-001"), TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    /// <summary>
    /// #5041 item 2 — the retired key must not survive alongside the new one, squatting on its slot in
    /// the lookup table's primary key. #5049 fixed and covered that, but only for a source shape that
    /// discovery could always bind (a static handler taking the raw event). On the two paths this PR
    /// newly enables the question could not even be ASKED before, because item 1 meant nothing was
    /// written for those event types at all.
    /// </summary>
    private async Task<string[]> naturalKeysForStreamAsync(string table, Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select natural_key_value from {schemaName}.{table} where stream_id = :id order by natural_key_value";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = streamId;
        cmd.Parameters.Add(parameter);

        var values = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(await reader.GetFieldValueAsync<string>(0));
        }

        return values.ToArray();
    }

    private async Task<Guid> appendRenameAsync<T>(Func<Guid, object> renameEvent) where T : class
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<T>(streamId, new ProductRegistered(streamId, "PROD-001"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, renameEvent(streamId));
            await session.SaveChangesAsync();
        }

        return streamId;
    }
}
