using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Marten.Testing.OtherAssembly.Issue4557;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// Reproduction for https://github.com/JasperFx/marten/issues/4557.
//
// A user upgrading 8 -> 9 has a self-aggregating immutable `record` snapshot with
// static Create/Apply, registered via `Projections.Snapshot<MyType>(Inline)`. It
// worked in v8 (runtime codegen) but in v9 fails at store construction with:
//
//   JasperFx.Events.Projections.InvalidProjectionException:
//   No source-generated dispatcher found for
//   Marten.Events.Aggregation.SingleStreamProjection<MyType, System.Guid>...
//
// The aggregate type + the Snapshot<MyType> registration both live in
// Marten.Testing.OtherAssembly, which references Marten but NOT the
// JasperFx.Events.SourceGenerator analyzer. Originally the analyzer never reached a
// consumer at all: Marten referenced it with PrivateAssets=all, so a project that only
// had `<PackageReference Include="Marten" />` never ran the generator, no
// `[GeneratedEvolver]` attribute was emitted, and the runtime scan in
// JasperFxAggregationProjectionBase.tryUseAssemblyRegisteredEvolver found nothing.
//
// Resolution (#4557): Marten's NuGet package now BUNDLES the analyzer in
// analyzers/dotnet/cs, so a real consumer that references only `Marten` gets the
// generator automatically. That packaging fix is validated by packing Marten and
// running the reproduction sample — it cannot be observed through a ProjectReference,
// which never receives a NuGet-bundled analyzer. This test therefore still exercises
// the underlying runtime fail-fast for the case where the analyzer genuinely did not
// run in the aggregate's assembly (e.g. a consumer that strips the `analyzers` asset,
// or the cross-assembly record gap below).
//
// (Defining these same types directly in EventSourcingTests would NOT reproduce,
// because this test assembly references the analyzer explicitly and Pipeline 3 of
// the generator would emit the dispatcher.)
public class Bug_4557_self_aggregating_snapshot_without_source_generator
{
    // Pins the runtime fail-fast: when no source-generated dispatcher is present in the
    // aggregate's assembly, registration throws at DocumentStore.For(...) — before any
    // database access — rather than failing later at first event dispatch.
    [Fact]
    public void snapshot_without_source_generator_throws_at_registration()
    {
        var ex = Should.Throw<JasperFx.Events.Projections.InvalidProjectionException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                Issue4557Registration.Configure(opts);
            });
        });

        ex.Message.ShouldContain("No source-generated dispatcher found");
    }

    // The end-to-end round trip a real consumer expects. This passes once the events
    // source generator runs in this aggregate's assembly — which is exactly what the
    // #4557 packaging fix does for package consumers (Marten now bundles the analyzer).
    // It stays skipped here because Marten.Testing.OtherAssembly is a *ProjectReference*
    // consumer and never receives a NuGet-bundled analyzer; the package-consumer path is
    // validated by packing Marten and running the reproduction sample instead. Un-skip
    // if/when OtherAssembly is given the analyzer or the cross-assembly generator change
    // lands.
    [Fact(Skip = "Validated via packaging (see PR); ProjectReference consumers don't receive a NuGet-bundled analyzer.")]
    public async Task snapshot_without_source_generator_should_round_trip()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "issue4557";
            Issue4557Registration.Configure(opts);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<MyType>(streamId,
                new MyTypeCreated(streamId, "test"),
                new MyTypeIncremented(1));
            await session.SaveChangesAsync();
        }

        await using (var query = store.QuerySession())
        {
            var snapshot = await query.LoadAsync<MyType>(streamId);
            snapshot.ShouldNotBeNull();
            snapshot.Name.ShouldBe("test");
            snapshot.Count.ShouldBe(1);
            snapshot.LastEvent.ShouldBe(nameof(MyTypeIncremented));
        }
    }
}
